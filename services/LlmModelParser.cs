// -----------------------------------------------------------------------------
// <copyright file="LlmModelParser.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Maps free-form model names ("deepseek-v3", "gemini-2.5-pro", "qwen2.5-72b", ...) to LlmModel.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.services;

/// <summary>
/// Maps a free-form model or product name to the <see cref="LlmModel"/> family used for token counting and pricing.
/// Matching is by family keyword, so versions and sizes do not matter ("qwen2.5-72b-instruct", "gemini-2.5-pro",
/// "deepseek-r1", "glm-4.6", "grok-4", "us.amazon.nova-pro-v1:0" ...). Unknown names map to <see cref="LlmModel.Gpt4"/>.
/// </summary>
public static class LlmModelParser
{
    private static readonly (string[] Keywords, LlmModel Model)[] Families =
    {
        (new[] { "gpt-3.5", "gpt3.5", "gpt35" }, LlmModel.Gpt3_5Turbo),
        (new[] { "claude", "anthropic", "sonnet", "opus", "haiku", "fable" }, LlmModel.Claude3),
        (new[] { "gemini" }, LlmModel.Gemini),
        (new[] { "gemma" }, LlmModel.Gemma3),
        (new[] { "llama" }, LlmModel.Llama3),
        (new[] { "qwen", "qwq", "tongyi" }, LlmModel.Qwen),
        (new[] { "deepseek" }, LlmModel.DeepSeek),
        (new[] { "grok", "xai" }, LlmModel.Grok),
        (new[] { "glm", "zhipu", "z.ai", "chatglm" }, LlmModel.Glm),
        (new[] { "spark", "xunfei", "iflytek" }, LlmModel.Spark),
        (new[] { "mistral", "mixtral", "codestral", "ministral", "pixtral" }, LlmModel.Mistral),
        (new[] { "nova" }, LlmModel.Nova),
        (new[] { "kimi", "moonshot" }, LlmModel.Kimi),
        (new[] { "ernie", "wenxin" }, LlmModel.Ernie),
        (new[] { "command", "cohere" }, LlmModel.Command),
        (new[] { "gpt", "openai", "chatgpt", "o1", "o3", "o4" }, LlmModel.Gpt4),
    };

    /// <summary>Returns the model family for <paramref name="name"/>; <see cref="LlmModel.Gpt4"/> when nothing matches.</summary>
    public static LlmModel Parse(string? name)
    {
        var n = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (n.Length == 0) return LlmModel.Gpt4;
        foreach (var (keywords, model) in Families)
            foreach (var k in keywords)
                if (n.Contains(k, StringComparison.Ordinal))
                    return model;
        return LlmModel.Gpt4;
    }
}
