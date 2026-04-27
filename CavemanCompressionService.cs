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
 * * AUTHOR: [Francesco Paolo Passaro]
 * DATE: April 2026
 *---------------------------------------------------------------------------------------*/
using Catalyst;
using LanguageDetection;
using Mosaik.Core;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace caveman.core
{

    using Catalyst;
    using LanguageDetection;
    using Mosaik.Core;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using static System.Net.Mime.MediaTypeNames;

    // 1. Definizione livelli di compressione

    public enum CavemanCompressionLevel
    {
        None = 0,       // Pass-through: nessuna modifica
        Light = 1,      // Stop-words: toglie articoli e congiunzioni (Veloce)
        Semantic = 2,   // POS Tagging: tiene solo Nomi, Verbi, Aggettivi
        Aggressive = 3  // Brutal: solo Nomi/Verbi e Lemmatizzazione (riduce alla radice)
    }
    public class CavemanCompressionService
    {
        // Cache delle pipeline per evitare ricaricamenti pesanti in RAM
        private static readonly Dictionary<Language, Pipeline> _pipelines = new();
        private readonly LanguageDetector _detector;

        public CavemanCompressionService()
        {
            _detector = new LanguageDetector();
            _detector.AddAllLanguages(); // Carica profili statistici leggeri
        }

        public async Task<string> CompressAsync(string input, CavemanCompressionLevel level)
        {
            if (level == CavemanCompressionLevel.None || string.IsNullOrWhiteSpace(input))
                return input;

            // 1. Rilevamento lingua (restituisce es. "ita")

            var detector = new LanguageDetector();
            detector.AddAllLanguages();

            string detectedCode = detector.Detect(input); // Restituisce es. "ita"
            Language catalystLang = LanguageMapper.GetCatalystLanguage(detectedCode);

            // 2. Elaborazione tramite NLP (Catalyst)
            return await ApplyNlpLogic(input, catalystLang, level);

        }

        public async Task<string> ApplyNlpLogic(string input, Language lang, CavemanCompressionLevel level)
        {
            // Forza la configurazione dello storage PRIMA di ogni operazione
            if (Storage.Current == null)
            {
                Storage.Current = new DiskStorage("catalyst-models");
            }
           
            if (!_pipelines.TryGetValue(lang, out var nlp))
            {
                // Registrazione esplicita
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
                    // Se ForAsync fallisce ancora, prova a specificare la versione del modello (di solito 0.0.1)
                    nlp = await Pipeline.ForAsync(lang);
                    _pipelines[lang] = nlp;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Errore critico caricamento pipeline {lang}: {ex.Message}");
                    throw;
                }
            }

            var doc = new Document(input, lang);
            nlp.ProcessSingle(doc);

            // Estraiamo i token filtrati
            var tokens = doc.SelectMany(span => span)
                            .Where(token => ShouldKeepToken(token, level))
                            .Select(token => level == CavemanCompressionLevel.Aggressive ? token.Lemma : token.ValueAsSpan.ToString());

            return string.Join(" ", tokens);
        }

        private bool ShouldKeepToken(IToken token, CavemanCompressionLevel level)
        {
            return level switch
            {
                // LIVELLO 1: Pulizia profonda senza perdere il senso logico
                CavemanCompressionLevel.Light =>
                    token.POS != PartOfSpeech.DET && // No Articoli (il, lo, una...)
                    token.POS != PartOfSpeech.ADP && // No Preposizioni (di, a, da, in...)
                    token.POS != PartOfSpeech.CCONJ && // No Congiunzioni coordinanti (e, o, ma...)
                    token.POS != PartOfSpeech.SCONJ && // No Congiunzioni subordinanti (che, se, perché...)
                    token.POS != PartOfSpeech.PRON && // No Pronomi (io, mi, lo...)
                    token.POS != PartOfSpeech.PUNCT && // No Punteggiatura
                    token.POS != PartOfSpeech.SYM,     // No Simboli matematici/speciali

                // LIVELLO 2: Solo il "carico utile" per Gemma 3
                CavemanCompressionLevel.Semantic =>
                    token.POS == PartOfSpeech.NOUN ||
                    token.POS == PartOfSpeech.VERB ||
                    token.POS == PartOfSpeech.ADJ ||
                    token.POS == PartOfSpeech.PROPN ||
                    token.POS == PartOfSpeech.ADV,     // Spesso utile per capire "come" fare un'azione

                // LIVELLO 3: Massima densità (Nomi e Verbi)
                CavemanCompressionLevel.Aggressive =>
                    token.POS == PartOfSpeech.NOUN ||
                    token.POS == PartOfSpeech.VERB ||
                    token.POS == PartOfSpeech.PROPN,

                _ => true
            };
        }

        // Metodo fondamentale per gestire la RAM: svuota i modelli caricati
        public void ReleaseMemory()
        {
            _pipelines.Clear();
            GC.Collect();
        }
    }
}
