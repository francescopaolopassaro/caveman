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

    private enum LineLevel { Error, Warn, Info, Debug, StackFrame, Summary, Unknown }

    private sealed record ScoredLine(int Index, string Text, LineLevel Level, float Score);

    /// <summary>Compresses log/stack-trace content. Returns the original when the content is too short to benefit.</summary>
    public LogCompressionResult Compress(string content, string? query = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Unchanged(content ?? string.Empty);

        var lines = content.Split('\n');
        if (lines.Length <= MaxTotalLines)
            return Unchanged(content);

        var scored = ClassifyAndScore(lines);
        var kept = SelectLines(scored, lines.Length);
        kept = AddContext(scored, kept, lines.Length);
        kept = DeduplicateWarnings(kept);

        if (kept.Count >= lines.Length)
            return Unchanged(content);

        var orderedIndexes = kept.Select(s => s.Index).Distinct().OrderBy(i => i).ToList();
        var sb = new StringBuilder();
        for (int i = 0; i < orderedIndexes.Count; i++)
        {
            if (i > 0 && orderedIndexes[i] > orderedIndexes[i - 1] + 1)
                sb.AppendLine("... [lines omitted] ...");
            sb.AppendLine(lines[orderedIndexes[i]]);
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
