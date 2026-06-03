// -----------------------------------------------------------------------------
// <copyright file="CompressionResult.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Result model for a compression operation: text, token counts, efficiency and sustainability estimates.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.entities;

/// <summary>Outcome of a compression operation: the text, token counts and savings estimates.</summary>
public class CompressionResult
{
    /// <summary>The compressed text (or the original input on error / level None).</summary>
    public string CompressedText { get; set; } = string.Empty;

    /// <summary>Approximate token count of the original input (whitespace-based).</summary>
    public int OriginalTokens { get; set; }

    /// <summary>Approximate token count of the compressed text.</summary>
    public int CompressedTokens { get; set; }

    /// <summary>Tokens saved (never negative).</summary>
    public int SavedTokens => Math.Max(0, OriginalTokens - CompressedTokens);

    /// <summary>Percentage of tokens removed, 0–100.</summary>
    public double EfficiencyPercentage => OriginalTokens == 0 ? 0 : (double)SavedTokens / OriginalTokens * 100;

    /// <summary>Estimated energy saved, in mWh (~0.005 mWh per saved token).</summary>
    public double EstimatedEnergySavedMWh => SavedTokens * 0.005;

    /// <summary>Estimated CO₂ avoided, in milligrams (~0.4 mg per mWh).</summary>
    public double EstimatedCO2SavedMg => EstimatedEnergySavedMWh * 0.4;

    /// <summary>GPT-tokenizer token count of the original input, when a tokenizer was supplied.</summary>
    public int GptOriginalTokens { get; set; }

    /// <summary>GPT-tokenizer token count of the compressed text, when a tokenizer was supplied.</summary>
    public int GptCompressedTokens { get; set; }

    /// <summary>GPT-tokenizer tokens saved (never negative).</summary>
    public int GptSavedTokens => Math.Max(0, GptOriginalTokens - GptCompressedTokens);

    /// <summary>Non-null when the operation failed or produced a warning.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>True when <see cref="ErrorMessage"/> is set.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}
