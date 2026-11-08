// -----------------------------------------------------------------------------
// <copyright file="CavemanStructuredCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses structured content (JSON, XML, HTML, Markdown, code, SQL) without ever removing it.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses structured content that appears inside chats and prompts: JSON, XML, HTML, Markdown,
/// source code and SQL. The content is never dropped and never turned into prose: JSON stays valid JSON,
/// XML stays well-formed XML, code keeps its structure. The result is never longer than the input.
/// </summary>
public sealed class CavemanStructuredCompressor
{
    private enum Kind { Json, Xml, Html, Markdown, Sql, Code, Whitespace, Raw }

    private readonly CavemanCodeCompressor _code = new();
    private readonly CavemanSqlCompressor _sql = new();
    private readonly CavemanContentDetector _detector = new();

    private static readonly HashSet<string> JsonTags = new(StringComparer.OrdinalIgnoreCase) { "json", "jsonc", "json5", "jsonl" };
    private static readonly HashSet<string> XmlTags = new(StringComparer.OrdinalIgnoreCase)
        { "xml", "xsd", "xsl", "xslt", "xaml", "svg", "csproj", "vbproj", "fsproj", "props", "targets", "config", "plist", "resx", "nuspec", "wsdl", "rss", "atom" };
    private static readonly HashSet<string> HtmlTags = new(StringComparer.OrdinalIgnoreCase) { "html", "htm", "xhtml" };
    private static readonly HashSet<string> MarkdownTags = new(StringComparer.OrdinalIgnoreCase) { "md", "markdown", "mdx" };
    private static readonly HashSet<string> WhitespaceTags = new(StringComparer.OrdinalIgnoreCase) { "yaml", "yml", "toml", "ini", "properties", "env", "dockerfile", "makefile", "text", "txt", "plaintext", "log" };
    private static readonly HashSet<string> RawTags = new(StringComparer.OrdinalIgnoreCase) { "diff", "patch", "csv", "tsv", "output", "console" };

    private static readonly Regex HtmlComment = new(@"<!--(?!\[if)[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex HtmlKeep = new(@"<(pre|textarea|script|style)\b[\s\S]*?</\1\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"[ \t\r\n]+", RegexOptions.Compiled);
    private static readonly Regex BetweenTags = new(@">\s+<", RegexOptions.Compiled);
    private static readonly Regex BlankRuns = new(@"(?:[ \t]*\r?\n){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Compresses <paramref name="body"/>. <paramref name="languageTag"/> is the Markdown fence tag when known
    /// (json, xml, html, md, sql, cs, py, ...); when null or empty the content type is detected.
    /// </summary>
    public string Compress(string body, string? languageTag = null)
        => Compress(body, languageTag, depth: 0);

    private string Compress(string body, string? languageTag, int depth)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;

        var kind = KindFromTag(languageTag) ?? Detect(body);
        string result;
        try
        {
            result = kind switch
            {
                Kind.Json => MinifyJson(body),
                Kind.Xml => MinifyXml(body),
                Kind.Html => MinifyHtml(body),
                Kind.Markdown => depth > 0 ? Tidy(body) : CompressMarkdown(body),
                Kind.Sql => _sql.Compress(body) is { WasCompressed: true } s ? s.Compressed : Tidy(body),
                Kind.Code => _code.Compress(body) is { WasCompressed: true } c ? c.Compressed : Tidy(body),
                Kind.Whitespace => Tidy(body),
                _ => body
            };
        }
        catch
        {
            // Malformed JSON/XML etc.: never fail a chat because of a code block, just tidy whitespace.
            result = kind is Kind.Json or Kind.Xml or Kind.Html ? Tidy(body) : body;
        }

        return string.IsNullOrWhiteSpace(result) || result.Length >= body.Length ? body : result;
    }

    /// <summary>Maps a Markdown fence tag to a content type; null when the tag is empty (detect instead).</summary>
    private static Kind? KindFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim();
        if (JsonTags.Contains(tag)) return Kind.Json;
        if (XmlTags.Contains(tag)) return Kind.Xml;
        if (HtmlTags.Contains(tag)) return Kind.Html;
        if (MarkdownTags.Contains(tag)) return Kind.Markdown;
        if (tag.Equals("sql", StringComparison.OrdinalIgnoreCase)) return Kind.Sql;
        if (WhitespaceTags.Contains(tag)) return Kind.Whitespace;
        if (RawTags.Contains(tag)) return Kind.Raw;
        return Kind.Code; // any other language tag is source code
    }

    private Kind Detect(string body) => _detector.Detect(body).Type switch
    {
        ContentType.JsonArray or ContentType.JsonObject => Kind.Json,
        ContentType.Xml => Kind.Xml,
        ContentType.Html => Kind.Html,
        ContentType.Sql => Kind.Sql,
        ContentType.Code => Kind.Code,
        ContentType.GitDiff or ContentType.Tabular => Kind.Raw,
        _ => Kind.Whitespace
    };

    /// <summary>True when the fence tag names a programming language (as opposed to data / markup).</summary>
    public bool IsSourceCode(string? languageTag) => KindFromTag(languageTag) == Kind.Code;

    /// <summary>True when <paramref name="text"/> looks like a standalone structured document (JSON, XML or HTML).</summary>
    public bool LooksStructured(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        if (t[0] != '{' && t[0] != '[' && t[0] != '<') return false;
        return _detector.Detect(text).Type is ContentType.JsonArray or ContentType.JsonObject or ContentType.Xml or ContentType.Html;
    }

    // ── JSON ────────────────────────────────────────────────────────────────

    private static string MinifyJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            doc.WriteTo(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── XML ─────────────────────────────────────────────────────────────────

    private static string MinifyXml(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.None); // None drops insignificant whitespace, honours xml:space
        foreach (var c in doc.DescendantNodes().OfType<XComment>().ToList()) c.Remove();
        var body = doc.ToString(SaveOptions.DisableFormatting);
        return doc.Declaration != null ? doc.Declaration + body : body;
    }

    // ── HTML ────────────────────────────────────────────────────────────────

    private static string MinifyHtml(string html)
    {
        var keep = new List<string>();
        var s = HtmlKeep.Replace(html, m => { keep.Add(m.Value); return "\u0001" + (keep.Count - 1) + "\u0001"; });
        s = HtmlComment.Replace(s, string.Empty);
        s = Spaces.Replace(s, " ");
        s = BetweenTags.Replace(s, "><").Trim();
        return Regex.Replace(s, "\u0001(\\d+)\u0001", m => keep[int.Parse(m.Groups[1].Value)]);
    }

    // ── Markdown ────────────────────────────────────────────────────────────

    private string CompressMarkdown(string md)
    {
        md = HtmlComment.Replace(md, string.Empty);
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(md.Length);
        var fence = new StringBuilder();
        string? fenceTag = null;
        string fenceOpen = string.Empty;
        bool inFence = false;
        int blank = 0;

        foreach (var raw in lines)
        {
            var line = raw;
            var trimmedStart = line.TrimStart();
            if (!inFence && trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = true;
                fenceOpen = line.TrimEnd();
                fenceTag = trimmedStart.Substring(3).Trim().Split(' ')[0];
                fence.Clear();
                blank = 0;
                continue;
            }
            if (inFence)
            {
                if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
                {
                    var inner = fence.ToString().TrimEnd('\n');
                    sb.Append(fenceOpen).Append('\n').Append(Compress(inner, fenceTag, depth: 1)).Append('\n').Append("```").Append('\n');
                    inFence = false;
                }
                else fence.Append(line).Append('\n');
                continue;
            }

            line = line.TrimEnd();
            if (line.Length == 0)
            {
                if (++blank > 1) continue;
                sb.Append('\n');
                continue;
            }
            blank = 0;
            if (line.TrimStart().StartsWith('|')) line = CompactTableRow(line);
            sb.Append(line).Append('\n');
        }
        if (inFence) sb.Append(fenceOpen).Append('\n').Append(fence).Append("```\n"); // unterminated fence: keep as is
        return sb.ToString().Trim('\n');
    }

    private static string CompactTableRow(string line)
    {
        var t = line.Trim();
        var cells = t.Trim('|').Split('|').Select(c => c.Trim()).ToList();
        bool separator = cells.All(c => c.Length > 0 && c.All(ch => ch == '-' || ch == ':'));
        if (separator)
            cells = cells.Select(c => (c.StartsWith(':') ? ":" : "") + "---" + (c.EndsWith(':') ? ":" : "")).ToList();
        return "|" + string.Join("|", cells) + "|";
    }

    // ── Whitespace ──────────────────────────────────────────────────────────

    /// <summary>Trailing spaces and runs of blank lines only: indentation (significant in YAML/Python) is untouched.</summary>
    private static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        return BlankRuns.Replace(string.Join("\n", lines), "\n\n").Trim('\n');
    }
}
