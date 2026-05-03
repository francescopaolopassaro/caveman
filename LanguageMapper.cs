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
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/
using Mosaik.Core;
public static class LanguageMapper
{
    private static readonly Dictionary<string, Language> Iso3ToCatalyst = new Dictionary<string, Language>
    {
        { "afr", Language.Afrikaans },
        { "sqi", Language.Albanian },
        { "ara", Language.Arabic },
        { "hye", Language.Armenian },
        { "eus", Language.Basque },
        { "bel", Language.Belarusian },
        { "ben", Language.Bengali },
        { "bul", Language.Bulgarian },
        { "cat", Language.Catalan },
        { "zho", Language.Chinese },
        { "hrv", Language.Croatian },
        { "ces", Language.Czech },
        { "dan", Language.Danish },
        { "nld", Language.Dutch },
        { "eng", Language.English },
        { "est", Language.Estonian },
        { "fin", Language.Finnish },
        { "fra", Language.French },
        { "glg", Language.Galician },
        { "deu", Language.German },
        { "ell", Language.Greek_Modern },
        { "heb", Language.Hebrew },
        { "hin", Language.Hindi },
        { "hun", Language.Hungarian },
        { "isl", Language.Icelandic },
        { "ind", Language.Indonesian },
        { "gle", Language.Irish },
        { "ita", Language.Italian },
        { "jpn", Language.Japanese },
        { "kan", Language.Kannada },
        { "kaz", Language.Kazakh },
        { "kor", Language.Korean },
        { "lat", Language.Latin },
        { "lav", Language.Latvian },
        { "lit", Language.Lithuanian },
        { "mkd", Language.Macedonian },
        { "msa", Language.Malay },
        { "mar", Language.Marathi },
        { "nor", Language.Norwegian },
        { "fas", Language.Persian },
        { "pol", Language.Polish },
        { "por", Language.Portuguese },
        { "ron", Language.Romanian },
        { "rus", Language.Russian },
        { "srp", Language.Serbian },
        { "slk", Language.Slovak },
        { "slv", Language.Slovenian },
        { "spa", Language.Spanish },
        { "swe", Language.Swedish },
        { "tam", Language.Tamil },
        { "tel", Language.Telugu },
        { "tha", Language.Thai },
        { "tur", Language.Turkish },
        { "ukr", Language.Ukrainian },
        { "urd", Language.Urdu },
        { "vie", Language.Vietnamese }
    };

    public static Language GetCatalystLanguage(string iso3Code)
    {
        if (string.IsNullOrEmpty(iso3Code)) return Language.Any;

        return Iso3ToCatalyst.TryGetValue(iso3Code.ToLower(), out var lang)
               ? lang
               : Language.Any; //fallback to 'Any' if not found
    }
}