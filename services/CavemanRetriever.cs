// -----------------------------------------------------------------------------
// <copyright file="CavemanRetriever.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>BM25+ retrieval over arbitrary text chunks, with an optional RM3 pseudo-relevance-feedback query expansion pass.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

/// <summary>One retrieved document: its position in the input list, its text, and its score.</summary>
public readonly record struct RetrievalResult(int Index, string Document, float Score);

/// <summary>
/// Ranks arbitrary text chunks (sentences, conversation turns, JSON rows serialized to
/// text, log lines, …) against a query using BM25+. <see cref="RetrieveWithFeedback"/>
/// additionally runs RM3 pseudo-relevance feedback: an initial BM25 pass finds the top
/// candidates, a "relevance model" is built from their vocabulary, and the query is expanded
/// with the terms that model favours before a second, final ranking pass — this finds
/// relevant chunks that don't literally contain the query's words but do contain words the
/// top initial results have in common. Pure term-frequency statistics; no external
/// dependency, no model, no network call.
/// </summary>
public sealed class CavemanRetriever
{
    private const float K1 = 1.5f, B = 0.75f;

    /// <summary>BM25+ lower-bound term added to every non-zero term match (default 1.0). See <see cref="CavemanJsonCrusher.Bm25Delta"/> for the rationale.</summary>
    public float Bm25Delta { get; init; } = 1.0f;

    /// <summary>Number of top initial-pass documents used to build the RM3 relevance model (default 10).</summary>
    public int RM3FeedbackDocs { get; init; } = 10;

    /// <summary>Number of expansion terms drawn from the relevance model (default 10).</summary>
    public int RM3ExpansionTerms { get; init; } = 10;

    /// <summary>
    /// Weight given to the original query terms vs. the expansion terms in the second pass
    /// (default 0.6 = 60% original query, 40% expansion terms — the standard RM3 default).
    /// </summary>
    public float RM3OriginalQueryWeight { get; init; } = 0.6f;

    private static readonly Regex WordSplit = new(@"[\p{L}\p{N}\p{M}]+", RegexOptions.Compiled);

    private readonly FunctionWordProvider? _wordProvider;

    public CavemanRetriever(FunctionWordProvider? wordProvider = null) => _wordProvider = wordProvider;

    /// <summary>Ranks <paramref name="documents"/> against <paramref name="query"/> with plain BM25+ (single pass, no feedback).</summary>
    public List<RetrievalResult> Retrieve(IReadOnlyList<string> documents, string query, int topK)
    {
        if (documents.Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return new List<RetrievalResult>();

        var tokenizedDocs = documents.Select(Tokenize).ToList();
        var queryWeights = UniformWeights(Tokenize(query));
        if (queryWeights.Count == 0) return new List<RetrievalResult>();

        var scores = ComputeWeightedBm25(tokenizedDocs, queryWeights);
        return RankTopK(documents, scores, topK);
    }

    /// <summary>
    /// Ranks <paramref name="documents"/> against <paramref name="query"/> using BM25+, then
    /// RM3 pseudo-relevance feedback: expands the query with terms characteristic of the
    /// initial top results and re-ranks. Falls back to a plain <see cref="Retrieve"/> pass
    /// when the initial pass finds no relevant document at all (nothing to build a
    /// relevance model from).
    /// </summary>
    public List<RetrievalResult> RetrieveWithFeedback(
        IReadOnlyList<string> documents, string query, int topK, string? iso3 = null)
    {
        if (documents.Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return new List<RetrievalResult>();

        var tokenizedDocs = documents.Select(Tokenize).ToList();
        var originalTerms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (originalTerms.Count == 0) return new List<RetrievalResult>();

        var initialWeights = UniformWeights(originalTerms.ToArray());
        var initialScores = ComputeWeightedBm25(tokenizedDocs, initialWeights);

        var feedback = Enumerable.Range(0, documents.Count)
            .Select(i => (idx: i, score: initialScores[i]))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(RM3FeedbackDocs)
            .ToList();

        if (feedback.Count == 0)
            return RankTopK(documents, initialScores, topK);

        var expandedWeights = BuildExpandedQuery(tokenizedDocs, feedback, originalTerms, iso3);
        var finalScores = ComputeWeightedBm25(tokenizedDocs, expandedWeights);
        return RankTopK(documents, finalScores, topK);
    }

    // Builds the RM3 relevance model P(w|R) = Σ_d P(w|d) · P(d|Q0) over the feedback set,
    // then blends its top terms with the original query terms (RM3OriginalQueryWeight vs.
    // 1 − RM3OriginalQueryWeight), producing the weighted query for the second BM25 pass.
    private Dictionary<string, float> BuildExpandedQuery(
        List<string[]> tokenizedDocs, List<(int idx, float score)> feedback, List<string> originalTerms, string? iso3)
    {
        float scoreSum = feedback.Sum(f => f.score);
        var functionWords = _wordProvider != null && !string.IsNullOrEmpty(iso3)
            ? _wordProvider.GetFunctionWords(iso3)
            : null;

        var relevanceModel = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (idx, score) in feedback)
        {
            var doc = tokenizedDocs[idx];
            if (doc.Length == 0 || scoreSum <= 0) continue;

            float pDocGivenQuery = score / scoreSum;
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in doc) tf[t] = tf.GetValueOrDefault(t) + 1;

            foreach (var (term, count) in tf)
            {
                if (functionWords != null && functionWords.Contains(term)) continue;
                float pTermGivenDoc = (float)count / doc.Length;
                relevanceModel[term] = relevanceModel.GetValueOrDefault(term) + pTermGivenDoc * pDocGivenQuery;
            }
        }

        var expansionTerms = relevanceModel
            .Where(kv => !originalTerms.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .Take(RM3ExpansionTerms)
            .ToList();

        var expanded = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        float origShare = RM3OriginalQueryWeight / originalTerms.Count;
        foreach (var term in originalTerms)
            expanded[term] = expanded.GetValueOrDefault(term) + origShare;

        float expansionSum = expansionTerms.Sum(kv => kv.Value);
        if (expansionSum > 0)
        {
            float expansionShare = 1f - RM3OriginalQueryWeight;
            foreach (var (term, weight) in expansionTerms)
                expanded[term] = expanded.GetValueOrDefault(term) + expansionShare * (weight / expansionSum);
        }

        return expanded;
    }

    private float[] ComputeWeightedBm25(List<string[]> tokenizedDocs, Dictionary<string, float> queryWeights)
    {
        int n = tokenizedDocs.Count;
        var scores = new float[n];
        if (n == 0 || queryWeights.Count == 0) return scores;

        var termFreqs = tokenizedDocs.Select(doc =>
        {
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in doc) tf[t] = tf.GetValueOrDefault(t) + 1;
            return tf;
        }).ToList();

        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in queryWeights.Keys)
            df[term] = termFreqs.Count(tf => tf.ContainsKey(term));

        float avgdl = n > 0 ? (float)tokenizedDocs.Sum(d => d.Length) / n : 1f;

        for (int i = 0; i < n; i++)
        {
            float docLen = tokenizedDocs[i].Length;
            foreach (var (term, qWeight) in queryWeights)
            {
                if (!termFreqs[i].TryGetValue(term, out int tfRaw) || tfRaw == 0) continue;
                int dfVal = df.GetValueOrDefault(term, 0);
                float idf = MathF.Log((n - dfVal + 0.5f) / (dfVal + 0.5f) + 1f);
                float tf = Bm25Delta + tfRaw * (K1 + 1) / (tfRaw + K1 * (1 - B + B * docLen / avgdl));
                scores[i] += qWeight * idf * tf;
            }
        }
        return scores;
    }

    private static Dictionary<string, float> UniformWeights(IReadOnlyCollection<string> terms)
    {
        var distinct = terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.ToDictionary(t => t, _ => 1f, StringComparer.OrdinalIgnoreCase);
    }

    private static List<RetrievalResult> RankTopK(IReadOnlyList<string> documents, float[] scores, int topK)
    {
        return Enumerable.Range(0, documents.Count)
            .Select(i => new RetrievalResult(i, documents[i], scores[i]))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    private static string[] Tokenize(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : WordSplit.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToArray();
}
