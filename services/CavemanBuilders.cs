// -----------------------------------------------------------------------------
// <copyright file="CavemanBuilders.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Fluent builders for composing CavemanTextRank / CavemanContextWindow with injected seams.</summary>
// -----------------------------------------------------------------------------
using caveman.core;

namespace caveman.core.services;

/// <summary>
/// Fluent builder for <see cref="CavemanTextRank"/>: set only the seams you want to override
/// (word data, token counter, parser, compression engine, language detector); the rest default.
/// Avoids the long positional constructor.
/// </summary>
public sealed class CavemanTextRankBuilder
{
    private FunctionWordProvider? _wordProvider;
    private ITokenCounter? _tokenCounter;
    private IConversationParser? _parser;
    private ICompressionService? _compressionService;
    private ILanguageDetector? _detector;

    public CavemanTextRankBuilder WithWordProvider(FunctionWordProvider wordProvider)
    {
        _wordProvider = wordProvider;
        return this;
    }

    public CavemanTextRankBuilder WithTokenCounter(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter;
        return this;
    }

    public CavemanTextRankBuilder WithParser(IConversationParser parser)
    {
        _parser = parser;
        return this;
    }

    public CavemanTextRankBuilder WithCompressionService(ICompressionService compressionService)
    {
        _compressionService = compressionService;
        return this;
    }

    public CavemanTextRankBuilder WithLanguageDetector(ILanguageDetector detector)
    {
        _detector = detector;
        return this;
    }

    public CavemanTextRank Build() =>
        new(_wordProvider ?? new FunctionWordProvider(), _tokenCounter, _parser, _compressionService, _detector);
}

/// <summary>
/// Fluent builder for <see cref="CavemanContextWindow"/>. The token budget is required;
/// everything else is optional.
/// </summary>
public sealed class CavemanContextWindowBuilder
{
    private int _maxTokens;
    private LlmModel _model = LlmModel.Gpt4;
    private int _keepLastTurns = 4;
    private string? _sessionId;
    private bool _deduplicateOnAppend;
    private CavemanTextRank? _textRank;
    private ITokenCounter? _tokenCounter;
    private ICompressionService? _compressionService;

    public CavemanContextWindowBuilder WithMaxTokens(int maxTokens)
    {
        _maxTokens = maxTokens;
        return this;
    }

    public CavemanContextWindowBuilder WithModel(LlmModel model)
    {
        _model = model;
        return this;
    }

    public CavemanContextWindowBuilder WithKeepLastTurns(int keepLastTurns)
    {
        _keepLastTurns = keepLastTurns;
        return this;
    }

    public CavemanContextWindowBuilder WithSessionId(string sessionId)
    {
        _sessionId = sessionId;
        return this;
    }

    public CavemanContextWindowBuilder WithDeduplicateOnAppend(bool enabled = true)
    {
        _deduplicateOnAppend = enabled;
        return this;
    }

    public CavemanContextWindowBuilder WithTextRank(CavemanTextRank textRank)
    {
        _textRank = textRank;
        return this;
    }

    public CavemanContextWindowBuilder WithTokenCounter(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter;
        return this;
    }

    public CavemanContextWindowBuilder WithCompressionService(ICompressionService compressionService)
    {
        _compressionService = compressionService;
        return this;
    }

    public CavemanContextWindow Build()
    {
        if (_maxTokens <= 0)
            throw new InvalidOperationException("MaxTokens must be set to a positive value (call WithMaxTokens).");

        return new CavemanContextWindow(_maxTokens, _model, _textRank, _tokenCounter, _compressionService)
        {
            KeepLastTurns = _keepLastTurns,
            SessionId = _sessionId,
            DeduplicateOnAppend = _deduplicateOnAppend
        };
    }
}
