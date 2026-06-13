// -----------------------------------------------------------------------------
// <copyright file="CavecrewService.cs" company="Digitalsolutions.it">
//   Caveman � NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo � Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Cavecrew micro-agents (investigator, builder, reviewer) for delegated code tasks.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

public class CavemanSummarizer : ISummarizer
{
    private readonly FunctionWordProvider _wordProvider;
    private readonly ILanguageDetector _detector;
    private readonly CavemanTextSplitter _splitter;
    private readonly CavemanSentenceDetector _sentenceDetector;
    private readonly ICompressionService _compressionService;

    public CavemanSummarizer()
        : this(new FunctionWordProvider())
    {
    }

    public CavemanSummarizer(FunctionWordProvider wordProvider)
        : this(wordProvider, null)
    {
    }

    /// <param name="compressionService">Optional compression engine used by <see cref="CompressWithSummaryAsync"/>.</param>
    public CavemanSummarizer(FunctionWordProvider wordProvider, ICompressionService? compressionService)
    {
        _wordProvider = wordProvider;
        _detector = new CavemanLanguageDetector(_wordProvider);
        _splitter = new CavemanTextSplitter();
        _sentenceDetector = new CavemanSentenceDetector(_wordProvider);
        _compressionService = compressionService ?? new CavemanCompressionService(null, _wordProvider);
    }

    /// <inheritdoc />
    public string Summarize(string text, int sentenceCount, string? iso3 = null)
        => iso3 is null ? CondenseText(text, sentenceCount) : CondenseText(text, sentenceCount, iso3);

    /// <inheritdoc />
    public string Summarize(string text, float ratio, string? iso3 = null)
        => iso3 is null ? CondenseText(text, ratio) : CondenseText(text, ratio, iso3);

    public string CondenseText(string text, int sentenceCount)
    {
        var iso3 = _detector.Detect(text);
        return CondenseText(text, sentenceCount, iso3);
    }

    public string CondenseText(string text, float ratio)
    {
        var iso3 = _detector.Detect(text);
        return CondenseText(text, ratio, iso3);
    }

    public string CondenseText(string text, int sentenceCount, string iso3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (sentences.Length <= sentenceCount)
            return text;

        var funcWords = _wordProvider.GetFunctionWords(iso3);
        var lemmas = LoadLemmas(iso3);
        var properNouns = LoadProperNouns(iso3);
        return BuildSummary(sentences, funcWords, lemmas, properNouns, sentenceCount);
    }

    public string CondenseText(string text, float ratio, string iso3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (ratio >= 1.0f)
            return text;

        var count = (int)Math.Max(1, Math.Round(sentences.Length * ratio, MidpointRounding.AwayFromZero));
        return CondenseText(text, count, iso3);
    }

    public Task<CompressionResult> CompressWithSummaryAsync(
        string input,
        CavemanCompressionLevel level,
        int summarySentenceCount,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(new CompressionResult { CompressedText = string.Empty });

        var iso3 = _detector.Detect(input);

        try
        {
            ct.ThrowIfCancellationRequested();

            var summary = CondenseText(input, summarySentenceCount, iso3);
            var compressed = _compressionService.ApplyCompression(summary, iso3, level);

            return Task.FromResult(compressed);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CompressionResult
            {
                CompressedText = input,
                ErrorMessage = $"Summarization failed: {ex.Message}"
            });
        }
    }

    private string BuildSummary(
        CavemanSentence[] sentences,
        HashSet<string> funcWords,
        Dictionary<string, string>? lemmas,
        HashSet<string> properNouns,
        int count)
    {
        var scored = ScoreSentences(sentences, funcWords, lemmas, properNouns);
        var selected = SelectWithMmr(scored, count, 0.55);
        var ordered = selected.OrderBy(s => s.Index).ToArray();

        var parts = new string[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            var text = sentences[ordered[i].Index].Text.Trim();
            if (text.Length > 0 && !char.IsWhiteSpace(text[0]) && i > 0)
                text = " " + text;
            parts[i] = text;
        }

        return string.Concat(parts).Trim();
    }

    private ScoredSentence[] ScoreSentences(
        CavemanSentence[] sentences,
        HashSet<string> funcWords,
        Dictionary<string, string>? lemmas,
        HashSet<string> properNouns)
    {
        var total = sentences.Length;
        var parsed = new ScoredSentence[total];

        for (int i = 0; i < total; i++)
        {
            var rawWords = sentences[i].Tokens
                .Where(t => t.Category == CavemanTokenCategory.Word)
                .Select(t => NormalizeWord(t.Value, lemmas))
                .Where(w => w.Length > 0 && !funcWords.Contains(w))
                .ToArray();

            var rawOriginals = sentences[i].Tokens
                .Where(t => t.Category == CavemanTokenCategory.Word)
                .Select(t => t.Value)
                .ToArray();

            parsed[i] = new ScoredSentence
            {
                Index = i,
                Words = rawWords,
                OriginalWords = rawOriginals,
                WordSet = new HashSet<string>(rawWords, StringComparer.OrdinalIgnoreCase),
                HasProperNoun = rawOriginals.Any(w => properNouns.Count > 0 && properNouns.Contains(w.ToLowerInvariant()))
            };
        }

        var wordFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsed)
        {
            foreach (var w in p.WordSet)
            {
                wordFrequency.TryGetValue(w, out var c);
                wordFrequency[w] = c + 1;
            }
        }

        for (int i = 0; i < total; i++)
        {
            var p = parsed[i];

            double tfidf = 0;
            if (p.Words.Length > 0)
            {
                var termFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in p.Words)
                {
                    termFreq.TryGetValue(w, out var c);
                    termFreq[w] = c + 1;
                }

                foreach (var (word, tf) in termFreq)
                {
                    var termF = (double)tf / p.Words.Length;
                    var docsWithWord = wordFrequency.TryGetValue(word, out var dc) ? dc : 1;
                    var idf = Math.Log((double)total / (1 + docsWithWord));
                    tfidf += termF * idf;
                }
            }

            double positionBonus = 0;
            double normalizedPos = (double)i / total;
            if (i == 0)
                positionBonus = 0.30;
            else if (normalizedPos < 0.15)
                positionBonus = 0.15;
            else if (normalizedPos > 0.85)
                positionBonus = 0.10;

            double properBonus = p.HasProperNoun ? 0.08 : 0;

            p.Score = tfidf + positionBonus + properBonus;
        }

        return parsed;
    }

    private static List<ScoredSentence> SelectWithMmr(ScoredSentence[] candidates, int count, double lambda)
    {
        if (candidates.Length <= count)
            return candidates.ToList();

        var remaining = new List<ScoredSentence>(candidates);
        var selected = new List<ScoredSentence>();
        var selectedWordSets = new List<HashSet<string>>();

        for (int round = 0; round < count; round++)
        {
            ScoredSentence? best = null;
            double bestMmr = double.MinValue;

            foreach (var cand in remaining)
            {
                double maxSim = 0;
                foreach (var selSet in selectedWordSets)
                {
                    var sim = JaccardSimilarity(cand.WordSet, selSet);
                    if (sim > maxSim)
                        maxSim = sim;
                }

                var mmr = lambda * cand.Score - (1 - lambda) * maxSim;
                if (mmr > bestMmr)
                {
                    bestMmr = mmr;
                    best = cand;
                }
            }

            if (best != null)
            {
                selected.Add(best);
                selectedWordSets.Add(best.WordSet);
                remaining.Remove(best);
            }
        }

        return selected;
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0;
        var intersection = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string NormalizeWord(string word, Dictionary<string, string>? lemmas)
    {
        var lower = word.ToLowerInvariant();
        if (lemmas != null && lemmas.TryGetValue(lower, out var lemma))
            return lemma;
        return lower;
    }

    private Dictionary<string, string>? LoadLemmas(string iso3)
    {
        var data = _wordProvider.LoadWordData(iso3);
        if (data == null)
            return null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (data.lemmas?.Count > 0)
        {
            foreach (var (form, lemma) in data.lemmas)
            {
                if (!string.IsNullOrEmpty(form) && !string.IsNullOrEmpty(lemma))
                    map[form] = lemma;
            }
        }

        if (data.verbs?.Count > 0)
        {
            foreach (var (lemma, forms) in data.verbs)
            {
                if (string.IsNullOrEmpty(lemma) || forms == null)
                    continue;
                foreach (var form in forms)
                {
                    if (!string.IsNullOrEmpty(form) && !map.ContainsKey(form))
                        map[form] = lemma;
                }
            }
        }

        return map.Count > 0 ? map : null;
    }

    private HashSet<string> LoadProperNouns(string iso3)
    {
        var data = _wordProvider.LoadWordData(iso3);
        if (data?.proper_nouns == null || data.proper_nouns.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(data.proper_nouns, StringComparer.OrdinalIgnoreCase);
    }

    internal class ScoredSentence
    {
        public int Index { get; set; }
        public string[] Words { get; set; } = [];
        public string[] OriginalWords { get; set; } = [];
        public HashSet<string> WordSet { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasProperNoun { get; set; }
        public double Score { get; set; }
    }
}
