// -----------------------------------------------------------------------------
// <copyright file="CavemanCcrStore.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Thread-safe in-memory store for CCR (Cache-Compress-Retrieve) dropped content with TTL eviction.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;

namespace caveman.core.services;

/// <summary>
/// Thread-safe in-memory store for content dropped by <see cref="CavemanJsonCrusher"/> during lossy row-drop.
/// Entries expire after 5 minutes. Use <see cref="Instance"/> for the process-wide singleton or
/// construct a new instance directly for isolated testing.
/// </summary>
public sealed class CavemanCcrStore
{
    private static readonly CavemanCcrStore _instance = new();
    /// <summary>Process-wide singleton.</summary>
    public static CavemanCcrStore Instance => _instance;

    private sealed record Entry(string Json, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;

    /// <param name="ttl">Override TTL (useful in tests). Defaults to 5 minutes.</param>
    public CavemanCcrStore(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromMinutes(5);

    /// <summary>Stores <paramref name="originalJson"/> under <paramref name="hash"/>, evicting stale entries first.</summary>
    public void Store(string hash, string originalJson)
    {
        Evict();
        _store[hash] = new Entry(originalJson, DateTimeOffset.UtcNow + _ttl);
    }

    /// <summary>Retrieves the original JSON for <paramref name="hash"/>, or null if missing or expired.</summary>
    public string? Retrieve(string hash)
    {
        if (!_store.TryGetValue(hash, out var entry))
            return null;
        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
        {
            _store.TryRemove(hash, out _);
            return null;
        }
        return entry.Json;
    }

    /// <summary>Removes all expired entries. Called automatically by <see cref="Store"/>.</summary>
    public void Evict()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _store)
            if (now > kv.Value.ExpiresAt)
                _store.TryRemove(kv.Key, out _);
    }
}
