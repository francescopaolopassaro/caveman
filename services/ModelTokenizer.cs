// -----------------------------------------------------------------------------
// <copyright file="ModelTokenizer.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Approximate token counting for common LLM models.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

public enum LlmModel
{
    Gpt4,
    Gpt3_5Turbo,
    Llama3,
    Gemma3,
    Claude3
}

/// <summary>
/// Abstraction over token counting so callers can plug in a real BPE/tiktoken counter
/// instead of the built-in heuristic. The default implementation is <see cref="ModelTokenizer"/>.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Counts the tokens of <paramref name="text"/> for the given <paramref name="model"/>.</summary>
    int CountTokens(string text, LlmModel model = LlmModel.Gpt4);
}

public class ModelTokenizer : ITokenCounter
{
    private static readonly Dictionary<LlmModel, string> ModelPatterns = new()
    {
        [LlmModel.Gpt4] = "gpt-4",
        [LlmModel.Gpt3_5Turbo] = "gpt-3.5",
        [LlmModel.Llama3] = "llama-3",
        [LlmModel.Gemma3] = "gemma-3",
        [LlmModel.Claude3] = "claude-3"
    };

    private static readonly HashSet<string> ShortTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "in", "on", "at", "to", "of", "is", "it",
        "as", "by", "or", "be", "if", "do", "no", "up", "we", "he",
        "she", "me", "my", "I", "u", "x", "y", "z", "&", "|"
    };

    private static readonly Regex WordSplit = new(
        @"[a-zA-Z0-9_]+|[^\s]",
        RegexOptions.Compiled);

    public int CountTokens(string text, LlmModel model = LlmModel.Gpt4)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        int baseTokens = CountBpeApprox(text);

        return model switch
        {
            LlmModel.Gpt4 => baseTokens,
            LlmModel.Gpt3_5Turbo => baseTokens,
            LlmModel.Llama3 => (int)(baseTokens * 1.05),
            LlmModel.Gemma3 => (int)(baseTokens * 1.02),
            LlmModel.Claude3 => (int)(baseTokens * 0.98),
            _ => baseTokens
        };
    }

    public (int gpt4, int gpt35, int llama3, int gemma3, int claude3) CountAllModels(string text)
    {
        int b = CountBpeApprox(text);
        return (
            b,
            b,
            (int)(b * 1.05),
            (int)(b * 1.02),
            (int)(b * 0.98)
        );
    }

    private int CountBpeApprox(string text)
    {
        int tokenCount = 0;
        var matches = WordSplit.Matches(text);

        foreach (Match match in matches)
        {
            var word = match.Value;

            if (ShortTokens.Contains(word))
            {
                tokenCount += 1;
                continue;
            }

            if (word.Length <= 4)
            {
                tokenCount += 1;
            }
            else if (word.Length <= 8)
            {
                tokenCount += 2;
            }
            else
            {
                tokenCount += 2 + (word.Length - 8 + 3) / 4;
            }
        }

        int whitespaceTokens = CountWhitespaceTokens(text);
        tokenCount += whitespaceTokens;

        return Math.Max(1, tokenCount);
    }

    private static int CountWhitespaceTokens(string text)
    {
        int count = 0;
        int run = 0;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                run++;
            }
            else
            {
                if (run >= 4)
                    count += run / 4;
                run = 0;
            }
        }
        if (run >= 4)
            count += run / 4;
        return count;
    }

    public string ModelName(LlmModel model) =>
        ModelPatterns.GetValueOrDefault(model, "unknown");
}
