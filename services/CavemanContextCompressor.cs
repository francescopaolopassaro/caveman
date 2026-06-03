// -----------------------------------------------------------------------------
// <copyright file="CavemanContextCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses project context files (CLAUDE.md, notes, ...) into caveman-speak.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

public class ContextCompressResult
{
    public string FilePath { get; init; } = string.Empty;
    public string OriginalContent { get; init; } = string.Empty;
    public string CompressedContent { get; init; } = string.Empty;
    public int OriginalTokens { get; init; }
    public int CompressedTokens { get; init; }
    public double SavingsPercent => OriginalTokens == 0 ? 0 : (1.0 - (double)CompressedTokens / OriginalTokens) * 100;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string? ErrorMessage { get; init; }
}

public class CavemanContextCompressor
{
    private readonly CavemanCompressionService _compressor;
    private static readonly HashSet<string> ContextFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLAUDE.md", "README.md", "TODO", "TODO.md", "TASKS.md",
        "CHANGELOG.md", "CONTRIBUTING.md", "ARCHITECTURE.md",
        "notes.md", "NOTES.md", "CONTEXT.md", "AGENTS.md",
        "ROADMAP.md", "PLAN.md"
    };

    public CavemanContextCompressor(CavemanCompressionService? compressor = null)
    {
        _compressor = compressor ?? new CavemanCompressionService();
    }

    public async Task<ContextCompressResult> CompressFileAsync(
        string filePath,
        CavemanCompressionLevel level = CavemanCompressionLevel.Aggressive)
    {
        try
        {
            if (!File.Exists(filePath))
                return new ContextCompressResult { FilePath = filePath, ErrorMessage = "File not found" };

            var content = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(content))
                return new ContextCompressResult { FilePath = filePath, OriginalContent = content, CompressedContent = content };

            var result = await _compressor.CompressAsync(content, level);

            return new ContextCompressResult
            {
                FilePath = filePath,
                OriginalContent = content,
                CompressedContent = result.CompressedText,
                OriginalTokens = result.OriginalTokens,
                CompressedTokens = result.CompressedTokens,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ContextCompressResult
            {
                FilePath = filePath,
                ErrorMessage = $"Compression failed: {ex.Message}"
            };
        }
    }

    public async Task<List<ContextCompressResult>> CompressDirectoryAsync(
        string directoryPath,
        CavemanCompressionLevel level = CavemanCompressionLevel.Aggressive,
        CancellationToken ct = default)
    {
        var results = new List<ContextCompressResult>();

        if (!Directory.Exists(directoryPath))
            return results;

        foreach (var pattern in ContextFilePatterns)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = Path.Combine(directoryPath, pattern);
            if (File.Exists(filePath))
            {
                var result = await CompressFileAsync(filePath, level);
                results.Add(result);
            }
        }

        return results;
    }

    public async Task<string> GenerateCompressedContextAsync(
        string directoryPath,
        CavemanCompressionLevel level = CavemanCompressionLevel.Aggressive,
        CancellationToken ct = default)
    {
        var results = await CompressDirectoryAsync(directoryPath, level, ct);
        if (results.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "## CAVEMAN-COMPRESSED CONTEXT",
            $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            $"Compression: {level}",
            $"Files: {results.Count}",
            "",
        };

        int totalOriginal = 0, totalCompressed = 0;

        foreach (var result in results)
        {
            if (result.HasError)
            {
                lines.Add($"### {Path.GetFileName(result.FilePath)} [ERROR: {result.ErrorMessage}]");
                lines.Add("");
                continue;
            }

            totalOriginal += result.OriginalTokens;
            totalCompressed += result.CompressedTokens;

            lines.Add($"### {Path.GetFileName(result.FilePath)}");
            lines.Add($"[SAVED: {result.OriginalTokens} → {result.CompressedTokens} tokens, {result.SavingsPercent:F1}%]");
            lines.Add("");
            lines.Add(result.CompressedContent);
            lines.Add("");
        }

        var totalSavings = totalOriginal == 0 ? 0 : (1.0 - (double)totalCompressed / totalOriginal) * 100;
        lines.Insert(4, $"Total tokens: {totalOriginal} → {totalCompressed} ({totalSavings:F1}% saved)");

        return string.Join(Environment.NewLine, lines);
    }
}
