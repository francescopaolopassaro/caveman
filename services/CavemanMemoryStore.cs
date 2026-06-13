// -----------------------------------------------------------------------------
// <copyright file="CavemanMemoryStore.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Append-only long-term memory store with embedding-free relevance recall.</summary>
// -----------------------------------------------------------------------------
using System.Text.Json;

namespace caveman.core.services;

/// <summary>
/// A long-term memory store for AI agents: accumulate distilled <see cref="MemoryNote"/>s across
/// sessions, then recall the most relevant ones for the current query using the embedding-free
/// <see cref="CavemanRelevanceFilter"/>. Serializable to JSON for persistence.
/// </summary>
public sealed class CavemanMemoryStore
{
    private readonly List<MemoryNote> _notes = new();
    private readonly CavemanRelevanceFilter _relevance;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions JsonOptionsIndented = new() { WriteIndented = true };

    public CavemanMemoryStore(CavemanRelevanceFilter? relevance = null)
    {
        _relevance = relevance ?? new CavemanRelevanceFilter();
    }

    /// <summary>All stored notes, in insertion order.</summary>
    public IReadOnlyList<MemoryNote> Notes => _notes;

    /// <summary>Number of stored notes.</summary>
    public int Count => _notes.Count;

    /// <summary>Adds a memory note (ignored when empty).</summary>
    public void Remember(MemoryNote note)
    {
        if (note != null && !string.IsNullOrWhiteSpace(note.Summary))
            _notes.Add(note);
    }

    /// <summary>Removes all notes.</summary>
    public void Clear() => _notes.Clear();

    /// <summary>
    /// Returns the <paramref name="topK"/> notes most relevant to <paramref name="query"/>,
    /// scored by lemmatized lexical overlap over each note's summary and keywords.
    /// Notes with zero overlap are excluded.
    /// </summary>
    public IReadOnlyList<MemoryNote> Recall(string query, int topK = 5)
    {
        if (_notes.Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<MemoryNote>();

        return _notes
            .Select(n => (Note: n, Score: _relevance.Score(n.Summary + " " + string.Join(" ", n.Keywords), query, n.Iso3)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Note)
            .ToList();
    }

    /// <summary>Serializes all notes to JSON.</summary>
    public string Save(bool indented = false) =>
        JsonSerializer.Serialize(_notes, indented ? JsonOptionsIndented : JsonOptions);

    /// <summary>Replaces all notes with those deserialized from JSON.</summary>
    public void Load(string json)
    {
        _notes.Clear();
        if (string.IsNullOrWhiteSpace(json))
            return;
        var loaded = JsonSerializer.Deserialize<List<MemoryNote>>(json);
        if (loaded != null)
            _notes.AddRange(loaded);
    }
}
