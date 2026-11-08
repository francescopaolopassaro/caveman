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
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// The original JSON object of the message, kept only when it carries tool data (tool_use / tool_result /
    /// tool_calls / function calls / role "tool"). Compaction never drops or rewrites such a message and
    /// <see cref="CavemanConversation.ToMessagesJson"/> emits this JSON unchanged, so ids and payloads survive.
    /// </summary>
    public string? RawJson { get; set; }

    /// <summary>
    /// Text-format tool markers used by models that serialize tool use inside the message text: Qwen / Hermes
    /// (<c>&lt;tool_call&gt;</c>), Claude XML (<c>&lt;function_calls&gt;</c>, <c>&lt;tool_use&gt;</c>), DeepSeek
    /// (<c>&lt;｜tool▁call▁begin｜&gt;</c>), Mistral (<c>[TOOL_CALLS]</c>), Llama 3.1 (<c>&lt;|python_tag|&gt;</c>),
    /// GLM (<c>&lt;|observation|&gt;</c>), Qwen-Agent (<c>✿FUNCTION✿</c>), Grok / Spark / Gemini XML-style calls.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ToolMarker = new(
        @"</?(?:tool_call|tool_calls|tool_use|tool_result|tool_response|tool_output|function_call|function_calls|function_results|function_response|invoke|antml:invoke)\b|"
        + @"\[(?:TOOL_CALLS|TOOL_RESULTS|AVAILABLE_TOOLS)\]|<\|(?:python_tag|observation|tool_call|tool_response|FunctionCallBegin|plugin|action_start)\|>|"
        + @"<｜tool▁|✿(?:FUNCTION|ARGS|RESULT|RETURN)✿",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>True for a message that takes part in an agentic tool exchange and must never be removed or altered.</summary>
    public bool HasToolData => RawJson != null || Role == CavemanRole.Tool || (Content.Length > 0 && ToolMarker.IsMatch(Content));

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
        // Messages that carry tool data are emitted exactly as they were parsed (ids, blocks and payloads intact).
        var items = Messages.Select(m => m.RawJson != null
            ? (object)JsonDocument.Parse(m.RawJson).RootElement.Clone()
            : new { role = RoleName(m), content = m.Content });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string RoleName(CavemanMessage m) => m.Role switch
    {
        CavemanRole.System => "system",
        CavemanRole.User => "user",
        CavemanRole.Assistant => "assistant",
        CavemanRole.Tool => "tool",
        _ => m.RawRole ?? "user"
    };

    /// <summary>
    /// Returns a copy that satisfies the strict rules of chat APIs such as Amazon Bedrock Converse (Nova, Claude):
    /// the system prompt is returned separately, only <c>user</c>/<c>assistant</c> roles remain, consecutive turns of
    /// the same role are merged, the first turn is a user turn and no turn is empty. Messages that carry tool data are
    /// never merged, rewritten or dropped.
    /// </summary>
    /// <param name="systemPrompt">The joined system prompt(s), or null when there was none.</param>
    /// <param name="leadingUserPlaceholder">Text of the user turn inserted when the first turn would be an assistant turn.</param>
    public CavemanConversation ToStrictAlternation(out string? systemPrompt, string leadingUserPlaceholder = "[…]")
    {
        var system = new List<string>();
        var result = new CavemanConversation { Format = Format };

        foreach (var m in Messages)
        {
            if (m.HasToolData)
            {
                result.Messages.Add(m);
                continue;
            }
            if (m.Role == CavemanRole.System)
            {
                if (!string.IsNullOrWhiteSpace(m.Content)) system.Add(m.Content.Trim());
                continue;
            }
            if (string.IsNullOrWhiteSpace(m.Content)) continue;

            var role = m.Role == CavemanRole.Assistant ? CavemanRole.Assistant : CavemanRole.User;
            var last = result.Messages.Count > 0 ? result.Messages[^1] : null;
            if (last != null && !last.HasToolData && last.Role == role)
                last.Content = last.Content + "\n\n" + m.Content.Trim();
            else
                result.Messages.Add(new CavemanMessage(role, m.Content.Trim(), role == CavemanRole.User ? "user" : "assistant"));
        }

        if (result.Messages.Count > 0 && result.Messages[0].Role != CavemanRole.User && !result.Messages[0].HasToolData)
            result.Messages.Insert(0, new CavemanMessage(CavemanRole.User, leadingUserPlaceholder, "user"));

        systemPrompt = system.Count > 0 ? string.Join("\n\n", system) : null;
        return result;
    }

    /// <summary>
    /// Serializes to the Amazon Bedrock Converse message shape
    /// (<c>[{ "role": "user", "content": [{ "text": "…" }] }, …]</c>) after <see cref="ToStrictAlternation"/>.
    /// Messages that carry tool data are emitted unchanged. Pass the returned system prompt in the request's <c>system</c> field.
    /// </summary>
    public string ToConverseMessagesJson(out string? systemPrompt, bool indented = false)
    {
        var strict = ToStrictAlternation(out systemPrompt);
        var turns = new List<(string Role, JsonArray Blocks)>();

        // Converse allows several content blocks per turn, so consecutive turns of the same role (for example the
        // results of parallel tool calls, or a tool result followed by the user's next question) become one turn.
        void Add(string role, IEnumerable<JsonNode> blocks)
        {
            var list = blocks.ToList();
            if (list.Count == 0) return;
            if (turns.Count > 0 && turns[^1].Role == role)
                foreach (var b in list) turns[^1].Blocks.Add(b);
            else
                turns.Add((role, new JsonArray(list.ToArray())));
        }

        foreach (var m in strict.Messages)
        {
            if (m.RawJson != null)
            {
                var (role, blocks) = ToConverseBlocks(m);
                Add(role, blocks);
            }
            else
            {
                Add(m.Role == CavemanRole.Assistant ? "assistant" : "user",
                    new JsonNode[] { new JsonObject { ["text"] = m.Content } });
            }
        }

        var array = new JsonArray(turns.Select(t => (JsonNode)new JsonObject
        {
            ["role"] = t.Role,
            ["content"] = t.Blocks
        }).ToArray());
        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>
    /// Maps a message that carries tool data to Converse content blocks: OpenAI-compatible
    /// (OpenAI, DeepSeek, Qwen, Grok, GLM, Spark, Mistral, Kimi...) <c>tool_calls</c> / <c>role: tool</c>, Anthropic
    /// <c>tool_use</c> / <c>tool_result</c> and native Converse <c>toolUse</c> / <c>toolResult</c>. A shape that cannot be
    /// mapped (for example Gemini <c>parts</c>) is carried as its readable text so the turn is never dropped.
    /// </summary>
    private static (string Role, List<JsonNode> Blocks) ToConverseBlocks(CavemanMessage m)
    {
        var node = JsonNode.Parse(m.RawJson!) as JsonObject;
        var blocks = new List<JsonNode>();
        var rawRole = node?["role"]?.GetValue<string>()?.ToLowerInvariant();
        if (node == null)
            return ("user", new List<JsonNode> { new JsonObject { ["text"] = m.Content } });

        // OpenAI-compatible: assistant tool_calls
        if (node["tool_calls"] is JsonArray calls)
        {
            var text = TextOf(node["content"]);
            if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new JsonObject { ["text"] = text });
            foreach (var call in calls.OfType<JsonObject>())
            {
                var fn = call["function"] as JsonObject;
                blocks.Add(new JsonObject
                {
                    ["toolUse"] = new JsonObject
                    {
                        ["toolUseId"] = call["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                        ["name"] = fn?["name"]?.GetValue<string>() ?? "tool",
                        ["input"] = ParseArguments(fn?["arguments"])
                    }
                });
            }
            return ("assistant", blocks);
        }

        // OpenAI-compatible: tool result
        if (rawRole == "tool" && node["tool_call_id"] != null)
        {
            blocks.Add(new JsonObject
            {
                ["toolResult"] = new JsonObject
                {
                    ["toolUseId"] = node["tool_call_id"]!.GetValue<string>(),
                    ["content"] = new JsonArray(new JsonObject { ["text"] = TextOf(node["content"]) })
                }
            });
            return ("user", blocks);
        }

        // Content blocks: Anthropic (tool_use / tool_result) or native Converse (toolUse / toolResult)
        if (node["content"] is JsonArray content)
        {
            foreach (var b in content.OfType<JsonObject>())
            {
                var type = b["type"]?.GetValue<string>();
                if (b["toolUse"] != null || b["toolResult"] != null) blocks.Add(b.DeepClone());
                else if (type == "tool_use")
                    blocks.Add(new JsonObject
                    {
                        ["toolUse"] = new JsonObject
                        {
                            ["toolUseId"] = b["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                            ["name"] = b["name"]?.GetValue<string>() ?? "tool",
                            ["input"] = b["input"]?.DeepClone() ?? new JsonObject()
                        }
                    });
                else if (type == "tool_result")
                    blocks.Add(new JsonObject
                    {
                        ["toolResult"] = new JsonObject
                        {
                            ["toolUseId"] = b["tool_use_id"]?.GetValue<string>() ?? string.Empty,
                            ["content"] = new JsonArray(new JsonObject { ["text"] = TextOf(b["content"]) })
                        }
                    });
                else if (b["text"] != null && !string.IsNullOrWhiteSpace(b["text"]!.GetValue<string>()))
                    blocks.Add(new JsonObject { ["text"] = b["text"]!.GetValue<string>() });
            }
            if (blocks.Count > 0)
                return (rawRole == "assistant" || rawRole == "model" ? "assistant" : "user", blocks);
        }

        // Anything else (Gemini parts, Cohere tool_results, text markers...): keep the turn as readable text.
        return (m.Role == CavemanRole.Assistant ? "assistant" : "user",
            new List<JsonNode> { new JsonObject { ["text"] = string.IsNullOrWhiteSpace(m.Content) ? "[tool]" : m.Content } });
    }

    private static string TextOf(JsonNode? n)
    {
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        if (n is JsonArray a)
            return string.Join("\n", a.OfType<JsonObject>().Select(o => o["text"]?.GetValue<string>()).Where(t => !string.IsNullOrEmpty(t)));
        return n?.ToJsonString() ?? string.Empty;
    }

    private static JsonNode ParseArguments(JsonNode? args)
    {
        try
        {
            if (args is JsonValue v && v.TryGetValue<string>(out var s))
                return JsonNode.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s) is JsonObject o ? o : new JsonObject();
            if (args is JsonObject obj) return obj.DeepClone();
        }
        catch (JsonException) { }
        return new JsonObject();
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
