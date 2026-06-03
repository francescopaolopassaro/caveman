// -----------------------------------------------------------------------------
// <copyright file="CavemanServicesPlugin.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Semantic Kernel plugin exposing the Caveman developer services (compress, commit, review, stats, safety).</summary>
// -----------------------------------------------------------------------------
using System.ComponentModel;
using caveman.core.services;
using Microsoft.SemanticKernel;

namespace caveman.core.SemanticKernel.Plugin;

public class CavemanServicesPlugin
{
    private readonly CavemanCommitGenerator _commitGen;
    private readonly CavemanReviewService _reviewer;
    private readonly CavecrewService _cavecrew;
    private readonly CavemanSafetyGuard _safety;
    private readonly CavemanStatsTracker _stats;
    private readonly CavemanContextCompressor _contextCompressor;

    public CavemanServicesPlugin(
        CavemanCommitGenerator? commitGen = null,
        CavemanReviewService? reviewer = null,
        CavecrewService? cavecrew = null,
        CavemanSafetyGuard? safety = null,
        CavemanStatsTracker? stats = null,
        CavemanContextCompressor? contextCompressor = null)
    {
        _commitGen = commitGen ?? new CavemanCommitGenerator();
        _reviewer = reviewer ?? new CavemanReviewService();
        _cavecrew = cavecrew ?? new CavecrewService();
        _safety = safety ?? new CavemanSafetyGuard();
        _stats = stats ?? new CavemanStatsTracker();
        _contextCompressor = contextCompressor ?? new CavemanContextCompressor();
    }

    [KernelFunction("generate_commit")]
    [Description("Generates an ultra-compact conventional commit message from a git diff. Returns type(scope): subject under 50 chars.")]
    public string GenerateCommit(
        [Description("The raw git diff/patch text to analyze")] string diffText)
    {
        var commit = _commitGen.GenerateFromDiff(diffText);
        return commit.FullMessage;
    }

    [KernelFunction("review_diff")]
    [Description("Analyzes a git diff and returns single-line code review comments. Detects bugs, security issues, performance concerns, and TODOs.")]
    public string ReviewDiff(
        [Description("The git diff/patch text to review")] string diffText)
    {
        var review = _reviewer.ReviewDiff(diffText);
        if (review.TotalIssues == 0)
            return "✅ No issues found.";

        var lines = review.Comments.Select(c => c.ToString()).ToList();
        return string.Join("\n", lines);
    }

    [KernelFunction("check_safety")]
    [Description("Checks a message for security-critical or destructive command patterns. Returns whether compression is safe to apply.")]
    public string CheckSafety(
        [Description("The message or command to check")] string message)
    {
        var verdict = _safety.Check(message);
        return $"Level: {verdict.Level}\nReason: {verdict.Reason}\nShouldCompress: {verdict.ShouldCompress}";
    }

    [KernelFunction("get_stats")]
    [Description("Returns current token and dollar savings statistics for the session and lifetime.")]
    public string GetStats()
    {
        return _stats.FormatFullReport();
    }

    [KernelFunction("track_compression")]
    [Description("Records a compression result into the stats tracker.")]
    public void TrackCompression(
        [Description("Original token count")] int originalTokens,
        [Description("Compressed token count")] int compressedTokens)
    {
        _stats.TrackResult(new entities.CompressionResult
        {
            CompressedText = "",
            OriginalTokens = originalTokens,
            CompressedTokens = compressedTokens
        });
    }

    [KernelFunction("investigate_project")]
    [Description("Scans a directory and maps classes, methods, and function definitions. Use to understand codebase structure.")]
    public async Task<string> InvestigateProject(
        [Description("Absolute or relative path to the project folder")] string projectPath)
    {
        var result = await _cavecrew.InvestigateAsync(projectPath);
        var output = $"[{result.Agent}] {result.Summary}\n";
        output += string.Join("\n", result.Details);
        return output;
    }

    [KernelFunction("compress_context")]
    [Description("Compresses project context files (CLAUDE.md, TODO, README.md) into caveman-speak for longer AI context windows.")]
    public async Task<string> CompressContext(
        [Description("Directory path containing context files")] string directoryPath)
    {
        var markdown = await _contextCompressor.GenerateCompressedContextAsync(directoryPath);
        return string.IsNullOrEmpty(markdown) ? "No context files found in the specified directory." : markdown;
    }

    [KernelFunction("reset_stats")]
    [Description("Resets the session statistics counter.")]
    public string ResetStats()
    {
        _stats.ResetSession();
        return "Session stats reset.";
    }
}
