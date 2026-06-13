// -----------------------------------------------------------------------------
// <copyright file="CavemanMemoryExtractor.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Distills a conversation into a compact, durable "memory note" (salient sentences + key terms).</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.services;

/// <summary>A compact, durable memory distilled from a conversation, suitable to carry across turns.</summary>
public sealed class MemoryNote
{
    /// <summary>The most salient sentences (TextRank), forming a short recap.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Key terms / entities (names, rare content words) worth remembering.</summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>Detected language (ISO 639-3).</summary>
    public string Iso3 { get; set; } = "eng";

    /// <summary>Renders the memory as a compact markdown-ish note.</summary>
    public override string ToString()
    {
        var keywords = Keywords.Count > 0 ? $"\nKey: {string.Join(", ", Keywords)}" : string.Empty;
        return $"{Summary}{keywords}".Trim();
    }
}

/// <summary>
/// Builds a long-term "memory" from a conversation: the few most central sentences plus the
/// most distinctive terms (proper-noun-like capitalized tokens and the most frequent content
/// words). Lets an agent forget the transcript while keeping what matters. No embeddings/LLM.
/// </summary>
public sealed class CavemanMemoryExtractor
{
    private readonly CavemanTextRank _textRank;
    private readonly ILanguageDetector _detector;
    private readonly FunctionWordProvider _wordProvider;
    private readonly CavemanTextSplitter _splitter;

    public CavemanMemoryExtractor(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider ?? new FunctionWordProvider();
        _detector = new CavemanLanguageDetector(_wordProvider);
        _splitter = new CavemanTextSplitter();
        _textRank = new CavemanTextRank(_wordProvider);
    }

    /// <summary>Extracts a <see cref="MemoryNote"/> from a (possibly markdown/JSON) conversation.</summary>
    public MemoryNote Extract(string conversation, int maxSentences = 5, int maxKeywords = 10, string? iso3 = null)
    {
        var note = new MemoryNote();
        if (string.IsNullOrWhiteSpace(conversation))
            return note;

        var clean = CavemanConversationToText.ExtractTextFromMarkdown(conversation);
        if (string.IsNullOrWhiteSpace(clean))
            return note;

        note.Iso3 = iso3 ?? _detector.Detect(clean);
        note.Summary = _textRank.RankAndSummarize(clean, Math.Max(1, maxSentences), note.Iso3).Trim();
        note.Keywords = ExtractKeywords(clean, note.Iso3, Math.Max(0, maxKeywords));
        return note;
    }

    private List<string> ExtractKeywords(string text, string iso3, int maxKeywords)
    {
        if (maxKeywords == 0)
            return new List<string>();

        var funcWords = _wordProvider.GetFunctionWords(iso3);

        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var capitalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in _splitter.ParseText(text))
        {
            if (token.Category != CavemanTokenCategory.Word)
                continue;

            var value = token.Value;
            var lower = value.ToLowerInvariant();
            if (lower.Length < 4 || funcWords.Contains(lower))
                continue;

            freq.TryGetValue(lower, out var c);
            freq[lower] = c + 1;
            if (!display.ContainsKey(lower))
                display[lower] = value;

            // Mid-sentence capitalization is a cheap proper-noun signal worth surfacing.
            if (char.IsUpper(value[0]))
                capitalized.Add(lower);
        }

        // Rank: capitalized (name-like) terms first, then by frequency, then alphabetically.
        return freq.Keys
            .OrderByDescending(k => capitalized.Contains(k))
            .ThenByDescending(k => freq[k])
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .Select(k => display[k])
            .ToList();
    }
}
