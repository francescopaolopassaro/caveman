// -----------------------------------------------------------------------------
// <copyright file="CavemanSqlCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>SQL-aware compressor (ported from Synthelion 1.2.5's SqlCompressor): collapses insignificant whitespace without mangling keywords, string/numeric literals, quoted identifiers or comments, with optional lossy VALUES-tuple folding backed by the CCR store.</summary>
// -----------------------------------------------------------------------------
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace caveman.core.services;

/// <summary>Outcome of a <see cref="CavemanSqlCompressor.Compress"/> call.</summary>
public sealed class SqlCompressionResult
{
    /// <summary>Compressed (or unchanged) SQL.</summary>
    public string Compressed { get; init; } = string.Empty;
    /// <summary>Original SQL input.</summary>
    public string Original { get; init; } = string.Empty;
    /// <summary>True when the output differs from the input.</summary>
    public bool WasCompressed { get; init; }
    /// <summary>Number of VALUES tuples dropped (0 unless VALUES folding ran).</summary>
    public int TuplesDropped { get; init; }
    /// <summary>12-char SHA-256 prefix of the dropped tuples (non-null only when tuples were dropped).</summary>
    public string? CcrHash { get; init; }
}

/// <summary>
/// Compresses SQL source while staying structurally safe. The default path is lossless:
/// SQL is routed away from the word-level NLP compressor — which would mangle keywords,
/// string/numeric literals, identifiers and comments — into this dedicated whitespace
/// normalizer instead. Optional VALUES-tuple folding (see <see cref="Compress(string, bool, bool, int)"/>)
/// drops surplus <c>VALUES (...), (...)</c> tuples with a CCR hash, mirroring the JSON
/// crusher's lossy row-drop.
/// </summary>
public sealed class CavemanSqlCompressor
{
    private const string CcrCommentPrefix = "-- CCR:";

    /// <summary>Default maximum number of VALUES tuples kept before folding the rest into a CCR marker.</summary>
    public const int DefaultMaxValues = 8;

    private readonly CavemanCcrStore _ccrStore;

    private static readonly Regex ValuesKeyword =
        new(@"\bVALUES\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="ccrStore">CCR store for dropped VALUES tuples. Defaults to <see cref="CavemanCcrStore.Instance"/>.</param>
    public CavemanSqlCompressor(CavemanCcrStore? ccrStore = null)
        => _ccrStore = ccrStore ?? CavemanCcrStore.Instance;

    /// <summary>Compresses <paramref name="sql"/> and returns (result, was_compressed) semantics via <see cref="SqlCompressionResult"/>.</summary>
    /// <param name="sql">SQL source.</param>
    /// <param name="stripComments">When true (default false), <c>--</c> and <c>/* */</c> comments are removed instead of preserved.</param>
    /// <param name="foldValues">When true, surplus <c>VALUES (...)</c> tuples beyond <paramref name="maxValues"/> are dropped and CCR-hashed.</param>
    /// <param name="maxValues">Maximum row-tuples kept before folding (default <see cref="DefaultMaxValues"/>).</param>
    public SqlCompressionResult Compress(string sql, bool stripComments = false, bool foldValues = false, int maxValues = DefaultMaxValues)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new SqlCompressionResult { Original = sql ?? string.Empty, Compressed = sql ?? string.Empty };

        var normalized = NormalizeWhitespace(sql, stripComments);
        if (string.IsNullOrWhiteSpace(normalized))
            return new SqlCompressionResult { Original = sql, Compressed = sql, WasCompressed = false };

        var result = normalized.Trim();
        int tuplesDropped = 0;
        string? ccrHash = null;

        if (foldValues && maxValues >= 1)
            (result, tuplesDropped, ccrHash) = FoldValueTuples(result, maxValues);

        return new SqlCompressionResult
        {
            Compressed = result,
            Original = sql,
            WasCompressed = !string.Equals(result, sql, StringComparison.Ordinal),
            TuplesDropped = tuplesDropped,
            CcrHash = ccrHash
        };
    }

    /// <summary>
    /// Collapses runs of insignificant whitespace to a single space while preserving
    /// string literals ('..', ''escaped''), quoted identifiers (".."/`..`/[..], including
    /// ""/"`` escapes) and comments verbatim (or stripping them when requested). Case is
    /// never changed. The equivalent of sqlparse's strip_whitespace used by the Python
    /// SqlCompressor, re-implemented dependency-free.
    /// </summary>
    internal static string NormalizeWhitespace(string sql, bool stripComments)
    {
        var sb = new StringBuilder(sql.Length);
        int i = 0, n = sql.Length;
        bool inSingle = false, inDouble = false, inBacktick = false, inBracket = false;
        bool inLineComment = false, inBlockComment = false;
        bool pendingSpace = false;

        void EnterComment(string marker)
        {
            if (stripComments)
            {
                pendingSpace = false;
            }
            else
            {
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(marker);
            }
        }

        while (i < n)
        {
            char c = sql[i];
            char nxt = i + 1 < n ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    // The newline is comment-terminating whitespace: ensure a single space
                    // separates the (kept) comment from whatever follows.
                    inLineComment = false;
                    pendingSpace = true;
                }
                else if (!stripComments)
                {
                    sb.Append(c);
                }
                i++;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && nxt == '/')
                {
                    inBlockComment = false;
                    pendingSpace = true;
                    i += 2;
                    continue;
                }
                if (!stripComments)
                    sb.Append(c);
                i++;
                continue;
            }

            if (inSingle)
            {
                if (c == '\'' && nxt == '\'') { sb.Append(c).Append(nxt); i += 2; continue; }
                if (c == '\'') inSingle = false;
                sb.Append(c);
                i++;
                continue;
            }

            if (inDouble)
            {
                if (c == '"' && nxt == '"') { sb.Append(c).Append(nxt); i += 2; continue; }
                if (c == '"') inDouble = false;
                sb.Append(c);
                i++;
                continue;
            }

            if (inBacktick)
            {
                if (c == '`') inBacktick = false;
                sb.Append(c);
                i++;
                continue;
            }

            if (inBracket)
            {
                if (c == ']') inBracket = false;
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '-' && nxt == '-') { inLineComment = true; EnterComment("--"); i += 2; continue; }
            if (c == '/' && nxt == '*') { inBlockComment = true; EnterComment("/*"); i += 2; continue; }
            if (c == '\'') { FlushSpace(); inSingle = true; sb.Append(c); i++; continue; }
            if (c == '"') { FlushSpace(); inDouble = true; sb.Append(c); i++; continue; }
            if (c == '`') { FlushSpace(); inBacktick = true; sb.Append(c); i++; continue; }
            if (c == '[') { FlushSpace(); inBracket = true; sb.Append(c); i++; continue; }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                i++;
                continue;
            }

            FlushSpace();
            sb.Append(c);
            i++;

            void FlushSpace()
            {
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            }
        }

        if (inLineComment || inBlockComment)
        {
            // Unterminated trailing comment: strip it when requested, otherwise keep as-is.
        }

        return sb.ToString();
    }

    /// <summary>Keeps the first <paramref name="maxValues"/> row-tuples after VALUES and CCR-hashes the rest. Walks with string/paren/comment awareness so commas inside literals or nested expressions are never mistaken for tuple separators.</summary>
    private (string sql, int dropped, string? hash) FoldValueTuples(string sql, int maxValues)
    {
        var match = ValuesKeyword.Match(sql);
        if (!match.Success)
            return (sql, 0, null);

        int start = match.Index + match.Length;
        while (start < sql.Length && char.IsWhiteSpace(sql[start]))
            start++;
        if (start >= sql.Length || sql[start] != '(')
            return (sql, 0, null);

        var tuples = new List<string>();
        int i = start;
        int n = sql.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(sql[i]))
                i++;
            if (i >= n || sql[i] != '(')
                break;

            int close = FindMatchingParen(sql, i);
            if (close < 0)
                break;
            tuples.Add(sql.Substring(i, close - i + 1));
            i = close + 1;

            while (i < n && char.IsWhiteSpace(sql[i]))
                i++;
            if (i < n && sql[i] == ',')
            {
                i++;
                continue;
            }
            break;
        }

        if (tuples.Count <= maxValues)
            return (sql, 0, null);

        int kept = maxValues;
        var keptText = string.Join(",", tuples.Take(kept));
        var droppedText = string.Join(",", tuples.Skip(kept));
        var hash = Sha256Prefix(droppedText);
        _ccrStore.Store(hash, droppedText);

        var rebuilt = sql.Substring(0, start) + keptText + sql.Substring(i);
        rebuilt += Environment.NewLine + $"{CcrCommentPrefix}{hash} {tuples.Count - kept} value tuples dropped";
        return (rebuilt, tuples.Count - kept, hash);
    }

    /// <summary>Paren-depth walk that ignores braces (and escaped quote pairs) inside strings, quoted identifiers and comments.</summary>
    private static int FindMatchingParen(string s, int openIndex)
    {
        int depth = 0;
        bool inSingle = false, inDouble = false, inBacktick = false, inBracket = false;
        bool inLineComment = false, inBlockComment = false;
        int i = openIndex;
        int n = s.Length;

        while (i < n)
        {
            char c = s[i];
            char nxt = i + 1 < n ? s[i + 1] : '\0';

            if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
            if (inBlockComment) { if (c == '*' && nxt == '/') { inBlockComment = false; i += 2; continue; } i++; continue; }
            if (inSingle) { if (c == '\'' && nxt == '\'') { i += 2; continue; } if (c == '\'') inSingle = false; i++; continue; }
            if (inDouble) { if (c == '"' && nxt == '"') { i += 2; continue; } if (c == '"') inDouble = false; i++; continue; }
            if (inBacktick) { if (c == '`') inBacktick = false; i++; continue; }
            if (inBracket) { if (c == ']') inBracket = false; i++; continue; }

            if (c == '-' && nxt == '-') { inLineComment = true; i += 2; continue; }
            if (c == '/' && nxt == '*') { inBlockComment = true; i += 2; continue; }
            if (c == '\'') { inSingle = true; i++; continue; }
            if (c == '"') { inDouble = true; i++; continue; }
            if (c == '`') { inBacktick = true; i++; continue; }
            if (c == '[') { inBracket = true; i++; continue; }

            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
            i++;
        }

        return -1;
    }

    private static string Sha256Prefix(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(12);
        for (int i = 0; i < 6 && i < bytes.Length; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}