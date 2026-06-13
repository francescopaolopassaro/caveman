// -----------------------------------------------------------------------------
// <copyright file="CavemanConversationToText.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Extracts clean plain text from markdown, excluding JSON blocks, for use with RankAndSummarize.</summary>
// -----------------------------------------------------------------------------
using System.Net;
using System.Text.RegularExpressions;

namespace caveman.core.services;

/// <summary>Controls which parts of a markdown/JSON/HTML blob are stripped vs. kept.</summary>
public sealed class MarkdownExtractOptions
{
    /// <summary>Keep JSON (fenced ```json blocks and inline objects) instead of removing it.</summary>
    public bool KeepJson { get; set; }

    /// <summary>Keep fenced code blocks (and inline code) verbatim instead of removing them.</summary>
    public bool KeepCode { get; set; }

    /// <summary>Extract the inner text of HTML elements (default). When false, HTML is left untouched.</summary>
    public bool ExtractHtml { get; set; } = true;

    /// <summary>The defaults reproduce the historical behavior (strip JSON + code, extract HTML text).</summary>
    public static MarkdownExtractOptions Default { get; } = new();
}

public static class CavemanConversationToText
{
    /// <summary>Extracts clean plain text from markdown, stripping JSON and code (historical behavior).</summary>
    public static string ExtractTextFromMarkdown(string markdown)
        => ExtractTextFromMarkdown(markdown, MarkdownExtractOptions.Default);

    /// <summary>Extracts clean text from markdown, honoring <paramref name="options"/> for JSON/code/HTML.</summary>
    public static string ExtractTextFromMarkdown(string markdown, MarkdownExtractOptions options)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        options ??= MarkdownExtractOptions.Default;
        var text = markdown;

        text = RemoveYamlFrontMatter(text);

        if (options.KeepJson)
        {
            // Keep the JSON body as plain text: drop only the ```json fence markers so it
            // survives the code-stripping steps below.
            text = UnwrapJsonFences(text);
        }
        else
        {
            text = RemoveJsonFencedBlocks(text);
            text = RemoveInlineJson(text);
        }

        if (options.ExtractHtml)
            text = ExtractHtmlText(text);

        text = RemoveImages(text);
        text = ConvertLinksToText(text);

        if (!options.KeepCode)
        {
            // JSON fences (if kept) were already unwrapped, so this only strips real code.
            text = RemoveFencedCodeBlocks(text);
            text = RemoveInlineCode(text);
        }

        text = RemoveHorizontalRules(text);
        text = RemoveHeadingMarkers(text);
        text = RemoveBoldItalic(text);
        text = RemoveBlockquoteMarkers(text);
        text = RemoveListMarkers(text);
        text = CollapseWhitespace(text);

        return text.Trim();
    }

    private static string UnwrapJsonFences(string text)
    {
        return Regex.Replace(text, @"```json[ \t]*\r?\n?([\s\S]*?)```", "$1",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }

    private static string RemoveYamlFrontMatter(string text)
    {
        return Regex.Replace(text, @"^---[\s\S]*?---\s*", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveJsonFencedBlocks(string text)
    {
        return Regex.Replace(text, @"```json[\s\S]*?```\s*", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveFencedCodeBlocks(string text)
    {
        return Regex.Replace(text, @"```[\s\S]*?```\s*", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveInlineCode(string text)
    {
        return Regex.Replace(text, @"`[^`]+`", string.Empty);
    }

    private static string RemoveImages(string text)
    {
        return Regex.Replace(text, @"!\[.*?\]\(.*?\)", string.Empty);
    }

    private static string ConvertLinksToText(string text)
    {
        return Regex.Replace(text, @"\[([^\]]*)\]\(.*?\)", "$1");
    }

    private static string RemoveHeadingMarkers(string text)
    {
        return Regex.Replace(text, @"^#{1,6}\s+", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveBoldItalic(string text)
    {
        text = Regex.Replace(text, @"(\*\*\*|___)(.*?)\1", "$2");
        text = Regex.Replace(text, @"(\*\*|__)(.*?)\1", "$2");
        text = Regex.Replace(text, @"(\*|_)(.*?)\1", "$2");
        return text;
    }

    private static string RemoveHorizontalRules(string text)
    {
        return Regex.Replace(text, @"^\s*(-{3,}|\*{3,}|_{3,})\s*$", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveBlockquoteMarkers(string text)
    {
        return Regex.Replace(text, @"^>\s?", string.Empty, RegexOptions.Multiline);
    }

    private static string RemoveListMarkers(string text)
    {
        text = Regex.Replace(text, @"^\s*[-*+]\s+", string.Empty, RegexOptions.Multiline);
        text = Regex.Replace(text, @"^\s*\d+\.\s+", string.Empty, RegexOptions.Multiline);
        return text;
    }

    private static string RemoveInlineJson(string text)
    {
        while (true)
        {
            var stripped = Regex.Replace(text, @"\{[^{}]*\}", m =>
                LooksLikeJson(m.Value) ? string.Empty : m.Value);
            stripped = Regex.Replace(stripped, @"\[[^\[\]]*\]", m =>
                LooksLikeJson(m.Value) ? string.Empty : m.Value);
            if (stripped.Length == text.Length)
                break;
            text = stripped;
        }
        return text;
    }

    private static bool LooksLikeJson(string block)
    {
        return Regex.IsMatch(block, @"\""[^\""]*\""\s*:");
    }

    private static string ExtractHtmlText(string text)
    {
        // Drop script/style blocks entirely: their content is not prose.
        text = Regex.Replace(text, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", " ",
            RegexOptions.IgnoreCase);

        // Turn line breaks and block-level closing tags into separators so that the
        // extracted content reads as plain text instead of merging into a single line.
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            @"</(p|div|li|tr|td|th|h[1-6]|table|ul|ol|section|article|header|footer|blockquote)>",
            "\n", RegexOptions.IgnoreCase);

        // Strip every remaining tag, replacing it with a space to avoid gluing words together.
        text = Regex.Replace(text, @"<[^>]+>", " ");

        // Decode HTML entities (&nbsp;, &amp;, &#233; ...) and normalize exotic spaces.
        text = WebUtility.HtmlDecode(text);
        text = text.Replace((char)0x00A0, ' ').Replace((char)0x2007, ' ').Replace((char)0x202F, ' ').Replace((char)0xFEFF, ' ');

        return text;
    }

    private static string CollapseWhitespace(string text)
    {
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"[ \t]+", " ");
        return text.Trim();
    }
}
