// -----------------------------------------------------------------------------
// <copyright file="CavemanContentDetector.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Heuristic content-type detector: classifies a string as JSON array, code, log, diff, HTML, search results, or plain text.</summary>
// -----------------------------------------------------------------------------
using System.Text.Json;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Classifies a string into a <see cref="ContentType"/> using purely structural/lexical heuristics.
/// Stateless and dependency-free; instantiate once and reuse.
/// </summary>
public sealed class CavemanContentDetector
{
    private static readonly Regex LogLevelLine = new(
        @"\b(ERROR|WARN(?:ING)?|INFO|DEBUG|FATAL|TRACE)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StackFrameLine = new(
        @"^\s*at\s+\S",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex DiffPlusMinusLine = new(
        @"^(\+\+\+|---)\s",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SearchResultLine = new(
        @"^\s*\d+\.\s+\S",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // grep/ripgrep format: path:linenum:content (not a Windows drive letter before colon)
    private static readonly Regex GrepResultLine = new(
        @"^[A-Za-z0-9_./@\-\\][^:\n]*:\d+:\S",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex UrlInLine = new(
        @"https?://\S+",
        RegexOptions.Compiled);

    private static readonly string[] HtmlMarkers = ["<html", "<!doctype", "<body", "<div", "<p>"];

    private static readonly string[] CodeIndicators =
        ["{", "}", ";", "=>", "->", "def ", "function ", "class ", "import ", "#include", "public ", "private "];

    /// <summary>Classifies <paramref name="content"/> and returns the best-guess <see cref="ContentDetectionResult"/>.</summary>
    public ContentDetectionResult Detect(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ContentDetectionResult { Type = ContentType.PlainText, Confidence = 1.0f };

        var trimmed = content.TrimStart();
        var first = trimmed[0];

        // 1 — JSON Array
        if (first == '[' && content.TrimEnd()[^1] == ']')
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return new ContentDetectionResult { Type = ContentType.JsonArray, Confidence = 0.98f };
            }
            catch (JsonException) { }
        }

        // 2 — JSON Object
        if (first == '{' && content.TrimEnd()[^1] == '}')
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return new ContentDetectionResult { Type = ContentType.JsonObject, Confidence = 0.95f };
            }
            catch (JsonException) { }
        }

        // 3 — Git Diff
        if (trimmed.StartsWith("diff --git", StringComparison.Ordinal) ||
            DiffPlusMinusLine.Matches(content).Count >= 5)
            return new ContentDetectionResult { Type = ContentType.GitDiff, Confidence = 0.92f };

        // 4 — HTML
        var probe = content.Length > 2000 ? content[..2000].ToLowerInvariant() : content.ToLowerInvariant();
        if (HtmlMarkers.Count(m => probe.Contains(m)) >= 3)
            return new ContentDetectionResult { Type = ContentType.Html, Confidence = 0.88f };

        // 5 — Log / Stacktrace
        int stackHits = StackFrameLine.Matches(content).Count;
        int logHits = LogLevelLine.Matches(content).Count;
        if (content.Contains("Traceback (most recent call last)", StringComparison.Ordinal)) stackHits += 3;
        // Require either stack frames OR a high density of log-level keywords (≥5 to avoid false positives on prose)
        if (stackHits >= 3 || (stackHits >= 1 && logHits >= 2) || logHits >= 5)
            return new ContentDetectionResult { Type = ContentType.LogOrStacktrace, Confidence = 0.85f };

        // 6 — Search Results (numbered list, URLs, or grep/ripgrep format)
        int searchLineHits = SearchResultLine.Matches(content).Count;
        int urlHits = UrlInLine.Matches(content).Count;
        int grepHits = GrepResultLine.Matches(content).Count;
        if (searchLineHits >= 5 || (searchLineHits >= 3 && urlHits >= 3) || grepHits >= 5)
            return new ContentDetectionResult { Type = ContentType.SearchResults, Confidence = 0.75f };

        // 7 — Tabular (CSV or markdown table)
        var firstLine = content.TrimStart().Split('\n')[0];
        bool isMdTable = firstLine.TrimStart().StartsWith('|') && firstLine.TrimEnd().EndsWith('|');
        bool isCsv = !isMdTable && (firstLine.Contains(',') || firstLine.Contains('\t') || firstLine.Contains(';'))
                     && content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length >= 3;
        if (isMdTable || isCsv)
            return new ContentDetectionResult { Type = ContentType.Tabular, Confidence = 0.72f };

        // 8 — Code
        int codeMatches = CodeIndicators.Count(ind => content.Contains(ind, StringComparison.Ordinal));
        if (codeMatches >= 4)
            return new ContentDetectionResult { Type = ContentType.Code, Confidence = 0.70f };

        // 9 — Plain text fallback
        return new ContentDetectionResult { Type = ContentType.PlainText, Confidence = 1.0f };
    }
}
