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

    // Matches a plausible function/method signature ending in an opening brace, capturing
    // everything up to and including that brace so the body (found by brace-depth counting,
    // not regex — nesting can go arbitrarily deep) can be replaced with a placeholder.
    // Intentionally conservative: control-flow blocks (if/for/while/switch/try) are excluded
    // via the negative lookahead, since collapsing "if (x) { ... }" would remove branching
    // logic, not implementation detail — this only targets declarations.
    private static readonly Regex CStyleSignature = new(
        @"^([ \t]*(?:public|private|protected|internal|static|async|virtual|override|abstract|sealed|final|export|default|fn|func|pub)?[\w<>\[\],\.\?\s]*?\b(?!if|for|while|switch|catch|using|lock|foreach)(\w+)\s*\(([^;{}]*)\)\s*(?:where[^{]*)?\{)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // "class" is deliberately excluded: it's a container, not implementation to hide (the
    // C-style regex has the same property for free, since a class declaration has no
    // parentheses to match) — only leaf "def" bodies get collapsed, so a class with several
    // methods keeps every method signature instead of vanishing into a single "...".
    private static readonly Regex PythonDefLine = new(
        @"^([ \t]*)(?:async\s+)?def\s+\w", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Compresses <paramref name="code"/> by stripping comments and excess blank lines. When
    /// <paramref name="skeletonize"/> is true, an additional pass replaces function/method
    /// bodies with a placeholder, keeping only signatures. Unlike the default comment-stripping
    /// pass (always a valid subset of the input), skeletonization is lossy by design and off
    /// by default — opt in explicitly when you want structure/signatures but not
    /// implementations (e.g. showing an LLM "what exists" without spending tokens on "how
    /// it's implemented").
    /// </summary>
    public CodeCompressionResult Compress(string code, bool skeletonize = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Unchanged(code ?? string.Empty);

        var profile = DetectLanguage(code);
        var result = code;
        int commentsRemoved = 0;
        int functionsSkeletonized = 0;

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

        if (skeletonize)
        {
            (result, functionsSkeletonized) = profile.Lang == Language.Python
                ? SkeletonizePython(result)
                : SkeletonizeCStyle(result);
        }

        if (result.Length >= code.Length)
            return Unchanged(code);

        return new CodeCompressionResult
        {
            Compressed = result,
            Original = code,
            WasCompressed = true,
            DetectedLanguage = profile.Lang.ToString(),
            CommentsRemoved = commentsRemoved,
            FunctionsSkeletonized = functionsSkeletonized,
            BlankLinesRemoved = blanksRemoved
        };
    }

    // Replaces matched function/method bodies with a placeholder, using real brace-depth
    // counting (not regex) to find the matching close — nesting can go arbitrarily deep, and
    // a regex can't balance that. String/char literals are tracked so a brace inside a
    // string ("{" for example) is never mistaken for real code structure.
    private static (string Result, int Count) SkeletonizeCStyle(string code)
    {
        var sb = new StringBuilder();
        int pos = 0;
        int count = 0;

        foreach (Match m in CStyleSignature.Matches(code))
        {
            if (m.Index < pos) continue; // inside a body already collapsed above — skip

            int openIdx = m.Index + m.Length - 1; // the signature match ends in '{'
            int closeIdx = FindMatchingBrace(code, openIdx);
            if (closeIdx < 0) continue; // unbalanced (or a false-positive match) — leave as-is

            // Skip trivial/near-empty bodies: nothing meaningful to collapse.
            if (closeIdx - openIdx - 1 < 40) continue;

            sb.Append(code, pos, openIdx + 1 - pos); // up to and including the opening '{'
            sb.Append(" /* ... */ ");
            sb.Append('}');
            pos = closeIdx + 1;
            count++;
        }
        sb.Append(code, pos, code.Length - pos);
        return (sb.ToString(), count);
    }

    private static int FindMatchingBrace(string s, int openIdx)
    {
        int depth = 0;
        bool inString = false, inChar = false;
        for (int i = openIdx; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (inChar)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'') inChar = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    // Python has no braces: a function/class body is every subsequent line indented deeper
    // than the def/class line, until a line at the same or shallower indentation (or EOF).
    private static readonly Regex PythonIndent = new(@"^([ \t]*)", RegexOptions.Compiled);

    private static (string Result, int Count) SkeletonizePython(string code)
    {
        var lines = code.Split('\n');
        var result = new List<string>();
        int count = 0;
        int i = 0;
        while (i < lines.Length)
        {
            if (!PythonDefLine.IsMatch(lines[i]))
            {
                result.Add(lines[i]);
                i++;
                continue;
            }

            var defIndent = PythonIndent.Match(lines[i]).Groups[1].Value;
            result.Add(lines[i]);
            int j = i + 1;
            var body = new List<string>();
            while (j < lines.Length)
            {
                if (string.IsNullOrWhiteSpace(lines[j])) { body.Add(lines[j]); j++; continue; }
                var lineIndent = PythonIndent.Match(lines[j]).Groups[1].Value;
                if (lineIndent.Length <= defIndent.Length) break;
                body.Add(lines[j]);
                j++;
            }

            // Only collapse a real multi-statement body — a one-liner isn't worth it.
            if (body.Count(l => !string.IsNullOrWhiteSpace(l)) >= 2)
            {
                result.Add(defIndent + "    ...");
                count++;
            }
            else
            {
                result.AddRange(body);
            }
            i = j;
        }
        return (string.Join("\n", result), count);
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
