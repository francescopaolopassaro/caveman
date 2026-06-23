// -----------------------------------------------------------------------------
// <copyright file="ICompressionService.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Abstraction over the prompt-compression engine for dependency injection / swapping.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;
using caveman.core.services;

namespace caveman.core;

/// <summary>
/// The prompt-compression engine surface. Implemented by <see cref="CavemanCompressionService"/>;
/// inject a custom implementation to swap or mock the engine.
/// </summary>
public interface ICompressionService
{
    /// <summary>Compresses <paramref name="input"/> at the given level, auto-detecting the language.</summary>
    Task<CompressionResult> CompressAsync(string input, CavemanCompressionLevel level, CancellationToken ct = default);

    /// <summary>Compresses <paramref name="input"/>, optionally applying a custom filter.</summary>
    Task<CompressionResult> CompressAsync(string input, CavemanCompressionLevel level, CompressionFilter? customFilter, CancellationToken ct = default);

    /// <summary>Compresses many prompts at once (order preserved).</summary>
    Task<CompressionResult[]> CompressBatchAsync(IEnumerable<string> inputs, CavemanCompressionLevel level, CancellationToken ct = default);

    /// <summary>Compresses many prompts at once with an optional custom filter.</summary>
    Task<CompressionResult[]> CompressBatchAsync(IEnumerable<string> inputs, CavemanCompressionLevel level, CompressionFilter? customFilter, CancellationToken ct = default);

    /// <summary>Compresses for an explicit language (ISO 639-3), skipping detection.</summary>
    CompressionResult ApplyCompression(string input, string iso3, CavemanCompressionLevel level, CompressionFilter? customFilter = null);

    /// <summary>Detects the language of the input (ISO 639-3).</summary>
    string DetectLanguage(string input);

    /// <summary>Per-language detection confidence scores.</summary>
    IReadOnlyDictionary<string, double> DetectLanguageScores(string input);

    /// <summary>Releases cached per-language data.</summary>
    void ReleaseMemory();

    /// <summary>
    /// Routes <paramref name="content"/> through the content-aware pipeline:
    /// JSON arrays are crushed, plain prose is NLP-compressed, code/diffs/logs/HTML are passed through unchanged.
    /// </summary>
    Task<RoutedCompressionResult> CompressContentAsync(
        string content,
        string? query = null,
        CancellationToken ct = default)
        => Task.FromResult(new RoutedCompressionResult
        {
            Original = content,
            Compressed = content,
            StrategyUsed = "Passthrough",
            TokensBefore = 0,
            TokensAfter = 0
        });
}
