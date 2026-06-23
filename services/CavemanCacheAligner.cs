// -----------------------------------------------------------------------------
// <copyright file="CavemanCacheAligner.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Scans a system prompt for volatile tokens (UUIDs, timestamps, JWTs, hex hashes) that break KV-cache reuse.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Detects tokens in a system prompt that change on every invocation (UUIDs, ISO-8601 datetimes,
/// JWTs, hex hashes) and therefore invalidate the LLM provider's KV-cache prefix.
/// Stateless; safe to use as a singleton.
/// </summary>
public sealed class CavemanCacheAligner
{
    private static readonly Regex UuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private static readonly Regex Iso8601Pattern = new(
        @"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(:\d{2})?(\.\d+)?(Z|[+-]\d{2}:\d{2})?",
        RegexOptions.Compiled);

    private static readonly Regex JwtPattern = new(
        @"[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}",
        RegexOptions.Compiled);

    private static readonly Regex HexHashPattern = new(
        @"\b[0-9a-fA-F]{32}\b|\b[0-9a-fA-F]{40}\b|\b[0-9a-fA-F]{64}\b",
        RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Label)[] Detectors =
    [
        (UuidPattern,    "UUID"),
        (Iso8601Pattern, "ISO8601"),
        (JwtPattern,     "JWT"),
        (HexHashPattern, "HexHash"),
    ];

    /// <summary>
    /// Scans <paramref name="systemPrompt"/> and returns one <see cref="VolatileFinding"/> per volatile
    /// token type found (at most four findings, one per type).
    /// </summary>
    public IReadOnlyList<VolatileFinding> Scan(string systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt))
            return [];

        var findings = new List<VolatileFinding>(4);
        foreach (var (pattern, label) in Detectors)
        {
            var m = pattern.Match(systemPrompt);
            if (m.Success)
            {
                var sample = m.Value.Length > 40 ? m.Value[..40] : m.Value;
                findings.Add(new VolatileFinding { Label = label, Sample = sample });
            }
        }
        return findings;
    }

    /// <summary>Returns true when any volatile token type is present in <paramref name="systemPrompt"/>.</summary>
    public bool HasVolatileTokens(string systemPrompt) => Scan(systemPrompt).Count > 0;
}
