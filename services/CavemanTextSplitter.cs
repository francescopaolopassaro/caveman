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
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace caveman.core.services;

public enum CavemanTokenCategory
{
    Word,
    Number,
    Punctuation,
    Whitespace,
    Email,
    Url,
    Emoji,
    Newline,
    Other
}

public class CavemanTextSplitter
{
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*",
        RegexOptions.Compiled);

    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>""']+|www\.[^\s<>""']+\.[^\s<>""']+",
        RegexOptions.Compiled);

    private static readonly Regex EmojiPattern = new(
        @"[\u2600-\u27BF\uFE00-\uFE0F]|[\uD83C][\uDF00-\uDFFF]|[\uD83D][\uDC00-\uDEFF]|[\uD83E][\uDD00-\uDDFF]",
        RegexOptions.Compiled);

    public CavemanToken[] ParseText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<CavemanToken>();

        var tokens = new List<CavemanToken>();
        int pos = 0;

        while (pos < text.Length)
        {
            if (TryMatchEmoji(text, pos, out var emojiMatch))
            {
                tokens.Add(new CavemanToken(emojiMatch.Value, emojiMatch.StartIndex, emojiMatch.EndIndex, CavemanTokenCategory.Emoji));
                pos = emojiMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchEmail(text, pos, out var emailMatch))
            {
                tokens.Add(new CavemanToken(emailMatch.Value, emailMatch.StartIndex, emailMatch.EndIndex, CavemanTokenCategory.Email));
                pos = emailMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchUrl(text, pos, out var urlMatch))
            {
                tokens.Add(new CavemanToken(urlMatch.Value, urlMatch.StartIndex, urlMatch.EndIndex, CavemanTokenCategory.Url));
                pos = urlMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchNewline(text, pos, out var nlMatch))
            {
                tokens.Add(new CavemanToken(nlMatch.Value, nlMatch.StartIndex, nlMatch.EndIndex, CavemanTokenCategory.Newline));
                pos = nlMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchWhitespace(text, pos, out var wsMatch))
            {
                tokens.Add(new CavemanToken(wsMatch.Value, wsMatch.StartIndex, wsMatch.EndIndex, CavemanTokenCategory.Whitespace));
                pos = wsMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchWord(text, pos, out var wordMatch))
            {
                tokens.Add(new CavemanToken(wordMatch.Value, wordMatch.StartIndex, wordMatch.EndIndex, CavemanTokenCategory.Word));
                pos = wordMatch.EndIndex + 1;
                continue;
            }

            if (TryMatchNumber(text, pos, out var numMatch))
            {
                tokens.Add(new CavemanToken(numMatch.Value, numMatch.StartIndex, numMatch.EndIndex, CavemanTokenCategory.Number));
                pos = numMatch.EndIndex + 1;
                continue;
            }

            tokens.Add(new CavemanToken(text[pos].ToString(), pos, pos, CavemanTokenCategory.Punctuation));
            pos++;
        }

        return tokens.ToArray();
    }

    public string CombineTokens(CavemanToken[] tokens)
    {
        var sb = new StringBuilder(tokens.Length * 8);
        foreach (var t in tokens)
            sb.Append(t.Value);
        return sb.ToString();
    }

    public string[] ExtractWords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var tokens = ParseText(text);
        return tokens
            .Where(t => t.Category == CavemanTokenCategory.Word)
            .Select(t => t.Value)
            .ToArray();
    }

    private static bool TryMatchEmoji(string text, int pos, out TokenMatch result)
    {
        result = default;
        var match = EmojiPattern.Match(text, pos);
        if (!match.Success || match.Index != pos)
            return false;
        result = new TokenMatch(match.Value, pos, pos + match.Length - 1);
        return true;
    }

    private static bool TryMatchEmail(string text, int pos, out TokenMatch result)
    {
        result = default;
        var match = EmailPattern.Match(text, pos);
        if (!match.Success || match.Index != pos)
            return false;
        result = new TokenMatch(match.Value, pos, pos + match.Length - 1);
        return true;
    }

    private static bool TryMatchUrl(string text, int pos, out TokenMatch result)
    {
        result = default;
        var match = UrlPattern.Match(text, pos);
        if (!match.Success || match.Index != pos)
            return false;
        result = new TokenMatch(match.Value, pos, pos + match.Length - 1);
        return true;
    }

    private static bool TryMatchNewline(string text, int pos, out TokenMatch result)
    {
        result = default;
        if (pos >= text.Length) return false;

        if (text[pos] == '\r')
        {
            if (pos + 1 < text.Length && text[pos + 1] == '\n')
            {
                result = new TokenMatch("\r\n", pos, pos + 1);
                return true;
            }
            result = new TokenMatch("\r", pos, pos);
            return true;
        }

        if (text[pos] == '\n')
        {
            result = new TokenMatch("\n", pos, pos);
            return true;
        }

        return false;
    }

    private static bool TryMatchWhitespace(string text, int pos, out TokenMatch result)
    {
        result = default;
        if (pos >= text.Length) return false;

        if (!char.IsWhiteSpace(text[pos]))
            return false;

        if (text[pos] == '\r' || text[pos] == '\n')
            return false;

        int start = pos;
        while (pos < text.Length && char.IsWhiteSpace(text[pos])
               && text[pos] != '\r' && text[pos] != '\n')
            pos++;

        result = new TokenMatch(text.Substring(start, pos - start), start, pos - 1);
        return true;
    }

    private static bool TryMatchWord(string text, int pos, out TokenMatch result)
    {
        result = default;
        if (pos >= text.Length) return false;

        var cat = CharUnicodeInfo.GetUnicodeCategory(text[pos]);
        if (cat != UnicodeCategory.UppercaseLetter &&
            cat != UnicodeCategory.LowercaseLetter &&
            cat != UnicodeCategory.TitlecaseLetter &&
            cat != UnicodeCategory.ModifierLetter &&
            cat != UnicodeCategory.OtherLetter)
            return false;

        int start = pos;
        while (pos < text.Length)
        {
            cat = CharUnicodeInfo.GetUnicodeCategory(text[pos]);
            if (cat == UnicodeCategory.UppercaseLetter ||
                cat == UnicodeCategory.LowercaseLetter ||
                cat == UnicodeCategory.TitlecaseLetter ||
                cat == UnicodeCategory.ModifierLetter ||
                cat == UnicodeCategory.OtherLetter)
            {
                pos++;
                continue;
            }

            if (text[pos] == '\'' && pos + 1 < text.Length
                && char.IsLetter(text[pos + 1]))
            {
                pos++;
                continue;
            }

            if (text[pos] == '-' && pos + 1 < text.Length
                && char.IsLetter(text[pos + 1]))
            {
                pos++;
                continue;
            }

            break;
        }

        result = new TokenMatch(text.Substring(start, pos - start), start, pos - 1);
        return true;
    }

    private static bool TryMatchNumber(string text, int pos, out TokenMatch result)
    {
        result = default;
        if (pos >= text.Length) return false;

        var cat = CharUnicodeInfo.GetUnicodeCategory(text[pos]);
        if (cat != UnicodeCategory.DecimalDigitNumber &&
            cat != UnicodeCategory.LetterNumber &&
            cat != UnicodeCategory.OtherNumber)
            return false;

        int start = pos;
        bool hadDot = false;

        while (pos < text.Length)
        {
            cat = CharUnicodeInfo.GetUnicodeCategory(text[pos]);
            if (cat == UnicodeCategory.DecimalDigitNumber)
            {
                pos++;
                continue;
            }

            if ((text[pos] == '.' || text[pos] == ',') && !hadDot
                && pos + 1 < text.Length
                && CharUnicodeInfo.GetUnicodeCategory(text[pos + 1]) == UnicodeCategory.DecimalDigitNumber)
            {
                hadDot = true;
                pos++;
                continue;
            }

            break;
        }

        if (pos == start) return false;

        result = new TokenMatch(text.Substring(start, pos - start), start, pos - 1);
        return true;
    }

    internal readonly struct TokenMatch(string value, int startIndex, int endIndex)
    {
        public string Value { get; } = value;
        public int StartIndex { get; } = startIndex;
        public int EndIndex { get; } = endIndex;
    }
}

public readonly struct CavemanToken(string value, int startIndex, int endIndex, CavemanTokenCategory category)
{
    public string Value { get; } = value;
    public int StartIndex { get; } = startIndex;
    public int EndIndex { get; } = endIndex;
    public CavemanTokenCategory Category { get; } = category;
    public int Length => Value.Length;

    public bool IsPunctuation => Category == CavemanTokenCategory.Punctuation;
    public bool IsWord => Category == CavemanTokenCategory.Word;
    public bool IsWhitespace => Category == CavemanTokenCategory.Whitespace || Category == CavemanTokenCategory.Newline;

    public override string ToString() => Value;
}
