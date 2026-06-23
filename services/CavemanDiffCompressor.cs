// -----------------------------------------------------------------------------
// <copyright file="CavemanDiffCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses unified git diffs by trimming context lines while preserving all additions and deletions.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses unified diff output. All <c>+</c> and <c>-</c> lines are kept; pure context lines
/// are trimmed to <see cref="MaxContextLines"/> per side. Respects <see cref="MaxHunksPerFile"/>
/// and <see cref="MaxFiles"/>. Pure-context hunks (no actual changes) are dropped.
/// </summary>
public sealed class CavemanDiffCompressor
{
    private static readonly Regex HunkHeader = new(@"^@@[^@]*@@", RegexOptions.Compiled);
    private static readonly Regex FileHeader = new(@"^diff --git\s+", RegexOptions.Compiled);
    private static readonly Regex IndexLine = new(@"^(index |--- |\+\+\+ )", RegexOptions.Compiled);

    /// <summary>Context lines kept on each side of a change (default 2).</summary>
    public int MaxContextLines { get; init; } = 2;
    /// <summary>Maximum hunks to keep per file (default 10).</summary>
    public int MaxHunksPerFile { get; init; } = 10;
    /// <summary>Maximum files to include (default 20).</summary>
    public int MaxFiles { get; init; } = 20;

    private enum LineKind { FileHeader, HunkHeader, Addition, Deletion, Context, Other }

    private sealed record DiffLine(string Text, LineKind Kind);

    /// <summary>Compresses a unified diff string. Returns the original when no compression is possible.</summary>
    public DiffCompressionResult Compress(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Unchanged(content ?? string.Empty);

        var rawLines = content.Split('\n');
        var classified = rawLines.Select(ClassifyLine).ToList();

        // Split into per-file blocks
        var files = SplitIntoFiles(classified);
        if (files.Count == 0)
            return Unchanged(content);

        int totalAdditions = 0, totalDeletions = 0, hunksKept = 0, hunksDropped = 0;
        var sb = new StringBuilder();
        int filesWritten = 0;

        foreach (var file in files)
        {
            if (filesWritten >= MaxFiles) break;

            var (fileHeader, hunks) = SplitHunks(file);
            bool anyOutput = false;

            // Write file header lines
            foreach (var line in fileHeader)
                sb.AppendLine(line.Text);

            int hunksWritten = 0;
            foreach (var hunk in hunks)
            {
                if (hunksWritten >= MaxHunksPerFile) { hunksDropped++; continue; }

                bool hasChanges = hunk.Any(l => l.Kind == LineKind.Addition || l.Kind == LineKind.Deletion);
                if (!hasChanges) { hunksDropped++; continue; }

                var trimmed = TrimContext(hunk);
                foreach (var line in trimmed)
                    sb.AppendLine(line.Text);

                totalAdditions += hunk.Count(l => l.Kind == LineKind.Addition);
                totalDeletions += hunk.Count(l => l.Kind == LineKind.Deletion);
                hunksKept++;
                hunksWritten++;
                anyOutput = true;
            }

            if (anyOutput) filesWritten++;
        }

        var compressed = sb.ToString().TrimEnd();
        if (compressed.Length >= content.Length)
            return Unchanged(content);

        return new DiffCompressionResult
        {
            Compressed = compressed,
            Original = content,
            WasCompressed = true,
            HunksKept = hunksKept,
            HunksDropped = hunksDropped,
            Additions = totalAdditions,
            Deletions = totalDeletions
        };
    }

    private List<DiffLine> TrimContext(List<DiffLine> hunk)
    {
        // Keep hunk header + MaxContextLines before/after each change, all changes
        var result = new List<DiffLine>();
        var changeIndexes = hunk
            .Select((l, i) => (l, i))
            .Where(x => x.l.Kind == LineKind.Addition || x.l.Kind == LineKind.Deletion)
            .Select(x => x.i)
            .ToList();

        var keepIndexes = new HashSet<int>();
        foreach (int ci in changeIndexes)
        {
            keepIndexes.Add(ci);
            for (int d = 1; d <= MaxContextLines; d++)
            {
                if (ci - d >= 0) keepIndexes.Add(ci - d);
                if (ci + d < hunk.Count) keepIndexes.Add(ci + d);
            }
        }

        for (int i = 0; i < hunk.Count; i++)
        {
            if (hunk[i].Kind == LineKind.HunkHeader || keepIndexes.Contains(i))
                result.Add(hunk[i]);
        }
        return result;
    }

    private static List<List<DiffLine>> SplitIntoFiles(List<DiffLine> lines)
    {
        var files = new List<List<DiffLine>>();
        List<DiffLine>? current = null;
        foreach (var line in lines)
        {
            if (line.Kind == LineKind.FileHeader)
            {
                current = [line];
                files.Add(current);
            }
            else current?.Add(line);
        }
        return files;
    }

    private static (List<DiffLine> Header, List<List<DiffLine>> Hunks) SplitHunks(List<DiffLine> file)
    {
        var header = new List<DiffLine>();
        var hunks = new List<List<DiffLine>>();
        List<DiffLine>? currentHunk = null;
        bool inHeader = true;

        foreach (var line in file)
        {
            if (line.Kind == LineKind.HunkHeader)
            {
                inHeader = false;
                currentHunk = [line];
                hunks.Add(currentHunk);
            }
            else if (inHeader) header.Add(line);
            else currentHunk?.Add(line);
        }
        return (header, hunks);
    }

    private static DiffLine ClassifyLine(string text)
    {
        if (FileHeader.IsMatch(text)) return new DiffLine(text, LineKind.FileHeader);
        if (HunkHeader.IsMatch(text)) return new DiffLine(text, LineKind.HunkHeader);
        if (IndexLine.IsMatch(text)) return new DiffLine(text, LineKind.FileHeader);
        if (text.StartsWith('+') && !text.StartsWith("+++")) return new DiffLine(text, LineKind.Addition);
        if (text.StartsWith('-') && !text.StartsWith("---")) return new DiffLine(text, LineKind.Deletion);
        if (text.StartsWith(' ')) return new DiffLine(text, LineKind.Context);
        return new DiffLine(text, LineKind.Other);
    }

    private static DiffCompressionResult Unchanged(string content) =>
        new() { Compressed = content, Original = content, WasCompressed = false };
}
