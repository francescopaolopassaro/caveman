// -----------------------------------------------------------------------------
// <copyright file="CavemanConversationParser.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Parses a conversation string in several common AI formats into a CavemanConversation.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Detects and parses a conversation transcript into a <see cref="CavemanConversation"/>.
/// Supported formats: OpenAI/Anthropic-style JSON (array or <c>{ "messages": [...] }</c>,
/// including Anthropic content-block arrays), ChatML (<c>&lt;|im_start|&gt;</c>),
/// Gemma (<c>&lt;start_of_turn&gt;</c>), Llama/Mistral instruction format
/// (<c>[INST] … [/INST]</c>, <c>&lt;&lt;SYS&gt;&gt;</c>), and plain labeled transcripts
/// (<c>User:</c> / <c>Assistant:</c> / <c>Utente:</c> …). Anything else falls back to a
/// single <see cref="CavemanRole.Unknown"/> message holding the whole text.
/// </summary>
public sealed class CavemanConversationParser
{
    private static readonly Regex TranscriptLabel = new(
        @"^\s*(system|sistema|developer|user|utente|human|umano|assistant|assistente|ai|bot|model|modello|tool|strumento|function|funzione)\s*[:>\-]\s?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parses <paramref name="raw"/>, always returning a conversation (never null).</summary>
    public CavemanConversation Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new CavemanConversation { Format = "plain" };

        if (TryParse(raw, out var conversation))
            return conversation;

        return new CavemanConversation
        {
            Format = "plain",
            Messages = { new CavemanMessage(CavemanRole.Unknown, raw.Trim()) }
        };
    }

    /// <summary>
    /// Attempts to parse <paramref name="raw"/> into a structured conversation. Returns false
    /// (and a single-message fallback) when no known structured format is recognized.
    /// </summary>
    public bool TryParse(string raw, out CavemanConversation conversation)
    {
        conversation = new CavemanConversation { Format = "plain" };
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.TrimStart();

        if ((trimmed.StartsWith("[") || trimmed.StartsWith("{")) && TryParseJson(raw, out conversation))
            return true;

        if (raw.Contains("<|im_start|>") && TryParseDelimited(raw, "<|im_start|>", "<|im_end|>", "chatml", out conversation))
            return true;

        if (raw.Contains("<start_of_turn>") && TryParseDelimited(raw, "<start_of_turn>", "<end_of_turn>", "gemma", out conversation))
            return true;

        if (raw.Contains("[INST]") && TryParseLlama(raw, out conversation))
            return true;

        if (TryParseTranscript(raw, out conversation))
            return true;

        return false;
    }

    // ---- JSON (OpenAI / Anthropic) ----------------------------------------

    private static bool TryParseJson(string raw, out CavemanConversation conversation)
    {
        conversation = new CavemanConversation { Format = "openai-json" };
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("messages", out var msgs) &&
                     msgs.ValueKind == JsonValueKind.Array)
            {
                array = msgs;
            }
            else
            {
                return false;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string? rawRole = item.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null;

                var content = ExtractJsonContent(item);

                // Preserve tool/function calls so the invocation isn't silently lost.
                var toolNote = ExtractToolCalls(item);
                if (toolNote.Length > 0)
                    content = string.IsNullOrWhiteSpace(content) ? toolNote : content.Trim() + "\n" + toolNote;

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                conversation.Messages.Add(new CavemanMessage(MapRole(rawRole), content.Trim(), rawRole));
            }

            return conversation.Messages.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads the "content" of a message, supporting both a string and an array of content blocks.</summary>
    private static string ExtractJsonContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            // Some tool messages use "text" or "output".
            if (message.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? string.Empty;
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.String)
                {
                    sb.Append(block.GetString());
                    sb.Append('\n');
                }
                else if (block.ValueKind == JsonValueKind.Object &&
                         block.TryGetProperty("text", out var txt) &&
                         txt.ValueKind == JsonValueKind.String)
                {
                    sb.Append(txt.GetString());
                    sb.Append('\n');
                }
                else if (block.ValueKind == JsonValueKind.Object &&
                         block.TryGetProperty("type", out var type) &&
                         type.ValueKind == JsonValueKind.String)
                {
                    // Non-text content block (image, audio, tool_use, …): keep a placeholder
                    // so the model still sees that something was there.
                    sb.Append($"[{type.GetString()}]");
                    sb.Append('\n');
                }
            }
            return sb.ToString().Trim();
        }

        return string.Empty;
    }

    /// <summary>Renders OpenAI-style tool/function calls on a message as a compact textual note.</summary>
    private static string ExtractToolCalls(JsonElement message)
    {
        var sb = new StringBuilder();

        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object ||
                    !call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object)
                    continue;
                AppendCall(sb, fn);
            }
        }

        // Legacy single function_call shape.
        if (message.TryGetProperty("function_call", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            AppendCall(sb, legacy);

        return sb.ToString().Trim();

        static void AppendCall(StringBuilder sb, JsonElement fn)
        {
            var name = fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "tool";
            var args = fn.TryGetProperty("arguments", out var a) ? a.ToString() : string.Empty;
            sb.Append($"[tool_call: {name}({args})]\n");
        }
    }

    // ---- Delimited (ChatML, Gemma) ----------------------------------------

    private static bool TryParseDelimited(
        string raw, string startTag, string endTag, string format, out CavemanConversation conversation)
    {
        conversation = new CavemanConversation { Format = format };

        int pos = 0;
        while (true)
        {
            int start = raw.IndexOf(startTag, pos, StringComparison.Ordinal);
            if (start < 0)
                break;

            int bodyStart = start + startTag.Length;
            int end = raw.IndexOf(endTag, bodyStart, StringComparison.Ordinal);
            string body = end < 0 ? raw.Substring(bodyStart) : raw.Substring(bodyStart, end - bodyStart);

            // First line/token is the role; the rest is the content.
            string role;
            string content;
            int nl = body.IndexOf('\n');
            if (nl >= 0)
            {
                role = body.Substring(0, nl).Trim();
                content = body.Substring(nl + 1).Trim();
            }
            else
            {
                role = body.Trim();
                content = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(content))
                conversation.Messages.Add(new CavemanMessage(MapRole(role), content, role));

            pos = end < 0 ? raw.Length : end + endTag.Length;
        }

        return conversation.Messages.Count > 0;
    }

    // ---- Llama / Mistral [INST] -------------------------------------------

    private static bool TryParseLlama(string raw, out CavemanConversation conversation)
    {
        conversation = new CavemanConversation { Format = "llama-inst" };

        // Pull out the system prompt if present: <<SYS>> ... <</SYS>>
        var sysMatch = Regex.Match(raw, @"<<SYS>>([\s\S]*?)<</SYS>>", RegexOptions.IgnoreCase);
        string working = raw;
        if (sysMatch.Success)
        {
            var sys = sysMatch.Groups[1].Value.Trim();
            if (sys.Length > 0)
                conversation.Messages.Add(new CavemanMessage(CavemanRole.System, sys, "system"));
            working = raw.Remove(sysMatch.Index, sysMatch.Length);
        }

        // Each [INST] ... [/INST] is a user turn; text after [/INST] up to the next [INST] is the assistant reply.
        var matches = Regex.Matches(working, @"\[INST\]([\s\S]*?)\[/INST\]", RegexOptions.IgnoreCase);
        if (matches.Count == 0 && conversation.Messages.Count == 0)
            return false;

        foreach (Match m in matches)
        {
            var user = StripTags(m.Groups[1].Value).Trim();
            if (user.Length > 0)
                conversation.Messages.Add(new CavemanMessage(CavemanRole.User, user, "user"));

            int replyStart = m.Index + m.Length;
            int nextInst = working.IndexOf("[INST]", replyStart, StringComparison.OrdinalIgnoreCase);
            int replyEnd = nextInst < 0 ? working.Length : nextInst;
            var reply = StripTags(working.Substring(replyStart, replyEnd - replyStart)).Trim();
            if (reply.Length > 0)
                conversation.Messages.Add(new CavemanMessage(CavemanRole.Assistant, reply, "assistant"));
        }

        return conversation.Messages.Count > 0;
    }

    private static string StripTags(string s) =>
        s.Replace("<s>", string.Empty, StringComparison.OrdinalIgnoreCase)
         .Replace("</s>", string.Empty, StringComparison.OrdinalIgnoreCase);

    // ---- Plain labeled transcript -----------------------------------------

    private static bool TryParseTranscript(string raw, out CavemanConversation conversation)
    {
        var conv = new CavemanConversation { Format = "transcript" };
        conversation = conv;

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        CavemanMessage? current = null;
        var buffer = new StringBuilder();
        int labeledLines = 0;

        void Flush()
        {
            if (current != null)
            {
                current.Content = buffer.ToString().Trim();
                if (current.Content.Length > 0)
                    conv.Messages.Add(current);
            }
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            var match = TranscriptLabel.Match(line);
            if (match.Success)
            {
                labeledLines++;
                Flush();
                var label = match.Groups[1].Value;
                current = new CavemanMessage(MapRole(label), string.Empty, label);
                buffer.Append(line.Substring(match.Length));
                buffer.Append('\n');
            }
            else if (current != null)
            {
                buffer.Append(line);
                buffer.Append('\n');
            }
        }
        Flush();

        // Require at least two labeled turns to treat the text as a real transcript,
        // otherwise a stray "Nota:" line would hijack ordinary prose.
        return labeledLines >= 2 && conversation.Messages.Count >= 2;
    }

    // ---- Role mapping ------------------------------------------------------

    private static CavemanRole MapRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return CavemanRole.Unknown;

        return role.Trim().ToLowerInvariant() switch
        {
            "system" or "sistema" or "developer" => CavemanRole.System,
            "user" or "utente" or "human" or "umano" => CavemanRole.User,
            "assistant" or "assistente" or "ai" or "bot" or "model" or "modello" or "gpt" => CavemanRole.Assistant,
            "tool" or "strumento" or "function" or "funzione" => CavemanRole.Tool,
            _ => CavemanRole.Unknown
        };
    }
}
