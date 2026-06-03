// -----------------------------------------------------------------------------
// <copyright file="CavemanCommitGenerator.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Generates ultra-compact conventional commit messages from git diffs.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

public class CommitSuggestion
{
    public string FullMessage { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Scope { get; init; }
    public string Subject { get; init; } = string.Empty;
    public int SubjectLength => Subject.Length;
}

public class CavemanCommitGenerator
{
    private static readonly string[] ConventionalTypes = { "feat", "fix", "docs", "style", "refactor", "perf", "test", "chore", "ci" };

    private static readonly HashSet<string> BreakingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "breaking", "breaking change", "major", "remove", "removed", "api change"
    };

    private static readonly Dictionary<string, string> TypePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        { "feat:", "feat" }, { "feature:", "feat" }, { "add", "feat" }, { "new", "feat" }, { "implement", "feat" },
        { "bug", "fix" }, { "fix", "fix" }, { "fixes", "fix" }, { "fixed", "fix" }, { "hotfix", "fix" }, { "patch", "fix" },
        { "doc", "docs" }, { "docs", "docs" }, { "document", "docs" },
        { "style", "style" }, { "format", "style" },
        { "refactor", "refactor" }, { "refactoring", "refactor" },
        { "perf", "perf" }, { "performance", "perf" }, { "optimize", "perf" },
        { "test", "test" }, { "tests", "test" },
        { "chore", "chore" }, { "bump", "chore" }, { "update dep", "chore" }, { "upgrade", "chore" },
        { "ci", "ci" }, { "pipeline", "ci" }, { "build", "ci" }
    };

    public CommitSuggestion GenerateFromDiff(string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
            return new CommitSuggestion { FullMessage = "chore: empty diff", Type = "chore", Subject = "empty diff" };

        var lines = diffText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var filePaths = new HashSet<string>();
        var addedLines = new List<string>();
        var removedLines = new List<string>();
        var hasBreaking = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("+++ b/") || line.StartsWith("--- a/"))
            {
                var path = line.Substring(6).Trim();
                if (!string.IsNullOrEmpty(path) && path != "/dev/null")
                    filePaths.Add(path);
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                var content = line.Substring(1).Trim();
                if (content.Length > 0)
                    addedLines.Add(content);
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                var content = line.Substring(1).Trim();
                if (content.Length > 0)
                    removedLines.Add(content);
            }
        }

        foreach (var line in addedLines.Concat(removedLines))
        {
            if (BreakingKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                hasBreaking = true;
                break;
            }
        }

        var type = DetectType(addedLines, removedLines);
        var scope = DetectScope(filePaths);
        var subject = BuildSubject(type, addedLines, removedLines, filePaths);

        var prefix = hasBreaking ? $"{type}!" : type;
        var scopePart = scope != null ? $"({scope})" : "";
        var fullMessage = hasBreaking
            ? $"{prefix}{scopePart}: {subject}"
            : $"{type}{scopePart}: {subject}";

        if (fullMessage.Length > 50 && subject.Length > 10)
        {
            var maxSubjectLen = 50 - (type.Length + (scope?.Length ?? 0) + 4);
            if (maxSubjectLen > 5 && subject.Length > maxSubjectLen)
                subject = subject[..(maxSubjectLen - 1)] + "…";
            fullMessage = $"{type}{scopePart}: {subject}";
        }

        return new CommitSuggestion
        {
            FullMessage = fullMessage,
            Type = type,
            Scope = scope,
            Subject = subject
        };
    }

    private string DetectType(List<string> added, List<string> removed)
    {
        var allContent = string.Join(" ", added.Concat(removed)).ToLowerInvariant();

        foreach (var kv in TypePatterns)
        {
            if (allContent.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        if (removed.Count > added.Count)
            return "fix";

        return "feat";
    }

    private static string? DetectScope(HashSet<string> paths)
    {
        var dirs = paths
            .Select(p => p.Replace('\\', '/'))
            .Select(p => p.Split('/').FirstOrDefault())
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        if (dirs.Count == 1)
        {
            var dir = dirs[0] ?? "";
            return dir.Length > 15 || dir.Length == 0 ? null : dir.TrimEnd(':');
        }

        return null;
    }

        private string BuildSubject(string type, List<string> added, List<string> removed, HashSet<string> files)
        {
            var keywords = new List<string>();

            foreach (var line in added.Concat(removed))
            {
                var words = Regex.Matches(line, @"\b[A-Z][a-z]+|[a-z]{3,}\b")
                    .Select(m => m.Value.ToLowerInvariant())
                    .Where(w => !StopWords.Contains(w))
                    .Take(3);

                keywords.AddRange(words);
            }

            if (keywords.Count == 0)
            {
                var fileNames = files
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(n => n != null)
                    .Take(2)
                    .Select(n => n!.ToLowerInvariant());
                keywords.AddRange(fileNames);
            }

            var unique = keywords.Distinct().Take(5).ToList();
            if (unique.Count == 0)
                return "update project files";

            var subject = string.Join(" ", unique);

            if (type == "fix" && subject.Length > 3)
                subject = "handle " + subject;

            if (subject.Length > 45)
                subject = subject[..42] + "…";

            return subject;
        }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "can", "shall", "to", "of", "in", "for",
        "on", "with", "at", "by", "from", "as", "into", "through", "during",
        "before", "after", "above", "below", "between", "out", "off", "over",
        "under", "again", "further", "then", "once", "this", "that", "these",
        "those", "not", "or", "and", "but", "if", "because", "so", "than",
        "too", "very", "just", "also", "about", "now", "get", "got", "set"
    };
}
