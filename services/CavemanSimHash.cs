// -----------------------------------------------------------------------------
// <copyright file="CavemanSimHash.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Charikar SimHash: a 64-bit near-duplicate fingerprint for text, with no external hashing dependency.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

/// <summary>
/// Computes a 64-bit SimHash fingerprint for text, used to detect near-duplicates (two
/// texts that differ in wording but are structurally almost the same) that exact-match
/// comparison misses. Two fingerprints with a small Hamming distance correspond to
/// near-duplicate inputs; the reverse is not guaranteed (SimHash is a locality-sensitive
/// hash, not a checksum). Pure C#, no external dependency: features are hashed with FNV-1a.
/// </summary>
public static class CavemanSimHash
{
    private static readonly Regex WordSplit = new(@"[\p{L}\p{N}\p{M}]+", RegexOptions.Compiled);

    /// <summary>
    /// Computes the 64-bit SimHash of <paramref name="text"/> over word-level features
    /// (optionally as <paramref name="shingleSize"/>-word shingles, default 1 = unigrams).
    /// </summary>
    public static ulong Compute(string text, int shingleSize = 1)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var words = WordSplit.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return 0;

        var features = new Dictionary<string, int>(StringComparer.Ordinal);
        int n = Math.Max(1, shingleSize);
        for (int i = 0; i + n <= words.Length; i++)
        {
            var shingle = n == 1 ? words[i] : string.Join(' ', words.Skip(i).Take(n));
            features[shingle] = features.GetValueOrDefault(shingle) + 1;
        }
        if (features.Count == 0)
            foreach (var w in words) features[w] = features.GetValueOrDefault(w) + 1;

        var bitWeights = new int[64];
        foreach (var (feature, weight) in features)
        {
            ulong h = Fnv1a64(feature);
            for (int bit = 0; bit < 64; bit++)
                bitWeights[bit] += ((h >> bit) & 1) == 1 ? weight : -weight;
        }

        ulong fingerprint = 0;
        for (int bit = 0; bit < 64; bit++)
            if (bitWeights[bit] > 0)
                fingerprint |= 1UL << bit;
        return fingerprint;
    }

    /// <summary>Number of differing bits between two fingerprints (0 = identical, 64 = maximally different).</summary>
    public static int HammingDistance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>
    /// True when the two texts' fingerprints differ by at most <paramref name="maxDistance"/>
    /// bits (default 3 of 64 — roughly 95%+ structural similarity for typical text lengths).
    /// </summary>
    public static bool AreNearDuplicates(string a, string b, int maxDistance = 3, int shingleSize = 1)
        => HammingDistance(Compute(a, shingleSize), Compute(b, shingleSize)) <= maxDistance;

    // FNV-1a 64-bit — a small, stable, dependency-free non-cryptographic hash. Deterministic
    // across runs/platforms (unlike string.GetHashCode(), which is randomised per-process).
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static ulong Fnv1a64(string s)
    {
        ulong hash = FnvOffsetBasis;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= FnvPrime;
        }
        return hash;
    }
}
