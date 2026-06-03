// -----------------------------------------------------------------------------
// <copyright file="CavecrewService.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Cavecrew micro-agents (investigator, builder, reviewer) for delegated code tasks.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

public class CavecrewFileMap
{
    public string FilePath { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public List<CavecrewSymbol> Symbols { get; init; } = new();
}

public class CavecrewSymbol
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int Line { get; init; }
}

public class BuilderSuggestion
{
    public string FilePath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Suggestion { get; init; }
}

public class CavecrewResult
{
    public string Agent { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Details { get; set; } = new();
}

public class CavecrewService
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".java", ".rs",
        ".go", ".rb", ".php", ".swift", ".kt", ".scala", ".cpp", ".c", ".h"
    };

    public async Task<CavecrewResult> InvestigateAsync(string path, CancellationToken ct = default)
    {
        var result = new CavecrewResult { Agent = "cavecrew-investigator" };
        var map = new List<CavecrewFileMap>();

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            result.Summary = "Path not found";
            return result;
        }

        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => SourceExtensions.Contains(Path.GetExtension(f)))
                .Take(50)
                .ToList()
            : new List<string> { path };

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var entry = new CavecrewFileMap
                {
                    FilePath = file,
                    FileType = Path.GetExtension(file).TrimStart('.'),
                    Symbols = ExtractSymbols(content, Path.GetExtension(file))
                };
                if (entry.Symbols.Count > 0)
                    map.Add(entry);
            }
            catch
            {
            }
        }

        var totalSymbols = map.Sum(m => m.Symbols.Count);
        result.Summary = $"Mapped {map.Count} files, {totalSymbols} symbols across {Path.GetFileName(path)}";

        result.Details.Add("Files:");
        foreach (var entry in map.OrderBy(m => m.FilePath))
        {
            var kinds = entry.Symbols.GroupBy(s => s.Kind)
                .Select(g => $"{g.Count()} {g.Key}");
            result.Details.Add($"  {entry.FilePath} [{string.Join(", ", kinds)}]");
            foreach (var sym in entry.Symbols.Take(5))
                result.Details.Add($"    L{sym.Line,5} {sym.Kind,-10} {sym.Name}");
            if (entry.Symbols.Count > 5)
                result.Details.Add($"    ... +{entry.Symbols.Count - 5} more");
        }

        return result;
    }

    public async Task<CavecrewResult> BuildAsync(string description, List<string> files)
    {
        var result = new CavecrewResult
        {
            Agent = "cavecrew-builder",
            Summary = $"Surgical change: {description}",
            Details = new List<string> { $"Files ({files.Count}):" }
        };

        foreach (var file in files)
        {
            result.Details.Add($"  {file}");
            if (!File.Exists(file))
            {
                result.Details.Add($"    ⚠ File not found");
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(file);
                var symbols = ExtractSymbols(content, Path.GetExtension(file));
                if (symbols.Count > 0)
                {
                    var keywords = description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => w.ToLowerInvariant())
                        .Where(w => w.Length > 3)
                        .ToHashSet();

                    var relevant = symbols.Where(s =>
                        keywords.Count == 0 || keywords.Any(k =>
                            s.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();

                    if (relevant.Count > 0)
                    {
                        result.Details.Add($"    Related symbols:");
                        foreach (var s in relevant)
                            result.Details.Add($"      L{s.Line} {s.Kind} {s.Name}");
                    }
                    else
                    {
                        result.Details.Add($"    Symbols available: {string.Join(", ", symbols.Take(3).Select(s => s.Name))}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Details.Add($"    ⚠ Error: {ex.Message}");
            }
        }

        result.Details.Add($"Suggested scope: {files.Count} file(s), {description}");
        return result;
    }

    public CavecrewResult Review(string diffText)
    {
        var result = new CavecrewResult { Agent = "cavecrew-reviewer" };

        if (string.IsNullOrWhiteSpace(diffText))
        {
            result.Summary = "No diff to analyze";
            result.Details.Add("Provide a git diff or patch text for analysis");
            return result;
        }

        var reviewer = new CavemanReviewService();
        var review = reviewer.ReviewDiff(diffText);

        result.Summary = $"Reviewed diff: {review.ChangedFiles} files, {review.TotalIssues} issues";
        result.Details.Add($"Changes: +{review.Additions} / -{review.Deletions} across {review.ChangedFiles} file(s)");
        result.Details.Add($"Issues found: {review.TotalIssues}");

        foreach (var comment in review.Comments.Take(20))
        {
            var icon = comment.Severity switch
            {
                "critical" => "🔴",
                "bug" => "🐛",
                "warning" => "⚠️",
                "perf" => "⚡",
                _ => "ℹ️"
            };
            result.Details.Add($"  {icon} {comment}");
        }

        if (review.TotalIssues > 20)
            result.Details.Add($"  ... +{review.TotalIssues - 20} more issues");

        if (review.TotalIssues == 0)
            result.Details.Add("  ✅ No issues detected");

        return result;
    }

    private List<CavecrewSymbol> ExtractSymbols(string content, string ext)
    {
        var symbols = new List<CavecrewSymbol>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#"))
                continue;

            var lineNum = i + 1;

            var classMatch = Regex.Match(line, @"\b(class|interface|struct|enum|record)\s+(\w+)");
            if (classMatch.Success)
            {
                symbols.Add(new CavecrewSymbol
                {
                    Name = classMatch.Groups[2].Value,
                    Kind = classMatch.Groups[1].Value,
                    Line = lineNum
                });
                continue;
            }

            var methodMatch = Regex.Match(line,
                @"(public|private|protected|internal|static|virtual|override|async|unsafe|\s)+" +
                @"\s+(\w+(?:<[^>]+>)?)\s+(\w+)\s*\(");
            if (methodMatch.Success && !methodMatch.Groups[1].Value.Contains("class "))
            {
                var name = methodMatch.Groups[3].Value;
                if (name != "if" && name != "while" && name != "for" && name != "foreach" && name != "using")
                {
                    symbols.Add(new CavecrewSymbol { Name = name, Kind = "method", Line = lineNum });
                    continue;
                }
            }

            var defMatch = Regex.Match(line, @"^(func|def|function|sub)\s+(\w+)");
            if (defMatch.Success)
            {
                symbols.Add(new CavecrewSymbol
                {
                    Name = defMatch.Groups[2].Value,
                    Kind = defMatch.Groups[1].Value,
                    Line = lineNum
                });
                continue;
            }

            var propMatch = Regex.Match(line,
                @"(public|private|protected|internal|static|readonly)?\s*(\w+(?:<[^>]+>)?)\s+(\w+)\s*\{\s*(get|set)");
            if (propMatch.Success && propMatch.Groups[3].Value.Length > 1)
            {
                symbols.Add(new CavecrewSymbol
                {
                    Name = propMatch.Groups[3].Value,
                    Kind = "property",
                    Line = lineNum
                });
            }
        }

        return symbols;
    }
}
