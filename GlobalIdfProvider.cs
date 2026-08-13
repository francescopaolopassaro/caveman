// -----------------------------------------------------------------------------
// <copyright file="GlobalIdfProvider.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Precomputed per-language global document-frequency tables (ported from Synthelion 1.2.5's GlobalIdfProvider) that ground TF-IDF word scoring in a real-world corpus instead of the single prompt at hand.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;

namespace caveman.core;

/// <summary>An immutable per-language document-frequency table: total corpus size plus a lemma → document-frequency map.</summary>
public sealed class GlobalIdfTable
{
    /// <summary>Number of documents in the reference corpus the table was built from.</summary>
    public int CorpusSize { get; init; }

    /// <summary>lemma (lower-case) → number of corpus documents that contain it.</summary>
    public IReadOnlyDictionary<string, int> DocumentFrequency { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Loads and caches per-language document-frequency tables from the embedded
/// <c>worddata/{iso3}.idf.br</c> resources (brotli-compressed text: first line is the
/// reference-corpus size, then one <c>lemma\t document_frequency</c> per line — the same
/// shipped format as Synthelion 1.2.5, same brotli + embedded-resource delivery as the
/// existing <c>*.yaml.br</c> worddata). Thread-safe and shared across instances; data is
/// read-only after load.
/// <para>
/// A language with no shipped table (or a corrupt/absent resource) simply reports
/// <see cref="HasData"/> == false and callers fall back to the local-only IDF computation,
/// so this is purely additive and never a required input.
/// </para>
/// </summary>
public sealed class CavemanGlobalIdfProvider
{
    private static readonly Assembly _assembly = typeof(CavemanGlobalIdfProvider).Assembly;
    private static readonly ConcurrentDictionary<string, Lazy<GlobalIdfTable?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the cached table for <paramref name="iso3"/>, or null when none is shipped.</summary>
    public GlobalIdfTable? GetTable(string iso3)
        => _cache.GetOrAdd(iso3, LoadTable).Value;

    /// <summary>True when a table is shipped for <paramref name="iso3"/> and loaded successfully.</summary>
    public bool HasData(string iso3) => GetTable(iso3) != null;

    /// <summary>Reference-corpus size for <paramref name="iso3"/>, or 0 when no table is available.</summary>
    public int GetCorpusSize(string iso3)
    {
        var table = GetTable(iso3);
        return table?.CorpusSize ?? 0;
    }

    /// <summary>Global document frequency of <paramref name="lemma"/> in <paramref name="iso3"/>'s reference corpus, or 0 when unknown/unavailable.</summary>
    public int GetDocumentFrequency(string lemma, string iso3)
    {
        var table = GetTable(iso3);
        if (table == null)
            return 0;
        return table.DocumentFrequency.TryGetValue(lemma, out var df) ? df : 0;
    }

    /// <summary>
    /// Warms the cache for <paramref name="iso3s"/> (or for every language that ships a
    /// table when null) ahead of the first real request. Decompressing/parsing the largest
    /// tables is a real but one-off cost; call this once at startup if you know which
    /// languages a long-running process will serve.
    /// </summary>
    public void Preload(IEnumerable<string>? iso3s = null)
    {
        foreach (var iso3 in iso3s ?? GetShippedLanguages())
            GetTable(iso3);
    }

    /// <summary>Enumerates the language codes with a shipped <c>.idf.br</c> embedded resource.</summary>
    public static IEnumerable<string> GetShippedLanguages()
    {
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            const string suffix = ".idf.br";
            int marker = name.LastIndexOf(suffix, StringComparison.Ordinal);
            if (marker < 0)
                continue;
            int codeDot = name.LastIndexOf('.', marker - 1);
            if (codeDot < 0 || marker - codeDot != 4)
                continue;
            yield return name.Substring(codeDot + 1, 3);
        }
    }

    private static Lazy<GlobalIdfTable?> LoadTable(string iso3)
    {
        return new Lazy<GlobalIdfTable?>(() =>
        {
            try
            {
                using var raw = _assembly.GetManifestResourceStream(
                    $"caveman.core.worddata.{iso3.ToLowerInvariant()}.idf.br");
                if (raw == null)
                    return null;

                using var br = new BrotliStream(raw, CompressionMode.Decompress);
                using var reader = new StreamReader(br);

                var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int corpusSize = 0;
                bool first = true;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                        continue;
                    if (first)
                    {
                        int.TryParse(line, out corpusSize);
                        first = false;
                        continue;
                    }
                    var idx = line.IndexOf('\t');
                    if (idx > 0 && int.TryParse(line.Substring(idx + 1), out var df))
                        dict[line.Substring(0, idx)] = df;
                }

                if (corpusSize <= 0 || dict.Count == 0)
                    return null;

                return new GlobalIdfTable { CorpusSize = corpusSize, DocumentFrequency = dict };
            }
            catch
            {
                return null;
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}