// -----------------------------------------------------------------------------
// <copyright file="CavemanWasteAnalyzer.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Detects and estimates token waste in LLM content: HTML noise, base64 blobs, excessive whitespace, and large JSON blocks.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Analyzes a string (or a conversation's messages) for token waste patterns: HTML tags,
/// large base64-encoded blobs, excessive whitespace, and large inline JSON blocks.
/// Returns a <see cref="WasteAnalysis"/> with per-category estimated token counts.
/// Does not modify the content; use alongside a compressor to act on the findings.
/// </summary>
public sealed class CavemanWasteAnalyzer
{
    // HTML tags and comments
    private static readonly Regex HtmlNoise = new(
        @"<[^>]{1,200}>|<!--[\s\S]*?-->",
        RegexOptions.Compiled);

    // Base64-encoded blobs: 50+ consecutive base64 characters
    private static readonly Regex Base64Blob = new(
        @"[A-Za-z0-9+/]{50,}={0,2}",
        RegexOptions.Compiled);

    // Excessive whitespace: 4+ consecutive spaces, or 3+ consecutive newlines
    private static readonly Regex ExcessWhitespace = new(
        @"[ \t]{4,}|\n{3,}",
        RegexOptions.Compiled);

    // Large JSON blocks: { ... } spanning 500+ characters
    private static readonly Regex JsonBloat = new(
        @"\{[\s\S]{500,?}\}",
        RegexOptions.Compiled);

    // Rough token estimation: ~4 chars per token (GPT-style)
    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    /// <summary>Analyzes <paramref name="content"/> for token waste patterns.</summary>
    public WasteAnalysis Analyze(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new WasteAnalysis();

        int htmlNoise = HtmlNoise.Matches(content).Sum(m => EstimateTokens(m.Value));
        int base64 = Base64Blob.Matches(content).Sum(m => EstimateTokens(m.Value));
        int whitespace = ExcessWhitespace.Matches(content).Sum(m => EstimateTokens(m.Value));
        int jsonBloat = JsonBloat.Matches(content).Sum(m => EstimateTokens(m.Value));

        return new WasteAnalysis
        {
            HtmlNoiseTokens = htmlNoise,
            Base64Tokens = base64,
            WhitespaceTokens = whitespace,
            JsonBloatTokens = jsonBloat
        };
    }

    /// <summary>
    /// Analyzes multiple message content strings and returns the aggregate waste.
    /// Useful for scanning a full conversation before compressing it.
    /// </summary>
    public WasteAnalysis AnalyzeMessages(IEnumerable<string> messageContents)
    {
        int html = 0, b64 = 0, ws = 0, json = 0;
        foreach (var msg in messageContents)
        {
            var a = Analyze(msg);
            html += a.HtmlNoiseTokens;
            b64 += a.Base64Tokens;
            ws += a.WhitespaceTokens;
            json += a.JsonBloatTokens;
        }
        return new WasteAnalysis { HtmlNoiseTokens = html, Base64Tokens = b64, WhitespaceTokens = ws, JsonBloatTokens = json };
    }
}
