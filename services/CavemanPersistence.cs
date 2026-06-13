// -----------------------------------------------------------------------------
// <copyright file="CavemanPersistence.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Serializable agent state (conversation + memory) and pluggable stores (file, in-memory).</summary>
// -----------------------------------------------------------------------------
using System.Text.Json;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>One persisted conversation turn, with a content fingerprint and a compaction flag.</summary>
public sealed class PersistedTurn
{
    public CavemanMessage Message { get; set; } = new();

    /// <summary>Fingerprint of the original turn content (idempotency / dedup key).</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>True if this turn is the product of a prior compaction (already summarized).</summary>
    public bool Compacted { get; set; }
}

/// <summary>
/// A versioned, serializable snapshot of an agent's durable state: the conversation turns,
/// any distilled long-term memory, and the window configuration. Only reconstructible data
/// is stored (never internal processing state), so the schema stays stable.
/// </summary>
public sealed class ConversationState
{
    public int Version { get; set; } = 1;
    public string? SessionId { get; set; }
    public int MaxTokens { get; set; }
    public LlmModel Model { get; set; } = LlmModel.Gpt4;
    public int KeepLastTurns { get; set; }
    public List<PersistedTurn> Turns { get; set; } = new();
    public List<MemoryNote> Memories { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions JsonOptionsIndented = new() { WriteIndented = true };

    public string ToJson(bool indented = false) =>
        JsonSerializer.Serialize(this, indented ? JsonOptionsIndented : JsonOptions);

    public static ConversationState? FromJson(string json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ConversationState>(json);

    /// <summary>Stable fingerprint of a turn's content (whitespace-normalized, case-insensitive).</summary>
    public static string Fingerprint(string content)
    {
        var normalized = Regex.Replace(content ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes, 0, 8); // 16 hex chars is plenty for dedup
    }
}

/// <summary>Pluggable persistence for <see cref="ConversationState"/> keyed by a session id.</summary>
public interface IConversationStore
{
    Task SaveAsync(string sessionId, ConversationState state, CancellationToken ct = default);
    Task<ConversationState?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// File-backed store: one <c>&lt;sessionId&gt;.json</c> per session under a directory. Writes are
/// atomic (temp file + move). Session ids are sanitized to prevent path traversal.
/// Not safe for concurrent writes to the same session id — serialize those yourself.
/// </summary>
public sealed class FileConversationStore : IConversationStore
{
    private readonly string _directory;

    public FileConversationStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(string sessionId, ConversationState state, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        var tmp = path + ".tmp";
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(tmp, state.ToJson(indented: true), new System.Text.UTF8Encoding(false), ct);
        File.Move(tmp, path, overwrite: true);  // atomic replace
    }

    public async Task<ConversationState?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return ConversationState.FromJson(json);
    }

    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string sessionId) => Path.Combine(_directory, Sanitize(sessionId) + ".json");

    private static string Sanitize(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        var safe = Regex.Replace(sessionId, @"[^A-Za-z0-9._-]", "_");
        return safe.Length == 0 ? "_" : safe;
    }
}

/// <summary>In-memory store (for tests / ephemeral use). Stores a serialized copy to avoid aliasing.</summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _store = new();

    public Task SaveAsync(string sessionId, ConversationState state, CancellationToken ct = default)
    {
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        _store[sessionId] = state.ToJson();
        return Task.CompletedTask;
    }

    public Task<ConversationState?> LoadAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(sessionId, out var json) ? ConversationState.FromJson(json) : null);

    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
