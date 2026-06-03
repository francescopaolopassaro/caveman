// -----------------------------------------------------------------------------
// <copyright file="CavemanLanguageDetector.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Heuristic language detection based on function-word frequency scoring.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core;

/// <summary>
/// Detects the language of a text by scoring it against each supported language's
/// stop-word list. Backed by a small embedded index, so it stays fast even though it
/// considers every supported language. Usable standalone, without compression.
/// </summary>
public class CavemanLanguageDetector
{
    private static readonly Regex WordSplit = new(@"\p{L}+(?:'\p{L}+)?", RegexOptions.Compiled);
    private readonly FunctionWordProvider _wordProvider;
    private readonly HashSet<string> _supportedLanguages;

    /// <summary>Creates a detector, optionally sharing an existing word-data provider.</summary>
    public CavemanLanguageDetector(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider ?? new FunctionWordProvider();
        _supportedLanguages = _wordProvider.GetAllSupportedIso3();
    }

    /// <summary>
    /// Returns the most likely language as an ISO 639-3 code (e.g. "eng", "ita"),
    /// falling back to "eng" for empty or genuinely ambiguous input. Short inputs
    /// (a word or two) are supported, but a single token shared by several languages
    /// stays ambiguous and resolves to "eng".
    /// </summary>
    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "eng";

        var tokens = Tokenize(text.ToLowerInvariant());
        if (tokens.Count == 0)
            return "eng";

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var wordCount = tokens.Count;

        foreach (var iso3 in _supportedLanguages)
        {
            var fw = _wordProvider.GetFunctionWords(iso3);
            if (fw.Count == 0)
                continue;

            var hits = 0;
            foreach (var token in tokens)
            {
                if (fw.Contains(token))
                    hits++;
            }

            if (hits > 0)
                scores[iso3] = hits;
        }

        if (scores.Count == 0)
            return "eng";

        var best = scores.MaxBy(kv => kv.Value);
        var bestScore = best.Value;
        var totalFuncWords = scores.Values.Sum();

        if (bestScore < 1)
            return "eng";

        var ratio = (double)bestScore / wordCount;
        if (ratio < 0.02)
            return "eng";

        var secondBest = scores.Where(kv => kv.Key != best.Key)
            .Select(kv => (int?)kv.Value)
            .Max() ?? 0;

        if (bestScore > secondBest || (bestScore == secondBest && bestScore >= 2))
            return best.Key;

        return "eng";
    }

    /// <summary>
    /// Returns the per-language match scores (ISO 639-3 code → ratio of tokens
    /// recognised as that language's stop words). Empty input yields { "eng": 1.0 }.
    /// </summary>
    public Dictionary<string, double> DetectWithScores(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new Dictionary<string, double> { { "eng", 1.0 } };

        var tokens = Tokenize(text.ToLowerInvariant());
        if (tokens.Count == 0)
            return new Dictionary<string, double> { { "eng", 1.0 } };

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var wordCount = tokens.Count;

        foreach (var iso3 in _supportedLanguages)
        {
            var fw = _wordProvider.GetFunctionWords(iso3);
            if (fw.Count == 0)
                continue;

            var hits = 0;
            foreach (var token in tokens)
            {
                if (fw.Contains(token))
                    hits++;
            }

            if (hits > 0)
                scores[iso3] = (double)hits / wordCount;
        }

        if (scores.Count == 0)
            return new Dictionary<string, double> { { "eng", 1.0 } };

        return scores;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var matches = WordSplit.Matches(text);
        foreach (Match match in matches)
        {
            var word = match.Value.ToLowerInvariant();
            if (word.Length >= 1)
                tokens.Add(word);
        }
        return tokens;
    }
}
