// -----------------------------------------------------------------------------
// <copyright file="CavemanAbstractions.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Public abstractions (seams) for the summarizers and the conversation parser.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// An extractive summarizer. Implemented by both <see cref="CavemanSummarizer"/> (TF-IDF)
/// and <see cref="CavemanTextRank"/> (graph-based), so callers can swap strategies.
/// </summary>
public interface ISummarizer
{
    /// <summary>Summarizes to a target number of sentences (auto-detects language when <paramref name="iso3"/> is null).</summary>
    string Summarize(string text, int sentenceCount, string? iso3 = null);

    /// <summary>Summarizes to a ratio (0.0–1.0) of the original sentences.</summary>
    string Summarize(string text, float ratio, string? iso3 = null);
}

/// <summary>Parses a raw conversation transcript (multiple AI formats) into a <see cref="CavemanConversation"/>.</summary>
public interface IConversationParser
{
    /// <summary>Parses <paramref name="raw"/>, always returning a conversation (never null).</summary>
    CavemanConversation Parse(string raw);

    /// <summary>Attempts to parse a structured conversation; returns false when no known format is recognized.</summary>
    bool TryParse(string raw, out CavemanConversation conversation);
}
