// -----------------------------------------------------------------------------
// <copyright file="CavemanCompressionService.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Core compression engine: tokenization, function-word and lemma filtering, and the Light/Semantic/Aggressive compression levels.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.core
{
    /// <summary>How aggressively a prompt is compressed.</summary>
    public enum CavemanCompressionLevel
    {
        /// <summary>No compression — the text is returned unchanged.</summary>
        None = 0,
        /// <summary>Removes stop words (articles, prepositions, conjunctions, …).</summary>
        Light = 1,
        /// <summary>Keeps content words only, normalised to their base form.</summary>
        Semantic = 2,
        /// <summary>Most aggressive: base-form content words, generic/descriptive terms pruned.</summary>
        Aggressive = 3
    }

    /// <summary>
    /// Overrides the default per-level rules. Categories are simple string tags:
    /// "FUNC" (function word), "PUNCT", "NUM", "PROPN", "CONTENT".
    /// </summary>
    public class CompressionFilter
    {
        /// <summary>If set, only tokens whose category is in this set are kept.</summary>
        public HashSet<string>? KeepOnly { get; set; }
        /// <summary>If set, tokens whose category is in this set are removed.</summary>
        public HashSet<string>? Remove { get; set; }
        /// <summary>Optional extra predicate; a token is kept only when it returns true.</summary>
        public Func<string, bool>? CustomPredicate { get; set; }
    }

    /// <summary>
    /// Compresses prompts for LLMs by removing grammatical noise and normalising words
    /// to their base form, while preserving names. Also exposes standalone language
    /// detection. Language data is embedded; nothing is downloaded at runtime.
    /// </summary>
    public class CavemanCompressionService : ICompressionService
    {
        private static readonly Regex WordSplit = new(
            @"\p{L}+(?:'\p{L}+)?|\p{N}+(?:[.,]\p{N}+)?|[^\p{L}\p{N}\s]",
            RegexOptions.Compiled);

        private readonly ILanguageDetector _detector;
        private readonly ModelTokenizer? _tokenizer;
        private readonly FunctionWordProvider _wordProvider;
        private readonly Lazy<CavemanContentRouter> _contentRouter;
        private static readonly ConcurrentDictionary<string, WordDataFile?> _dataCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>?> _lemmaCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, HashSet<string>> _properNounCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _emptyProperNouns = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> EnglishAdverbSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ly", "wise", "wards"
        };

        private static readonly HashSet<string> EnglishAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ous", "al", "ive", "able", "ible", "ful", "less", "ic", "ical", "ant", "ent", "ish", "like", "some"
        };

        private static readonly HashSet<string> RomanceAdverbSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "mente"
        };

        private static readonly HashSet<string> RomanceAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "oso", "osa", "ivo", "iva", "abile", "ibile", "ale", "are", "ese", "ista"
        };

        private static readonly HashSet<string> GermanAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "lich", "ig", "isch", "sam", "bar", "haft", "los", "voll", "arm", "reich"
        };

        private static readonly HashSet<string> SlavicAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ный", "ная", "ное", "ные", "ний", "ня", "нє", "ова", "еви", "ски", "цки", "скиј", "чки"
        };

        public CavemanCompressionService() : this(null, null) { }

        public CavemanCompressionService(ModelTokenizer? tokenizer) : this(tokenizer, null) { }

        public CavemanCompressionService(ModelTokenizer? tokenizer, FunctionWordProvider? wordProvider)
            : this(tokenizer, wordProvider, null) { }

        /// <param name="detector">Optional language detector; defaults to <see cref="CavemanLanguageDetector"/>.</param>
        public CavemanCompressionService(ModelTokenizer? tokenizer, FunctionWordProvider? wordProvider, ILanguageDetector? detector)
        {
            _wordProvider = wordProvider ?? new FunctionWordProvider();
            _detector = detector ?? new CavemanLanguageDetector(_wordProvider);
            _tokenizer = tokenizer;
            _contentRouter = new Lazy<CavemanContentRouter>(() => new CavemanContentRouter(this, _tokenizer));
        }

        /// <summary>
        /// Detects the language of the given text and returns its ISO 639-3 code
        /// (e.g. "eng", "ita"). Returns "eng" when the language cannot be determined.
        /// Useful on its own, without performing any compression.
        /// </summary>
        public string DetectLanguage(string input) => _detector.Detect(input);

        /// <summary>
        /// Returns per-language detection confidence scores: ISO 639-3 code mapped to
        /// the ratio of input tokens recognised as that language's stop words.
        /// </summary>
        public IReadOnlyDictionary<string, double> DetectLanguageScores(string input) =>
            _detector.DetectWithScores(input);

        /// <summary>
        /// Compresses <paramref name="input"/> at the given <paramref name="level"/>,
        /// auto-detecting the language. The work is synchronous; the method returns a
        /// completed task for convenient use in async call sites.
        /// </summary>
        public Task<CompressionResult> CompressAsync(
            string input,
            CavemanCompressionLevel level,
            CancellationToken ct = default)
            => CompressAsync(input, level, null, ct);

        /// <summary>
        /// Compresses <paramref name="input"/> at the given <paramref name="level"/>
        /// (auto-detecting the language), optionally applying a custom
        /// <see cref="CompressionFilter"/> instead of the level's default rules.
        /// </summary>
        public Task<CompressionResult> CompressAsync(
            string input,
            CavemanCompressionLevel level,
            CompressionFilter? customFilter,
            CancellationToken ct = default)
            => Task.FromResult(CompressOne(input, level, customFilter, ct));

        private CompressionResult CompressOne(
            string input,
            CavemanCompressionLevel level,
            CompressionFilter? customFilter,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new CompressionResult { CompressedText = string.Empty };

            if (level == CavemanCompressionLevel.None && customFilter == null)
            {
                var fallbackTokens = CountTokensApprox(input);
                return new CompressionResult
                {
                    CompressedText = input,
                    OriginalTokens = fallbackTokens,
                    CompressedTokens = fallbackTokens,
                    GptOriginalTokens = CountGptTokens(input),
                    GptCompressedTokens = CountGptTokens(input)
                };
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                string iso3 = _detector.Detect(input);

                if (string.IsNullOrEmpty(iso3))
                {
                    return new CompressionResult
                    {
                        CompressedText = input,
                        ErrorMessage = $"Language '{iso3}' is not supported."
                    };
                }

                var result = ApplyCompression(input, iso3, level, customFilter);

                result.GptOriginalTokens = CountGptTokens(input);
                result.GptCompressedTokens = CountGptTokens(result.CompressedText);

                return result;
            }
            catch (OperationCanceledException)
            {
                return new CompressionResult
                {
                    CompressedText = input,
                    ErrorMessage = "Operation cancelled."
                };
            }
            catch (Exception ex)
            {
                return new CompressionResult
                {
                    CompressedText = input,
                    ErrorMessage = $"Compression failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Compresses many prompts at once. The returned array preserves input order;
        /// per-item failures surface on each result's <see cref="CompressionResult.ErrorMessage"/>.
        /// </summary>
        public Task<CompressionResult[]> CompressBatchAsync(
            IEnumerable<string> inputs,
            CavemanCompressionLevel level,
            CancellationToken ct = default)
            => CompressBatchAsync(inputs, level, null, ct);

        /// <summary>
        /// Compresses many prompts at once, optionally applying a custom
        /// <see cref="CompressionFilter"/>. The returned array preserves input order.
        /// </summary>
        public Task<CompressionResult[]> CompressBatchAsync(
            IEnumerable<string> inputs,
            CavemanCompressionLevel level,
            CompressionFilter? customFilter,
            CancellationToken ct = default)
        {
            if (inputs == null)
                return Task.FromException<CompressionResult[]>(new ArgumentNullException(nameof(inputs)));

            try
            {
                var results = new List<CompressionResult>();
                foreach (var input in inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(CompressOne(input, level, customFilter, ct));
                }
                return Task.FromResult(results.ToArray());
            }
            catch (OperationCanceledException)
            {
                return Task.FromCanceled<CompressionResult[]>(
                    ct.IsCancellationRequested ? ct : new CancellationToken(true));
            }
        }

        /// <summary>
        /// Compresses <paramref name="input"/> for an explicitly known language
        /// (ISO 639-3 code), skipping automatic detection.
        /// </summary>
        public CompressionResult ApplyCompression(
            string input,
            string iso3,
            CavemanCompressionLevel level,
            CompressionFilter? customFilter = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new CompressionResult { CompressedText = string.Empty };

            var functionWords = _wordProvider.GetFunctionWords(iso3);
            var lemmas = GetLemmas(iso3);
            var properNouns = GetProperNouns(iso3);

            var words = Tokenize(input);
            int originalCount = words.Count;

            IReadOnlyList<WordToken> filtered;
            if (customFilter != null)
            {
                filtered = ApplyCustomFilter(words, functionWords, customFilter);
            }
            else
            {
                filtered = ApplyLevelFilter(words, functionWords, level, iso3, lemmas, properNouns);
            }

            var compressed = string.Join(" ", filtered.Select(w => w.Text));

            return new CompressionResult
            {
                CompressedText = compressed,
                OriginalTokens = originalCount,
                CompressedTokens = filtered.Count
            };
        }

        private static List<WordToken> Tokenize(string input)
        {
            var tokens = new List<WordToken>();
            var matches = WordSplit.Matches(input);

            foreach (Match match in matches)
            {
                tokens.Add(new WordToken
                {
                    Text = match.Value,
                    IsPunctuation = !char.IsLetterOrDigit(match.Value[0])
                });
            }

            return tokens;
        }

        private IReadOnlyList<WordToken> ApplyLevelFilter(
            List<WordToken> words,
            HashSet<string> functionWords,
            CavemanCompressionLevel level,
            string iso3,
            Dictionary<string, string>? lemmas,
            HashSet<string> properNouns)
        {
            return level switch
            {
                CavemanCompressionLevel.Light => FilterLight(words, functionWords),
                CavemanCompressionLevel.Semantic => FilterSemantic(words, functionWords, iso3, lemmas, properNouns),
                CavemanCompressionLevel.Aggressive => FilterAggressive(words, functionWords, iso3, lemmas, properNouns),
                _ => words
            };
        }

        private static IReadOnlyList<WordToken> ApplyCustomFilter(
            List<WordToken> words,
            HashSet<string> functionWords,
            CompressionFilter customFilter)
        {
            return words.Where(w =>
            {
                if (w.IsPunctuation && customFilter.Remove?.Contains("PUNCT") == true)
                    return false;

                if (customFilter.KeepOnly != null)
                {
                    var cat = CategorizeWord(w.Text, functionWords);
                    return customFilter.KeepOnly.Contains(cat);
                }

                if (customFilter.Remove != null && customFilter.Remove.Contains("FUNC"))
                    return !functionWords.Contains(w.Text.ToLowerInvariant());

                if (customFilter.CustomPredicate != null)
                    return customFilter.CustomPredicate(w.Text);

                return true;
            }).ToList();
        }

        private static IReadOnlyList<WordToken> FilterLight(
            List<WordToken> words,
            HashSet<string> functionWords)
        {
            return words.Where(w =>
            {
                if (w.IsPunctuation)
                    return false;

                var lower = w.Text.ToLowerInvariant();
                return !functionWords.Contains(lower);
            }).ToList();
        }

        private static string? LemmaOrLower(string text, Dictionary<string, string>? lemmas)
        {
            var lower = text.ToLowerInvariant();
            if (lemmas != null && lemmas.TryGetValue(lower, out var lemma))
                return lemma;
            return lower;
        }

        // Languages whose orthography capitalises all common nouns (so an initial
        // capital is NOT a proper-noun signal). The heuristic is disabled for these.
        private static readonly HashSet<string> CapitalizedNounLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "deu" // German (and Luxembourgish, not currently supported)
        };

        // Proper-noun detection. A token is treated as a name (and kept verbatim) when
        // it is upper-case-initial AND either:
        //   - it is not at the start of a sentence (positional heuristic), or
        //   - its lower-cased form is in the UD-derived name gazetteer.
        // The gazetteer also re-enables protection at sentence start and for languages
        // that capitalise all common nouns (German), where the positional heuristic is off.
        private static bool[] DetectProperNouns(List<WordToken> words, string iso3, HashSet<string> properNouns)
        {
            var isProper = new bool[words.Count];
            bool capitalizedNounLanguage = CapitalizedNounLanguages.Contains(iso3);
            bool hasGazetteer = properNouns.Count > 0;

            bool sentenceStart = true;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (w.IsPunctuation)
                {
                    if (w.Text is "." or "!" or "?" or "…")
                        sentenceStart = true;
                    continue;
                }

                if (char.IsUpper(w.Text[0]))
                {
                    bool inGazetteer = hasGazetteer && properNouns.Contains(w.Text.ToLowerInvariant());
                    isProper[i] = capitalizedNounLanguage
                        ? inGazetteer
                        : (!sentenceStart || inGazetteer);
                }
                sentenceStart = false;
            }
            return isProper;
        }

        private static IReadOnlyList<WordToken> FilterSemantic(
            List<WordToken> words,
            HashSet<string> functionWords,
            string iso3,
            Dictionary<string, string>? lemmas,
            HashSet<string> properNouns)
        {
            var isProper = DetectProperNouns(words, iso3, properNouns);
            return words.Select((w, i) =>
            {
                if (w.IsPunctuation)
                    return null;

                // Keep proper nouns verbatim: no lemmatization, no removal.
                if (isProper[i])
                    return w;

                if (IsNumber(w.Text))
                    return null;

                // Drop function words by their surface form, before lemmatization:
                // a stopword must be removed regardless of any (possibly noisy) lemma.
                if (functionWords.Contains(w.Text.ToLowerInvariant()))
                    return null;

                var normalized = LemmaOrLower(w.Text, lemmas);
                if (normalized == null)
                    return null;

                if (functionWords.Contains(normalized))
                    return null;

                if (!normalized.Equals(w.Text, StringComparison.OrdinalIgnoreCase))
                    return new WordToken { Text = normalized, IsPunctuation = false };

                return w;
            }).Where(w => w != null).Select(w => w!).ToList();
        }

        private IReadOnlyList<WordToken> FilterAggressive(
            List<WordToken> words,
            HashSet<string> functionWords,
            string iso3,
            Dictionary<string, string>? lemmas,
            HashSet<string> properNouns)
        {
            var langGroup = GetLanguageGroup(iso3);
            var genericWords = _wordProvider.GetGenericWords(iso3);
            if (genericWords.Count == 0)
                genericWords = GenericWordsFallback;
            var isProper = DetectProperNouns(words, iso3, properNouns);

            return words.Select((w, i) =>
            {
                if (w.IsPunctuation)
                    return null;

                // Keep proper nouns verbatim: names must never be compressed.
                if (isProper[i])
                    return w;

                if (w.Text.Length <= 1 && !char.IsLetter(w.Text[0]))
                    return null;

                if (IsNumber(w.Text))
                    return null;

                // Drop function words by their surface form, before lemmatization.
                if (functionWords.Contains(w.Text.ToLowerInvariant()))
                    return null;

                var normalized = LemmaOrLower(w.Text, lemmas);
                if (normalized == null)
                    return null;

                if (normalized.Length <= 1)
                    return null;

                if (functionWords.Contains(normalized))
                    return null;

                if (genericWords.Contains(normalized))
                    return null;

                if (IsDescriptiveWord(normalized, langGroup))
                    return null;

                if (!normalized.Equals(w.Text, StringComparison.OrdinalIgnoreCase))
                    return new WordToken { Text = normalized, IsPunctuation = false };

                return w;
            }).Where(w => w != null).Select(w => w!).ToList();
        }

        private static string GetLanguageGroup(string iso3)
        {
            return iso3.ToLowerInvariant() switch
            {
                "eng" => "en",
                "ita" or "fra" or "spa" or "por" or "ron" or "cat" or "glg" or "lat" => "romance",
                "deu" or "nld" or "afr" or "swe" or "dan" or "nor" or "isl" => "germanic",
                "rus" or "ukr" or "bel" or "bul" or "srp" or "hrv" or "slv" or "slk" or "ces" or "pol" or "mkd" => "slavic",
                "ara" or "heb" or "fas" or "urd" => "semitic",
                "hin" or "ben" or "mar" or "urd" or "tel" or "tam" or "kan" => "indic",
                "zho" or "jpn" or "kor" or "tha" or "vie" => "east_asian",
                "tur" or "kaz" or "fin" or "est" or "hun" or "hye" or "ell" => "uralic_altaic",
                _ => "other"
            };
        }

        // Used only when a language has no curated {iso3}.generic.yaml.br resource
        // (see FunctionWordProvider.GetGenericWords). Kept intentionally small and
        // English-only: per-language generic-word lists now live in worddata, not code.
        private static readonly HashSet<string> GenericWordsFallback = new(StringComparer.OrdinalIgnoreCase)
        {
            "time", "day", "year", "person", "thing", "place", "world", "life",
            "big", "small", "new", "old", "good", "bad",
            "now", "then", "today", "yesterday", "tomorrow", "always", "never",
        };

        private static bool IsNumber(string text)
        {
            foreach (var c in text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',' && c != '-')
                    return false;
            }
            return text.Length > 0;
        }

        private static bool IsDescriptiveWord(string lower, string langGroup)
        {
            if (lower.Length < 4)
                return false;

            switch (langGroup)
            {
                case "en":
                    foreach (var suffix in EnglishAdverbSuffixes)
                        if (lower.EndsWith(suffix) && lower.Length > suffix.Length + 1)
                            return true;
                    foreach (var suffix in EnglishAdjectiveSuffixes)
                        if (lower.EndsWith(suffix) && lower.Length > suffix.Length + 2)
                            return true;
                    break;

                case "romance":
                    if (lower.EndsWith("mente") && lower.Length > 7)
                        return true;
                    foreach (var suffix in RomanceAdjectiveSuffixes)
                        if (lower.EndsWith(suffix) && lower.Length > suffix.Length + 2)
                            return true;
                    break;

                case "germanic":
                    foreach (var suffix in GermanAdjectiveSuffixes)
                        if (lower.EndsWith(suffix) && lower.Length > suffix.Length + 2)
                            return true;
                    break;

                case "slavic":
                    foreach (var suffix in SlavicAdjectiveSuffixes)
                        if (lower.EndsWith(suffix) && lower.Length > suffix.Length + 2)
                            return true;
                    break;

                case "semitic":
                    if (lower.Length > 5)
                    {
                        if (lower.StartsWith("al") && lower.Length > 6)
                            return true;
                        if (lower.StartsWith("el") && lower.Length > 6)
                            return true;
                    }
                    break;

                case "uralic_altaic":
                    if (lower.EndsWith("лар") || lower.EndsWith("лер") ||
                        lower.EndsWith("дар") || lower.EndsWith("дер") ||
                        lower.EndsWith("тар") || lower.EndsWith("тер"))
                        return true;
                    if (lower.EndsWith("мен") || lower.EndsWith("бен") || lower.EndsWith("пен"))
                        return true;
                    break;

                case "indic":
                    if (lower.Length > 5)
                    {
                        if (lower.EndsWith("గా") || lower.EndsWith("ను") ||
                            lower.EndsWith("லாக") || lower.EndsWith("ான"))
                            return true;
                    }
                    break;
            }

            return false;
        }

        private static string CategorizeWord(string word, HashSet<string> functionWords)
        {
            if (string.IsNullOrEmpty(word))
                return "X";

            if (functionWords.Contains(word.ToLowerInvariant()))
                return "FUNC";

            if (IsNumber(word))
                return "NUM";

            if (char.IsUpper(word[0]) && word.Length > 1)
                return "PROPN";

            return "CONTENT";
        }


        private WordDataFile? GetWordData(string iso3) =>
            _dataCache.GetOrAdd(iso3, key => _wordProvider.LoadWordData(key));

        private HashSet<string> GetProperNouns(string iso3)
        {
            return _properNounCache.GetOrAdd(iso3, key =>
            {
                var data = GetWordData(key);
                if (data?.proper_nouns == null || data.proper_nouns.Count == 0)
                    return _emptyProperNouns;
                return new HashSet<string>(data.proper_nouns, StringComparer.OrdinalIgnoreCase);
            });
        }

        private Dictionary<string, string>? GetLemmas(string iso3)
        {
            return _lemmaCache.GetOrAdd(iso3, key =>
            {
                var data = GetWordData(key);
                if (data == null)
                    return null;

                var hasLemmas = data.lemmas?.Count > 0;
                var hasVerbs = data.verbs?.Count > 0;
                if (!hasLemmas && !hasVerbs)
                    return null;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Verbs are stored as lemma -> [conjugated forms]; invert them into
                // form -> lemma so every conjugation collapses to its base verb.
                if (hasVerbs)
                {
                    foreach (var (lemma, forms) in data.verbs!)
                    {
                        if (string.IsNullOrEmpty(lemma) || forms == null)
                            continue;
                        foreach (var form in forms)
                        {
                            if (!string.IsNullOrEmpty(form))
                                map[form] = lemma;
                        }
                    }
                }

                // Explicit lemmas take priority over verb-derived mappings.
                if (hasLemmas)
                {
                    foreach (var (form, lemma) in data.lemmas!)
                    {
                        if (!string.IsNullOrEmpty(form) && !string.IsNullOrEmpty(lemma))
                            map[form] = lemma;
                    }
                }

                return map.Count > 0 ? map : null;
            });
        }

        public void ReleaseMemory()
        {
            _lemmaCache.Clear();
            _properNounCache.Clear();
            _dataCache.Clear();
        }

        public Task<RoutedCompressionResult> CompressContentAsync(
            string content, string? query = null, CancellationToken ct = default)
            => _contentRouter.Value.RouteAsync(content, query, ct);

        private int CountTokensApprox(string text) =>
            text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        private int CountGptTokens(string text)
        {
            if (_tokenizer != null)
                return _tokenizer.CountTokens(text, LlmModel.Gpt4);
            return text.Length / 4;
        }
    }

    internal class WordToken
    {
        public string Text { get; init; } = string.Empty;
        public bool IsPunctuation { get; init; }
    }
}
