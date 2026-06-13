// -----------------------------------------------------------------------------
// <copyright file="CavemanCostEstimator.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Indicative cost estimation (USD/EUR) for token savings; prices are approximate and overridable.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.services;

/// <summary>
/// Estimates the monetary value of token savings. Default per-model prices are
/// <b>indicative input prices</b> (USD per 1K tokens) and may be out of date — pass your own
/// price for accuracy. Self-hosted models default to 0.
/// </summary>
public static class CavemanCostEstimator
{
    /// <summary>Default USD→EUR conversion rate (indicative, overridable).</summary>
    public const decimal DefaultUsdToEur = 0.92m;

    /// <summary>Indicative input price in USD per 1K tokens for a model.</summary>
    public static decimal DefaultUsdPer1KTokens(LlmModel model) => model switch
    {
        LlmModel.Gpt4 => 0.03m,
        LlmModel.Gpt3_5Turbo => 0.0015m,
        LlmModel.Claude3 => 0.015m,
        LlmModel.Llama3 => 0m,   // typically self-hosted: negligible per-token cost
        LlmModel.Gemma3 => 0m,
        _ => 0m
    };

    /// <summary>Cost in USD for <paramref name="tokens"/> at the given USD price per 1K tokens.</summary>
    public static decimal Usd(int tokens, decimal usdPer1K) => tokens / 1000m * usdPer1K;

    /// <summary>Cost in EUR for <paramref name="tokens"/> given a USD price and a USD→EUR rate.</summary>
    public static decimal Eur(int tokens, decimal usdPer1K, decimal usdToEur) => Usd(tokens, usdPer1K) * usdToEur;
}
