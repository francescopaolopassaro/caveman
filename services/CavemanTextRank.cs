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
using System.Text.RegularExpressions;

namespace caveman.core.services;

public class CavemanTextRank
{
    private readonly FunctionWordProvider _wordProvider;
    private readonly CavemanLanguageDetector _detector;
    private readonly CavemanTextSplitter _splitter;
    private readonly CavemanSentenceDetector _sentenceDetector;

    public CavemanTextRank()
        : this(new FunctionWordProvider())
    {
    }

    public CavemanTextRank(FunctionWordProvider wordProvider)
    {
        _wordProvider = wordProvider;
        _detector = new CavemanLanguageDetector(_wordProvider);
        _splitter = new CavemanTextSplitter();
        _sentenceDetector = new CavemanSentenceDetector(_wordProvider);
    }

    public string RankAndSummarize(string text, int sentenceCount)
    {
        var iso3 = _detector.Detect(text);
        return RankAndSummarize(text, sentenceCount, iso3);
    }

    public string RankAndSummarize(string text, float ratio)
    {
        var iso3 = _detector.Detect(text);
        return RankAndSummarize(text, ratio, iso3);
    }

    public string RankAndSummarize(string text, int sentenceCount, string iso3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (sentences.Length <= sentenceCount)
            return text;

        var funcWords = _wordProvider.GetFunctionWords(iso3);
        return BuildSummary(sentences, funcWords, sentenceCount);
    }

    public string RankAndSummarize(string text, float ratio, string iso3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (ratio >= 1.0f)
            return text;

        var count = (int)Math.Max(1, Math.Round(sentences.Length * ratio, MidpointRounding.AwayFromZero));
        return RankAndSummarize(text, count, iso3);
    }

    /// <summary>
    /// Processes a full chatbot/LLM conversation transcript. It cleans the markdown/JSON,
    /// segments the text into blocks and applies TextRank only to the long natural-language
    /// "discourse" blocks that exceed the configured quota. Short structured blocks
    /// (service results / keyword lists such as "I.5 - Stemma, gonfalone, sigillo") and
    /// discourses already below the quota are preserved verbatim.
    /// </summary>
    /// <param name="conversation">The raw conversation context (markdown/JSON allowed).</param>
    /// <param name="options">Optional thresholds; sensible defaults are used when null.</param>
    public string RankAndSummarizeChat(string conversation, ChatSummarizeOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(conversation))
            return string.Empty;

        options ??= new ChatSummarizeOptions();

        var clean = options.AlreadyClean
            ? conversation
            : CavemanConversationToText.ExtractTextFromMarkdown(conversation);

        if (string.IsNullOrWhiteSpace(clean))
            return string.Empty;

        var iso3 = options.Iso3 ?? _detector.Detect(clean);
        var funcWords = _wordProvider.GetFunctionWords(iso3);

        var blocks = SplitIntoBlocks(clean);
        var output = new List<string>(blocks.Count);

        foreach (var block in blocks)
        {
            if (IsLongDiscourse(block, funcWords, iso3, options, out var sentenceCount))
            {
                var count = ComputeSummaryCount(sentenceCount, options);
                output.Add(RankAndSummarize(block, count, iso3).Trim());
            }
            else
            {
                // Service result / keyword list / already-short discourse: keep as is.
                output.Add(block.Trim());
            }
        }

        return string.Join("\n\n", output.Where(b => b.Length > 0));
    }

    /// <summary>Splits cleaned text into paragraph-level blocks on blank lines.</summary>
    private static List<string> SplitIntoBlocks(string text)
    {
        var raw = Regex.Split(text, @"\n\s*\n");
        var blocks = new List<string>(raw.Length);
        foreach (var b in raw)
        {
            var trimmed = b.Trim();
            if (trimmed.Length > 0)
                blocks.Add(trimmed);
        }
        return blocks;
    }

    /// <summary>
    /// Decides whether a block is a long natural-language discourse worth summarizing.
    /// Combines three heuristics: word quota, stop-word density (prose vs keyword list)
    /// and sentence count. A block must clear all three to qualify.
    /// </summary>
    private bool IsLongDiscourse(
        string block,
        HashSet<string> funcWords,
        string iso3,
        ChatSummarizeOptions options,
        out int sentenceCount)
    {
        sentenceCount = 0;

        var words = _splitter.ParseText(block)
            .Where(t => t.Category == CavemanTokenCategory.Word)
            .Select(t => t.Value.ToLowerInvariant())
            .ToArray();

        // Quota: below this it is treated as already-elaborated / a short result.
        if (words.Length < options.MinDiscourseWords)
            return false;

        // Prose has a high function-word density; keyword/service lists do not.
        var funcCount = words.Count(funcWords.Contains);
        var funcRatio = (double)funcCount / words.Length;
        if (funcRatio < options.MinFunctionWordRatio)
            return false;

        sentenceCount = _sentenceDetector.SplitText(block, iso3).Length;
        return sentenceCount >= options.MinDiscourseSentences;
    }

    /// <summary>Derives how many sentences to keep for a discourse block from its size.</summary>
    private static int ComputeSummaryCount(int sentenceCount, ChatSummarizeOptions options)
    {
        var target = (int)Math.Round(sentenceCount * options.SummaryRatio, MidpointRounding.AwayFromZero);
        target = Math.Max(options.MinSummarySentences, target);
        target = Math.Min(target, options.MaxSummarySentences);
        target = Math.Min(target, sentenceCount);
        return Math.Max(1, target);
    }

    private string BuildSummary(
        CavemanSentence[] sentences,
        HashSet<string> funcWords,
        int count)
    {
        var total = sentences.Length;
        var wordSets = new HashSet<string>[total];

        for (int i = 0; i < total; i++)
        {
            var words = sentences[i].Tokens
                .Where(t => t.Category == CavemanTokenCategory.Word)
                .Select(t => t.Value.ToLowerInvariant())
                .Where(w => w.Length > 0 && !funcWords.Contains(w))
                .ToArray();

            wordSets[i] = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
        }

        var scores = ComputeTextRank(wordSets);
        var selected = SelectWithMmr(wordSets, scores, count, 0.5);
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

    private static double[] ComputeTextRank(HashSet<string>[] wordSets)
    {
        var n = wordSets.Length;
        if (n == 0) return [];

        var sim = new double[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var s = JaccardSimilarity(wordSets[i], wordSets[j]);
                sim[i, j] = s;
                sim[j, i] = s;
            }
        }

        var sumRow = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i != j)
                    sumRow[i] += sim[i, j];
            }
        }

        var scores = new double[n];
        for (int i = 0; i < n; i++)
            scores[i] = 1.0 / n;

        const double d = 0.85;
        const int maxIter = 100;
        const double tol = 1e-4;

        for (int iter = 0; iter < maxIter; iter++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || sumRow[j] == 0) continue;
                    sum += sim[j, i] * scores[j] / sumRow[j];
                }
                next[i] = (1 - d) / n + d * sum;
            }

            double diff = 0;
            for (int i = 0; i < n; i++)
                diff += Math.Abs(next[i] - scores[i]);

            scores = next;
            if (diff < tol) break;
        }

        return scores;
    }

    private static List<(int Index, double Score)> SelectWithMmr(
        HashSet<string>[] wordSets,
        double[] scores,
        int count,
        double lambda)
    {
        var n = scores.Length;
        if (n <= count)
            return Enumerable.Range(0, n).Select(i => (i, scores[i])).ToList();

        var remaining = new List<int>(Enumerable.Range(0, n));
        var selected = new List<int>();
        var selectedWordSets = new List<HashSet<string>>();

        for (int round = 0; round < count; round++)
        {
            int bestIdx = -1;
            double bestMmr = double.MinValue;

            foreach (var idx in remaining)
            {
                double maxSim = 0;
                foreach (var selSet in selectedWordSets)
                {
                    var s = JaccardSimilarity(wordSets[idx], selSet);
                    if (s > maxSim) maxSim = s;
                }

                var mmr = lambda * scores[idx] - (1 - lambda) * maxSim;
                if (mmr > bestMmr)
                {
                    bestMmr = mmr;
                    bestIdx = idx;
                }
            }

            if (bestIdx < 0) break;
            selected.Add(bestIdx);
            selectedWordSets.Add(wordSets[bestIdx]);
            remaining.Remove(bestIdx);
        }

        return selected.Select(i => (i, scores[i])).ToList();
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0;
        var intersection = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }
}

/// <summary>Thresholds that drive <see cref="CavemanTextRank.RankAndSummarizeChat"/>.</summary>
public sealed class ChatSummarizeOptions
{
    /// <summary>When true, skips markdown/JSON extraction (input is already plain text).</summary>
    public bool AlreadyClean { get; set; }

    /// <summary>Optional ISO-639-3 language override; auto-detected when null.</summary>
    public string? Iso3 { get; set; }

    /// <summary>Word quota: a block must reach this many words to be a summarizable discourse.</summary>
    public int MinDiscourseWords { get; set; } = 50;

    /// <summary>Minimum number of sentences for a block to qualify as a discourse.</summary>
    public int MinDiscourseSentences { get; set; } = 3;

    /// <summary>Minimum stop-word ratio separating prose from keyword/service lists.</summary>
    public double MinFunctionWordRatio { get; set; } = 0.20;

    /// <summary>Fraction of a discourse's sentences to keep when summarizing.</summary>
    public float SummaryRatio { get; set; } = 0.4f;

    /// <summary>Lower bound on sentences kept per summarized discourse.</summary>
    public int MinSummarySentences { get; set; } = 2;

    /// <summary>Upper bound on sentences kept per summarized discourse.</summary>
    public int MaxSummarySentences { get; set; } = 8;
}
