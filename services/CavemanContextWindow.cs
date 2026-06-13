// -----------------------------------------------------------------------------
// <copyright file="CavemanContextWindow.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>A rolling, token-budget-bounded conversation buffer ("agent working memory").</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// A self-managing conversation buffer for AI agents. You append turns as they happen;
/// whenever the running token count exceeds <see cref="MaxTokens"/> the window auto-compacts
/// the older turns (summarizing long discourses, keeping the most recent turns and any system
/// prompt verbatim) so the context always fits the model's window. No embeddings, no LLM calls.
/// </summary>
public sealed class CavemanContextWindow
{
    private readonly CavemanTextRank _textRank;
    private readonly ITokenCounter _tokenizer;
    private readonly List<CavemanMessage> _messages = new();
    private readonly HashSet<string> _seenHashes = new(StringComparer.OrdinalIgnoreCase);
    // Fingerprints of turns already produced by a compaction — never re-summarized again.
    private HashSet<string> _compactedHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The hard token budget the window keeps the conversation within.</summary>
    public int MaxTokens { get; }

    /// <summary>The model used for token counting.</summary>
    public LlmModel Model { get; }

    /// <summary>How many of the most recent turns are always kept verbatim during compaction.</summary>
    public int KeepLastTurns { get; set; } = 4;

    /// <summary>Optional id used when persisting via an <see cref="IConversationStore"/>.</summary>
    public string? SessionId { get; set; }

    /// <summary>When true, appending a turn whose content was already seen is skipped (idempotent).</summary>
    public bool DeduplicateOnAppend { get; set; }

    public CavemanContextWindow(
        int maxTokens, LlmModel model = LlmModel.Gpt4,
        CavemanTextRank? textRank = null, ITokenCounter? tokenCounter = null)
    {
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Token budget must be positive.");
        MaxTokens = maxTokens;
        Model = model;
        _tokenizer = tokenCounter ?? new ModelTokenizer();
        _textRank = textRank ?? new CavemanTextRank(new FunctionWordProvider(), _tokenizer);
    }

    /// <summary>Number of turns currently held.</summary>
    public int MessageCount => _messages.Count;

    /// <summary>Approximate token count of the current window (per <see cref="Model"/>).</summary>
    public int TokenCount => _tokenizer.CountTokens(Render(), Model);

    /// <summary>Appends a turn and compacts the window if it now exceeds the budget.</summary>
    public void Append(CavemanRole role, string content)
        => Append(new CavemanMessage(role, content ?? string.Empty));

    /// <summary>Appends a turn and compacts the window if it now exceeds the budget.</summary>
    public void Append(CavemanMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.Content))
            return;

        var hash = ConversationState.Fingerprint(message.Content);
        if (DeduplicateOnAppend && _seenHashes.Contains(hash))
            return;     // idempotent: this exact turn was already added

        _seenHashes.Add(hash);
        _messages.Add(message);
        CompactIfNeeded();
    }

    /// <summary>A snapshot of the current (possibly compacted) conversation.</summary>
    public CavemanConversation Snapshot() => new()
    {
        Format = "transcript",
        Messages = _messages.Select(m => new CavemanMessage(m.Role, m.Content, m.RawRole)).ToList()
    };

    /// <summary>Renders the window as a labeled transcript.</summary>
    public string Render() => Snapshot().ToTranscript();

    /// <summary>Renders the window as an OpenAI/Anthropic-style messages JSON array.</summary>
    public string ToMessagesJson(bool indented = false) => Snapshot().ToMessagesJson(indented);

    /// <summary>Clears all turns.</summary>
    public void Clear()
    {
        _messages.Clear();
        _seenHashes.Clear();
        _compactedHashes.Clear();
    }

    // ---- Persistence ----

    /// <summary>Captures the window as a serializable <see cref="ConversationState"/>.</summary>
    public ConversationState ToState() => new()
    {
        SessionId = SessionId,
        MaxTokens = MaxTokens,
        Model = Model,
        KeepLastTurns = KeepLastTurns,
        Turns = _messages.Select(m => new PersistedTurn
        {
            Message = m,
            Hash = ConversationState.Fingerprint(m.Content)
        }).ToList()
    };

    /// <summary>Serializes the window to JSON.</summary>
    public string Save(bool indented = true) => ToState().ToJson(indented);

    /// <summary>Rebuilds a window from a serializable state.</summary>
    public static CavemanContextWindow FromState(
        ConversationState state, CavemanTextRank? textRank = null, ITokenCounter? tokenCounter = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var window = new CavemanContextWindow(Math.Max(1, state.MaxTokens), state.Model, textRank, tokenCounter)
        {
            KeepLastTurns = state.KeepLastTurns,
            SessionId = state.SessionId
        };
        foreach (var turn in state.Turns)
        {
            if (turn.Message == null || string.IsNullOrWhiteSpace(turn.Message.Content))
                continue;
            window._messages.Add(turn.Message);
            window._seenHashes.Add(string.IsNullOrEmpty(turn.Hash)
                ? ConversationState.Fingerprint(turn.Message.Content)
                : turn.Hash);
        }
        window.CompactIfNeeded();
        return window;
    }

    /// <summary>Deserializes a window from JSON.</summary>
    public static CavemanContextWindow Load(
        string json, CavemanTextRank? textRank = null, ITokenCounter? tokenCounter = null)
    {
        var state = ConversationState.FromJson(json)
            ?? throw new ArgumentException("Invalid conversation state JSON.", nameof(json));
        return FromState(state, textRank, tokenCounter);
    }

    /// <summary>Persists the window to a store under <see cref="SessionId"/>.</summary>
    public Task SaveAsync(IConversationStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(SessionId))
            throw new InvalidOperationException("SessionId must be set before saving to a store.");
        return store.SaveAsync(SessionId, ToState(), ct);
    }

    /// <summary>Loads a window from a store, or null when the session is not present.</summary>
    public static async Task<CavemanContextWindow?> LoadAsync(
        IConversationStore store, string sessionId,
        CavemanTextRank? textRank = null, ITokenCounter? tokenCounter = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var state = await store.LoadAsync(sessionId, ct);
        return state == null ? null : FromState(state, textRank, tokenCounter);
    }

    private void CompactIfNeeded()
    {
        if (TokenCount <= MaxTokens)
            return;

        // The most recent turns stay "fresh" (eligible for summarization later); everything
        // else in the output is considered already-compacted and won't be re-summarized.
        var recentFresh = _messages
            .Skip(Math.Max(0, _messages.Count - KeepLastTurns))
            .Select(m => ConversationState.Fingerprint(m.Content))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conversation = new CavemanConversation
        {
            Format = "openai-json",
            Messages = _messages.ToList()
        };

        var result = _textRank.RankAndSummarizeChatDetailed(conversation.ToMessagesJson(), new ChatSummarizeOptions
        {
            ParseConversation = true,
            KeepLastTurnsVerbatim = KeepLastTurns,
            KeepSystemVerbatim = true,
            MaxTokens = MaxTokens,
            TokenModel = Model,
            VerbatimContentHashes = _compactedHashes   // don't re-summarize already-compacted turns
        });

        _messages.Clear();
        _messages.AddRange(result.Conversation.Messages);

        // Recompute the compacted set: every surviving turn that isn't one of the recent
        // fresh turns is now (or stays) compacted.
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _messages)
        {
            var fp = ConversationState.Fingerprint(m.Content);
            if (!recentFresh.Contains(fp))
                next.Add(fp);
        }
        _compactedHashes = next;
    }
}
