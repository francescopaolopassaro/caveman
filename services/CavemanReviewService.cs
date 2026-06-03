// -----------------------------------------------------------------------------
// <copyright file="CavemanReviewService.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Generates single-line pull-request review comments from diffs.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

public class ReviewComment
{
    public int Line { get; init; }
    public string Severity { get; init; } = "info";
    public string? Emoji { get; init; }
    public string Message { get; init; } = string.Empty;
    public override string ToString() => $"L{Line}: {Emoji} {Severity}: {Message}";
}

public class ReviewResult
{
    public List<ReviewComment> Comments { get; set; } = new();
    public int ChangedFiles { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int TotalIssues => Comments.Count;
}

public class CavemanReviewService
{
    private static readonly HashSet<string> BugPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "nullptr", "null reference", "nullreference",
        "exception", "throw", "catch",
        "todo", "hack", "fixme", "xxx",
        "undefined", "undef",
        "memory leak", "deadlock",
        "infinite loop", "crash",
        "sqli", "xss", "injection", "exploit"
    };

    private static readonly HashSet<string> SecurityPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "apikey", "api_key",
        "connectionstring", "connstring",
        "plaintext", "base64", "decrypt",
        "eval(", "exec(", "shell_exec"
    };

    private static readonly HashSet<string> PerfPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "for(", "foreach(", "while(", "n+1", "select *",
        "orderby", "sort", "distinct",
        "recursive", "iteration"
    };

    public ReviewResult ReviewDiff(string diffText)
    {
        var result = new ReviewResult();
        if (string.IsNullOrWhiteSpace(diffText))
            return result;

        var lines = diffText.Split('\n');
        int currentLine = 0;
        string? currentFile = null;
        bool inHunk = false;

        foreach (var line in lines)
        {
            var fileMatch = Regex.Match(line, @"^\+\+\+ b/(.+)");
            if (fileMatch.Success)
            {
                currentFile = fileMatch.Groups[1].Value;
                result.ChangedFiles++;
                inHunk = false;
                continue;
            }

            var hunkMatch = Regex.Match(line, @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@");
            if (hunkMatch.Success)
            {
                currentLine = int.Parse(hunkMatch.Groups[1].Value);
                inHunk = true;
                continue;
            }

            if (!inHunk) continue;

            if (line.StartsWith("+"))
            {
                result.Additions++;
                var content = line.Substring(1);

                var comment = AnalyzeLine(currentLine, content, currentFile);
                if (comment != null)
                    result.Comments.Add(comment);
            }
            else if (line.StartsWith("-"))
            {
                result.Deletions++;
            }

            if (line.StartsWith("+") || line.StartsWith("-") || line.StartsWith(" "))
                currentLine++;
        }

        return result;
    }

    private ReviewComment? AnalyzeLine(int line, string content, string? file)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
            return null;

        if (content.Length > 200)
            return new ReviewComment
            {
                Line = line,
                Severity = "warning",
                Emoji = "\U0001f4c4",
                Message = "long line " + content.Length + "ch. Consider split."
            };

        foreach (var pattern in SecurityPatterns)
        {
            if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                var ctx = ExtractContext(content, pattern);
                return new ReviewComment
                {
                    Line = line,
                    Severity = "critical",
                    Emoji = "\U0001f6a8",
                    Message = $"security: possible {ctx} leak"
                };
            }
        }

        foreach (var pattern in BugPatterns)
        {
            if (content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (pattern == "null" || pattern == "nullptr")
                {
                    if (!content.Contains("!=") && !content.Contains("== null") && !content.Contains("is null") &&
                        !content.Contains("?.", StringComparison.Ordinal) && !content.Contains("??", StringComparison.Ordinal))
                        continue;
                }

                var ctx = ExtractContext(content, pattern);
                return new ReviewComment
                {
                    Line = line,
                    Severity = pattern == "todo" ? "info" : "bug",
                    Emoji = pattern == "todo" ? "\u2705" : "\U0001f534",
                    Message = $"{pattern}: {ctx}"
                };
            }
        }

        foreach (var pattern in PerfPatterns)
        {
            if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                var ctx = ExtractContext(content, pattern);
                return new ReviewComment
                {
                    Line = line,
                    Severity = "perf",
                    Emoji = "\u26a1",
                    Message = $"perf: {ctx} may impact performance"
                };
            }
        }

        if (Regex.IsMatch(content, @"^\s*var\s+\w+\s*=\s*null;?$"))
        {
            return new ReviewComment
            {
                Line = line,
                Severity = "warning",
                Emoji = "\U0001f6a7",
                Message = "var init null. Use default or nullable?"
            };
        }

        if (Regex.IsMatch(content, @"try\s*\{", RegexOptions.IgnoreCase) &&
            !content.Contains("catch", StringComparison.OrdinalIgnoreCase) &&
            line > 0)
        {
            return new ReviewComment
            {
                Line = line,
                Severity = "warning",
                Emoji = "\U0001f6a7",
                Message = "bare try without catch"
            };
        }

        if (content.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            var todo = ExtractContext(content, "todo");
            return new ReviewComment
            {
                Line = line,
                Severity = "info",
                Emoji = "\u2705",
                Message = "todo: " + todo
            };
        }

        return null;
    }

    private static string ExtractContext(string content, string pattern)
    {
        var idx = content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return pattern;

        var start = Math.Max(0, idx - 15);
        var end = Math.Min(content.Length, idx + pattern.Length + 15);
        var ctx = content[start..end].Trim();
        return ctx.Length > 30 ? ctx[..27] + "…" : ctx;
    }
}
