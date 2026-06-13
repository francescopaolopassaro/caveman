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
using caveman.core;
using caveman.core.entities;

namespace caveman.core.services;

public class CavemanTextRank : ISummarizer
{
    private readonly FunctionWordProvider _wordProvider;
    private readonly ILanguageDetector _detector;
    private readonly CavemanTextSplitter _splitter;
    private readonly CavemanSentenceDetector _sentenceDetector;
    private readonly ICompressionService _compressor;
    private readonly CavemanSafetyGuard _safety;
    private readonly ITokenCounter _tokenCounter;
    private readonly IConversationParser _parser;

    /// <summary>Starts a fluent builder; override only the seams you need.</summary>
    public static CavemanTextRankBuilder CreateBuilder() => new();

    public CavemanTextRank()
        : this(new FunctionWordProvider())
    {
    }

    public CavemanTextRank(FunctionWordProvider wordProvider)
        : this(wordProvider, null)
    {
    }

    /// <param name="tokenCounter">Optional token counter for metrics/budget; defaults to the built-in heuristic.</param>
    public CavemanTextRank(FunctionWordProvider wordProvider, ITokenCounter? tokenCounter)
        : this(wordProvider, tokenCounter, null)
    {
    }

    public CavemanTextRank(FunctionWordProvider wordProvider, ITokenCounter? tokenCounter, IConversationParser? parser)
        : this(wordProvider, tokenCounter, parser, null)
    {
    }

    public CavemanTextRank(
        FunctionWordProvider wordProvider, ITokenCounter? tokenCounter,
        IConversationParser? parser, ICompressionService? compressionService)
        : this(wordProvider, tokenCounter, parser, compressionService, null)
    {
    }

    /// <param name="parser">Optional conversation parser; defaults to <see cref="CavemanConversationParser"/>.</param>
    /// <param name="compressionService">Optional compression engine; defaults to <see cref="CavemanCompressionService"/>.</param>
    /// <param name="detector">Optional language detector; defaults to <see cref="CavemanLanguageDetector"/>.</param>
    public CavemanTextRank(
        FunctionWordProvider wordProvider, ITokenCounter? tokenCounter,
        IConversationParser? parser, ICompressionService? compressionService, ILanguageDetector? detector)
    {
        _wordProvider = wordProvider;
        _detector = detector ?? new CavemanLanguageDetector(_wordProvider);
        _splitter = new CavemanTextSplitter();
        _sentenceDetector = new CavemanSentenceDetector(_wordProvider);
        _compressor = compressionService ?? new CavemanCompressionService(null, _wordProvider);
        _safety = new CavemanSafetyGuard();
        _tokenCounter = tokenCounter ?? new ModelTokenizer();
        _parser = parser ?? new CavemanConversationParser();
    }

    /// <inheritdoc />
    public string Summarize(string text, int sentenceCount, string? iso3 = null)
        => iso3 is null ? RankAndSummarize(text, sentenceCount) : RankAndSummarize(text, sentenceCount, iso3);

    /// <inheritdoc />
    public string Summarize(string text, float ratio, string? iso3 = null)
        => iso3 is null ? RankAndSummarize(text, ratio) : RankAndSummarize(text, ratio, iso3);

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
    /// discourses already below the quota are preserved verbatim. Optional features
    /// (role parsing, recency window, deduplication, safety, post-compression and a token
    /// budget) are all controlled through <see cref="ChatSummarizeOptions"/>; with the
    /// defaults the behavior is identical to previous versions.
    /// </summary>
    /// <param name="conversation">The raw conversation context (markdown/JSON/HTML allowed).</param>
    /// <param name="options">Optional thresholds; sensible defaults are used when null.</param>
    public string RankAndSummarizeChat(string conversation, ChatSummarizeOptions? options = null, CancellationToken ct = default)
        => RankAndSummarizeChatDetailed(conversation, options, ct).Text;

    /// <summary>Async wrapper over <see cref="RankAndSummarizeChat"/> (offloads the CPU-bound work, honors cancellation).</summary>
    public Task<string> RankAndSummarizeChatAsync(string conversation, ChatSummarizeOptions? options = null, CancellationToken ct = default)
        => Task.Run(() => RankAndSummarizeChat(conversation, options, ct), ct);

    /// <summary>Async wrapper over <see cref="RankAndSummarizeChatDetailed"/>.</summary>
    public Task<ChatSummarizeResult> RankAndSummarizeChatDetailedAsync(string conversation, ChatSummarizeOptions? options = null, CancellationToken ct = default)
        => Task.Run(() => RankAndSummarizeChatDetailed(conversation, options, ct), ct);

    /// <summary>
    /// Same as <see cref="RankAndSummarizeChat"/> but returns a rich <see cref="ChatSummarizeResult"/>
    /// with token metrics (per the chosen model) and per-block statistics.
    /// </summary>
    public ChatSummarizeResult RankAndSummarizeChatDetailed(string conversation, ChatSummarizeOptions? options = null, CancellationToken ct = default)
    {
        var result = new ChatSummarizeResult();
        if (string.IsNullOrWhiteSpace(conversation))
            return result;

        ct.ThrowIfCancellationRequested();
        options ??= new ChatSummarizeOptions();
        var tokenizer = _tokenCounter;
        result.OriginalTokens = tokenizer.CountTokens(conversation, options.TokenModel);

        List<ChatBlock> blocks;
        if (options.ParseConversation)
        {
            var conv = _parser.Parse(conversation);
            result.Format = conv.Format;
            blocks = BuildBlocksFromConversation(conv, options);
        }
        else
        {
            result.Format = "flat";
            blocks = BuildBlocksFromFlat(conversation, options);
        }

        if (options.Deduplicate)
            result.DuplicatesRemoved = Deduplicate(blocks);

        ProcessBlocks(blocks, options, ct);
        EnforceBudget(blocks, options, tokenizer, ct);

        result.Text = Render(blocks);
        result.CompressedTokens = tokenizer.CountTokens(result.Text, options.TokenModel);
        result.Conversation = BuildResultConversation(blocks, result.Format);
        if (options.CollectTrace)
            result.Trace = BuildTrace(blocks);

        result.Blocks = blocks.Count;
        result.SummarizedBlocks = blocks.Count(b => b.Summarized && !b.Dropped);
        result.CompressedBlocks = blocks.Count(b => b.Compressed && !b.Dropped);
        result.KeptVerbatimBlocks = blocks.Count(b => !b.Summarized && !b.Compressed && !b.Dropped);
        result.DroppedBlocks = blocks.Count(b => b.DroppedByBudget);
        result.SkippedForSafety = blocks.Count(b => b.Critical);
        result.WithinBudget = options.MaxTokens <= 0 || result.CompressedTokens <= options.MaxTokens;

        var usdPer1K = options.UsdPer1KTokens ?? CavemanCostEstimator.DefaultUsdPer1KTokens(options.TokenModel);
        result.EstimatedSavedUsd = CavemanCostEstimator.Usd(result.SavedTokens, usdPer1K);
        result.EstimatedSavedEur = CavemanCostEstimator.Eur(result.SavedTokens, usdPer1K, options.UsdToEurRate);

        return result;
    }

    private static MarkdownExtractOptions MdOptions(ChatSummarizeOptions o) => new()
    {
        KeepJson = o.KeepJson,
        KeepCode = o.KeepCode,
        ExtractHtml = o.ExtractHtml
    };

    private List<ChatBlock> BuildBlocksFromFlat(string conversation, ChatSummarizeOptions options)
    {
        var clean = options.AlreadyClean
            ? conversation
            : CavemanConversationToText.ExtractTextFromMarkdown(conversation, MdOptions(options));

        var result = new List<ChatBlock>();
        if (string.IsNullOrWhiteSpace(clean))
            return result;

        var iso3 = options.Iso3 ?? _detector.Detect(clean);
        var funcWords = _wordProvider.GetFunctionWords(iso3);

        int order = 0;
        foreach (var text in SplitIntoBlocks(clean))
            result.Add(MakeBlock(text, iso3, funcWords, options, CavemanRole.Unknown, null, order++, keepVerbatim: false));

        return result;
    }

    private List<ChatBlock> BuildBlocksFromConversation(CavemanConversation conv, ChatSummarizeOptions options)
    {
        var result = new List<ChatBlock>();
        int messageCount = conv.Messages.Count;
        int recencyFrom = options.KeepLastTurnsVerbatim > 0
            ? messageCount - options.KeepLastTurnsVerbatim
            : int.MaxValue;

        for (int i = 0; i < messageCount; i++)
        {
            var msg = conv.Messages[i];
            bool isRecent = i >= recencyFrom;
            bool keepVerbatim = isRecent ||
                                (msg.Role == CavemanRole.System && options.KeepSystemVerbatim);

            // A turn already produced by a previous compaction: keep as-is, don't re-summarize.
            bool frozen = !keepVerbatim &&
                          options.VerbatimContentHashes is { Count: > 0 } hashes &&
                          hashes.Contains(ConversationState.Fingerprint(msg.Content));

            var clean = options.AlreadyClean
                ? msg.Content
                : CavemanConversationToText.ExtractTextFromMarkdown(msg.Content, MdOptions(options));
            if (string.IsNullOrWhiteSpace(clean))
                continue;

            var iso3 = options.Iso3 ?? _detector.Detect(clean);
            var funcWords = _wordProvider.GetFunctionWords(iso3);

            bool first = true;
            foreach (var text in SplitIntoBlocks(clean))
            {
                var prefix = first && options.ShowRolePrefixes && msg.RoleLabel.Length > 0 ? msg.RoleLabel : null;
                result.Add(MakeBlock(text, iso3, funcWords, options, msg.Role, prefix, i, keepVerbatim, frozen));
                first = false;
            }
        }

        return result;
    }

    private ChatBlock MakeBlock(
        string text, string iso3, HashSet<string> funcWords, ChatSummarizeOptions options,
        CavemanRole role, string? rolePrefix, int turnIndex, bool keepVerbatim, bool freeze = false)
    {
        var block = new ChatBlock
        {
            Iso3 = iso3,
            Original = text,
            Rendered = text,
            Role = role,
            RolePrefix = rolePrefix,
            TurnIndex = turnIndex
        };

        if (options.RespectSafety && _safety.Check(text).Level == SafetyLevel.Critical)
        {
            block.Protected = true;
            block.Critical = true;
            return block;
        }

        if (keepVerbatim || MatchesMustKeep(text, options))
        {
            block.Protected = true;     // recency / system prompt / pinned fact: keep verbatim
            return block;
        }

        if (freeze)
            return block;               // already compacted: keep as-is, never re-summarize (still droppable)

        if (IsLongDiscourse(text, funcWords, iso3, options, out var sentenceCount))
        {
            block.IsDiscourse = true;
            block.SentenceCount = sentenceCount;
        }

        return block;
    }

    // Any integer/decimal/date/time figure (e.g. 12345, 3.14, 12:30, 2026-06-13).
    private static readonly Regex NumberOrDate = new(
        @"\d+([.,:/\-]\d+)*", RegexOptions.Compiled);

    private static bool MatchesMustKeep(string text, ChatSummarizeOptions options)
    {
        if (options.KeepNumbersAndDates && NumberOrDate.IsMatch(text))
            return true;

        foreach (var pattern in options.MustKeepPatterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            catch (ArgumentException)
            {
                // Ignore an invalid user-supplied pattern rather than throwing.
            }
        }
        return false;
    }

    private void ProcessBlocks(List<ChatBlock> blocks, ChatSummarizeOptions options, CancellationToken ct = default)
    {
        foreach (var b in blocks)
        {
            ct.ThrowIfCancellationRequested();
            if (b.Protected || b.Dropped)
                continue;

            if (b.IsDiscourse)
            {
                b.KeptSentences = ComputeSummaryCount(b.SentenceCount, options);
                b.Rendered = RankAndSummarize(b.Original, b.KeptSentences, b.Iso3).Trim();
                b.Summarized = true;
            }

            if (options.CompressKeptText)
            {
                b.Rendered = CavemanCompress(b.Rendered, b.Iso3, options.CompressionLevel);
                b.Compressed = true;
            }
        }
    }

    private void EnforceBudget(List<ChatBlock> blocks, ChatSummarizeOptions options, ITokenCounter tokenizer, CancellationToken ct = default)
    {
        if (options.MaxTokens <= 0)
            return;

        int guard = 0;
        while (guard++ < 500)
        {
            ct.ThrowIfCancellationRequested();
            if (tokenizer.CountTokens(Render(blocks), options.TokenModel) <= options.MaxTokens)
                break;

            // 1) Shrink the largest summarizable discourse that can still lose a sentence.
            var shrink = blocks
                .Where(b => !b.Protected && !b.Dropped && b.IsDiscourse && b.KeptSentences > 1)
                .OrderByDescending(b => b.Rendered.Length)
                .FirstOrDefault();
            if (shrink != null)
            {
                shrink.KeptSentences--;
                shrink.Rendered = RankAndSummarize(shrink.Original, shrink.KeptSentences, shrink.Iso3).Trim();
                if (shrink.Compressed)
                    shrink.Rendered = CavemanCompress(shrink.Rendered, shrink.Iso3, options.CompressionLevel);
                continue;
            }

            // 2) Caveman-compress (aggressively) the largest not-yet-compressed block.
            var compress = blocks
                .Where(b => !b.Protected && !b.Dropped && !b.Compressed)
                .OrderByDescending(b => b.Rendered.Length)
                .FirstOrDefault();
            if (compress != null)
            {
                compress.Rendered = CavemanCompress(compress.Rendered, compress.Iso3, CavemanCompressionLevel.Aggressive);
                compress.Compressed = true;
                continue;
            }

            // 3) Drop the oldest droppable block entirely.
            var drop = blocks
                .Where(b => !b.Protected && !b.Dropped)
                .OrderBy(b => b.TurnIndex)
                .FirstOrDefault();
            if (drop == null)
                break;
            drop.Dropped = true;
            drop.DroppedByBudget = true;
        }
    }

    private static int Deduplicate(List<ChatBlock> blocks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (var b in blocks)
        {
            var key = Regex.Replace(b.Original, @"\s+", " ").Trim().ToLowerInvariant();
            if (key.Length == 0)
                continue;
            if (!seen.Add(key))
            {
                b.Dropped = true;
                removed++;
            }
        }
        return removed;
    }

    private const string TruncationMarker = "[…]";

    private static string Render(List<ChatBlock> blocks)
    {
        var parts = new List<string>(blocks.Count);
        bool lastWasMarker = false;

        foreach (var b in blocks)
        {
            if (b.Dropped)
            {
                // Budget-dropped turns leave a compact placeholder (collapsing consecutive ones)
                // so the model can tell context was truncated; dedup-dropped blocks vanish silently.
                if (b.DroppedByBudget && !lastWasMarker)
                {
                    parts.Add(TruncationMarker);
                    lastWasMarker = true;
                }
                continue;
            }

            var text = b.Rendered.Trim();
            if (text.Length == 0)
                continue;

            parts.Add(b.RolePrefix is { Length: > 0 } p ? $"{p}: {text}" : text);
            lastWasMarker = false;
        }

        return string.Join("\n\n", parts);
    }

    private static List<BlockTrace> BuildTrace(List<ChatBlock> blocks)
    {
        var trace = new List<BlockTrace>(blocks.Count);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            string action =
                b.Dropped ? (b.DroppedByBudget ? "dropped" : "deduplicated") :
                b.Critical ? "critical" :
                b.Summarized ? (b.Compressed ? "summarized+compressed" : "summarized") :
                b.Compressed ? "compressed" :
                "kept";

            trace.Add(new BlockTrace
            {
                Index = i,
                Role = b.RolePrefix ?? string.Empty,
                Action = action,
                Discourse = b.IsDiscourse,
                OriginalChars = b.Original.Length,
                FinalChars = b.Dropped ? 0 : b.Rendered.Trim().Length
            });
        }
        return trace;
    }

    private static CavemanConversation BuildResultConversation(List<ChatBlock> blocks, string format)
    {
        var conv = new CavemanConversation { Format = format };
        foreach (var group in blocks.Where(b => !b.Dropped).GroupBy(b => b.TurnIndex).OrderBy(g => g.Key))
        {
            var content = string.Join("\n\n",
                group.Select(b => b.Rendered.Trim()).Where(t => t.Length > 0));
            if (content.Length == 0)
                continue;
            conv.Messages.Add(new CavemanMessage(group.First().Role, content));
        }
        return conv;
    }

    private string CavemanCompress(string text, string iso3, CavemanCompressionLevel level)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        var compressed = _compressor.ApplyCompression(text, iso3, level).CompressedText;
        return string.IsNullOrWhiteSpace(compressed) ? text : compressed;
    }

    /// <summary>Internal working unit: one segment of the conversation with its processing state.</summary>
    private sealed class ChatBlock
    {
        public string Iso3 = "eng";
        public string Original = string.Empty;
        public string Rendered = string.Empty;
        public CavemanRole Role = CavemanRole.Unknown;
        public bool IsDiscourse;
        public int SentenceCount;
        public int KeptSentences;
        public bool Protected;
        public bool Critical;
        public bool Summarized;
        public bool Compressed;
        public bool Dropped;
        public bool DroppedByBudget;
        public string? RolePrefix;
        public int TurnIndex;
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

    // ---- Conversation structure (additive; defaults preserve the flat behavior) ----

    /// <summary>Parse the input into role-tagged turns (OpenAI/Anthropic JSON, ChatML, Gemma, Llama/Mistral, transcripts).</summary>
    public bool ParseConversation { get; set; }

    /// <summary>Keep the last N turns verbatim (recency window). Requires <see cref="ParseConversation"/>.</summary>
    public int KeepLastTurnsVerbatim { get; set; }

    /// <summary>Re-emit role labels (e.g. "User:", "Assistant:") when rendering a parsed conversation.</summary>
    public bool ShowRolePrefixes { get; set; } = true;

    /// <summary>Drop duplicate blocks (e.g. a system prompt repeated every turn).</summary>
    public bool Deduplicate { get; set; }

    /// <summary>Keep system/developer turns verbatim (instructions are rarely safe to summarize). Requires <see cref="ParseConversation"/>.</summary>
    public bool KeepSystemVerbatim { get; set; } = true;

    /// <summary>Regex patterns (case-insensitive) that pin a block: any block matching one is kept verbatim, never summarized.</summary>
    public List<string> MustKeepPatterns { get; set; } = new();

    /// <summary>When true, blocks containing numbers/dates/times are kept verbatim (so factual figures are never summarized away).</summary>
    public bool KeepNumbersAndDates { get; set; }

    /// <summary>Consult <see cref="CavemanSafetyGuard"/>; security-critical blocks are kept verbatim.</summary>
    public bool RespectSafety { get; set; }

    /// <summary>
    /// Content fingerprints (see <c>ConversationState.Fingerprint</c>) of turns that were already
    /// compacted in a previous pass. Such turns are kept as-is and never re-summarized (still
    /// droppable under a budget). Used by <see cref="CavemanContextWindow"/> for incremental compaction.
    /// </summary>
    public HashSet<string>? VerbatimContentHashes { get; set; }

    // ---- Markdown / JSON / HTML extraction ----

    /// <summary>Keep JSON content (fenced and inline) instead of stripping it.</summary>
    public bool KeepJson { get; set; }

    /// <summary>Keep fenced code blocks verbatim instead of stripping them (useful for coding chats).</summary>
    public bool KeepCode { get; set; }

    /// <summary>Extract the inner text from HTML (default); when false HTML is left untouched.</summary>
    public bool ExtractHtml { get; set; } = true;

    // ---- Extra compression of the kept text ----

    /// <summary>After summarizing, also run caveman compression (stop-word/lemma) on the kept text.</summary>
    public bool CompressKeptText { get; set; }

    /// <summary>Compression level used when <see cref="CompressKeptText"/> is enabled.</summary>
    public CavemanCompressionLevel CompressionLevel { get; set; } = CavemanCompressionLevel.Semantic;

    // ---- Token budget ----

    /// <summary>Hard token budget; when &gt; 0 the result is tightened (shrink, compress, drop oldest) to fit.</summary>
    public int MaxTokens { get; set; }

    /// <summary>Model used for token counting (metrics and budget).</summary>
    public LlmModel TokenModel { get; set; } = LlmModel.Gpt4;

    // ---- Cost estimate (USD + EUR) ----

    /// <summary>Input price in USD per 1K tokens; when null, an indicative default for <see cref="TokenModel"/> is used.</summary>
    public decimal? UsdPer1KTokens { get; set; }

    /// <summary>USD→EUR conversion rate used for the EUR cost estimate.</summary>
    public decimal UsdToEurRate { get; set; } = CavemanCostEstimator.DefaultUsdToEur;

    // ---- Observability ----

    /// <summary>When true, the result is populated with a per-block <c>Trace</c> explaining each action.</summary>
    public bool CollectTrace { get; set; }

    // ---- Presets (convenience factories) ----

    /// <summary>Conservative: parse roles, keep system/code verbatim, only summarize clearly long prose.</summary>
    public static ChatSummarizeOptions Faithful() => new()
    {
        ParseConversation = true,
        KeepSystemVerbatim = true,
        KeepCode = true,
        MinDiscourseWords = 80,
        SummaryRatio = 0.6f
    };

    /// <summary>Rolling agent memory: parse roles, keep recent turns &amp; system verbatim, dedup, fit a budget.</summary>
    public static ChatSummarizeOptions AgentMemory(int maxTokens, LlmModel model = LlmModel.Gpt4) => new()
    {
        ParseConversation = true,
        KeepLastTurnsVerbatim = 4,
        KeepSystemVerbatim = true,
        Deduplicate = true,
        MaxTokens = maxTokens,
        TokenModel = model
    };

    /// <summary>Coding chat: parse roles and keep fenced code blocks verbatim.</summary>
    public static ChatSummarizeOptions CodingChat() => new()
    {
        ParseConversation = true,
        KeepCode = true,
        KeepSystemVerbatim = true
    };

    /// <summary>Maximum savings: summarize hard and also caveman-compress the kept text.</summary>
    public static ChatSummarizeOptions Aggressive() => new()
    {
        ParseConversation = true,
        SummaryRatio = 0.25f,
        MaxSummarySentences = 4,
        CompressKeptText = true,
        CompressionLevel = CavemanCompressionLevel.Aggressive
    };
}
