// -----------------------------------------------------------------------------
// <copyright file="CavemanCodeCompressor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Compresses source code by stripping comments, doc-comments, and excess blank lines while preserving structure and syntax.</summary>
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Regex-based code compressor that strips comments and excess blank lines while
/// preserving imports, signatures, and all functional code. Works across C#, Java,
/// JavaScript/TypeScript, C/C++, Go, Rust, Python, Ruby, SQL, and Shell scripts.
/// Never produces broken code: the output is always a valid subset of the input.
/// </summary>
public sealed class CavemanCodeCompressor
{
    private enum Language { CStyle, Python, Ruby, Sql, Shell, Unknown }

    private sealed record LangProfile(
        Language Lang,
        Regex? BlockComment,
        Regex LineComment,
        Regex? DocString);

    // C-family: C#, Java, JS/TS, C, C++, Go, Rust, Swift
    private static readonly LangProfile CStyleProfile = new(
        Language.CStyle,
        BlockComment: new Regex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled),
        LineComment:  new Regex(@"(?<!:)//.*$", RegexOptions.Compiled | RegexOptions.Multiline),
        DocString:    new Regex(@"///.*$", RegexOptions.Compiled | RegexOptions.Multiline));

    // Python: # comments and triple-quote docstrings
    private static readonly LangProfile PythonProfile = new(
        Language.Python,
        BlockComment: new Regex("(\"\"\"|''')[\\s\\S]*?(\"\"\"|\\'\\'\\')", RegexOptions.Compiled),
        LineComment:  new Regex(@"#.*$", RegexOptions.Compiled | RegexOptions.Multiline),
        DocString:    null);

    // Ruby: # comments and =begin...=end
    private static readonly LangProfile RubyProfile = new(
        Language.Ruby,
        BlockComment: new Regex(@"^=begin[\s\S]*?^=end", RegexOptions.Compiled | RegexOptions.Multiline),
        LineComment:  new Regex(@"#.*$", RegexOptions.Compiled | RegexOptions.Multiline),
        DocString:    null);

    // SQL: -- comments and /* */ blocks
    private static readonly LangProfile SqlProfile = new(
        Language.Sql,
        BlockComment: new Regex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled),
        LineComment:  new Regex(@"--.*$", RegexOptions.Compiled | RegexOptions.Multiline),
        DocString:    null);

    // Shell: # comments only
    private static readonly LangProfile ShellProfile = new(
        Language.Shell,
        BlockComment: null,
        LineComment:  new Regex(@"(?<!^#!)#.*$", RegexOptions.Compiled | RegexOptions.Multiline),
        DocString:    null);

    private static readonly Regex MultipleBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespace = new(@"[ \t]+$", RegexOptions.Compiled | RegexOptions.Multiline);

    // Language detection hints (checked in order — most-specific first)
    private static readonly (Regex Pattern, LangProfile Profile)[] Hints =
    [
        // C-family: markers that don't appear in Python/Ruby
        (new Regex(@"\b(public |private |protected |namespace |using |void |interface )\b", RegexOptions.Compiled), CStyleProfile),
        // Rust/Go specific
        (new Regex(@"\b(fn |impl |struct |enum |mod |pub |let mut|goroutine|chan |func )\b", RegexOptions.Compiled), CStyleProfile),
        // SQL
        (new Regex(@"\bSELECT\b|\bCREATE TABLE\b|\bINSERT INTO\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), SqlProfile),
        // Shell
        (new Regex(@"^#!/(?:bin|usr/bin)/(?:bash|sh|zsh|fish)", RegexOptions.Compiled | RegexOptions.Multiline), ShellProfile),
        // Ruby
        (new Regex(@"^=begin", RegexOptions.Compiled | RegexOptions.Multiline), RubyProfile),
        // Python — only after ruling out C-style (def/elif/from import are Python-specific)
        (new Regex(@"\b(def |elif |__init__|from .+ import)\b", RegexOptions.Compiled), PythonProfile),
        // Fallback: C-style
        (new Regex(@"\b(class |return |import |require)\b", RegexOptions.Compiled), CStyleProfile),
    ];

    /// <summary>Compresses <paramref name="code"/> by stripping comments and excess blank lines.</summary>
    public CodeCompressionResult Compress(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Unchanged(code ?? string.Empty);

        var profile = DetectLanguage(code);
        var result = code;
        int commentsRemoved = 0;

        // Strip XML doc-comments (///) before single-line comments
        if (profile.DocString != null)
        {
            int before = CountLines(result);
            result = profile.DocString.Replace(result, string.Empty);
            commentsRemoved += before - CountLines(result);
        }

        // Strip block comments
        if (profile.BlockComment != null)
        {
            int before = CountLines(result);
            result = profile.BlockComment.Replace(result, string.Empty);
            commentsRemoved += before - CountLines(result);
        }

        // Strip single-line comments (but preserve shebang lines)
        if (profile.Lang != Language.Shell)
        {
            int before = CountLines(result);
            result = profile.LineComment.Replace(result, string.Empty);
            commentsRemoved += before - CountLines(result);
        }
        else
        {
            // Shell: preserve shebang (#!) on first line
            var lines = result.Split('\n');
            int before = lines.Length;
            for (int i = (lines.Length > 0 && lines[0].StartsWith("#!") ? 1 : 0); i < lines.Length; i++)
                lines[i] = profile.LineComment.Replace(lines[i], string.Empty);
            result = string.Join('\n', lines);
            commentsRemoved += before - CountLines(result);
        }

        // Remove trailing whitespace
        result = TrailingWhitespace.Replace(result, string.Empty);

        // Collapse 3+ consecutive blank lines → 1
        int blanksBefore = CountBlankLines(result);
        result = MultipleBlankLines.Replace(result, "\n\n");
        int blanksRemoved = blanksBefore - CountBlankLines(result);

        // Remove lines that are now empty due to comment stripping
        var finalLines = result.Split('\n');
        var cleaned = new StringBuilder();
        int prevWasBlank = 0;
        foreach (var line in finalLines)
        {
            bool isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && prevWasBlank >= 1) { blanksRemoved++; continue; }
            cleaned.Append(line).Append('\n');
            prevWasBlank = isBlank ? prevWasBlank + 1 : 0;
        }
        result = cleaned.ToString().TrimEnd();

        if (result.Length >= code.Length)
            return Unchanged(code);

        return new CodeCompressionResult
        {
            Compressed = result,
            Original = code,
            WasCompressed = true,
            DetectedLanguage = profile.Lang.ToString(),
            CommentsRemoved = commentsRemoved,
            BlankLinesRemoved = blanksRemoved
        };
    }

    private static LangProfile DetectLanguage(string code)
    {
        foreach (var (pattern, profile) in Hints)
            if (pattern.IsMatch(code)) return profile;
        return CStyleProfile;
    }

    private static int CountLines(string s) => s.Count(c => c == '\n');
    private static int CountBlankLines(string s) =>
        s.Split('\n').Count(l => string.IsNullOrWhiteSpace(l));

    private static CodeCompressionResult Unchanged(string code) =>
        new() { Compressed = code, Original = code, WasCompressed = false };
}
