// -----------------------------------------------------------------------------
// <copyright file="CavemanSharedContext.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Inter-agent compressed context store: compresses content on Put, serves compressed or original on Get, with TTL eviction.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Thread-safe store for sharing compressed context across multiple AI agents.
/// On <see cref="Put"/>, content is NLP-compressed and both the original and the
/// compressed copy are stored. On <see cref="Get"/>, callers receive the compressed
/// version by default (saving tokens) or the original when <c>full=true</c>.
/// Entries expire after <see cref="Ttl"/> (default 30 minutes) and are evicted lazily.
/// </summary>
public sealed class CavemanSharedContext
{
    private static readonly CavemanSharedContext _instance = new();
    /// <summary>Process-wide singleton.</summary>
    public static CavemanSharedContext Instance => _instance;

    private sealed record Entry(SharedContextEntry Data, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICompressionService _compression;
    private readonly ITokenCounter _tokenCounter;

    /// <summary>Entry lifetime. Defaults to 30 minutes.</summary>
    public TimeSpan Ttl { get; }

    /// <summary>Maximum entries retained at any time (oldest evicted first when exceeded).</summary>
    public int MaxEntries { get; }

    /// <param name="compression">Compressor used on Put. Defaults to a new <see cref="CavemanCompressionService"/>.</param>
    /// <param name="tokenCounter">Token counter. Defaults to a new <see cref="ModelTokenizer"/>.</param>
    /// <param name="ttl">Entry lifetime. Defaults to 30 minutes.</param>
    /// <param name="maxEntries">Capacity cap. Defaults to 500.</param>
    public CavemanSharedContext(
        ICompressionService? compression = null,
        ITokenCounter? tokenCounter = null,
        TimeSpan? ttl = null,
        int maxEntries = 500)
    {
        _compression = compression ?? new CavemanCompressionService();
        _tokenCounter = tokenCounter ?? new ModelTokenizer();
        Ttl = ttl ?? TimeSpan.FromMinutes(30);
        MaxEntries = maxEntries;
    }

    /// <summary>
    /// Compresses <paramref name="content"/> and stores both original and compressed under <paramref name="key"/>.
    /// Returns a <see cref="SharedContextEntry"/> with token savings metadata.
    /// </summary>
    public SharedContextEntry Put(string key, string content, string? agentName = null)
    {
        Evict();
        EnforceCapacity();

        int tokensBefore = _tokenCounter.CountTokens(content, LlmModel.Gpt4);

        // Run Semantic compression synchronously
        var r = _compression.CompressAsync(content, CavemanCompressionLevel.Semantic)
                            .GetAwaiter().GetResult();

        int tokensAfter = _tokenCounter.CountTokens(r.CompressedText, LlmModel.Gpt4);

        var entry = new SharedContextEntry
        {
            Key = key,
            Original = content,
            Compressed = r.CompressedText,
            TokensBefore = tokensBefore,
            TokensAfter = tokensAfter,
            AgentName = agentName
        };

        _store[key] = new Entry(entry, DateTimeOffset.UtcNow + Ttl);
        return entry;
    }

    /// <summary>
    /// Retrieves the compressed content for <paramref name="key"/> (or the original when <paramref name="full"/> is true).
    /// Returns null when the key is absent or expired.
    /// </summary>
    public string? Get(string key, bool full = false)
    {
        if (!_store.TryGetValue(key, out var e)) return null;
        if (DateTimeOffset.UtcNow > e.ExpiresAt) { _store.TryRemove(key, out _); return null; }
        return full ? e.Data.Original : e.Data.Compressed;
    }

    /// <summary>Returns the full <see cref="SharedContextEntry"/> for <paramref name="key"/>, or null if absent/expired.</summary>
    public SharedContextEntry? GetEntry(string key)
    {
        if (!_store.TryGetValue(key, out var e)) return null;
        if (DateTimeOffset.UtcNow > e.ExpiresAt) { _store.TryRemove(key, out _); return null; }
        return e.Data;
    }

    /// <summary>Removes all expired entries.</summary>
    public void Evict()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _store)
            if (now > kv.Value.ExpiresAt)
                _store.TryRemove(kv.Key, out _);
    }

    /// <summary>Aggregate token savings across all non-expired entries.</summary>
    public (int Entries, int TotalTokensBefore, int TotalTokensAfter, int TotalSaved) Stats
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            int entries = 0, before = 0, after = 0;
            foreach (var kv in _store.Values)
            {
                if (now > kv.ExpiresAt) continue;
                entries++;
                before += kv.Data.TokensBefore;
                after += kv.Data.TokensAfter;
            }
            return (entries, before, after, before - after);
        }
    }

    private void EnforceCapacity()
    {
        if (_store.Count < MaxEntries) return;
        // Evict the single oldest entry to make room
        var oldest = _store.OrderBy(kv => kv.Value.ExpiresAt).FirstOrDefault();
        if (oldest.Key != null) _store.TryRemove(oldest.Key, out _);
    }
}
