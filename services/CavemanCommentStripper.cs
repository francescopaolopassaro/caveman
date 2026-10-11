// -----------------------------------------------------------------------------
// <copyright file="CavemanCommentStripper.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution). See the LICENSE file.
// </copyright>
// <summary>String-aware comment stripper used by CavemanCodeCompressor.</summary>
// -----------------------------------------------------------------------------
using System.Text;

namespace caveman.core.services;

/// <summary>
/// Removes comments from source code with a single left-to-right scan that tracks string literals, so a comment
/// marker INSIDE a string is never treated as a comment. The regex stripper it replaces cut
/// <c>"redis://localhost:6379"</c> to <c>"redis:</c> (Python/JS: <c>//</c>), <c>"color: #fff"</c> to <c>"color: </c>
/// (<c>#</c>) and <c>"src/**/*.js"</c> into a block comment that swallowed the code after it - leaving the file
/// syntactically broken (Synthelion 1.2.5, issue #19). Output is always the input with comment spans removed.
/// </summary>
internal static class CavemanCommentStripper
{
    internal enum Dialect { CStyle, Python, Ruby, Sql, Shell }

    public static string Strip(string src, Dialect d)
    {
        var sb = new StringBuilder(src.Length);
        var n = src.Length;
        var i = 0;
        var lineStart = true;   // only whitespace seen on this line so far

        bool At(int pos, string s) => pos + s.Length <= n && string.CompareOrdinal(src, pos, s, 0, s.Length) == 0;

        void CopyLineRest()
        {
            while (i < n && src[i] != '\n') sb.Append(src[i++]);
        }

        while (i < n)
        {
            var c = src[i];

            if (c == '\n') { sb.Append(c); i++; lineStart = true; continue; }
            if (c == '\r') { sb.Append(c); i++; continue; }

            // shebang is kept verbatim
            if (i == 0 && d == Dialect.Shell && At(0, "#!")) { CopyLineRest(); continue; }

            // Ruby =begin ... =end (only at column 0)
            if (d == Dialect.Ruby && (i == 0 || src[i - 1] == '\n') && At(i, "=begin"))
            {
                var end = src.IndexOf("\n=end", i, StringComparison.Ordinal);
                if (end < 0) { i = n; continue; }
                i = end + 5;
                while (i < n && src[i] != '\n') i++;
                continue;
            }

            // Python docstring: a triple-quoted string in statement position is documentation, not data
            if (d == Dialect.Python && lineStart && (At(i, "\"\"\"") || At(i, "'''")))
            {
                var q = src.Substring(i, 3);
                var end = src.IndexOf(q, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 3;
                    lineStart = false;
                    continue;
                }
            }

            // string literals are copied verbatim
            if (TryCopyString(src, ref i, sb, d)) { lineStart = false; continue; }

            // block comments
            if ((d == Dialect.CStyle || d == Dialect.Sql) && At(i, "/*"))
            {
                var end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                continue;
            }

            // line comments
            if (IsLineComment(src, i, d, lineStart))
            {
                while (i < n && src[i] != '\n' && src[i] != '\r') i++;
                continue;
            }

            sb.Append(c);
            if (!char.IsWhiteSpace(c)) lineStart = false;
            i++;
        }
        return sb.ToString();
    }

    private static bool IsLineComment(string s, int i, Dialect d, bool lineStart)
    {
        switch (d)
        {
            case Dialect.CStyle:
                return s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/';
            case Dialect.Sql:
                return s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-';
            case Dialect.Python:
                return s[i] == '#';
            default: // Ruby, Shell: '#' starts a comment only at a word start ($#, ${#x}, a#b are not comments)
                if (s[i] != '#') return false;
                if (i == 0 || lineStart) return true;
                var p = s[i - 1];
                return char.IsWhiteSpace(p) || p == ';' || p == '&' || p == '|';
        }
    }

    /// <summary>If a string literal starts at <paramref name="i"/>, copies it whole and returns true.</summary>
    private static bool TryCopyString(string s, ref int i, StringBuilder sb, Dialect d)
    {
        var n = s.Length;
        var c = s[i];

        // triple-quoted (Python, and C#11 raw / Java / Kotlin / Swift text blocks)
        if ((c == '"' || c == '\'') && i + 2 < n && s[i + 1] == c && s[i + 2] == c &&
            (d == Dialect.Python || (d == Dialect.CStyle && c == '"')))
        {
            var q = new string(c, 3);
            var end = s.IndexOf(q, i + 3, StringComparison.Ordinal);
            var stop = end < 0 ? n : end + 3;
            sb.Append(s, i, stop - i); i = stop;
            return true;
        }

        if (c == '"' || c == '\'' || c == '`')
        {
            if (c == '`' && d is not (Dialect.CStyle or Dialect.Ruby or Dialect.Shell or Dialect.Sql)) return false;
            // C# verbatim string: @"..." - backslash is not an escape, "" is a quote
            var verbatim = d == Dialect.CStyle && c == '"' && i > 0 && (s[i - 1] == '@' || (i > 1 && s[i - 1] == '$' && s[i - 2] == '@') || (i > 1 && s[i - 1] == '@' && s[i - 2] == '$'));
            var sqlStyle = d == Dialect.Sql;       // '' and "" escape a quote
            // Rust lifetimes ('a) and apostrophes have no closing quote on the line: don't swallow code
            var multiline = c == '`' || verbatim || (d is Dialect.Ruby or Dialect.Shell or Dialect.Sql);
            var j = i + 1;
            while (j < n)
            {
                var ch = s[j];
                if (ch == '\n' && !multiline) break;
                if (verbatim)
                {
                    if (ch == '"') { if (j + 1 < n && s[j + 1] == '"') { j += 2; continue; } j++; break; }
                    j++; continue;
                }
                if (sqlStyle)
                {
                    if (ch == c) { if (j + 1 < n && s[j + 1] == c) { j += 2; continue; } j++; break; }
                    j++; continue;
                }
                if (ch == '\\' && !(d == Dialect.Shell && c == '\'')) { j += 2; continue; }
                if (ch == c) { j++; break; }
                j++;
            }
            if (j > n) j = n;
            // an unterminated quote on a single line (lifetime, apostrophe in a comment-free line) is not a string
            if (!multiline && (j >= n || s[j - 1] != c || j - 1 == i) && !(j <= n && j - 1 > i && s[j - 1] == c))
            {
                return false;
            }
            sb.Append(s, i, j - i); i = j;
            return true;
        }
        return false;
    }
}
