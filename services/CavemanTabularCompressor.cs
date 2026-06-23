// -----------------------------------------------------------------------------
// <copyright file="CavemanTabularCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses CSV and markdown tables by sampling representative rows and removing redundant/empty columns.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses CSV and markdown tables:
/// <list type="bullet">
///   <item>Drops columns that are empty or identical across all rows.</item>
///   <item>Keeps first/last rows as anchors plus top-scored rows by query relevance.</item>
///   <item>Reports how many rows were dropped.</item>
/// </list>
/// Input that is not recognisable as a table is returned unchanged.
/// </summary>
public sealed class CavemanTabularCompressor
{
    private static readonly Regex MdTableRow = new(@"^\|(.+)\|$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MdSeparator = new(@"^\|[-:| ]+\|$", RegexOptions.Compiled);
    private static readonly char[] CsvSep = [',', '\t', ';'];

    /// <summary>Maximum rows to keep in the output (default 30).</summary>
    public int MaxRows { get; init; } = 30;

    /// <summary>Minimum savings ratio required for compression to be applied (default 15%).</summary>
    public float MinSavingsRatio { get; init; } = 0.15f;

    public TabularCompressionResult Compress(string content, string? query = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Unchanged(content ?? string.Empty);

        var mdRows = MdTableRow.Matches(content);
        if (mdRows.Count >= 3)
            return CompressMarkdown(content, mdRows, query);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (IsCsv(lines))
            return CompressCsv(content, lines, query);

        return Unchanged(content);
    }

    // ── Markdown table ───────────────────────────────────────────────────────

    private TabularCompressionResult CompressMarkdown(string original, MatchCollection rows, string? query)
    {
        var dataRows = rows
            .Select(m => m.Groups[1].Value.Split('|').Select(c => c.Trim()).ToArray())
            .ToList();

        if (dataRows.Count < 2) return Unchanged(original);

        var header = dataRows[0];
        // Skip separator row(s)
        var data = dataRows.Skip(1).Where(r => !MdSeparator.IsMatch("|" + string.Join("|", r) + "|")).ToList();
        if (data.Count == 0) return Unchanged(original);

        var (keepCols, dropCols) = FindUselessColumns(header, data);
        if (keepCols.Count == header.Length && data.Count <= MaxRows)
            return Unchanged(original);

        var keptRows = SampleRows(data, query, MaxRows);

        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", keepCols.Select(i => header[i])) + " |");
        sb.AppendLine("| " + string.Join(" | ", keepCols.Select(_ => "---")) + " |");
        foreach (var row in keptRows)
            sb.AppendLine("| " + string.Join(" | ", keepCols.Select(i => i < row.Length ? row[i] : string.Empty)) + " |");

        var compressed = sb.ToString().TrimEnd();
        if (SavingsRatio(original, compressed) < MinSavingsRatio) return Unchanged(original);

        return new TabularCompressionResult
        {
            Compressed = compressed, Original = original, WasCompressed = true,
            OriginalRows = data.Count, KeptRows = keptRows.Count,
            DroppedColumns = dropCols.Count
        };
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    private TabularCompressionResult CompressCsv(string original, string[] lines, string? query)
    {
        char sep = DetectSeparator(lines[0]);
        var rows = lines.Select(l => l.Split(sep)).ToList();
        if (rows.Count < 2) return Unchanged(original);

        var header = rows[0];
        var data = rows.Skip(1).ToList();

        var (keepCols, dropCols) = FindUselessColumns(header, data);
        if (keepCols.Count == header.Length && data.Count <= MaxRows)
            return Unchanged(original);

        var keptRows = SampleRows(data, query, MaxRows);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, keepCols.Select(i => CsvEncode(i < header.Length ? header[i] : string.Empty, sep))));
        foreach (var row in keptRows)
            sb.AppendLine(string.Join(sep, keepCols.Select(i => CsvEncode(i < row.Length ? row[i] : string.Empty, sep))));

        var compressed = sb.ToString().TrimEnd();
        if (SavingsRatio(original, compressed) < MinSavingsRatio) return Unchanged(original);

        return new TabularCompressionResult
        {
            Compressed = compressed, Original = original, WasCompressed = true,
            OriginalRows = data.Count, KeptRows = keptRows.Count,
            DroppedColumns = dropCols.Count
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (List<int> Keep, List<int> Drop) FindUselessColumns(string[] header, List<string[]> data)
    {
        var keep = new List<int>();
        var drop = new List<int>();
        for (int c = 0; c < header.Length; c++)
        {
            var vals = data.Select(r => c < r.Length ? r[c].Trim() : string.Empty).ToList();
            bool allEmpty = vals.All(string.IsNullOrEmpty);
            bool allSame = vals.Distinct(StringComparer.Ordinal).Count() <= 1;
            if (allEmpty || (allSame && data.Count > 1)) drop.Add(c);
            else keep.Add(c);
        }
        if (keep.Count == 0) keep.AddRange(Enumerable.Range(0, header.Length));
        return (keep, drop);
    }

    private static List<string[]> SampleRows(List<string[]> data, string? query, int max)
    {
        if (data.Count <= max) return data;

        var kept = new HashSet<int>();
        int firstK = (int)Math.Ceiling(data.Count * 0.25);
        int lastK = (int)Math.Ceiling(data.Count * 0.10);
        for (int i = 0; i < firstK && i < data.Count; i++) kept.Add(i);
        for (int i = Math.Max(0, data.Count - lastK); i < data.Count; i++) kept.Add(i);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var scored = data.Select((r, i) => (i, score: terms.Sum(t => string.Join(" ", r).ToLowerInvariant().Contains(t) ? 1 : 0)))
                             .OrderByDescending(x => x.score);
            foreach (var (i, _) in scored)
            {
                if (kept.Count >= max) break;
                kept.Add(i);
            }
        }

        while (kept.Count < max && kept.Count < data.Count)
            kept.Add(Enumerable.Range(0, data.Count).First(i => !kept.Contains(i)));

        return kept.OrderBy(i => i).Select(i => data[i]).ToList();
    }

    private static bool IsCsv(string[] lines)
    {
        if (lines.Length < 2) return false;
        char sep = DetectSeparator(lines[0]);
        if (sep == '\0') return false;
        int cols = lines[0].Split(sep).Length;
        return cols >= 2 && lines.Take(5).All(l => l.Split(sep).Length == cols);
    }

    private static char DetectSeparator(string line)
    {
        foreach (var c in CsvSep)
            if (line.Contains(c)) return c;
        return '\0';
    }

    private static string CsvEncode(string v, char sep)
    {
        if (v.Contains(sep) || v.Contains('"') || v.Contains('\n'))
            return '"' + v.Replace("\"", "\"\"") + '"';
        return v;
    }

    private static float SavingsRatio(string a, string b) =>
        a.Length == 0 ? 0f : 1f - (float)b.Length / a.Length;

    private static TabularCompressionResult Unchanged(string c) =>
        new() { Compressed = c, Original = c, WasCompressed = false };
}
