// -----------------------------------------------------------------------------
// <copyright file="CavemanLogCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses build/test logs and stack traces by scoring lines, keeping errors and context, and deduplicating repeated warnings.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses build/test output and stack traces. Lines are scored by severity; errors,
/// stack frames and summaries are always kept; surrounding context lines are preserved;
/// repeated warnings are deduplicated. Non-log input is returned unchanged.
/// </summary>
public sealed class CavemanLogCompressor
{
    private static readonly Regex ErrorPattern = new(
        @"\b(error|exception|fatal|fail(?:ed|ure)?|critical|crash)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WarnPattern = new(
        @"\b(warn(?:ing)?|deprecated?|caution)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InfoPattern = new(
        @"\b(info(?:rmation)?|notice|note)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DebugPattern = new(
        @"\b(debug|trace|verbose)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Stack frame patterns across multiple languages
    private static readonly Regex StackFramePattern = new(
        @"^\s*at\s+\S|^\s*at\s+[A-Za-z]|" +         // C#/Java: "  at Foo.Bar()"
        @"File "".+"", line \d+|" +                    // Python
        @"^\s+\d+\s*\|\s+|" +                         // Rust compiler
        @"^\s*\d+:\s*(error|warning)\[|" +             // Rust lint
        @"goroutine \d+ \[",                           // Go
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SummaryPattern = new(
        @"\b(\d+\s+(error|warning|test|passed|failed|skipped)s?|build\s+(succeeded|failed)|tests?\s+run:)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Normalize noisy tails: digits → N, hex addresses → ADDR
    private static readonly Regex DigitNoise = new(@"\b\d{4,}\b", RegexOptions.Compiled);
    private static readonly Regex HexNoise = new(@"\b0x[0-9a-fA-F]{4,}\b", RegexOptions.Compiled);
    private static readonly Regex PathNoise = new(@"[A-Za-z]:\\[^\s,]+|/(?:[^\s/]+/){2,}[^\s]+", RegexOptions.Compiled);

    /// <summary>Maximum total output lines (default 80).</summary>
    public int MaxTotalLines { get; init; } = 80;
    /// <summary>Context lines kept around each error (default 2).</summary>
    public int ErrorContextLines { get; init; } = 2;
    /// <summary>Maximum error/exception lines to keep (default 10).</summary>
    public int MaxErrors { get; init; } = 10;
    /// <summary>Maximum warning lines to keep (default 5).</summary>
    public int MaxWarnings { get; init; } = 5;

    /// <summary>
    /// When true, a run of consecutive lines folds together if they are SimHash
    /// near-duplicates (see <see cref="CavemanSimHash"/>), not just exact matches after
    /// digit/hex/path normalisation. Off by default: normalisation-based exact matching is
    /// the safer, already-proven behaviour, and fuzzy folding can occasionally group two
    /// genuinely distinct lines that just happen to share most of their wording — opt in
    /// when your logs have variation exact normalisation doesn't cover (e.g. reordered
    /// key=value pairs, minor phrasing differences from a templated logger).
    /// </summary>
    public bool FuzzyFold { get; init; }

    /// <summary>
    /// Max Hamming distance (of 64 bits) for two lines to count as near-duplicates when
    /// <see cref="FuzzyFold"/> is on (default 18). Calibrated empirically on templated log
    /// lines ~10 words long: lines sharing the same template but substituting 1-2 values
    /// (a username, an IP address) land roughly 10-20 bits apart depending on how much the
    /// substituted value itself varies in length/shingles (e.g. "8.8.8.8" vs
    /// "192.168.1.5"), while genuinely unrelated lines land 30+ bits apart — there is a
    /// wide gap between the two clusters, but exactly where in that gap to draw the line
    /// depends on your log format. This default favours folding over missing matches;
    /// lower it if two truly distinct lines are ever grouped together.
    /// </summary>
    public int FuzzyFoldMaxDistance { get; init; } = 18;

    private enum LineLevel { Error, Warn, Info, Debug, StackFrame, Summary, Unknown }

    private sealed record ScoredLine(int Index, string Text, LineLevel Level, float Score);

    /// <summary>Minimum consecutive repeats of a structurally-identical line before folding it (default 3).</summary>
    public int MinFoldRun { get; init; } = 3;

    /// <summary>
    /// Compresses log/stack-trace content. Two independent passes:
    /// <list type="number">
    ///   <item>Fold consecutive structurally-identical lines (after normalising digits/hex/
    ///   paths) into one line plus a "(repeated Nx)" count — this wins on genuinely
    ///   repetitive logs (e.g. many identical passing-test lines) regardless of total log
    ///   length, unlike the pass below.</item>
    ///   <item>If the folded log still exceeds <see cref="MaxTotalLines"/>, fall back to the
    ///   severity-scored line selection (errors/summaries/warnings kept, context preserved).</item>
    /// </list>
    /// Returns the original unchanged when neither pass finds anything to fold or drop.
    /// </summary>
    public LogCompressionResult Compress(string content, string? query = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Unchanged(content ?? string.Empty);

        var lines = content.Split('\n');
        var folded = FoldRepeatedLines(lines);

        if (folded.Count <= MaxTotalLines)
        {
            if (folded.Count >= lines.Length)
                return Unchanged(content);

            return new LogCompressionResult
            {
                Compressed = string.Join("\n", folded),
                Original = content,
                WasCompressed = true,
                OriginalLines = lines.Length,
                KeptLines = folded.Count
            };
        }

        var foldedLines = folded.ToArray();
        var scored = ClassifyAndScore(foldedLines);
        var kept = SelectLines(scored, foldedLines.Length);
        kept = AddContext(scored, kept, foldedLines.Length);
        kept = DeduplicateWarnings(kept);

        if (kept.Count >= foldedLines.Length && folded.Count >= lines.Length)
            return Unchanged(content);

        var orderedIndexes = kept.Select(s => s.Index).Distinct().OrderBy(i => i).ToList();
        var sb = new StringBuilder();
        for (int i = 0; i < orderedIndexes.Count; i++)
        {
            if (i > 0 && orderedIndexes[i] > orderedIndexes[i - 1] + 1)
                sb.AppendLine("... [lines omitted] ...");
            sb.AppendLine(foldedLines[orderedIndexes[i]]);
        }

        var compressed = sb.ToString().TrimEnd();
        return new LogCompressionResult
        {
            Compressed = compressed,
            Original = content,
            WasCompressed = true,
            OriginalLines = lines.Length,
            KeptLines = orderedIndexes.Count
        };
    }

    // Collapses runs of MinFoldRun+ consecutive lines that are identical once digits, hex
    // addresses and paths are normalised to placeholders (the same normalisation already
    // used for warning dedup) — e.g. 40 lines of "PASS test_user_42 (12ms)" fold to one line
    // plus a repeat count instead of surviving verbatim just because the log is short.
    private List<string> FoldRepeatedLines(string[] lines)
    {
        var result = new List<string>(lines.Length);
        int i = 0;
        while (i < lines.Length)
        {
            int runEnd = i + 1;
            while (runEnd < lines.Length && SameFoldGroup(lines[i], lines[runEnd]))
                runEnd++;

            int runLength = runEnd - i;
            if (runLength >= MinFoldRun)
                result.Add($"{lines[i]}  (repeated {runLength}x)");
            else
                for (int k = i; k < runEnd; k++)
                    result.Add(lines[k]);

            i = runEnd;
        }
        return result;
    }

    // Two lines fold together when they're identical after digit/hex/path normalisation
    // (the safe, exact-match default) or — only when FuzzyFold is enabled — when their
    // SimHash fingerprints are close enough to count as near-duplicates. Both lines are
    // compared against the run's first line, not against each other pairwise, so a run
    // can't slowly "drift" into unrelated content one near-duplicate step at a time.
    private bool SameFoldGroup(string a, string b)
    {
        if (Normalize(a) == Normalize(b)) return true;
        return FuzzyFold && CavemanSimHash.AreNearDuplicates(a, b, FuzzyFoldMaxDistance);
    }

    private static string Normalize(string line)
    {
        var n = DigitNoise.Replace(line, "N");
        n = HexNoise.Replace(n, "ADDR");
        n = PathNoise.Replace(n, "/PATH");
        return n;
    }

    private List<ScoredLine> ClassifyAndScore(string[] lines)
    {
        var result = new List<ScoredLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            LineLevel level;
            float score;

            if (ErrorPattern.IsMatch(line)) { level = LineLevel.Error; score = 1.0f; }
            else if (StackFramePattern.IsMatch(line)) { level = LineLevel.StackFrame; score = 0.8f; }
            else if (SummaryPattern.IsMatch(line)) { level = LineLevel.Summary; score = 0.9f; }
            else if (WarnPattern.IsMatch(line)) { level = LineLevel.Warn; score = 0.5f; }
            else if (InfoPattern.IsMatch(line)) { level = LineLevel.Info; score = 0.1f; }
            else if (DebugPattern.IsMatch(line)) { level = LineLevel.Debug; score = 0.05f; }
            else { level = LineLevel.Unknown; score = 0.0f; }

            result.Add(new ScoredLine(i, line, level, score));
        }
        return result;
    }

    private List<ScoredLine> SelectLines(List<ScoredLine> scored, int total)
    {
        var kept = new HashSet<int>();
        var result = new List<ScoredLine>();

        // Always keep first and last line (anchors)
        kept.Add(0);
        kept.Add(total - 1);
        result.Add(scored[0]);
        if (total > 1) result.Add(scored[total - 1]);

        // Keep top errors
        var errors = scored.Where(s => s.Level == LineLevel.Error)
                           .OrderByDescending(s => s.Score)
                           .Take(MaxErrors);
        foreach (var e in errors) if (kept.Add(e.Index)) result.Add(e);

        // Keep summaries
        var summaries = scored.Where(s => s.Level == LineLevel.Summary);
        foreach (var s in summaries) if (kept.Add(s.Index)) result.Add(s);

        // Keep top warnings
        var warnings = scored.Where(s => s.Level == LineLevel.Warn)
                             .OrderByDescending(s => s.Score)
                             .Take(MaxWarnings);
        foreach (var w in warnings) if (kept.Add(w.Index)) result.Add(w);

        // Fill to MaxTotalLines with highest-scored remaining lines
        if (result.Count < MaxTotalLines)
        {
            var remaining = scored.Where(s => !kept.Contains(s.Index))
                                  .OrderByDescending(s => s.Score)
                                  .Take(MaxTotalLines - result.Count);
            foreach (var r in remaining) if (kept.Add(r.Index)) result.Add(r);
        }

        return result;
    }

    private List<ScoredLine> AddContext(List<ScoredLine> all, List<ScoredLine> kept, int total)
    {
        var keepIndexes = new HashSet<int>(kept.Select(s => s.Index));
        var contextToAdd = new HashSet<int>();

        foreach (var line in kept.Where(l => l.Level == LineLevel.Error || l.Level == LineLevel.StackFrame))
        {
            for (int offset = -ErrorContextLines; offset <= ErrorContextLines; offset++)
            {
                int idx = line.Index + offset;
                if (idx >= 0 && idx < total && !keepIndexes.Contains(idx))
                    contextToAdd.Add(idx);
            }
        }

        var result = new List<ScoredLine>(kept);
        foreach (var idx in contextToAdd)
            result.Add(all[idx]);
        return result;
    }

    private static List<ScoredLine> DeduplicateWarnings(List<ScoredLine> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScoredLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Level != LineLevel.Warn)
            {
                result.Add(line);
                continue;
            }
            // Normalize the tail: replace digits, hex addresses and paths with placeholders
            var normalized = DigitNoise.Replace(line.Text, "N");
            normalized = HexNoise.Replace(normalized, "ADDR");
            normalized = PathNoise.Replace(normalized, "/PATH");
            if (seen.Add(normalized))
                result.Add(line);
        }
        return result;
    }

    private static LogCompressionResult Unchanged(string content) =>
        new() { Compressed = content, Original = content, WasCompressed = false, OriginalLines = content.Split('\n').Length, KeptLines = content.Split('\n').Length };
}
