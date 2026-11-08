// -----------------------------------------------------------------------------
// <copyright file="CavemanSemanticCacheEntities.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Options, hit and statistics types for CavemanSemanticCache.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.entities;

/// <summary>Configuration for <see cref="caveman.core.services.CavemanSemanticCache"/>.</summary>
public sealed class CavemanSemanticCacheOptions
{
    /// <summary>Minimum Jaccard similarity (0..1) of the normalized token sets to count as a hit. Default 0.65.</summary>
    public double SimilarityThreshold { get; set; } = 0.65;

    /// <summary>Maximum number of entries; the least recently used entry is evicted beyond it. Default 1000.</summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>Optional time-to-live of an entry; null = entries never expire.</summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>Forced language (ISO 639-1 or 639-3). Null = auto-detect for every query.</summary>
    public string? Language { get; set; }

    /// <summary>Collapse inflected forms onto their lemma using the Caveman worddata. Default true.</summary>
    public bool UseLemmas { get; set; } = true;

    /// <summary>Minimum length of a plain token (shorter tokens are dropped, as in the reference algorithm). Default 3.</summary>
    public int MinTokenLength { get; set; } = 3;

    /// <summary>Clock used for TTL and LRU; defaults to <see cref="TimeProvider.System"/>. Mainly for tests.</summary>
    public TimeProvider? TimeProvider { get; set; }
}

/// <summary>A successful semantic cache lookup.</summary>
/// <param name="Response">The cached response.</param>
/// <param name="Similarity">Jaccard similarity with the matched query (0..1).</param>
/// <param name="MatchedQuery">The original query the response was stored under.</param>
/// <param name="Language">ISO 639-3 language used to normalize the match.</param>
public sealed record CavemanSemanticCacheHit(string Response, double Similarity, string MatchedQuery, string Language);

/// <summary>Counters of a semantic cache.</summary>
/// <param name="Hits">Number of lookups answered from the cache.</param>
/// <param name="Misses">Number of lookups that found nothing.</param>
/// <param name="CharsSaved">Total length of the responses served from the cache (a proxy for avoided generation).</param>
public sealed record CavemanSemanticCacheStats(long Hits, long Misses, long CharsSaved)
{
    /// <summary>Hits / (Hits + Misses), 0 when nothing was looked up.</summary>
    public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
}
