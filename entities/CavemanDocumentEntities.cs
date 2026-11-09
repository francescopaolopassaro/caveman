// -----------------------------------------------------------------------------
// <copyright file="CavemanDocumentEntities.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Options and result types of CavemanDocumentCompressor (whole markdown documents such as agent instruction files).</summary>
// -----------------------------------------------------------------------------
using caveman.core.services;

namespace caveman.core.entities;

/// <summary>
/// Options for <see cref="CavemanDocumentCompressor"/>. Every property has a conservative default;
/// <see cref="Maximum"/> is the preset that squeezes a document as hard as the library can.
/// </summary>
public sealed class CavemanDocumentOptions
{
    /// <summary>NLP level applied to prose lines (code, tables and fenced blocks never go through it).</summary>
    public CavemanCompressionLevel Level { get; init; } = CavemanCompressionLevel.Syntactic;

    /// <summary>
    /// When <c>true</c> (default) the document is returned untouched if <see cref="CavemanSafetyGuard"/> finds critical content
    /// (destructive commands such as <c>rm -rf</c> or <c>git push --force</c>, CVEs, security terms). Set it to <c>false</c> to
    /// compress such documents anyway — for instance an instruction file that merely NAMES those commands as forbidden.
    /// </summary>
    public bool CheckDestructiveCommands { get; init; } = true;

    /// <summary>Removes markdown decoration that carries no meaning for a model: heading hashes, bold/italic/strike markers, blockquote markers, horizontal rules, table separator rows, link/image syntax (the link text and, optionally, the URL stay).</summary>
    public bool StripMarkdownDecoration { get; init; } = true;

    /// <summary>Keeps the URL of a link as <c>text (url)</c>; <c>false</c> keeps only the text.</summary>
    public bool KeepLinkUrls { get; init; } = true;

    /// <summary>Removes the backtick delimiters of inline code (<c>`Foo.Bar`</c> becomes <c>Foo.Bar</c>). The code itself is never changed.</summary>
    public bool StripInlineCodeDelimiters { get; init; }

    /// <summary>Minifies fenced blocks and embedded structures without loss: JSON, XML, HTML, SQL and code (comments and blank lines) through <see cref="CavemanStructuredCompressor"/>.</summary>
    public bool MinifyEmbeddedStructures { get; init; } = true;

    /// <summary>Removes HTML comments (<c>&lt;!-- ... --&gt;</c>) outside code blocks.</summary>
    public bool StripComments { get; init; } = true;

    /// <summary>Drops every blank line (<c>true</c>) or only collapses runs of them to one (<c>false</c>).</summary>
    public bool RemoveBlankLines { get; init; } = true;

    /// <summary>Compresses prose lines with the NLP level above; <c>false</c> applies only the structural clean-up.</summary>
    public bool CompressProse { get; init; } = true;

    /// <summary>
    /// Keeps comparison operators (<c>&gt;0</c>, <c>!=</c>), backtick spans, hyphenated ids (<c>RF-M1-01</c>), paths, file names, camelCase identifiers
    /// and acronyms verbatim at every level (see <see cref="CavemanCompressionService.WithCodeProtection"/>).
    /// </summary>
    public bool PreserveOperatorsAndIdentifiers { get; init; } = true;

    /// <summary>Prose lines shorter than this are left as written: nothing to gain.</summary>
    public int MinProseChars { get; init; } = 30;

    /// <summary>The preset with the highest compression: Aggressive NLP, destructive-command check OFF, decoration stripped, embedded structures minified, blank lines removed, operators and identifiers preserved.</summary>
    public static CavemanDocumentOptions Maximum => new()
    {
        Level = CavemanCompressionLevel.Aggressive,
        CheckDestructiveCommands = false,
        StripInlineCodeDelimiters = false,
    };

    /// <summary>The prudent preset: Light NLP, destructive-command check ON.</summary>
    public static CavemanDocumentOptions Safe => new()
    {
        Level = CavemanCompressionLevel.Light,
        CheckDestructiveCommands = true,
    };
}

/// <summary>Outcome of <see cref="CavemanDocumentCompressor.CompressAsync"/>.</summary>
public sealed class DocumentCompressionResult
{
    /// <summary>The compressed document (or the original when nothing was gained or the safety guard declined).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Characters of the input, measured on LF-normalised text so that CRLF to LF never counts as a saving.</summary>
    public int CharsBefore { get; init; }

    /// <summary>Characters of <see cref="Text"/>.</summary>
    public int CharsAfter { get; init; }

    /// <summary>Tokens of the input counted by the configured <see cref="ITokenCounter"/>.</summary>
    public int TokensBefore { get; init; }

    /// <summary>Tokens of <see cref="Text"/> counted by the same counter.</summary>
    public int TokensAfter { get; init; }

    /// <summary>True when the output differs from the input because something was compressed.</summary>
    public bool WasCompressed { get; init; }

    /// <summary>True when <see cref="CavemanDocumentOptions.CheckDestructiveCommands"/> was on and the guard declined the whole document.</summary>
    public bool SkippedBySafetyGuard { get; init; }

    /// <summary>The verdict of the safety guard; null when the check was disabled.</summary>
    public SafetyVerdict? SafetyVerdict { get; init; }

    /// <summary>Fenced blocks that were minified.</summary>
    public int FencedBlocksMinified { get; init; }

    /// <summary>Prose lines that went through the NLP level.</summary>
    public int ProseLinesCompressed { get; init; }

    /// <summary>Characters saved (never negative).</summary>
    public int SavedChars => Math.Max(0, CharsBefore - CharsAfter);

    /// <summary>Percentage of characters removed, 0-100.</summary>
    public double SavedPercent => CharsBefore == 0 ? 0 : 100.0 * SavedChars / CharsBefore;
}
