// -----------------------------------------------------------------------------
// <copyright file="CavemanConversation.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Structured human/assistant conversation model (roles + turns) used by the chat summarizer.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json;

namespace caveman.core.entities;

/// <summary>The speaker behind a conversation turn.</summary>
public enum CavemanRole
{
    /// <summary>The role could not be determined (e.g. a flat, unstructured transcript).</summary>
    Unknown = 0,
    /// <summary>System / developer instructions.</summary>
    System = 1,
    /// <summary>The human user (also: "human").</summary>
    User = 2,
    /// <summary>The AI assistant (also: "ai", "model", "bot").</summary>
    Assistant = 3,
    /// <summary>A tool / function call result.</summary>
    Tool = 4
}

/// <summary>A single turn in a conversation.</summary>
public sealed class CavemanMessage
{
    /// <summary>The normalized role of the speaker.</summary>
    public CavemanRole Role { get; set; } = CavemanRole.Unknown;

    /// <summary>The original role label as found in the source (e.g. "human", "model").</summary>
    public string? RawRole { get; set; }

    /// <summary>The textual content of the turn.</summary>
    public string Content { get; set; } = string.Empty;

    public CavemanMessage() { }

    public CavemanMessage(CavemanRole role, string content, string? rawRole = null)
    {
        Role = role;
        Content = content;
        RawRole = rawRole;
    }

    /// <summary>A human-readable role label (e.g. "User", "Assistant") for rendering.</summary>
    public string RoleLabel => Role switch
    {
        CavemanRole.System => "System",
        CavemanRole.User => "User",
        CavemanRole.Assistant => "Assistant",
        CavemanRole.Tool => "Tool",
        _ => RawRole ?? string.Empty
    };
}

/// <summary>An ordered sequence of conversation turns, plus the detected source format.</summary>
public sealed class CavemanConversation
{
    /// <summary>The turns, in order.</summary>
    public List<CavemanMessage> Messages { get; set; } = new();

    /// <summary>The format the parser recognized (e.g. "openai-json", "chatml", "transcript", "plain").</summary>
    public string Format { get; set; } = "plain";

    /// <summary>True when the source had explicit role structure (not a single flat blob).</summary>
    public bool IsStructured => Format != "plain" && Messages.Count > 0 &&
                                Messages.Any(m => m.Role != CavemanRole.Unknown);

    /// <summary>
    /// Serializes to an OpenAI/Anthropic-style messages array
    /// (<c>[{ "role": "user", "content": "…" }, …]</c>), ready to be re-fed to an LLM API.
    /// </summary>
    public string ToMessagesJson(bool indented = false)
    {
        var items = Messages.Select(m => new
        {
            role = m.Role switch
            {
                CavemanRole.System => "system",
                CavemanRole.User => "user",
                CavemanRole.Assistant => "assistant",
                CavemanRole.Tool => "tool",
                _ => m.RawRole ?? "user"
            },
            content = m.Content
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>Renders the conversation as a plain labeled transcript (<c>Role: content</c>).</summary>
    public string ToTranscript()
    {
        var sb = new StringBuilder();
        foreach (var m in Messages)
        {
            var label = m.RoleLabel;
            sb.Append(label.Length > 0 ? $"{label}: {m.Content}" : m.Content);
            sb.Append("\n\n");
        }
        return sb.ToString().Trim();
    }
}
