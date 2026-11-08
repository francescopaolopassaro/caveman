// -----------------------------------------------------------------------------
// <copyright file="CavemanSemanticCache.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Algorithmic (no embeddings) multilingual semantic cache: stop-word/lemma normalization + Jaccard similarity.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Cache that answers "almost the same" queries from a previous answer, so coding agents don't pay
/// again for paraphrases. Queries are normalized with Caveman's own worddata (function words and
/// lemmas of every supported language) and compared with the Jaccard index of their token sets.
/// <para>
/// Safety for coding agents: numbers, paths, identifiers and quoted strings are "protected" tokens
/// that must be identical for a hit ("delete a.cs" never matches "delete b.cs"). JSON, code and
/// Markdown inside a query are tokenized, not discarded. Thread-safe.
/// </para>
/// </summary>
public sealed class CavemanSemanticCache
{
    private static readonly Regex ProtectedPattern = new(
        "\"[^\"\\r\\n]{1,200}\"|`[^`\\r\\n]{1,200}`" +      // double-quoted / backtick strings
        @"|[\w\-]+(?:[./\\:][\w\-]+)+" +                     // paths, file names, dotted ids, times
        @"|\b\w*[a-z][A-Z]\w*\b|\b\w+_\w+\b" +               // camelCase / snake_case
        @"|\d[\d.,]*",                                       // numbers
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(@"[\p{L}\p{M}\p{N}]+", RegexOptions.Compiled);

    private readonly CavemanSemanticCacheOptions _options;
    private readonly FunctionWordProvider _provider;
    private readonly ILanguageDetector _detector;
    private readonly TimeProvider _clock;
    private readonly List<Entry> _entries = new();
    private readonly object _lock = new();
    private long _hits, _misses, _charsSaved;

    /// <summary>Creates a cache; every argument is optional.</summary>
    public CavemanSemanticCache(
        CavemanSemanticCacheOptions? options = null,
        FunctionWordProvider? provider = null,
        ILanguageDetector? detector = null)
    {
        _options = options ?? new CavemanSemanticCacheOptions();
        _provider = provider ?? new FunctionWordProvider();
        _detector = detector ?? new CavemanLanguageDetector(_provider);
        _clock = _options.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>Number of live entries.</summary>
    public int Count { get { lock (_lock) { PurgeExpired(); return _entries.Count; } } }

    /// <summary>Hit/miss counters.</summary>
    public CavemanSemanticCacheStats Stats
    {
        get { lock (_lock) return new CavemanSemanticCacheStats(_hits, _misses, _charsSaved); }
    }

    /// <summary>Removes every entry (counters are kept).</summary>
    public void Clear() { lock (_lock) _entries.Clear(); }

    /// <summary>Stores <paramref name="response"/> under <paramref name="query"/>. Empty queries are ignored.</summary>
    public void Set(string query, string response)
    {
        if (string.IsNullOrWhiteSpace(query) || response is null) return;
        var n = Normalize(query);
        if (n.Tokens.Count == 0) return;
        lock (_lock)
        {
            PurgeExpired();
            var now = _clock.GetUtcNow();
            // Same normalized query → overwrite instead of duplicating.
            var existing = _entries.Find(e => e.Language == n.Language && e.Tokens.SetEquals(n.Tokens));
            if (existing != null) _entries.Remove(existing);
            _entries.Add(new Entry(query, response, n.Language, n.Tokens, n.Protected, now, now));
            while (_entries.Count > Math.Max(1, _options.MaxEntries))
            {
                var lru = _entries[0];
                foreach (var e in _entries) if (e.LastUsed < lru.LastUsed) lru = e;
                _entries.Remove(lru);
            }
        }
    }

    /// <summary>Returns the best cached answer whose similarity reaches the threshold, or null.</summary>
    public CavemanSemanticCacheHit? Get(string query) => TryGet(query, out var hit) ? hit : null;

    /// <summary>Looks <paramref name="query"/> up; true and a populated <paramref name="hit"/> on a cache hit.</summary>
    public bool TryGet(string query, out CavemanSemanticCacheHit hit)
    {
        hit = null!;
        if (string.IsNullOrWhiteSpace(query)) { lock (_lock) _misses++; return false; }
        lock (_lock)
        {
            PurgeExpired();
            // Short paraphrases are easily detected as a different language than the stored original,
            // so the query is also normalized in the language of every cached entry (a handful at most).
            var detected = ResolveLanguage(query);
            var languages = new List<string> { detected };
            if (string.IsNullOrWhiteSpace(_options.Language))
                foreach (var e in _entries) if (!languages.Contains(e.Language)) languages.Add(e.Language);

            Entry? best = null;
            double bestSim = 0;
            foreach (var lang in languages)
            {
                var n = Normalize(query, lang);
                if (n.Tokens.Count == 0) continue;
                foreach (var e in _entries)
                {
                    if (e.Language != lang || !e.Protected.SetEquals(n.Protected)) continue;
                    var sim = Jaccard(n.Tokens, e.Tokens);
                    if (sim > bestSim) { bestSim = sim; best = e; }
                }
            }
            if (best is null || bestSim < _options.SimilarityThreshold) { _misses++; return false; }
            best.LastUsed = _clock.GetUtcNow();
            _hits++;
            _charsSaved += best.Response.Length;
            hit = new CavemanSemanticCacheHit(best.Response, bestSim, best.Query, best.Language);
            return true;
        }
    }

    /// <summary>Jaccard index (intersection / union) of two token sets; 0 when either is empty.</summary>
    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int inter = 0;
        foreach (var t in a) if (b.Contains(t)) inter++;
        int union = a.Count + b.Count - inter;
        return (double)inter / union;
    }

    private void PurgeExpired()
    {
        if (_options.Ttl is not { } ttl) return;
        var limit = _clock.GetUtcNow() - ttl;
        _entries.RemoveAll(e => e.Created < limit);
    }

    private sealed record Normalized(string Language, HashSet<string> Tokens, HashSet<string> Protected);

    private Normalized Normalize(string text, string? language = null)
    {
        var iso3 = language ?? ResolveLanguage(text);
        var function = _provider.GetFunctionWords(iso3);
        IReadOnlyDictionary<string, string>? lemmas = _options.UseLemmas ? _provider.GetLemmaMap(iso3) : null;

        // 1. Protected tokens (numbers, paths, identifiers, quoted strings) are lifted out verbatim.
        var prot = new HashSet<string>(StringComparer.Ordinal);
        var rest = ProtectedPattern.Replace(text, m =>
        {
            prot.Add(m.Value.ToLowerInvariant().Trim('"', '`'));
            return " ";
        });

        // 2. Remaining text → plain tokens (stop words dropped, lemmatized).
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WordPattern.Matches(rest.ToLowerInvariant()))
            foreach (var piece in SplitScripts(m.Value))
            {
                if (piece.Length < _options.MinTokenLength && !IsUnspacedScript(piece[0])) continue;
                if (function.Contains(piece)) continue;
                if (lemmas != null && lemmas.TryGetValue(piece, out var lemma) && !string.IsNullOrEmpty(lemma)) tokens.Add(lemma);
                else tokens.Add(piece);
            }

        // Protected tokens also take part in the similarity so that queries made only of ids still compare.
        foreach (var p in prot) tokens.Add("\u0001" + p);
        return new Normalized(iso3, tokens, prot);
    }

    private string ResolveLanguage(string text)
    {
        if (!string.IsNullOrWhiteSpace(_options.Language))
        {
            var forced = _options.Language!.Trim().ToLowerInvariant();
            return forced.Length == 3 ? forced : LanguageMapper.GetIso3(forced);
        }
        try
        {
            var d = _detector.Detect(text);
            return string.IsNullOrWhiteSpace(d) ? "eng" : d;
        }
        catch { return "eng"; }
    }

    private static bool IsUnspacedScript(char c) =>
        CavemanCjkSegmenter.IsHan(c) || (c >= '぀' && c <= 'ヿ') || (c >= '฀' && c <= '๿');

    /// <summary>Splits a letter run into words; unspaced scripts (Han via the Caveman segmenter, kana/Thai as bigrams).</summary>
    private IEnumerable<string> SplitScripts(string run)
    {
        if (!run.Any(IsUnspacedScript)) return new[] { run };

        var parts = new List<string>();
        var sb = new StringBuilder();
        char kind = ' ';
        void Flush()
        {
            if (sb.Length == 0) return;
            var s = sb.ToString();
            sb.Clear();
            if (kind == 'H') parts.AddRange(CavemanCjkSegmenter.SegmentHanRun(s, _provider));
            else if (kind == 'K')
            {
                if (s.Length == 1) parts.Add(s);
                else for (int i = 0; i < s.Length - 1; i++) parts.Add(s.Substring(i, 2));
            }
            else parts.Add(s);
        }
        foreach (var c in run)
        {
            char k = CavemanCjkSegmenter.IsHan(c) ? 'H' : IsUnspacedScript(c) ? 'K' : 'L';
            if (kind != k) { Flush(); kind = k; }
            sb.Append(c);
        }
        Flush();
        return parts;
    }

    private sealed class Entry
    {
        public Entry(string query, string response, string language, HashSet<string> tokens, HashSet<string> prot, DateTimeOffset created, DateTimeOffset lastUsed)
        { Query = query; Response = response; Language = language; Tokens = tokens; Protected = prot; Created = created; LastUsed = lastUsed; }
        public string Query { get; }
        public string Response { get; }
        public string Language { get; }
        public HashSet<string> Tokens { get; }
        public HashSet<string> Protected { get; }
        public DateTimeOffset Created { get; }
        public DateTimeOffset LastUsed { get; set; }
    }
}
