// -----------------------------------------------------------------------------
// <copyright file="CavemanRelevanceFilter.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Query-focused context shaping: keep only the blocks most relevant to a question.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.services;

/// <summary>One scored block produced by <see cref="CavemanRelevanceFilter"/>.</summary>
public sealed class RelevanceHit
{
    /// <summary>The block text.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Relevance score in [0,1] (lexical overlap with the query).</summary>
    public double Score { get; set; }
    /// <summary>The block's original position in the source.</summary>
    public int Index { get; set; }
}

/// <summary>
/// Lightweight, embedding-free "attention": given a query and a body of text, scores each block
/// (paragraph) by content-word overlap with the query and keeps the top-K most relevant ones.
/// Ideal for shaping a large context down to what actually matters for the current question.
/// </summary>
public sealed class CavemanRelevanceFilter
{
    private readonly CavemanLanguageDetector _detector;
    private readonly FunctionWordProvider _wordProvider;
    private readonly CavemanTextSplitter _splitter;

    public CavemanRelevanceFilter(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider ?? new FunctionWordProvider();
        _detector = new CavemanLanguageDetector(_wordProvider);
        _splitter = new CavemanTextSplitter();
    }

    /// <summary>Returns the <paramref name="topK"/> most query-relevant blocks, in original order, joined.</summary>
    public string Focus(string text, string query, int topK, string? iso3 = null)
    {
        var hits = Rank(text, query, iso3);
        if (hits.Count == 0)
            return string.Empty;

        var kept = hits
            .Take(Math.Max(1, topK))
            .OrderBy(h => h.Index)
            .Select(h => h.Text);

        return string.Join("\n\n", kept);
    }

    /// <summary>Scores every block against the query, highest relevance first.</summary>
    public List<RelevanceHit> Rank(string text, string query, string? iso3 = null)
    {
        var result = new List<RelevanceHit>();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return result;

        var clean = CavemanConversationToText.ExtractTextFromMarkdown(text);
        if (string.IsNullOrWhiteSpace(clean))
            return result;

        iso3 ??= _detector.Detect(query + " " + clean);
        var funcWords = _wordProvider.GetFunctionWords(iso3);
        var lemmas = _wordProvider.GetLemmaMap(iso3);

        var queryTerms = ContentWords(query, funcWords, lemmas);
        if (queryTerms.Count == 0)
            return result;

        var blocks = SplitBlocks(clean);
        for (int i = 0; i < blocks.Count; i++)
        {
            var blockTerms = ContentWords(blocks[i], funcWords, lemmas);
            result.Add(new RelevanceHit
            {
                Text = blocks[i],
                Index = i,
                Score = Similarity(queryTerms, blockTerms)
            });
        }

        return result
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Index)
            .ToList();
    }

    /// <summary>Relevance score in [0,1] of a single text against a query (lemmatized lexical overlap).</summary>
    public double Score(string text, string query, string? iso3 = null)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return 0;

        iso3 ??= _detector.Detect(query + " " + text);
        var funcWords = _wordProvider.GetFunctionWords(iso3);
        var lemmas = _wordProvider.GetLemmaMap(iso3);

        var q = ContentWords(query, funcWords, lemmas);
        var t = ContentWords(text, funcWords, lemmas);
        return Similarity(q, t);
    }

    private HashSet<string> ContentWords(string text, HashSet<string> funcWords, IReadOnlyDictionary<string, string> lemmas)
    {
        return _splitter.ParseText(text)
            .Where(t => t.Category == CavemanTokenCategory.Word)
            .Select(t => t.Value.ToLowerInvariant())
            .Where(w => w.Length > 1 && !funcWords.Contains(w))
            .Select(w => lemmas.TryGetValue(w, out var lemma) ? lemma : w)  // normalize variants
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Overlap coefficient against the query (rewards blocks that cover the query terms).
    private static double Similarity(HashSet<string> query, HashSet<string> block)
    {
        if (query.Count == 0 || block.Count == 0)
            return 0;
        int shared = query.Count(block.Contains);
        return (double)shared / query.Count;
    }

    private static List<string> SplitBlocks(string text)
    {
        return System.Text.RegularExpressions.Regex
            .Split(text, @"\n\s*\n")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();
    }
}
