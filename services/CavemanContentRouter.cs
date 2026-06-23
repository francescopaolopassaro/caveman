// -----------------------------------------------------------------------------
// <copyright file="CavemanContentRouter.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Content-aware compression pipeline: detects content type, checks two-tier cache, and routes to the best compressor.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Detects the structural type of content and routes it to the most appropriate compressor,
/// with a two-tier cache to skip re-compression of identical content:
/// <list type="bullet">
///   <item>JSON arrays → <see cref="CavemanJsonCrusher"/> (lossless CSV/table or lossy BM25 row-drop)</item>
///   <item>Log/stack traces → <see cref="CavemanLogCompressor"/></item>
///   <item>Search results → <see cref="CavemanSearchCompressor"/></item>
///   <item>Git diffs → <see cref="CavemanDiffCompressor"/></item>
///   <item>HTML → <see cref="CavemanHtmlExtractor"/> then NLP compression</item>
///   <item>Plain text / JSON objects → NLP compression via <see cref="ICompressionService"/></item>
///   <item>Code → passthrough (structural content; lexical compression would break it)</item>
/// </list>
/// </summary>
public sealed class CavemanContentRouter
{
    private readonly CavemanContentDetector _detector;
    private readonly CavemanJsonCrusher _crusher;
    private readonly ICompressionService _compression;
    private readonly ITokenCounter _tokenCounter;
    private readonly CavemanLogCompressor _logCompressor;
    private readonly CavemanSearchCompressor _searchCompressor;
    private readonly CavemanDiffCompressor _diffCompressor;
    private readonly CavemanHtmlExtractor _htmlExtractor;
    private readonly CavemanCodeCompressor _codeCompressor;
    private readonly CavemanTabularCompressor _tabularCompressor;
    private readonly CavemanCompressionCache _cache;
    private readonly CavemanCompressionLevel _proseLevel;

    private int _consecutiveFailures;
    private const int CircuitBreakerThreshold = 3;

    /// <param name="compression">NLP compressor for prose. Defaults to a new <see cref="CavemanCompressionService"/>.</param>
    /// <param name="tokenCounter">Token counter. Defaults to a new <see cref="ModelTokenizer"/>.</param>
    /// <param name="ccrStore">CCR store for dropped JSON rows. Defaults to <see cref="CavemanCcrStore.Instance"/>.</param>
    /// <param name="crusher">JSON crusher. Defaults to a new instance with 15 max output items.</param>
    /// <param name="cache">Two-tier compression cache. Defaults to a new instance with 30-min TTL.</param>
    /// <param name="proseLevel">NLP compression level applied to plain-text content.</param>
    public CavemanContentRouter(
        ICompressionService? compression = null,
        ITokenCounter? tokenCounter = null,
        CavemanCcrStore? ccrStore = null,
        CavemanJsonCrusher? crusher = null,
        CavemanCompressionCache? cache = null,
        CavemanCompressionLevel proseLevel = CavemanCompressionLevel.Semantic)
    {
        _compression = compression ?? new CavemanCompressionService();
        _tokenCounter = tokenCounter ?? new ModelTokenizer();
        _crusher = crusher ?? new CavemanJsonCrusher(ccrStore);
        _cache = cache ?? new CavemanCompressionCache();
        _proseLevel = proseLevel;
        _detector = new CavemanContentDetector();
        _logCompressor = new CavemanLogCompressor();
        _searchCompressor = new CavemanSearchCompressor();
        _diffCompressor = new CavemanDiffCompressor();
        _htmlExtractor = new CavemanHtmlExtractor();
        _codeCompressor = new CavemanCodeCompressor();
        _tabularCompressor = new CavemanTabularCompressor();
    }

    /// <summary>Creates a pre-configured router from a <see cref="CompressionProfile"/> preset.</summary>
    public static CavemanContentRouter FromProfile(CompressionProfile profile)
    {
        var (maxItems, proseLevel) = profile switch
        {
            CompressionProfile.Light      => (25, CavemanCompressionLevel.Light),
            CompressionProfile.Balanced   => (15, CavemanCompressionLevel.Semantic),
            CompressionProfile.Agent      => (10, CavemanCompressionLevel.Semantic),
            CompressionProfile.Aggressive => (8,  CavemanCompressionLevel.Aggressive),
            _                             => (15, CavemanCompressionLevel.Semantic)
        };
        var cache = profile == CompressionProfile.Agent
            ? new CavemanCompressionCache(TimeSpan.FromMinutes(5))
            : new CavemanCompressionCache();
        return new CavemanContentRouter(
            crusher: new CavemanJsonCrusher { MaxOutputItems = maxItems },
            cache: cache,
            proseLevel: proseLevel);
    }

    /// <summary>Synchronous wrapper around <see cref="RouteAsync"/>.</summary>
    public RoutedCompressionResult Route(string content, string? query = null)
        => RouteAsync(content, query).GetAwaiter().GetResult();

    /// <summary>Detects the content type and dispatches to the appropriate compressor.</summary>
    public async Task<RoutedCompressionResult> RouteAsync(
        string content,
        string? query = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(content))
            return Passthrough(content, ContentType.PlainText);

        // Circuit breaker
        if (_consecutiveFailures >= CircuitBreakerThreshold)
            return Passthrough(content, ContentType.PlainText);

        // Tier-1 cache: known non-compressible
        if (_cache.IsSkipped(content))
            return Passthrough(content, ContentType.PlainText);

        // Tier-2 cache: previously compressed
        if (_cache.TryGetResult(content, out var cached))
        {
            int tokensBefore = _tokenCounter.CountTokens(content, LlmModel.Gpt4);
            int tokensAfter = _tokenCounter.CountTokens(cached!.Compressed, LlmModel.Gpt4);
            return new RoutedCompressionResult
            {
                Compressed = cached.Compressed,
                Original = content,
                DetectedType = ContentType.PlainText,
                StrategyUsed = cached.Strategy + ":cached",
                TokensBefore = tokensBefore,
                TokensAfter = tokensAfter
            };
        }

        var detection = _detector.Detect(content);
        int before = _tokenCounter.CountTokens(content, LlmModel.Gpt4);

        try
        {
            RoutedCompressionResult result = detection.Type switch
            {
                ContentType.JsonArray       => RouteJsonArray(content, query, before),
                ContentType.LogOrStacktrace => RouteLog(content, query, before),
                ContentType.SearchResults   => RouteSearch(content, query, before),
                ContentType.GitDiff         => RouteDiff(content, before),
                ContentType.Html            => await RouteHtmlAsync(content, before, ct),
                ContentType.PlainText       => await RouteProseAsync(content, detection.Type, before, ct),
                ContentType.JsonObject      => await RouteProseAsync(content, detection.Type, before, ct, light: true),
                ContentType.Code            => RouteCode(content, before),
                ContentType.Tabular         => RouteTabular(content, query, before),
                _                           => Passthrough(content, detection.Type)
            };

            // Inflation guard: if the output is longer than the input, revert
            if (result.TokensAfter > result.TokensBefore && result.TokensBefore > 0)
                result = Passthrough(content, detection.Type);

            Interlocked.Exchange(ref _consecutiveFailures, 0);

            // Update cache
            if (result.WasCompressed())
                _cache.PutResult(content, result.Compressed, result.SavingsPercent / 100.0, result.StrategyUsed);
            else
                _cache.MarkSkip(content);

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _consecutiveFailures);
            return new RoutedCompressionResult
            {
                Compressed = content, Original = content,
                DetectedType = detection.Type, StrategyUsed = "Passthrough",
                TokensBefore = before, TokensAfter = before,
                ErrorMessage = ex.Message
            };
        }
    }

    // ─── Routing methods ────────────────────────────────────────────────────

    private RoutedCompressionResult RouteJsonArray(string content, string? query, int tokensBefore)
    {
        var crush = _crusher.Crush(content, query);
        int tokensAfter = crush.WasCrushed
            ? _tokenCounter.CountTokens(crush.Compressed, LlmModel.Gpt4)
            : tokensBefore;

        return new RoutedCompressionResult
        {
            Compressed = crush.Compressed, Original = content,
            DetectedType = ContentType.JsonArray,
            StrategyUsed = crush.Strategy switch
            {
                JsonCrushStrategy.Csv => "JsonCrush:Csv",
                JsonCrushStrategy.MarkdownTable => "JsonCrush:MarkdownTable",
                JsonCrushStrategy.LossyRowDrop => "JsonCrush:RowDrop",
                _ => "Passthrough"
            },
            TokensBefore = tokensBefore, TokensAfter = tokensAfter,
            CcrHash = crush.CcrHash
        };
    }

    private RoutedCompressionResult RouteLog(string content, string? query, int tokensBefore)
    {
        var r = _logCompressor.Compress(content, query);
        int tokensAfter = r.WasCompressed ? _tokenCounter.CountTokens(r.Compressed, LlmModel.Gpt4) : tokensBefore;
        return new RoutedCompressionResult
        {
            Compressed = r.Compressed, Original = content,
            DetectedType = ContentType.LogOrStacktrace,
            StrategyUsed = r.WasCompressed ? "LogCompression" : "Passthrough",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private RoutedCompressionResult RouteSearch(string content, string? query, int tokensBefore)
    {
        var r = _searchCompressor.Compress(content, query);
        int tokensAfter = r.WasCompressed ? _tokenCounter.CountTokens(r.Compressed, LlmModel.Gpt4) : tokensBefore;
        return new RoutedCompressionResult
        {
            Compressed = r.Compressed, Original = content,
            DetectedType = ContentType.SearchResults,
            StrategyUsed = r.WasCompressed ? "SearchCompression" : "Passthrough",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private RoutedCompressionResult RouteDiff(string content, int tokensBefore)
    {
        var r = _diffCompressor.Compress(content);
        int tokensAfter = r.WasCompressed ? _tokenCounter.CountTokens(r.Compressed, LlmModel.Gpt4) : tokensBefore;
        return new RoutedCompressionResult
        {
            Compressed = r.Compressed, Original = content,
            DetectedType = ContentType.GitDiff,
            StrategyUsed = r.WasCompressed ? "DiffCompression" : "Passthrough",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private async Task<RoutedCompressionResult> RouteHtmlAsync(string content, int tokensBefore, CancellationToken ct)
    {
        var extracted = _htmlExtractor.Extract(content);
        if (extracted.Length >= content.Length)
            return Passthrough(content, ContentType.Html);

        // Then NLP-compress the extracted text
        var r = await _compression.CompressAsync(extracted, CavemanCompressionLevel.Semantic, ct);
        int tokensAfter = _tokenCounter.CountTokens(r.CompressedText, LlmModel.Gpt4);
        return new RoutedCompressionResult
        {
            Compressed = r.CompressedText, Original = content,
            DetectedType = ContentType.Html,
            StrategyUsed = "HtmlExtract+NlpCompression",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private RoutedCompressionResult RouteTabular(string content, string? query, int tokensBefore)
    {
        var r = _tabularCompressor.Compress(content, query);
        int tokensAfter = r.WasCompressed ? _tokenCounter.CountTokens(r.Compressed, LlmModel.Gpt4) : tokensBefore;
        return new RoutedCompressionResult
        {
            Compressed = r.Compressed, Original = content,
            DetectedType = ContentType.Tabular,
            StrategyUsed = r.WasCompressed ? "TabularCompression" : "Passthrough",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private async Task<RoutedCompressionResult> RouteProseAsync(
        string content, ContentType type, int tokensBefore, CancellationToken ct, bool light = false)
    {
        var level = light ? CavemanCompressionLevel.Light : _proseLevel;
        var r = await _compression.CompressAsync(content, level, ct);
        int tokensAfter = _tokenCounter.CountTokens(r.CompressedText, LlmModel.Gpt4);
        return new RoutedCompressionResult
        {
            Compressed = r.CompressedText, Original = content,
            DetectedType = type,
            StrategyUsed = light ? "LightNlp" : "NlpCompression",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private RoutedCompressionResult RouteCode(string content, int tokensBefore)
    {
        var r = _codeCompressor.Compress(content);
        int tokensAfter = r.WasCompressed ? _tokenCounter.CountTokens(r.Compressed, LlmModel.Gpt4) : tokensBefore;
        return new RoutedCompressionResult
        {
            Compressed = r.Compressed, Original = content,
            DetectedType = ContentType.Code,
            StrategyUsed = r.WasCompressed ? "CodeCompression" : "Passthrough",
            TokensBefore = tokensBefore, TokensAfter = tokensAfter
        };
    }

    private static RoutedCompressionResult Passthrough(string content, ContentType type) =>
        new()
        {
            Compressed = content, Original = content,
            DetectedType = type, StrategyUsed = "Passthrough",
            TokensBefore = 0, TokensAfter = 0
        };
}

internal static class RoutedCompressionResultExtensions
{
    internal static bool WasCompressed(this RoutedCompressionResult r) =>
        r.StrategyUsed != "Passthrough" && !r.StrategyUsed.EndsWith(":cached") && r.Compressed != r.Original;
}
