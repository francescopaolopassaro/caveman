// -----------------------------------------------------------------------------
// <copyright file="CavemanJsonCrusher.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses JSON arrays via lossless CSV/markdown-table compaction or lossy BM25 row-drop with CCR markers.</summary>
// -----------------------------------------------------------------------------
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses a JSON array string using one of three strategies:
/// <list type="bullet">
///   <item>Lossless markdown-table compaction (≤6 keys, ≤50 rows, ≥15% savings)</item>
///   <item>Lossless CSV compaction (≥15% savings)</item>
///   <item>Lossy BM25 row-drop with a CCR marker for dropped rows</item>
/// </list>
/// Stateless; safe to use as a singleton or <c>new()</c> per call.
/// </summary>
public sealed class CavemanJsonCrusher
{
    private const float LosslessMinSavingsRatio = 0.15f;
    private const float FirstFraction = 0.30f;
    private const float LastFraction = 0.15f;

    /// <summary>Maximum number of rows to keep in lossy mode (default 15).</summary>
    public int MaxOutputItems { get; init; } = 15;

    private readonly CavemanCcrStore _ccrStore;

    /// <param name="ccrStore">Store for dropped rows. Defaults to <see cref="CavemanCcrStore.Instance"/>.</param>
    public CavemanJsonCrusher(CavemanCcrStore? ccrStore = null)
        => _ccrStore = ccrStore ?? CavemanCcrStore.Instance;

    /// <summary>Compresses a JSON array string. Non-array or unparseable input is returned unchanged.</summary>
    public JsonCrushResult Crush(string jsonArrayInput, string? query = null)
    {
        if (string.IsNullOrWhiteSpace(jsonArrayInput))
            return Unchanged(jsonArrayInput ?? string.Empty);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonArrayInput); }
        catch (JsonException) { return Unchanged(jsonArrayInput); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return Unchanged(jsonArrayInput);

            var rows = root.EnumerateArray().ToList();
            if (rows.Count == 0)
                return Unchanged(jsonArrayInput);

            var (isUniform, schema) = AnalyzeUniformity(rows);

            if (isUniform && schema.Length > 0)
            {
                // Try markdown table (≤6 keys, ≤50 rows)
                if (schema.Length <= 6 && rows.Count <= 50)
                {
                    var mdResult = RenderMarkdownTable(rows, schema);
                    if (SavingsRatio(jsonArrayInput, mdResult) >= LosslessMinSavingsRatio)
                        return new JsonCrushResult
                        {
                            Compressed = mdResult,
                            Original = jsonArrayInput,
                            WasCrushed = true,
                            Strategy = JsonCrushStrategy.MarkdownTable,
                            OriginalRows = rows.Count,
                            KeptRows = rows.Count
                        };
                }

                // Try CSV
                var csvResult = RenderCsv(rows, schema);
                if (SavingsRatio(jsonArrayInput, csvResult) >= LosslessMinSavingsRatio)
                    return new JsonCrushResult
                    {
                        Compressed = csvResult,
                        Original = jsonArrayInput,
                        WasCrushed = true,
                        Strategy = JsonCrushStrategy.Csv,
                        OriginalRows = rows.Count,
                        KeptRows = rows.Count
                    };
            }

            // Lossy row-drop
            return ApplyLossyRowDrop(jsonArrayInput, rows, query);
        }
    }

    // -------------------------------------------------------------------------
    // Uniformity analysis
    // -------------------------------------------------------------------------

    private static (bool isUniform, string[] schema) AnalyzeUniformity(List<JsonElement> rows)
    {
        if (!rows.All(r => r.ValueKind == JsonValueKind.Object))
            return (false, []);

        var keyUnion = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var prop in row.EnumerateObject())
                keyUnion.Add(prop.Name);

        if (keyUnion.Count > 20)
            return (false, []);

        var schema = keyUnion.Order().ToArray();
        int fullRows = rows.Count(r => schema.All(k => r.TryGetProperty(k, out _)));
        bool uniform = fullRows >= rows.Count * 0.80;
        return (uniform, schema);
    }

    // -------------------------------------------------------------------------
    // Lossless renderers
    // -------------------------------------------------------------------------

    private static string RenderMarkdownTable(List<JsonElement> rows, string[] schema)
    {
        var sb = new StringBuilder();
        sb.Append('|');
        foreach (var key in schema) { sb.Append(' '); sb.Append(key); sb.Append(" |"); }
        sb.AppendLine();
        sb.Append('|');
        foreach (var _ in schema) sb.Append("---|");
        sb.AppendLine();
        foreach (var row in rows)
        {
            sb.Append('|');
            foreach (var key in schema)
            {
                sb.Append(' ');
                sb.Append(GetValueString(row, key).Replace("|", "\\|"));
                sb.Append(" |");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderCsv(List<JsonElement> rows, string[] schema)
    {
        var sb = new StringBuilder();
        sb.Append("#schema: ");
        sb.AppendLine(string.Join(",", schema));
        foreach (var row in rows)
        {
            var values = schema.Select(k => CsvEncode(GetValueString(row, k)));
            sb.AppendLine(string.Join(",", values));
        }
        return sb.ToString().TrimEnd();
    }

    private static string GetValueString(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var prop)) return string.Empty;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : prop.ToString();
    }

    private static string CsvEncode(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // -------------------------------------------------------------------------
    // Lossy row-drop
    // -------------------------------------------------------------------------

    private JsonCrushResult ApplyLossyRowDrop(string original, List<JsonElement> rows, string? query)
    {
        int n = rows.Count;
        int firstK = (int)Math.Ceiling(n * FirstFraction);
        int lastK = (int)Math.Ceiling(n * LastFraction);

        var keepSet = new HashSet<int>();
        for (int i = 0; i < firstK && i < n; i++) keepSet.Add(i);
        for (int i = Math.Max(0, n - lastK); i < n; i++) keepSet.Add(i);

        // Anomaly detection: rows with unique keys
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (row.ValueKind == JsonValueKind.Object)
                foreach (var prop in row.EnumerateObject())
                    keyCounts[prop.Name] = keyCounts.GetValueOrDefault(prop.Name) + 1;

        for (int i = 0; i < n; i++)
        {
            if (rows[i].ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in rows[i].EnumerateObject())
                if (keyCounts.GetValueOrDefault(prop.Name) == 1) { keepSet.Add(i); break; }
        }

        // BM25 scoring for remaining rows
        var tokenizedDocs = rows.Select(r => TokenizeJson(r)).ToList();
        var queryTerms = string.IsNullOrWhiteSpace(query)
            ? []
            : TokenizeText(query);

        var scores = ComputeBm25Scores(tokenizedDocs, queryTerms);

        // Fill up to MaxOutputItems from remaining rows by score
        var remaining = Enumerable.Range(0, n)
            .Where(i => !keepSet.Contains(i))
            .OrderByDescending(i => scores[i])
            .ToList();

        foreach (var i in remaining)
        {
            if (keepSet.Count >= MaxOutputItems) break;
            keepSet.Add(i);
        }

        var keptIndexes = keepSet.OrderBy(i => i).ToList();
        var droppedIndexes = Enumerable.Range(0, n).Where(i => !keepSet.Contains(i)).ToList();

        if (droppedIndexes.Count == 0)
            return Unchanged(original);

        // Serialize kept rows
        var keptRows = keptIndexes.Select(i => rows[i]).ToList();
        var compressedJson = SerializeRows(keptRows);

        // Serialize dropped rows → CCR
        var droppedRows = droppedIndexes.Select(i => rows[i]).ToList();
        var droppedJson = SerializeRows(droppedRows);
        var ccrHash = ComputeCcrHash(droppedJson);
        _ccrStore.Store(ccrHash, droppedJson);

        var compressed = compressedJson + $"\n<<ccr:{ccrHash},dropped={droppedIndexes.Count}/{n}>>";

        return new JsonCrushResult
        {
            Compressed = compressed,
            Original = original,
            WasCrushed = true,
            Strategy = JsonCrushStrategy.LossyRowDrop,
            CcrHash = ccrHash,
            OriginalRows = n,
            KeptRows = keptIndexes.Count
        };
    }

    // -------------------------------------------------------------------------
    // BM25
    // -------------------------------------------------------------------------

    private static float[] ComputeBm25Scores(List<string[]> tokenizedDocs, string[] queryTerms)
    {
        int n = tokenizedDocs.Count;
        var scores = new float[n];
        if (n == 0 || queryTerms.Length == 0) return scores;

        const float k1 = 1.5f, b = 0.75f;

        // Build per-doc term frequency dictionaries
        var termFreqs = tokenizedDocs.Select(doc =>
        {
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in doc) tf[t] = tf.GetValueOrDefault(t) + 1;
            return tf;
        }).ToList();

        // Document frequency per query term
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in queryTerms.Distinct(StringComparer.OrdinalIgnoreCase))
            df[term] = termFreqs.Count(tf => tf.ContainsKey(term));

        float avgdl = tokenizedDocs.Count > 0
            ? (float)tokenizedDocs.Sum(d => d.Length) / n
            : 1f;

        for (int i = 0; i < n; i++)
        {
            float docLen = tokenizedDocs[i].Length;
            foreach (var term in queryTerms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!termFreqs[i].TryGetValue(term, out int tfRaw) || tfRaw == 0) continue;
                int dfVal = df.GetValueOrDefault(term, 0);
                float idf = MathF.Log((n - dfVal + 0.5f) / (dfVal + 0.5f) + 1f);
                float tf = tfRaw * (k1 + 1) / (tfRaw + k1 * (1 - b + b * docLen / avgdl));
                scores[i] += idf * tf;
            }
        }
        return scores;
    }

    private static readonly Regex TokenSplit = new(@"\W+", RegexOptions.Compiled);

    private static string[] TokenizeJson(JsonElement element)
        => TokenizeText(element.ToString());

    private static string[] TokenizeText(string text)
        => TokenSplit.Split(text.ToLowerInvariant())
                     .Where(t => t.Length >= 2)
                     .ToArray();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string SerializeRows(List<JsonElement> rows)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        return JsonSerializer.Serialize(rows.Select(r => JsonSerializer.Deserialize<JsonElement>(r.GetRawText())), opts);
    }

    private static float SavingsRatio(string original, string compressed)
        => original.Length == 0 ? 0f : 1f - (float)compressed.Length / original.Length;

    private static JsonCrushResult Unchanged(string input) =>
        new() { Compressed = input, Original = input, WasCrushed = false, Strategy = JsonCrushStrategy.None, OriginalRows = 0, KeptRows = 0 };

    private static string ComputeCcrHash(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
