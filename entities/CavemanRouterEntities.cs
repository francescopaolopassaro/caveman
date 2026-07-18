// -----------------------------------------------------------------------------
// <copyright file="CavemanRouterEntities.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Result and config types for the content-aware routing pipeline (ContentRouter, JsonCrusher, CacheAligner, CcrStore).</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.entities;

/// <summary>Structural type of a piece of content, as detected by <see cref="caveman.core.services.CavemanContentDetector"/>.</summary>
public enum ContentType
{
    PlainText,
    JsonArray,
    JsonObject,
    Code,
    LogOrStacktrace,
    GitDiff,
    Html,
    SearchResults,
    Tabular
}

/// <summary>Outcome of content-type detection.</summary>
public sealed class ContentDetectionResult
{
    /// <summary>Detected content type.</summary>
    public ContentType Type { get; init; }
    /// <summary>Detection confidence, 0.0–1.0.</summary>
    public float Confidence { get; init; }
}

/// <summary>Compression strategy applied by <see cref="caveman.core.services.CavemanJsonCrusher"/>.</summary>
public enum JsonCrushStrategy
{
    /// <summary>No compression was applied.</summary>
    None,
    /// <summary>Lossless CSV compaction (schema header + one row per line).</summary>
    Csv,
    /// <summary>Lossless markdown table compaction.</summary>
    MarkdownTable,
    /// <summary>Lossy row-drop with BM25 relevance scoring and CCR marker.</summary>
    LossyRowDrop
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanJsonCrusher.Crush"/> operation.</summary>
public sealed class JsonCrushResult
{
    /// <summary>The compressed output (or the original when <see cref="WasCrushed"/> is false).</summary>
    public string Compressed { get; init; } = string.Empty;
    /// <summary>The original JSON array string.</summary>
    public string Original { get; init; } = string.Empty;
    /// <summary>True when the output is meaningfully different from the original.</summary>
    public bool WasCrushed { get; init; }
    /// <summary>The strategy that produced the result.</summary>
    public JsonCrushStrategy Strategy { get; init; }
    /// <summary>12-character hex SHA-256 prefix identifying the dropped rows (null unless <see cref="Strategy"/> is <see cref="JsonCrushStrategy.LossyRowDrop"/>).</summary>
    public string? CcrHash { get; init; }
    /// <summary>Number of rows in the original array.</summary>
    public int OriginalRows { get; init; }
    /// <summary>Number of rows in the compressed output.</summary>
    public int KeptRows { get; init; }
}

/// <summary>A single volatile-token finding from <see cref="caveman.core.services.CavemanCacheAligner"/>.</summary>
public sealed class VolatileFinding
{
    /// <summary>"UUID", "ISO8601", "JWT", or "HexHash".</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>First 40 characters of the matched token.</summary>
    public string Sample { get; init; } = string.Empty;
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanLogCompressor.Compress"/> call.</summary>
public sealed class LogCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public bool WasCompressed { get; init; }
    public int OriginalLines { get; init; }
    public int KeptLines { get; init; }
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanSearchCompressor.Compress"/> call.</summary>
public sealed class SearchCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public bool WasCompressed { get; init; }
    public int OriginalMatches { get; init; }
    public int KeptMatches { get; init; }
    public int FilesKept { get; init; }
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanDiffCompressor.Compress"/> call.</summary>
public sealed class DiffCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public bool WasCompressed { get; init; }
    public int HunksKept { get; init; }
    public int HunksDropped { get; init; }
    public int Additions { get; init; }
    public int Deletions { get; init; }
}

/// <summary>Waste signals detected in a piece of content by <see cref="caveman.core.services.CavemanWasteAnalyzer"/>.</summary>
public sealed class WasteAnalysis
{
    /// <summary>Estimated tokens wasted by HTML tags and comments.</summary>
    public int HtmlNoiseTokens { get; init; }
    /// <summary>Estimated tokens wasted by large base64-encoded blobs.</summary>
    public int Base64Tokens { get; init; }
    /// <summary>Estimated tokens wasted by excessive whitespace.</summary>
    public int WhitespaceTokens { get; init; }
    /// <summary>Estimated tokens wasted by large inline JSON structures.</summary>
    public int JsonBloatTokens { get; init; }
    /// <summary>Total estimated wasted tokens across all categories.</summary>
    public int TotalWasteTokens => HtmlNoiseTokens + Base64Tokens + WhitespaceTokens + JsonBloatTokens;
    /// <summary>True when any waste was detected.</summary>
    public bool HasWaste => TotalWasteTokens > 0;
}

/// <summary>A cached compression result stored by <see cref="caveman.core.services.CavemanCompressionCache"/>.</summary>
public sealed class CachedCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public double Ratio { get; init; }
    public string Strategy { get; init; } = string.Empty;
}

/// <summary>Verbosity steering level injected into system prompts by <see cref="caveman.core.services.CavemanOutputShaper"/>.</summary>
public enum VerbosityLevel
{
    /// <summary>No steering injected.</summary>
    Off = 0,
    /// <summary>Skip preamble and postamble; start with substance.</summary>
    SkipCeremony = 1,
    /// <summary>SkipCeremony + never restate code/files/diffs shown in context.</summary>
    NoRestatement = 2,
    /// <summary>NoRestatement + conclusions only, omit rationale unless asked.</summary>
    ConclusionsOnly = 3,
    /// <summary>Minimum tokens. Fragments OK. No preamble, restatement, rationale.</summary>
    MinimumTokens = 4
}

/// <summary>A compressed entry stored in <see cref="caveman.core.services.CavemanSharedContext"/>.</summary>
public sealed class SharedContextEntry
{
    public string Key { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public string Compressed { get; init; } = string.Empty;
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int TokensSaved => Math.Max(0, TokensBefore - TokensAfter);
    public string? AgentName { get; init; }
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanMessageDeduplicator.FindDuplicates"/> call.</summary>
public sealed class DeduplicationResult
{
    /// <summary>Pairs of (earlier-index, later-index) where the later message is a duplicate of the earlier.</summary>
    public IReadOnlyList<(int Original, int Duplicate)> DuplicatePairs { get; init; } = [];
    /// <summary>Estimated tokens wasted by duplicate messages.</summary>
    public int EstimatedWastedTokens { get; init; }
    /// <summary>True when at least one duplicate was found.</summary>
    public bool HasDuplicates => DuplicatePairs.Count > 0;
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanTabularCompressor.Compress"/> call.</summary>
public sealed class TabularCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public bool WasCompressed { get; init; }
    public int OriginalRows { get; init; }
    public int KeptRows { get; init; }
    public int DroppedColumns { get; init; }
}

/// <summary>
/// Preset compression profile for <see cref="caveman.core.services.CavemanContentRouter"/>.
/// Each profile pre-configures JSON crusher limits, NLP compression level, verbosity steering and cache settings.
/// </summary>
public enum CompressionProfile
{
    /// <summary>Conservative: Light NLP, no verbosity steering, large JSON output window.</summary>
    Light,
    /// <summary>Balanced default: Semantic NLP, SkipCeremony steering, 15 JSON rows.</summary>
    Balanced,
    /// <summary>Agent loop optimized: Semantic NLP, NoRestatement steering, 10 JSON rows, short-TTL cache.</summary>
    Agent,
    /// <summary>Maximum savings: Aggressive NLP, MinimumTokens steering, 8 JSON rows.</summary>
    Aggressive
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanCodeCompressor.Compress"/> call.</summary>
public sealed class CodeCompressionResult
{
    public string Compressed { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public bool WasCompressed { get; init; }
    public string DetectedLanguage { get; init; } = string.Empty;
    public int CommentsRemoved { get; init; }
    /// <summary>Number of function/method bodies replaced with a placeholder (0 unless <c>skeletonize: true</c> was requested).</summary>
    public int FunctionsSkeletonized { get; init; }
    public int BlankLinesRemoved { get; init; }
}

/// <summary>Outcome of a <see cref="caveman.core.services.CavemanContentRouter.Route"/> call.</summary>
public sealed class RoutedCompressionResult
{
    /// <summary>Compressed (or unchanged) content.</summary>
    public string Compressed { get; init; } = string.Empty;
    /// <summary>Original content before routing.</summary>
    public string Original { get; init; } = string.Empty;
    /// <summary>Content type detected before routing.</summary>
    public ContentType DetectedType { get; init; }
    /// <summary>Label of the strategy used (e.g., "JsonCrush:Csv", "NlpCompression", "Passthrough").</summary>
    public string StrategyUsed { get; init; } = string.Empty;
    /// <summary>Approximate GPT-4 token count of the original content.</summary>
    public int TokensBefore { get; init; }
    /// <summary>Approximate GPT-4 token count of the compressed content.</summary>
    public int TokensAfter { get; init; }
    /// <summary>Tokens saved (never negative).</summary>
    public int TokensSaved => Math.Max(0, TokensBefore - TokensAfter);
    /// <summary>Percentage of tokens removed, 0–100.</summary>
    public double SavingsPercent => TokensBefore == 0 ? 0 : (double)TokensSaved / TokensBefore * 100;
    /// <summary>CCR hash identifying dropped rows (non-null only when a lossy row-drop was applied).</summary>
    public string? CcrHash { get; init; }
    /// <summary>Non-null when the routing or compression step threw an exception.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>True when <see cref="ErrorMessage"/> is set.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}
