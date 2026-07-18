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
    private readonly CavemanTopicSegmenter _topicSegmenter;

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
        _topicSegmenter = new CavemanTopicSegmenter(_wordProvider);
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

    /// <summary>
    /// Topic-aware summarization: segments the text with <see cref="CavemanTopicSegmenter"/>
    /// first, then allocates the sentence budget proportionally across topics (largest-
    /// remainder rounding) and scores/selects sentences independently within each topic,
    /// instead of scoring the whole document as one undifferentiated bag of sentences.
    /// Existing behaviour (<see cref="CondenseText(string, int)"/> and its overloads) is
    /// unchanged — this is an additional, separate method, not a replacement — because on a
    /// single-topic document the two approaches converge and there's nothing to gain from
    /// segmentation, while on a genuinely multi-topic document, plain TF-IDF scoring can let
    /// one statistically dense topic dominate the whole summary and starve the others
    /// entirely; this method guarantees every detected topic gets represented.
    /// Falls back to <see cref="CondenseText(string, int, string)"/> when segmentation finds
    /// no real topic structure (a single segment) — no separate code path to diverge from it.
    /// </summary>
    public string CondenseTextTopicAware(string text, int sentenceCount, string? iso3 = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        iso3 ??= _detector.Detect(text);

        var segments = _topicSegmenter.Segment(text, iso3);
        if (segments.Count <= 1)
            return CondenseText(text, sentenceCount, iso3);

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (sentences.Length <= sentenceCount)
            return text;

        var funcWords = _wordProvider.GetFunctionWords(iso3);
        var lemmas = LoadLemmas(iso3);
        var properNouns = LoadProperNouns(iso3);

        var budget = AllocateSegmentBudget(segments, sentenceCount);

        var selectedIndexes = new List<int>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (budget[i] <= 0) continue;
            var segSentences = sentences.Skip(segments[i].StartSentence).Take(segments[i].SentenceCount).ToArray();
            if (segSentences.Length == 0) continue;

            var scored = ScoreSentences(segSentences, funcWords, lemmas, properNouns);
            var selected = SelectWithMmr(scored, Math.Min(budget[i], segSentences.Length), 0.55);
            foreach (var s in selected)
                selectedIndexes.Add(segments[i].StartSentence + s.Index);
        }

        var ordered = selectedIndexes.Distinct().OrderBy(i => i).ToArray();
        var parts = new string[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            var t = sentences[ordered[i]].Text.Trim();
            parts[i] = i > 0 ? " " + t : t;
        }
        return string.Concat(parts).Trim();
    }

    // Largest-remainder apportionment: gives each topic segment a sentence share
    // proportional to how much of the document it covers, then hands out the few leftover
    // slots (from integer rounding) to the segments with the biggest fractional remainder —
    // the standard way to round a set of proportions so they still sum to the target total.
    private static int[] AllocateSegmentBudget(List<TopicSegment> segments, int totalBudget)
    {
        int totalSentences = segments.Sum(s => s.SentenceCount);
        if (totalSentences == 0) return new int[segments.Count];

        var raw = segments.Select(s => (double)s.SentenceCount / totalSentences * totalBudget).ToArray();
        var alloc = raw.Select(r => (int)Math.Floor(r)).ToArray();

        int remaining = totalBudget - alloc.Sum();
        var byRemainder = Enumerable.Range(0, segments.Count)
            .OrderByDescending(i => raw[i] - Math.Floor(raw[i]))
            .ToList();
        for (int k = 0; k < remaining && k < byRemainder.Count; k++)
            alloc[byRemainder[k]]++;

        return alloc;
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
