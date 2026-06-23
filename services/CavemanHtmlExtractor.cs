// -----------------------------------------------------------------------------
// <copyright file="CavemanHtmlExtractor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Extracts readable text from HTML strings using pure regex: removes scripts/styles, converts block elements to newlines, decodes entities.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;
using System.Web;

namespace caveman.core.services;

/// <summary>
/// Extracts readable text content from an HTML string without any external dependencies.
/// Removes scripts, styles and comments; converts headings and block elements to
/// newline-separated text; decodes HTML entities. Output is plain text suitable for
/// NLP compression or direct LLM ingestion.
/// </summary>
public sealed class CavemanHtmlExtractor
{
    // Remove <script>...</script> and <style>...</style> blocks (including multi-line)
    private static readonly Regex ScriptBlock = new(
        @"<script[^>]*>[\s\S]*?</script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StyleBlock = new(
        @"<style[^>]*>[\s\S]*?</style>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // HTML comments
    private static readonly Regex HtmlComment = new(
        @"<!--[\s\S]*?-->",
        RegexOptions.Compiled);

    // Block elements that should become newlines
    private static readonly Regex BlockElement = new(
        @"<(?:/?(?:p|div|article|section|header|footer|main|nav|aside|h[1-6]|ul|ol|li|table|tr|thead|tbody|tfoot|blockquote|pre|figure|figcaption|br)\b)[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Headings — prepend a # marker so structure is visible
    private static readonly Regex HeadingOpen = new(
        @"<h([1-6])[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // All remaining tags
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);

    // Normalize excessive whitespace
    private static readonly Regex MultipleNewlines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingSpaces = new(@"[ \t]+\n", RegexOptions.Compiled);

    /// <summary>
    /// Extracts plain text from <paramref name="html"/>.
    /// Returns the original string when it contains no HTML markers.
    /// </summary>
    public string Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? string.Empty;

        // Quick check: skip if no HTML present
        if (!html.Contains('<')) return html;

        var text = html;
        text = ScriptBlock.Replace(text, string.Empty);
        text = StyleBlock.Replace(text, string.Empty);
        text = HtmlComment.Replace(text, string.Empty);

        // Convert headings to markdown-style markers before stripping
        text = HeadingOpen.Replace(text, m =>
        {
            int level = int.Parse(m.Groups[1].Value);
            return "\n" + new string('#', level) + " ";
        });

        // Block elements → newline
        text = BlockElement.Replace(text, "\n");

        // Strip remaining tags
        text = AnyTag.Replace(text, string.Empty);

        // Decode HTML entities (&amp; &lt; &gt; &nbsp; numeric refs, etc.)
        text = HttpUtility.HtmlDecode(text);

        // Normalize whitespace
        text = TrailingSpaces.Replace(text, "\n");
        text = MultipleNewlines.Replace(text, "\n\n");

        return text.Trim();
    }
}
