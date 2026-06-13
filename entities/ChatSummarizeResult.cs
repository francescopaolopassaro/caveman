// -----------------------------------------------------------------------------
// <copyright file="ChatSummarizeResult.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Rich result of a conversation summarization, with token metrics and per-block stats.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.entities;

/// <summary>Outcome of <c>CavemanTextRank.RankAndSummarizeChatDetailed</c>: the condensed text plus metrics.</summary>
public sealed class ChatSummarizeResult
{
    /// <summary>The condensed conversation text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The source format detected by the parser ("flat", "openai-json", "chatml", "transcript", …).</summary>
    public string Format { get; set; } = "flat";

    /// <summary>Approximate token count of the original input (per the chosen model).</summary>
    public int OriginalTokens { get; set; }

    /// <summary>Approximate token count of the produced text (per the chosen model).</summary>
    public int CompressedTokens { get; set; }

    /// <summary>Tokens saved (never negative).</summary>
    public int SavedTokens => Math.Max(0, OriginalTokens - CompressedTokens);

    /// <summary>Percentage of tokens removed, 0–100.</summary>
    public double EfficiencyPercentage => OriginalTokens == 0 ? 0 : (double)SavedTokens / OriginalTokens * 100;

    /// <summary>Total number of blocks the conversation was segmented into.</summary>
    public int Blocks { get; set; }

    /// <summary>Blocks that were summarized via TextRank.</summary>
    public int SummarizedBlocks { get; set; }

    /// <summary>Blocks that were caveman-compressed (stop-word/lemma).</summary>
    public int CompressedBlocks { get; set; }

    /// <summary>Blocks kept verbatim (short results, keyword lists, recency window).</summary>
    public int KeptVerbatimBlocks { get; set; }

    /// <summary>Blocks dropped to honor the token budget.</summary>
    public int DroppedBlocks { get; set; }

    /// <summary>Duplicate blocks removed by deduplication.</summary>
    public int DuplicatesRemoved { get; set; }

    /// <summary>Blocks left untouched because the safety guard flagged them as critical.</summary>
    public int SkippedForSafety { get; set; }

    /// <summary>True when no token budget was set, or the result fits the budget.</summary>
    public bool WithinBudget { get; set; } = true;

    /// <summary>Estimated money saved by the removed tokens, in USD (indicative — see CavemanCostEstimator).</summary>
    public decimal EstimatedSavedUsd { get; set; }

    /// <summary>Estimated money saved by the removed tokens, in EUR (indicative).</summary>
    public decimal EstimatedSavedEur { get; set; }

    /// <summary>
    /// The condensed conversation as structured, role-tagged messages — ready to be
    /// re-serialized (e.g. with <c>ToMessagesJson()</c>) and fed back to an LLM API.
    /// </summary>
    public CavemanConversation Conversation { get; set; } = new();

    /// <summary>
    /// Per-block trace explaining what happened to each segment. Populated only when
    /// <c>ChatSummarizeOptions.CollectTrace</c> is enabled; empty otherwise.
    /// </summary>
    public List<BlockTrace> Trace { get; set; } = new();
}

/// <summary>What the summarizer did to one block, for debugging/observability.</summary>
public sealed class BlockTrace
{
    /// <summary>The block's position in the conversation.</summary>
    public int Index { get; set; }

    /// <summary>Role label of the owning turn (when the conversation was parsed), else empty.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Action taken: "summarized", "compressed", "kept", "critical", "dropped".</summary>
    public string Action { get; set; } = "kept";

    /// <summary>True when the block was classified as a long discourse.</summary>
    public bool Discourse { get; set; }

    /// <summary>Character length of the block before processing.</summary>
    public int OriginalChars { get; set; }

    /// <summary>Character length of the block after processing (0 if dropped).</summary>
    public int FinalChars { get; set; }
}
