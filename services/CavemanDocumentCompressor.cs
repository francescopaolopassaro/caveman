// -----------------------------------------------------------------------------
// <copyright file="CavemanDocumentCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses whole markdown documents (agent instruction files, memories): decoration stripped, embedded structures minified, prose compressed.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Compresses a whole markdown document in one pass. Fenced blocks are minified without loss (JSON, XML, HTML, SQL, code),
/// markdown decoration that carries no meaning for a model is removed, and prose lines go through the configured NLP level
/// with operators, backtick spans and identifiers protected. See <see cref="CavemanDocumentOptions"/> and its <c>Maximum</c> preset.
/// </summary>
public sealed class CavemanDocumentCompressor
{
    private static readonly Regex Fence = new(@"^\s{0,3}(```+|~~~+)\s*([\w+#.-]*)", RegexOptions.Compiled);
    private static readonly Regex HtmlComment = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSep = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^(\s*)>+\s?", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*(?!\s)(.+?)(?<!\s)\*\*", RegexOptions.Compiled);
    private static readonly Regex BoldU = new(@"(?<![\w])__(?!\s)(.+?)(?<!\s)__(?![\w])", RegexOptions.Compiled);
    private static readonly Regex Strike = new(@"~~(?!\s)(.+?)(?<!\s)~~", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![\w*])\*(?![\s*])([^*\n]+?)(?<![\s*])\*(?![\w*])", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex ListPrefix = new(@"^(\s*(?:[-*+]|\d+[.)])\s+)", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"(?<=\S)[ \t]{2,}(?=\S)", RegexOptions.Compiled);

    private readonly CavemanSafetyGuard _guard;
    private readonly CavemanStructuredCompressor _structured = new();

    /// <summary>Creates a compressor with the default safety guard.</summary>
    public CavemanDocumentCompressor() : this(new CavemanSafetyGuard()) { }

    /// <summary>Creates a compressor with a custom safety guard (used only when <see cref="CavemanDocumentOptions.CheckDestructiveCommands"/> is on).</summary>
    public CavemanDocumentCompressor(CavemanSafetyGuard guard) { _guard = guard ?? throw new ArgumentNullException(nameof(guard)); }

    /// <summary>Compresses <paramref name="document"/>. Returns a completed task for convenient use in async call sites.</summary>
    public Task<DocumentCompressionResult> CompressAsync(string document, CavemanDocumentOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(Compress(document, options, ct));

    /// <summary>Compresses <paramref name="document"/> synchronously.</summary>
    public DocumentCompressionResult Compress(string document, CavemanDocumentOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CavemanDocumentOptions();
        document ??= string.Empty;
        string src = document.Replace("\r\n", "\n");
        int tokBefore = CountTokens(src);

        SafetyVerdict? verdict = null;
        if (options.CheckDestructiveCommands)
        {
            verdict = _guard.Check(src);
            if (verdict.Level == SafetyLevel.Critical)
                return new DocumentCompressionResult
                {
                    Text = document, CharsBefore = src.Length, CharsAfter = src.Length,
                    TokensBefore = tokBefore, TokensAfter = tokBefore,
                    SkippedBySafetyGuard = true, SafetyVerdict = verdict,
                };
        }

        var svc = new CavemanCompressionService().WithCodeProtection(options.PreserveOperatorsAndIdentifiers);
        var sb = new StringBuilder(src.Length);
        int blocks = 0, prose = 0;

        // Segmentation: fenced blocks vs everything else.
        var lines = src.Split('\n');
        var text = new List<string>();
        int i = 0;
        void FlushText()
        {
            if (text.Count == 0) return;
            string joined = string.Join("\n", text);
            if (options.StripComments) joined = HtmlComment.Replace(joined, string.Empty);
            foreach (var raw in joined.Split('\n'))
            {
                ct.ThrowIfCancellationRequested();
                string? l = ProcessLine(raw, options, svc, ref prose);
                if (l == null) continue;
                if (l.Length == 0)
                {
                    if (options.RemoveBlankLines || sb.Length == 0) continue;
                    if (sb.Length >= 2 && sb[^1] == '\n' && sb[^2] == '\n') continue;
                }
                sb.Append(l).Append('\n');
            }
            text.Clear();
        }

        while (i < lines.Length)
        {
            var m = Fence.Match(lines[i]);
            if (!m.Success) { text.Add(lines[i++]); continue; }
            FlushText();
            string marker = m.Groups[1].Value;
            string tag = m.Groups[2].Value;
            int start = i++;
            var body = new List<string>();
            bool closed = false;
            while (i < lines.Length)
            {
                if (lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal) && lines[i].Trim().Trim(marker[0]).Length == 0) { closed = true; break; }
                body.Add(lines[i++]);
            }
            string bodyText = string.Join("\n", body);
            if (options.MinifyEmbeddedStructures && (tag.Length > 0 || _structured.LooksStructured(bodyText)))
            {
                try
                {
                    string min = _structured.Compress(bodyText, tag.Length > 0 ? tag : null);
                    if (min.Length < bodyText.Length) { bodyText = min; blocks++; }
                }
                catch { /* lossless or nothing: keep the block as written */ }
            }
            sb.Append(lines[start].TrimEnd()).Append('\n');
            if (bodyText.Length > 0) sb.Append(bodyText.TrimEnd('\n')).Append('\n');
            if (closed) { sb.Append(lines[i].Trim()).Append('\n'); i++; }
        }
        FlushText();

        string outText = sb.ToString().TrimEnd('\n') + (src.EndsWith('\n') ? "\n" : string.Empty);
        if (outText.Length >= src.Length) { outText = src; blocks = 0; prose = 0; }
        if (document.Contains("\r\n") && !ReferenceEquals(outText, src)) outText = outText.Replace("\n", "\r\n");

        return new DocumentCompressionResult
        {
            Text = outText,
            CharsBefore = src.Length,
            CharsAfter = outText.Replace("\r\n", "\n").Length,
            TokensBefore = tokBefore,
            TokensAfter = CountTokens(outText),
            WasCompressed = outText.Replace("\r\n", "\n") != src,
            SafetyVerdict = verdict,
            FencedBlocksMinified = blocks,
            ProseLinesCompressed = prose,
        };
    }

    private static string? ProcessLine(string raw, CavemanDocumentOptions o, CavemanCompressionService svc, ref int prose)
    {
        string line = raw.TrimEnd();
        if (line.Length == 0) return string.Empty;
        bool table = line.TrimStart().StartsWith('|');

        if (o.StripMarkdownDecoration)
        {
            if (Rule.IsMatch(line) && !ListPrefix.IsMatch(line)) return null;
            if (table && TableSep.IsMatch(line)) return null;
            if (!table && TableSep.IsMatch(line) && line.Contains('-') && line.Trim().Length >= 3 && line.Contains('|')) return null;
            line = Heading.Replace(line, string.Empty);
            line = Quote.Replace(line, "$1");
            line = Image.Replace(line, "$1");
            line = Link.Replace(line, o.KeepLinkUrls ? "$1 ($2)" : "$1");
            line = Bold.Replace(line, "$1");
            line = BoldU.Replace(line, "$1");
            line = Strike.Replace(line, "$1");
            line = Italic.Replace(line, "$1");
            if (o.StripInlineCodeDelimiters) line = InlineCode.Replace(line, "$1");
        }
        line = MultiSpace.Replace(line, " ");
        if (line.Trim().Length == 0) return string.Empty;

        if (o.CompressProse && !table && line.Trim().Length >= o.MinProseChars)
        {
            string prefix = string.Empty, rest = line;
            var lp = ListPrefix.Match(line);
            if (lp.Success) { prefix = lp.Groups[1].Value; rest = line[prefix.Length..]; }
            else { prefix = line[..(line.Length - line.TrimStart().Length)]; rest = line.TrimStart(); }
            if (rest.Length >= o.MinProseChars)
            {
                try
                {
                    var r = svc.CompressAsync(rest, o.Level).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(r.CompressedText) && r.CompressedText.Length < rest.Length)
                    {
                        prose++;
                        return prefix + r.CompressedText.Trim();
                    }
                }
                catch { /* keep the line as written */ }
            }
        }
        return line;
    }

    private static int CountTokens(string s)
        => string.IsNullOrWhiteSpace(s) ? 0 : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
