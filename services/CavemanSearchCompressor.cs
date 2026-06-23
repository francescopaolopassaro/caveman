// -----------------------------------------------------------------------------
// <copyright file="CavemanSearchCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses grep/ripgrep search results by grouping by file, scoring matches, and keeping first/last anchors plus top-scored hits.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses grep/ripgrep output (file:line:content format). Groups matches by file,
/// scores each match by query relevance and error-signal weight, then retains the
/// first/last match per file plus top-scored matches up to <see cref="MaxMatchesPerFile"/>.
/// Only the top <see cref="MaxFiles"/> files (by aggregate score) are kept.
/// </summary>
public sealed class CavemanSearchCompressor
{
    // Handles both Unix and Windows paths; line number is an unambiguous integer after the last colon before a digit sequence
    private static readonly Regex GrepLine = new(
        @"^(?<file>.+?):(?<line>\d+):(?<content>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ErrorSignal = new(
        @"\b(error|exception|fatal|fail(?:ed)?|critical|panic|assert)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WordSplit = new(@"\W+", RegexOptions.Compiled);

    /// <summary>Maximum matches to keep per file (default 5).</summary>
    public int MaxMatchesPerFile { get; init; } = 5;
    /// <summary>Maximum files to include in output (default 15).</summary>
    public int MaxFiles { get; init; } = 15;

    private sealed record Match(string File, int Line, string Content, float Score);

    /// <summary>Compresses grep/ripgrep output. Returns original when no structured matches are found.</summary>
    public SearchCompressionResult Compress(string content, string? query = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Unchanged(content ?? string.Empty);

        var matches = ParseMatches(content);
        if (matches.Count == 0)
            return Unchanged(content);

        var queryTerms = string.IsNullOrWhiteSpace(query)
            ? []
            : WordSplit.Split(query.ToLowerInvariant()).Where(t => t.Length > 2).ToArray();

        var scored = matches.Select(m => m with { Score = ScoreMatch(m.Content, queryTerms) }).ToList();

        var byFile = scored.GroupBy(m => m.File).ToDictionary(g => g.Key, g => g.ToList());

        // Per-file: keep first + last + top-scored
        var kept = new List<Match>();
        foreach (var (file, fileMatches) in byFile)
        {
            var selected = SelectPerFile(fileMatches);
            kept.AddRange(selected);
        }

        // Global: keep top MaxFiles by aggregate score
        var topFiles = kept.GroupBy(m => m.File)
                           .OrderByDescending(g => g.Sum(m => m.Score))
                           .Take(MaxFiles)
                           .Select(g => g.Key)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var final = kept.Where(m => topFiles.Contains(m.File))
                        .OrderBy(m => m.File)
                        .ThenBy(m => m.Line)
                        .ToList();

        if (final.Count >= matches.Count)
            return Unchanged(content);

        var sb = new StringBuilder();
        string? currentFile = null;
        foreach (var m in final)
        {
            if (m.File != currentFile)
            {
                if (currentFile != null) sb.AppendLine();
                sb.AppendLine($"# {m.File}");
                currentFile = m.File;
            }
            sb.AppendLine($"{m.Line}:{m.Content}");
        }

        return new SearchCompressionResult
        {
            Compressed = sb.ToString().TrimEnd(),
            Original = content,
            WasCompressed = true,
            OriginalMatches = matches.Count,
            KeptMatches = final.Count,
            FilesKept = topFiles.Count
        };
    }

    private static List<Match> ParseMatches(string content)
    {
        var result = new List<Match>();
        foreach (System.Text.RegularExpressions.Match m in GrepLine.Matches(content))
        {
            if (int.TryParse(m.Groups["line"].Value, out int lineNum))
                result.Add(new Match(m.Groups["file"].Value, lineNum, m.Groups["content"].Value, 0f));
        }
        return result;
    }

    private static float ScoreMatch(string content, string[] queryTerms)
    {
        float score = 0f;
        if (ErrorSignal.IsMatch(content)) score += 0.5f;
        if (queryTerms.Length > 0)
        {
            var lower = content.ToLowerInvariant();
            foreach (var term in queryTerms)
                if (lower.Contains(term)) score += 0.3f;
        }
        return Math.Min(score, 1.0f);
    }

    private List<Match> SelectPerFile(List<Match> fileMatches)
    {
        if (fileMatches.Count <= MaxMatchesPerFile)
            return fileMatches;

        var kept = new HashSet<int>();
        var result = new List<Match>();

        void Add(Match m, int idx) { if (kept.Add(idx)) result.Add(m); }

        // Anchors
        Add(fileMatches[0], 0);
        Add(fileMatches[^1], fileMatches.Count - 1);

        // Top-scored remaining
        var topScored = fileMatches
            .Select((m, i) => (m, i))
            .OrderByDescending(x => x.m.Score)
            .Take(MaxMatchesPerFile);

        foreach (var (m, i) in topScored) Add(m, i);

        return result.OrderBy(m => m.Line).ToList();
    }

    private static SearchCompressionResult Unchanged(string content) =>
        new() { Compressed = content, Original = content, WasCompressed = false, OriginalMatches = 0, KeptMatches = 0, FilesKept = 0 };
}
