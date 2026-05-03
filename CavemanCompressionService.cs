/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor)
 * * DESCRIPTION:
 * This system implements NLP-based "Prompt Contraction" logic.
 * The core objective is to drastically reduce the token count sent to LLMs 
 * (such as Gemma 3, Llama 3, or GPT-4) by selectively stripping low-semantic 
 * value grammatical elements (Stopwords, Determiners, Conjunctions).
 * * THE "CAVEMAN" PRINCIPLE:
 * The guiding philosophy is to transform complex natural language into an essential, 
 * high-density format that preserves the original intent. 
 * (e.g., "I would like to order a pepperoni pizza" -> "Order pepperoni pizza").
 * * BENEFITS:
 * 1. Reduced Inference Latency: Faster response times from local or cloud models.
 * 2. API Cost Optimization: Significant savings for token-based billing.
 * 3. Context Window Efficiency: Allows more information to fit within the model's memory.
 * * TECHNOLOGY STACK:
 * - Core: Catalyst NLP (Universal Dependencies Standard)
 * - Methodology: POS (Part-of-Speech) filtering and Lemmatization.
 * * AUTHOR: [Passaro Francesco Paolo]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/
using Catalyst;
using caveman.core.entities;
using LanguageDetection;
using Mosaik.Core;

namespace caveman.core
{

    // 2. Definition of compression levels
    public enum CavemanCompressionLevel
    {
        None = 0,       // Pass-through: no modification
        Light = 1,      // Stop-words: removes articles and conjunctions (Fast)
        Semantic = 2,   // POS Tagging: keeps only Nouns, Verbs, Adjectives
        Aggressive = 3  // Brutal: only Nouns/Verbs and Lemmatization (reduces to root)
    }

    public class CavemanCompressionService
    {
        // Pipeline cache to avoid heavy RAM reloading
        private static readonly Dictionary<Language, Pipeline> _pipelines = new();
        private readonly LanguageDetector _detector;

        public CavemanCompressionService()
        {
            _detector = new LanguageDetector();
            _detector.AddAllLanguages(); // Loads lightweight statistical profiles
        }

        public async Task<CompressionResult> CompressAsync(string input, CavemanCompressionLevel level)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new CompressionResult { CompressedText = string.Empty };

            if (level == CavemanCompressionLevel.None)
            {
                // Basic token approximation for pass-through
                int fallbackTokens = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                return new CompressionResult
                {
                    CompressedText = input,
                    OriginalTokens = fallbackTokens,
                    CompressedTokens = fallbackTokens
                };
            }

            // 1. Language detection
            string detectedCode = _detector.Detect(input);
            Language catalystLang = LanguageMapper.GetCatalystLanguage(detectedCode);

            // 2. NLP processing (Catalyst)
            return await ApplyNlpLogic(input, catalystLang, level);
        }

        public async Task<CompressionResult> ApplyNlpLogic(string input, Language lang, CavemanCompressionLevel level)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new CompressionResult { CompressedText = string.Empty };

            // Force storage configuration BEFORE any operation
            if (Storage.Current == null)
            {
                Storage.Current = new DiskStorage("catalyst-models");
            }

            if (!_pipelines.TryGetValue(lang, out var nlp))
            {
                // Explicit registration (keeping only a few languages for brevity, keep your full switch here)
                switch (lang)
                {
                    case Language.Afrikaans: Catalyst.Models.Afrikaans.Register(); break;
                    //case Language.Albanian: Catalyst.Models.Albanian.Register(); break;
                    case Language.Arabic: Catalyst.Models.Arabic.Register(); break;
                    case Language.Armenian: Catalyst.Models.Armenian.Register(); break;
                    case Language.Basque: Catalyst.Models.Basque.Register(); break;
                    case Language.Belarusian: Catalyst.Models.Belarusian.Register(); break;
                    //case Language.Bengali: Catalyst.Models.Bengali.Register(); break;
                    case Language.Bulgarian: Catalyst.Models.Bulgarian.Register(); break;
                    case Language.Catalan: Catalyst.Models.Catalan.Register(); break;
                    case Language.Chinese: Catalyst.Models.Chinese.Register(); break;
                    case Language.Croatian: Catalyst.Models.Croatian.Register(); break;
                    case Language.Czech: Catalyst.Models.Czech.Register(); break;
                    case Language.Danish: Catalyst.Models.Danish.Register(); break;
                    case Language.Dutch: Catalyst.Models.Dutch.Register(); break;
                    case Language.English: Catalyst.Models.English.Register(); break;
                    case Language.Estonian: Catalyst.Models.Estonian.Register(); break;
                    case Language.Finnish: Catalyst.Models.Finnish.Register(); break;
                    case Language.French: Catalyst.Models.French.Register(); break;
                    case Language.Galician: Catalyst.Models.Galician.Register(); break;
                    case Language.German: Catalyst.Models.German.Register(); break;
                    // case Language.Greek_Modern: Catalyst.Models.Greek.Register(); break;
                    case Language.Hebrew: Catalyst.Models.Hebrew.Register(); break;
                    case Language.Hindi: Catalyst.Models.Hindi.Register(); break;
                    case Language.Hungarian: Catalyst.Models.Hungarian.Register(); break;
                    case Language.Icelandic: Catalyst.Models.Icelandic.Register(); break;
                    case Language.Indonesian: Catalyst.Models.Indonesian.Register(); break;
                    case Language.Irish: Catalyst.Models.Irish.Register(); break;
                    case Language.Italian: Catalyst.Models.Italian.Register(); break;
                    case Language.Japanese: Catalyst.Models.Japanese.Register(); break;
                    //case Language.Kannada: Catalyst.Models.Kannada.Register(); break;
                    case Language.Kazakh: Catalyst.Models.Kazakh.Register(); break;
                    case Language.Korean: Catalyst.Models.Korean.Register(); break;
                    case Language.Latin: Catalyst.Models.Latin.Register(); break;
                    case Language.Latvian: Catalyst.Models.Latvian.Register(); break;
                    case Language.Lithuanian: Catalyst.Models.Lithuanian.Register(); break;
                    case Language.Macedonian: Catalyst.Models.Macedonian.Register(); break;
                    // case Language.Malay: Catalyst.Models.Malay.Register(); break;
                    case Language.Marathi: Catalyst.Models.Marathi.Register(); break;
                    case Language.Norwegian: Catalyst.Models.Norwegian.Register(); break;
                    case Language.Persian: Catalyst.Models.Persian.Register(); break;
                    case Language.Polish: Catalyst.Models.Polish.Register(); break;
                    case Language.Portuguese: Catalyst.Models.Portuguese.Register(); break;
                    case Language.Romanian: Catalyst.Models.Romanian.Register(); break;
                    case Language.Russian: Catalyst.Models.Russian.Register(); break;
                    case Language.Serbian: Catalyst.Models.Serbian.Register(); break;
                    case Language.Slovak: Catalyst.Models.Slovak.Register(); break;
                    case Language.Slovenian: Catalyst.Models.Slovenian.Register(); break;
                    case Language.Spanish: Catalyst.Models.Spanish.Register(); break;
                    case Language.Swedish: Catalyst.Models.Swedish.Register(); break;
                    case Language.Tamil: Catalyst.Models.Tamil.Register(); break;
                    case Language.Telugu: Catalyst.Models.Telugu.Register(); break;
                    //case Language.Thai: Catalyst.Models.Thai.Register(); break;
                    case Language.Turkish: Catalyst.Models.Turkish.Register(); break;
                    case Language.Ukrainian: Catalyst.Models.Ukrainian.Register(); break;
                    case Language.Urdu: Catalyst.Models.Urdu.Register(); break;
                    case Language.Vietnamese: Catalyst.Models.Vietnamese.Register(); break;
                    default:
                        throw new NotSupportedException($"Il modello per la lingua {lang} non è attualmente configurato.");
                }

                try
                {
                    nlp = await Pipeline.ForAsync(lang);
                    _pipelines[lang] = nlp;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Critical error loading pipeline {lang}: {ex.Message}");
                    throw;
                }
            }

            var doc = new Document(input, lang);
            nlp.ProcessSingle(doc);

            // Calculate Original Tokens (excluding whitespaces)
            var allTokens = doc.SelectMany(span => span).ToList();
            int originalTokenCount = allTokens.Count;

            // Extract filtered tokens
            var filteredTokens = allTokens.Where(token => ShouldKeepToken(token, level)).ToList();
            int compressedTokenCount = filteredTokens.Count;

            var compressedWords = filteredTokens.Select(token =>
                level == CavemanCompressionLevel.Aggressive ? token.Lemma : token.ValueAsSpan.ToString());

            return new CompressionResult
            {
                CompressedText = string.Join(" ", compressedWords),
                OriginalTokens = originalTokenCount,
                CompressedTokens = compressedTokenCount
            };
        }

        private bool ShouldKeepToken(IToken token, CavemanCompressionLevel level)
        {
            return level switch
            {
                // Level 1: Deep clean without losing logical sense
                CavemanCompressionLevel.Light =>
                    token.POS != PartOfSpeech.DET &&     // No Articles
                    token.POS != PartOfSpeech.ADP &&     // No Adpositions/Prepositions
                    token.POS != PartOfSpeech.CCONJ &&   // No Coordinating Conjunctions
                    token.POS != PartOfSpeech.SCONJ &&   // No Subordinating Conjunctions
                    token.POS != PartOfSpeech.PRON &&    // No Pronouns
                    token.POS != PartOfSpeech.PUNCT &&   // No Punctuation
                    token.POS != PartOfSpeech.SYM,       // No Symbols

                // Level 2: Only the "payload" for LLMs
                CavemanCompressionLevel.Semantic =>
                    token.POS == PartOfSpeech.NOUN ||
                    token.POS == PartOfSpeech.VERB ||
                    token.POS == PartOfSpeech.ADJ ||
                    token.POS == PartOfSpeech.PROPN ||
                    token.POS == PartOfSpeech.ADV,

                // Level 3: Maximum density (Nouns and Verbs)
                CavemanCompressionLevel.Aggressive =>
                    token.POS == PartOfSpeech.NOUN ||
                    token.POS == PartOfSpeech.VERB ||
                    token.POS == PartOfSpeech.PROPN,

                _ => true
            };
        }

        // Essential method to manage RAM: clears loaded models
        public void ReleaseMemory()
        {
            _pipelines.Clear();
            GC.Collect();
        }
    }
}