// -----------------------------------------------------------------------------
// <copyright file="CavemanCompressionCache.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Two-tier compression cache: fast skip-set for non-compressible content + TTL result cache for compressed outputs.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Two-tier cache for <see cref="CavemanContentRouter"/>:
/// <list type="bullet">
///   <item>Tier 1 (skip set) — O(1) hash check for content known to not compress; avoids re-running expensive compressors.</item>
///   <item>Tier 2 (result cache) — stores compressed output + strategy + ratio with TTL-based lazy eviction.</item>
/// </list>
/// Thread-safe; entries expire lazily on access (no background GC thread).
/// </summary>
public sealed class CavemanCompressionCache
{
    private readonly TimeSpan _ttl;

    private sealed record ResultEntry(string Compressed, double Ratio, string Strategy, DateTimeOffset ExpiresAt);
    private sealed record SkipEntry(DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<int, ResultEntry> _results = new();
    private readonly ConcurrentDictionary<int, SkipEntry> _skip = new();

    private int _hits;
    private int _misses;
    private int _skipHits;
    private int _evictions;

    /// <param name="ttl">Cache entry lifetime. Defaults to 30 minutes.</param>
    public CavemanCompressionCache(TimeSpan? ttl = null)
        => _ttl = ttl ?? TimeSpan.FromMinutes(30);

    /// <summary>Looks up a cached result. Returns false (and null) on cache miss or expired entry.</summary>
    public bool TryGetResult(string content, out CachedCompressionResult? result)
    {
        int key = content.GetHashCode();
        if (_results.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                Interlocked.Increment(ref _hits);
                result = new CachedCompressionResult { Compressed = entry.Compressed, Ratio = entry.Ratio, Strategy = entry.Strategy };
                return true;
            }
            _results.TryRemove(key, out _);
            Interlocked.Increment(ref _evictions);
        }
        Interlocked.Increment(ref _misses);
        result = null;
        return false;
    }

    /// <summary>Returns true when the content is known to not compress (Tier 1 skip set check).</summary>
    public bool IsSkipped(string content)
    {
        int key = content.GetHashCode();
        if (_skip.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                Interlocked.Increment(ref _skipHits);
                return true;
            }
            _skip.TryRemove(key, out _);
            Interlocked.Increment(ref _evictions);
        }
        return false;
    }

    /// <summary>Stores a compressed result in Tier 2.</summary>
    public void PutResult(string content, string compressed, double ratio, string strategy)
    {
        int key = content.GetHashCode();
        _results[key] = new ResultEntry(compressed, ratio, strategy, DateTimeOffset.UtcNow + _ttl);
    }

    /// <summary>Marks content as non-compressible in Tier 1 (skip set).</summary>
    public void MarkSkip(string content)
    {
        int key = content.GetHashCode();
        _skip[key] = new SkipEntry(DateTimeOffset.UtcNow + _ttl);
    }

    /// <summary>Cache statistics snapshot.</summary>
    public (int Hits, int Misses, int SkipHits, int Evictions, int ResultSize, int SkipSize) Stats =>
        (_hits, _misses, _skipHits, _evictions, _results.Count, _skip.Count);
}
