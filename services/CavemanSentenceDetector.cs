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
using System.Text;
using System.Text.RegularExpressions;

namespace caveman.core.services;

public class CavemanSentenceDetector
{
    private static readonly Regex SentenceEndPattern = new(
        @"[.!?…\u3002\uFF01\uFF1F\uFE12\uFE52\uFF0E\u2026]+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> CommonAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "av", "vd",
        "vs", "etc", "eg", "ie", "inc", "ltd", "co", "corp", "llc",
        "dept", "est", "govt", "mt", "ft", "blvd", "ave", "rd", "ln",
        "approx", "appt", "dept", "min", "max", "temp", "tel", "est",
        "gen", "sgt", "capt", "col", "maj", "lt", "cpt", "sir",
    };

    private readonly FunctionWordProvider? _wordProvider;

    public CavemanSentenceDetector(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider;
    }

    public CavemanSentence[] SplitText(string text, string? iso3 = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<CavemanSentence>();

        var splitter = new CavemanTextSplitter();
        var tokens = splitter.ParseText(text);
        return SplitTokens(tokens, iso3);
    }

    public CavemanSentence[] SplitTokens(CavemanToken[] tokens, string? iso3 = null)
    {
        if (tokens == null || tokens.Length == 0)
            return Array.Empty<CavemanSentence>();

        var sentences = new List<CavemanSentence>();
        var currentTokens = new List<CavemanToken>();
        var abbreviations = GatherAbbreviations(iso3);

        for (int i = 0; i < tokens.Length; i++)
        {
            currentTokens.Add(tokens[i]);

            if (IsSentenceEnd(tokens, i, abbreviations))
            {
                TryAddSentence(sentences, currentTokens);
                currentTokens = new List<CavemanToken>();
                continue;
            }
        }

        if (currentTokens.Count > 0)
            TryAddSentence(sentences, currentTokens);

        return sentences.ToArray();
    }

    public string[] ExtractPhrases(string text, string? iso3 = null)
    {
        var sentences = SplitText(text, iso3);
        var result = new string[sentences.Length];
        for (int i = 0; i < sentences.Length; i++)
            result[i] = sentences[i].Text;
        return result;
    }

    private static bool IsSentenceEnd(CavemanToken[] tokens, int index, HashSet<string> abbreviations)
    {
        var token = tokens[index];

        if (token.Category == CavemanTokenCategory.Newline)
        {
            if (index + 1 < tokens.Length &&
                tokens[index + 1].Category == CavemanTokenCategory.Newline)
                return true;

            if (index + 1 < tokens.Length &&
                (tokens[index + 1].Category == CavemanTokenCategory.Word &&
                 char.IsUpper(tokens[index + 1].Value[0])))
                return true;

            return false;
        }

        if (token.Category != CavemanTokenCategory.Punctuation)
            return false;

        var value = token.Value;
        if (!SentenceEndPattern.IsMatch(value))
            return false;

        if (index + 1 >= tokens.Length)
            return true;

        if (IsAbbreviation(tokens, index, abbreviations))
            return false;

        if (IsEllipsis(value))
            return false;

        var next = tokens[index + 1];
            if (next.Category == CavemanTokenCategory.Punctuation)
            {
                if (next.Value is "\"" or "'" or "''" or "\u201D" or "\u2019" or ")" or "]" or "}")
                    return true;
                return false;
            }

            if (next.Category == CavemanTokenCategory.Newline)
                return true;

            if (next.Category == CavemanTokenCategory.Whitespace)
            {
                if (index + 2 >= tokens.Length)
                    return false;

                var afterWs = tokens[index + 2];
                if (afterWs.Category == CavemanTokenCategory.Word &&
                    (char.IsUpper(afterWs.Value[0]) ||
                     afterWs.Value.Length == 1 && !char.IsLetter(afterWs.Value[0])))
                    return true;

                return false;
            }

            if (next.Category == CavemanTokenCategory.Word && char.IsUpper(next.Value[0]))
                return true;

            return false;
    }

    private static bool IsAbbreviation(CavemanToken[] tokens, int index, HashSet<string> abbreviations)
    {
        if (index == 0)
            return false;

        var prev = tokens[index - 1];
        if (prev.Category != CavemanTokenCategory.Word)
            return false;

        var lower = prev.Value.ToLowerInvariant().TrimEnd('.');
        if (abbreviations.Contains(lower))
            return true;

        if (lower.Length == 1 && char.IsLetter(lower[0]))
            return true;

        if (lower.Length <= 3 && lower.All(char.IsLetter) && lower.All(char.IsUpper))
            return true;

        return false;
    }

    private static bool IsEllipsis(string value)
    {
        return value is "…" or "..." or ". . .";
    }

    private static void TryAddSentence(List<CavemanSentence> sentences, List<CavemanToken> tokens)
    {
        var text = string.Concat(tokens.Select(t => t.Value));
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (!tokens.Any(t => t.Category == CavemanTokenCategory.Word))
            return;
        sentences.Add(new CavemanSentence(text, tokens.ToArray()));
    }

    private HashSet<string> GatherAbbreviations(string? iso3)
    {
        var result = new HashSet<string>(CommonAbbreviations, StringComparer.OrdinalIgnoreCase);

        if (_wordProvider != null && !string.IsNullOrEmpty(iso3))
        {
            var funcWords = _wordProvider.GetFunctionWords(iso3);
            foreach (var fw in funcWords)
            {
                if (fw.Length <= 4 && fw.All(char.IsLetter))
                    result.Add(fw);
            }
        }

        return result;
    }
}

public readonly struct CavemanSentence
{
    public string Text { get; }
    public CavemanToken[] Tokens { get; }
    public int WordCount { get; }

    public CavemanSentence(string text, CavemanToken[] tokens)
    {
        Text = text;
        Tokens = tokens;
        WordCount = tokens.Count(t => t.Category == CavemanTokenCategory.Word);
    }

    public override string ToString() => Text;
}
