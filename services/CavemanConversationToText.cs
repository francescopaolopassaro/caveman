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

public static class CavemanConversationToText
{
    public static string ExtractTextFromMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var text = markdown;

        text = RemoveYamlFrontMatter(text);
        text = RemoveJsonFencedBlocks(text);
        text = RemoveInlineJson(text);
        text = ExtractHtmlText(text);
        text = RemoveImages(text);
        text = ConvertLinksToText(text);
        text = RemoveFencedCodeBlocks(text);
        text = RemoveInlineCode(text);
        text = RemoveHorizontalRules(text);
        text = RemoveHeadingMarkers(text);
        text = RemoveBoldItalic(text);
        text = RemoveBlockquoteMarkers(text);
        text = RemoveListMarkers(text);
        text = CollapseWhitespace(text);

        return text.Trim();
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
