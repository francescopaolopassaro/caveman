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
/// stop-word list. Uses a two-pass strategy:
/// <list type="number">
///   <item>Raw function-word hit count per language.</item>
///   <item>Exclusive-marker boost: if a language has words that appear in no other
///   curated language, it wins — even a single exclusive marker beats an ambiguous
///   multi-language tie (fixes false positives on shared words like "per", "a", "in").</item>
/// </list>
/// </summary>
public class CavemanLanguageDetector : ILanguageDetector
{
    // \p{M} keeps combining marks (Kannada/Hindi/Tamil/Thai vowel signs, virama, …) attached
    // to their base letter — see the identical fix/rationale in CavemanCompressionService.
    private static readonly Regex WordSplit = new(@"[\p{L}\p{M}]+(?:'[\p{L}\p{M}]+)?", RegexOptions.Compiled);
    private readonly FunctionWordProvider _wordProvider;
    private readonly HashSet<string> _supportedLanguages;
    private readonly HashSet<string> _curatedIso3s;

    // Languages with curated exclusive-marker files (.excl.yaml.br).
    private static readonly HashSet<string> CuratedSet = new(
        new[] { "eng", "ita", "fra", "deu", "spa", "por", "nld" },
        StringComparer.OrdinalIgnoreCase);

    // A curated language is preferred over a YAML-only result when its score
    // is at least this fraction of the best YAML score.
    private const double CuratedPreferenceThreshold = 0.75;

    /// <summary>Creates a detector, optionally sharing an existing word-data provider.</summary>
    public CavemanLanguageDetector(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider ?? new FunctionWordProvider();
        _supportedLanguages = _wordProvider.GetAllSupportedIso3();
        _curatedIso3s = CuratedSet;
    }

    /// <summary>
    /// Returns the most likely language as an ISO 639-3 code (e.g. "eng", "ita"),
    /// falling back to "eng" for empty or genuinely ambiguous input.
    /// </summary>
    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "eng";

        var tokens = Tokenize(text.ToLowerInvariant());
        if (tokens.Count == 0)
            return "eng";

        // ── Pass 1: raw function-word scoring ────────────────────────────────
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var iso3 in _supportedLanguages)
        {
            var fw = _wordProvider.GetFunctionWords(iso3);
            if (fw.Count == 0) continue;
            var hits = 0;
            foreach (var token in tokens)
                if (fw.Contains(token)) hits++;
            if (hits > 0)
                scores[iso3] = hits;
        }

        if (scores.Count == 0)
            return "eng";

        var wordCount = tokens.Count;
        var best = scores.MaxBy(kv => kv.Value);
        var bestScore = best.Value;

        if (bestScore < 1 || (double)bestScore / wordCount < 0.02)
            return "eng";

        // ── Pass 2: exclusive-marker boost ────────────────────────────────────
        // Words unique to one language cut through ties caused by shared words.
        var exclScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var iso3 in _curatedIso3s)
        {
            var excl = _wordProvider.GetExclusiveMarkers(iso3);
            if (excl.Count == 0) continue;
            var hits = 0;
            foreach (var token in tokens)
                if (excl.Contains(token)) hits++;
            if (hits > 0)
                exclScores[iso3] = hits;
        }

        if (exclScores.Count > 0)
        {
            var bestExcl = exclScores.MaxBy(kv => kv.Value);
            var secondExcl = exclScores
                .Where(kv => kv.Key != bestExcl.Key)
                .Select(kv => (int?)kv.Value)
                .Max() ?? 0;

            if (bestExcl.Value > secondExcl)
                return bestExcl.Key;
        }

        // ── Pass 1 winner — prefer curated over YAML-only when close ─────────
        if (!_curatedIso3s.Contains(best.Key))
        {
            var bestCurated = scores
                .Where(kv => _curatedIso3s.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .Cast<KeyValuePair<string, int>?>()
                .FirstOrDefault();

            if (bestCurated.HasValue &&
                bestCurated.Value.Value >= bestScore * CuratedPreferenceThreshold)
            {
                best = KeyValuePair.Create(bestCurated.Value.Key, bestCurated.Value.Value);
                bestScore = best.Value;
            }
        }

        var secondBest = scores.Where(kv => kv.Key != best.Key)
            .Select(kv => (int?)kv.Value)
            .Max() ?? 0;

        if (bestScore > secondBest || (bestScore == secondBest && bestScore >= 2))
            return best.Key;

        // Tiebreak: prefer a curated language.
        var tiedCurated = scores
            .Where(kv => kv.Value == bestScore && _curatedIso3s.Contains(kv.Key))
            .Select(kv => kv.Key)
            .FirstOrDefault();

        return tiedCurated ?? "eng";
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

    // Explicit interface implementation: the public method returns a concrete Dictionary
    // (kept for backward compatibility), while the interface exposes the read-only view.
    IReadOnlyDictionary<string, double> ILanguageDetector.DetectWithScores(string input) => DetectWithScores(input);

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
