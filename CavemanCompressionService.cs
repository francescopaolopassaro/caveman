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
        Aggressive = 3,
        /// <summary>
        /// TF-IDF word scoring instead of curated dictionaries: keeps words that are frequent
        /// in this prompt but rare across the rest of the language's standard vocabulary
        /// (function/generic words), dropping the rest. Keeps at least one word per sentence.
        /// </summary>
        Statistical = 4,
        /// <summary>
        /// Rule-based syntactic pruning: same content-word filtering as Aggressive, but a
        /// function word survives when it is grammatical glue directly touching a word that
        /// itself survives (e.g. a determiner in front of its noun), so the result reads as a
        /// terse but grammatical sentence rather than a keyword bag. Never empties a sentence
        /// that had any content to begin with.
        /// </summary>
        Syntactic = 5
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
        // \p{M} (combining marks) is included alongside \p{L}: scripts like Kannada, Hindi,
        // Tamil and Thai attach vowel signs/virama as separate Unicode Mark codepoints, not
        // Letters, so a letters-only pattern split words apart at every combining mark
        // (e.g. Kannada "ಪರೀಕ್ಷೆ" fragmented into "ಪರ", "ಕ", "ಷ", …). Keeping the mark
        // attached to the preceding base letter keeps those scripts' words intact.
        private static readonly Regex WordSplit = new(
            @"[\p{L}\p{M}]+(?:'[\p{L}\p{M}]+)?|\p{N}+(?:[.,]\p{N}+)?|[^\p{L}\p{M}\p{N}\s]",
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

        // "al" and "ical" were deliberately dropped from this list: they catch genuinely
        // decorative adjectives ("occasional", "additional") but just as often catch a
        // domain-specifying adjective that IS the information ("financial"/"medical"/"legal"
        // report are not interchangeable) — the comprehensibility test suite caught
        // "quarterly financial report" losing "financial" in Aggressive/Syntactic mode. Same
        // asymmetry as the Romance "-are" suffix fix: losing real content outweighs catching
        // a few filler adjectives, so both suffixes stay out.
        private static readonly HashSet<string> EnglishAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ous", "ive", "able", "ible", "ful", "less", "ic", "ant", "ent", "ish", "like", "some"
        };

        private static readonly HashSet<string> RomanceAdverbSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "mente"
        };

        // "are" was deliberately dropped from this list: it matches Italian adjectives like
        // "regolare"/"particolare", but it *also* matches every first-conjugation -are
        // infinitive verb ("analizzare", "parlare", …), which silently deleted the sentence's
        // main verb in Aggressive/Syntactic compression. Losing a handful of "-are" adjectives
        // is a far smaller quality hit than losing the verb, so the suffix stays out.
        private static readonly HashSet<string> RomanceAdjectiveSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "oso", "osa", "ivo", "iva", "abile", "ibile", "ale", "ese", "ista"
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
                CavemanCompressionLevel.Statistical => FilterStatistical(words, functionWords, iso3, lemmas, properNouns),
                CavemanCompressionLevel.Syntactic => FilterSyntactic(words, functionWords, iso3, lemmas, properNouns),
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

            var result = words.Select((w, i) =>
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

            // Safety floor: a short, content-bearing prompt (e.g. a one-word imperative like
            // "Vai."/"Go.") can have its only word classified as generic/descriptive and
            // pruned to nothing. An empty compressed prompt is a total loss of meaning, which
            // is worse than keeping one extra word, so fall back to the mildest available
            // signal instead of returning nothing.
            if (result.Count == 0 && words.Any(w => !w.IsPunctuation))
                return FilterSemantic(words, functionWords, iso3, lemmas, properNouns);

            return result;
        }

        // Sentence-ending punctuation used to split the token stream into pseudo-documents
        // for TF-IDF (Statistical) and into clauses for verb detection (Syntactic).
        private static readonly HashSet<string> SentenceEnders = new() { ".", "!", "?", "…" };

        /// <summary>
        /// Statistical alternative to the curated dictionaries: scores each distinct word by
        /// TF-IDF (frequency in this prompt vs. how many of the prompt's own sentences contain
        /// it), grounding "common" words against the language's curated function/generic word
        /// lists exactly as a real reference corpus would. Keeps words scoring at or above the
        /// median of the prompt's own positively-scored vocabulary, so the cut is relative to
        /// this text rather than a fixed threshold. Never drops a sentence to nothing.
        /// </summary>
        private IReadOnlyList<WordToken> FilterStatistical(
            List<WordToken> words,
            HashSet<string> functionWords,
            string iso3,
            Dictionary<string, string>? lemmas,
            HashSet<string> properNouns)
        {
            var genericWords = _wordProvider.GetGenericWords(iso3);
            if (genericWords.Count == 0)
                genericWords = GenericWordsFallback;
            var isProper = DetectProperNouns(words, iso3, properNouns);
            var n = words.Count;

            var sentenceOf = new int[n];
            int sentenceIdx = 0;
            for (int i = 0; i < n; i++)
            {
                sentenceOf[i] = sentenceIdx;
                if (words[i].IsPunctuation && SentenceEnders.Contains(words[i].Text))
                    sentenceIdx++;
            }
            int sentenceCount = sentenceIdx + 1;

            // The scoring key per token: null for punctuation/proper nouns/numbers (handled
            // separately, never scored away), otherwise its lemma/lowercased surface form.
            var keys = new string?[n];
            for (int i = 0; i < n; i++)
            {
                if (words[i].IsPunctuation || isProper[i] || IsNumber(words[i].Text))
                    continue;
                keys[i] = LemmaOrLower(words[i].Text, lemmas);
            }

            var termFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var docsWithTerm = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                var k = keys[i];
                if (k == null) continue;
                termFreq[k] = termFreq.GetValueOrDefault(k) + 1;
                if (!docsWithTerm.TryGetValue(k, out var set))
                    docsWithTerm[k] = set = new HashSet<int>();
                set.Add(sentenceOf[i]);
            }

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (word, freq) in termFreq)
            {
                // Words already known to be common across the language (function/generic
                // words) are the "standard corpus" reference: they score at the floor exactly
                // like a naturally high document-frequency word would.
                if (functionWords.Contains(word) || genericWords.Contains(word))
                {
                    scores[word] = 0.0;
                    continue;
                }

                var df = docsWithTerm[word].Count;
                var idf = sentenceCount > 1
                    ? Math.Log((double)sentenceCount / (1 + df)) + 1.0
                    : 1.0; // a single-sentence prompt has no cross-sentence signal to lean on
                scores[word] = freq * idf;
            }

            if (scores.Count == 0)
                return words.Where(w => !w.IsPunctuation && !IsNumber(w.Text)).ToList();

            var positive = scores.Values.Where(v => v > 0).OrderBy(v => v).ToList();
            var threshold = positive.Count > 0 ? positive[positive.Count / 2] : 0;

            var keep = new bool[n];
            var sentenceHasKeep = new bool[sentenceCount];
            for (int i = 0; i < n; i++)
            {
                if (words[i].IsPunctuation) continue;
                if (isProper[i]) { keep[i] = true; sentenceHasKeep[sentenceOf[i]] = true; continue; }

                var k = keys[i];
                if (k == null) continue;
                if (scores.TryGetValue(k, out var score) && score > 0 && score >= threshold)
                {
                    keep[i] = true;
                    sentenceHasKeep[sentenceOf[i]] = true;
                }
            }

            // Safety floor: a sentence that scored nothing above threshold still keeps its
            // single highest-scoring word, so compression never erases a whole sentence.
            for (int s = 0; s < sentenceCount; s++)
            {
                if (sentenceHasKeep[s]) continue;
                int bestIdx = -1;
                double bestScore = -1;
                for (int i = 0; i < n; i++)
                {
                    if (sentenceOf[i] != s || words[i].IsPunctuation || isProper[i]) continue;
                    var k = keys[i];
                    if (k == null) continue;
                    if (scores.TryGetValue(k, out var sc) && sc > bestScore)
                    {
                        bestScore = sc;
                        bestIdx = i;
                    }
                }
                if (bestIdx >= 0) keep[bestIdx] = true;
            }

            var result = new List<WordToken>();
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                if (isProper[i]) { result.Add(words[i]); continue; }
                result.Add(new WordToken { Text = keys[i]!, IsPunctuation = false });
            }
            return result;
        }

        /// <summary>
        /// Rule-based syntactic pruning: the same content-word filtering as Aggressive
        /// (function words, generic words and descriptive modifiers dropped, everything else
        /// lemmatised), except a function word survives when it is the grammatical glue
        /// directly touching a word that itself survives — a determiner right in front of its
        /// noun, for instance — so the result reads as a terse but grammatical sentence
        /// instead of a keyword bag. Kept function words are never lemmatised (they keep their
        /// surface form).
        /// <para>
        /// When the language has POS lookup data (<see cref="FunctionWordProvider.GetPosTags"/>,
        /// a frequency-baseline tagger built offline from Universal Dependencies treebanks — no
        /// runtime model), a leading hedging/matrix clause ("I kindly ask you to…", "vorrei che
        /// tu…") is additionally elided in favour of the sentence's last verb. A first attempt
        /// at this without real POS evidence broke on verb/preposition homographs (Italian
        /// "entro" = "by/within" vs. 1st-person "I enter") and was removed; with a POS tag per
        /// word this is safe, and is further restricted to only fire when every token between
        /// two candidate verbs is grammatical glue or a descriptive modifier — never a real
        /// content noun — so coordinated independent clauses ("I bought bread and ate cake")
        /// are never mistaken for a hedge clause and gutted. Proper nouns are never elided.
        /// Without POS data for the language, this step is a no-op.
        /// </para>
        /// </summary>
        private IReadOnlyList<WordToken> FilterSyntactic(
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
            var posTags = _wordProvider.GetPosTags(iso3);
            var n = words.Count;

            bool NextSurvives(int j)
            {
                if (j >= n || words[j].IsPunctuation) return false;
                if (isProper[j]) return true;
                if (IsNumber(words[j].Text)) return false;
                var jLower = words[j].Text.ToLowerInvariant();
                if (functionWords.Contains(jLower)) return false;
                var jNorm = LemmaOrLower(words[j].Text, lemmas) ?? jLower;
                if (functionWords.Contains(jNorm) || genericWords.Contains(jNorm)) return false;
                if (IsDescriptiveWord(jNorm, langGroup)) return false;
                return true;
            }

            bool IsGlueOrModifier(int i)
            {
                if (words[i].IsPunctuation || isProper[i]) return true;
                var lower = words[i].Text.ToLowerInvariant();
                var norm = LemmaOrLower(words[i].Text, lemmas) ?? lower;
                return functionWords.Contains(lower) || functionWords.Contains(norm)
                    || IsDescriptiveWord(norm, langGroup);
            }

            // elided[i]: inside a leading hedge/matrix clause the POS-gated pass below chose
            // to drop wholesale (proper nouns are exempted and flow through normal handling).
            var elided = new bool[n];
            if (posTags.Count > 0)
            {
                int clauseStart = 0;
                for (int i = 0; i <= n; i++)
                {
                    if (i < n && !(words[i].IsPunctuation && SentenceEnders.Contains(words[i].Text)))
                        continue;

                    var s = clauseStart;
                    var e = i;
                    clauseStart = i + 1;

                    var verbIdx = new List<int>();
                    for (int k = s; k < e; k++)
                    {
                        if (words[k].IsPunctuation || isProper[k]) continue;
                        if (posTags.TryGetValue(words[k].Text.ToLowerInvariant(), out var tag) && tag == "VERB")
                            verbIdx.Add(k);
                    }
                    if (verbIdx.Count < 2) continue;

                    var gapsClear = true;
                    for (int k = 0; k < verbIdx.Count - 1 && gapsClear; k++)
                        for (int j = verbIdx[k] + 1; j < verbIdx[k + 1]; j++)
                            if (!IsGlueOrModifier(j)) { gapsClear = false; break; }

                    if (gapsClear)
                        for (int j = s; j < verbIdx[^1]; j++)
                            if (!isProper[j]) elided[j] = true;
                }
            }

            // isFunc[i] marks tokens kept purely as grammatical glue: their surface form is
            // preserved verbatim in the output pass below, never run through lemmatisation.
            var keep = new bool[n];
            var isFunc = new bool[n];

            for (int i = 0; i < n; i++)
            {
                var w = words[i];
                if (w.IsPunctuation) continue;
                if (elided[i]) continue;
                if (isProper[i]) { keep[i] = true; continue; }
                if (IsNumber(w.Text)) continue;

                var lower = w.Text.ToLowerInvariant();
                var normalized = LemmaOrLower(w.Text, lemmas) ?? lower;

                if (functionWords.Contains(lower) || functionWords.Contains(normalized))
                {
                    keep[i] = NextSurvives(i + 1);
                    isFunc[i] = true;
                    continue;
                }

                if (genericWords.Contains(normalized))
                    continue;

                if (IsDescriptiveWord(normalized, langGroup))
                    continue;

                keep[i] = true;
            }

            // Safety floor per sentence: never elide a whole sentence to nothing.
            var sentenceOf = new int[n];
            int sentenceIdx = 0;
            for (int i = 0; i < n; i++)
            {
                sentenceOf[i] = sentenceIdx;
                if (words[i].IsPunctuation && SentenceEnders.Contains(words[i].Text))
                    sentenceIdx++;
            }
            int sentenceCount = sentenceIdx + 1;
            var sentenceHasKeep = new bool[sentenceCount];
            for (int i = 0; i < n; i++)
                if (keep[i]) sentenceHasKeep[sentenceOf[i]] = true;

            for (int i = 0; i < n; i++)
            {
                if (sentenceHasKeep[sentenceOf[i]] || words[i].IsPunctuation) continue;
                keep[i] = true;
                sentenceHasKeep[sentenceOf[i]] = true;
            }

            var result = new List<WordToken>();
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                var w = words[i];
                if (isProper[i] || isFunc[i]) { result.Add(w); continue; }

                var normalized = LemmaOrLower(w.Text, lemmas);
                result.Add(normalized != null && !normalized.Equals(w.Text, StringComparison.OrdinalIgnoreCase)
                    ? new WordToken { Text = normalized, IsPunctuation = false }
                    : w);
            }
            return result;
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
