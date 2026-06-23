// -----------------------------------------------------------------------------
// <copyright file="CavemanMessageDeduplicator.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Detects duplicate tool results and repeated content blocks across a conversation's messages.</summary>
// -----------------------------------------------------------------------------
using System.Security.Cryptography;
using System.Text;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Scans a sequence of message content strings for duplicates: identifies which later
/// messages carry the same content as an earlier one (a re-read, not a poll).
/// A pair is only flagged as a duplicate when the two messages are more than
/// <see cref="AdjacentGap"/> positions apart (closer messages are assumed to be
/// back-to-back polling, not wasteful re-reads).
/// </summary>
public sealed class CavemanMessageDeduplicator
{
    /// <summary>Minimum message-position gap to classify a repeat as a re-read (default 3).</summary>
    public int AdjacentGap { get; init; } = 3;

    /// <summary>Minimum content length (characters) before a message is considered for dedup (default 50).</summary>
    public int MinContentLength { get; init; } = 50;

    /// <summary>
    /// Finds duplicate messages in <paramref name="messageContents"/>.
    /// Messages shorter than <see cref="MinContentLength"/> are ignored (exit codes, "ok" responses).
    /// </summary>
    public DeduplicationResult FindDuplicates(IEnumerable<string> messageContents)
    {
        var contents = messageContents.ToList();
        var hashToFirstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var pairs = new List<(int Original, int Duplicate)>();
        int wastedTokens = 0;

        for (int i = 0; i < contents.Count; i++)
        {
            var content = contents[i];
            if (string.IsNullOrEmpty(content) || content.Length < MinContentLength)
                continue;

            var hash = ComputeHash(content);
            if (hashToFirstIndex.TryGetValue(hash, out int firstIdx))
            {
                if (i - firstIdx > AdjacentGap)
                {
                    pairs.Add((firstIdx, i));
                    wastedTokens += EstimateTokens(content);
                }
            }
            else
            {
                hashToFirstIndex[hash] = i;
            }
        }

        return new DeduplicationResult
        {
            DuplicatePairs = pairs,
            EstimatedWastedTokens = wastedTokens
        };
    }

    /// <summary>
    /// Returns a new list with duplicate messages replaced by a short reference placeholder.
    /// Preserves original message count and order; only replaces content at duplicate indexes.
    /// </summary>
    public IReadOnlyList<string> RemoveDuplicates(IEnumerable<string> messageContents)
    {
        var contents = messageContents.ToList();
        var result = FindDuplicates(contents);
        var duplicateIndexes = result.DuplicatePairs.Select(p => p.Duplicate).ToHashSet();

        return contents
            .Select((c, i) => duplicateIndexes.Contains(i)
                ? $"[duplicate of message #{result.DuplicatePairs.First(p => p.Duplicate == i).Original + 1}]"
                : c)
            .ToList();
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes, 0, 8); // 16 hex chars — collision-resistant enough
    }

    private static int EstimateTokens(string content) => Math.Max(1, content.Length / 4);
}
