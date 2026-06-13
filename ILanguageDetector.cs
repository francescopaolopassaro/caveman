// -----------------------------------------------------------------------------
// <copyright file="ILanguageDetector.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Abstraction over language detection for dependency injection / swapping.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core;

/// <summary>
/// Detects the language of a text. Implemented by <see cref="CavemanLanguageDetector"/>;
/// inject a custom implementation to swap or mock detection.
/// </summary>
public interface ILanguageDetector
{
    /// <summary>Returns the most likely language as an ISO 639-3 code (e.g. "eng", "ita").</summary>
    string Detect(string input);

    /// <summary>Returns per-language match scores (ISO 639-3 → ratio of recognized stop words).</summary>
    IReadOnlyDictionary<string, double> DetectWithScores(string input);
}
