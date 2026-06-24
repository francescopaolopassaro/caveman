// -----------------------------------------------------------------------------
// <copyright file="FunctionWordProvider.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Loads per-language function words, lemmas and verbs from the embedded worddata resources via a fast streaming parser.</summary>
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;

namespace caveman.core;

public class FunctionWordProvider
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _lemmaCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HashSet<string>> _exclCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _emptyLemmas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _empty = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Assembly _assembly = typeof(FunctionWordProvider).Assembly;

    private static readonly HashSet<string> En = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those",
        "i", "you", "he", "she", "it", "we", "they",
        "me", "him", "her", "us", "them",
        "my", "your", "his", "its", "our", "their",
        "mine", "yours", "hers", "ours", "theirs",
        "myself", "yourself", "himself", "herself", "itself", "ourselves", "themselves",
        "who", "whom", "whose", "which", "what",
        "in", "on", "at", "to", "for", "of", "with", "by", "from",
        "into", "onto", "upon", "within", "without",
        "during", "before", "after",
        "above", "below", "between", "among", "amongst",
        "across", "against", "around", "behind", "beneath",
        "beside", "besides", "beyond", "inside",
        "near", "off", "outside", "over", "past",
        "through", "toward", "towards", "under", "underneath",
        "via", "per",
        "and", "or", "but", "if", "because", "although", "though",
        "while", "whereas", "unless", "since", "so", "yet",
        "nor", "both", "whether", "either", "neither",
        "be", "am", "is", "are", "was", "were", "been", "being",
        "have", "has", "had", "having",
        "do", "does", "did", "doing", "done",
        "will", "would", "shall", "should",
        "can", "could", "may", "might", "must",
        "need", "dare", "ought",
        "not", "no", "nor", "never",
        "as", "than",
        "very", "too", "quite", "rather",
        "here", "there", "where",
        "when", "why", "how",
        "then", "now",
        "just", "only", "even",
        "also", "still", "already",
        "indeed", "however", "therefore",
        "otherwise", "nevertheless",
        "maybe", "perhaps",
        "please", "yes",
        "oh", "ah",
        "any", "some", "every", "each", "all", "few", "many", "much", "several",
        "no", "none", "nothing",
        "something", "anything", "everything",
        "someone", "anyone", "everyone",
        "such", "same", "else", "other", "another",
        "more", "most", "less", "least",
        "up", "down", "out",
        "well",
    };

    private static readonly HashSet<string> It = new(StringComparer.OrdinalIgnoreCase)
    {
        "il", "lo", "la", "i", "gli", "le",
        "un", "uno", "una",
        "questo", "questa", "questi", "queste", "quel", "quella", "quelli", "quelle",
        "io", "tu", "lui", "lei", "noi", "voi", "loro",
        "mi", "ti", "si", "ci", "vi", "ne",
        "mio", "tuo", "suo", "nostro", "vostro",
        "mia", "tua", "sua", "nostra", "vostra",
        "miei", "tuoi", "suoi", "nostri", "vostri",
        "mie", "tue", "sue", "nostre", "vostre",
        "che", "cui", "chi",
        "in", "a", "da", "di", "con", "su", "per", "tra", "fra",
        "del", "dello", "della", "dei", "degli", "delle",
        "al", "allo", "alla", "ai", "agli", "alle",
        "dal", "dallo", "dalla", "dai", "dagli", "dalle",
        "nel", "nello", "nella", "nei", "negli", "nelle",
        "sul", "sullo", "sulla", "sui", "sugli", "sulle",
        "e", "ed", "o", "od", "ma",
        "se", "come",
        "mentre", "quando", "dove",
        "non", "neppure", "nemmeno",
        "sono", "sei", "e", "siamo", "siete", "sia", "siano",
        "ho", "hai", "ha", "abbiamo", "avete", "hanno",
        "sto", "stai", "sta", "stiamo", "state", "stanno",
        "posso", "puoi", "puo", "possiamo", "potete", "possono",
        "voglio", "vuoi", "vuole", "vogliamo", "volete", "vogliono",
        "devo", "devi", "deve", "dobbiamo", "dovete", "devono",
        "lo", "la", "li",
        "qui", "qua", "li",
        "gia", "piu", "meno", "molto", "poco", "troppo",
        "anche", "pure", "ancora", "sempre", "mai",
        "poi", "dopo", "prima", "ora", "adesso",
        "allora", "dunque", "quindi", "inoltre",
        "perche",
        "fa", "fanno", "fare",
        "essere", "avere", "stare", "potere", "volere", "dovere",
    };

    private static readonly HashSet<string> Fr = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "l",
        "un", "une", "des",
        "du", "de", "d",
        "ce", "cet", "cette", "ces",
        "mon", "ton", "son", "ma", "ta", "sa",
        "mes", "tes", "ses", "nos", "vos", "leurs",
        "notre", "votre",
        "chaque", "quelques",
        "tout", "toute", "tous", "toutes",
        "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles",
        "me", "te", "se", "lui", "leur",
        "moi", "toi", "soi", "eux",
        "qui", "que", "quoi", "dont", "ou",
        "a", "dans", "par", "pour", "en", "vers", "avec",
        "sans", "sous", "sur", "chez", "entre",
        "pendant", "depuis",
        "et", "ou", "mais", "donc", "car", "ni",
        "que", "lorsque", "quand", "puisque",
        "si",
        "suis", "es", "est", "sommes", "etes", "sont",
        "ai", "as", "a", "avons", "avez", "ont",
        "ete", "etre", "avoir",
        "ne", "pas", "plus",
        "tres", "aussi", "bien", "deja", "encore", "toujours", "jamais",
        "alors", "puis", "ainsi", "enfin",
        "trop", "assez", "moins", "beaucoup", "peu",
        "oui", "non",
        "ce", "c", "ca",
        "y",
    };

    private static readonly HashSet<string> De = new(StringComparer.OrdinalIgnoreCase)
    {
        "der", "die", "das", "den", "dem", "des",
        "ein", "eine", "einer", "eines", "einem", "einen",
        "mein", "dein", "sein", "ihr", "unser", "euer",
        "meine", "deine", "seine", "ihre", "unsere", "eure",
        "dieser", "diese", "dieses", "diesen", "diesem",
        "ich", "du", "er", "sie", "es", "wir", "ihr", "Sie",
        "mich", "dich", "sich", "uns", "euch",
        "mir", "dir", "ihm", "ihr", "uns",
        "man",
        "in", "auf", "mit", "von", "zu", "aus", "bei", "nach",
        "um", "durch", "fur", "gegen", "ohne",
        "uber", "unter", "vor", "hinter", "neben", "zwischen",
        "an", "bis", "seit",
        "und", "oder", "aber", "denn", "weil",
        "dass", "wenn", "als", "ob",
        "wahrend", "nachdem", "bevor", "seitdem",
        "bin", "bist", "ist", "sind", "seid", "war", "waren",
        "habe", "hast", "hat", "haben", "habt",
        "kann", "kannst", "konnen", "konnt",
        "muss", "musst", "mussen", "musst",
        "soll", "sollst", "sollen", "sollt",
        "will", "willst", "wollen", "wollt",
        "darf", "darfst", "durfen", "durft",
        "mag", "magst", "mogen",
        "nicht", "kein", "keine", "keinen",
        "nichts", "nie", "niemals",
        "sehr", "viel", "wenig", "zu", "ganz",
        "etwas", "mehr", "weniger",
        "schon", "noch", "immer",
        "ja", "nein",
        "auch", "nur",
    };

    private static readonly HashSet<string> Es = new(StringComparer.OrdinalIgnoreCase)
    {
        "el", "la", "los", "las",
        "un", "una", "unos", "unas",
        "este", "esta", "estos", "estas",
        "ese", "esa", "esos", "esas",
        "aquel", "aquella", "aquellos", "aquellas",
        "mi", "tu", "su", "nuestro", "vuestro",
        "mis", "tus", "sus", "nuestros", "vuestros",
        "yo", "tu", "el", "ella", "usted",
        "nosotros", "vosotros", "ellos", "ellas", "ustedes",
        "me", "te", "se", "nos", "os",
        "lo", "le", "la", "les",
        "a", "ante", "bajo", "con", "contra", "de", "desde",
        "durante", "en", "entre", "hacia", "hasta",
        "mediante", "para", "por", "segun", "sin", "sobre", "tras",
        "y", "e", "o", "u", "pero", "sino",
        "aunque", "porque", "pues", "como", "que", "si",
        "cuando", "mientras",
        "soy", "eres", "es", "somos", "sois", "son",
        "he", "has", "ha", "hemos", "habeis", "han",
        "estoy", "estas", "esta", "estamos", "estais", "estan",
        "tengo", "tienes", "tiene", "tenemos", "teneis", "tienen",
        "puedo", "puedes", "puede", "podemos", "podeis", "pueden",
        "quiero", "quieres", "quiere", "queremos", "quereis", "quieren",
        "debo", "debes", "debe", "debemos", "debeis", "deben",
        "no", "nada", "nadie",
        "ningun", "ninguna",
        "nunca", "jamas",
        "muy", "mucho", "poca", "poco",
        "bastante", "demasiado",
        "mas", "menos",
        "casi", "solo", "solamente",
        "tambien", "siempre",
        "ya", "aun", "todavia",
        "aqui", "ahi", "alli",
        "bien", "mal",
    };

    private static readonly HashSet<string> Pt = new(StringComparer.OrdinalIgnoreCase)
    {
        "o", "a", "os", "as",
        "um", "uma", "uns", "umas",
        "este", "esta", "estes", "estas",
        "esse", "essa", "esses", "essas",
        "aquele", "aquela", "aqueles", "aquelas",
        "meu", "minha", "teu", "tua", "seu", "sua",
        "nosso", "nossa", "vosso", "vossa",
        "meus", "minhas", "teus", "tuas", "seus", "suas",
        "nossos", "nossas", "vossos", "vossas",
        "eu", "tu", "ele", "ela", "nos", "vos", "eles", "elas",
        "voce", "voces",
        "me", "te", "se", "lhe", "lhes",
        "a", "ante", "apos", "ate", "com", "contra", "de",
        "desde", "em", "entre", "para", "perante", "por",
        "sem", "sob", "sobre", "tras",
        "e", "mas", "ou", "porque", "pois", "como", "que", "se",
        "quando", "enquanto", "embora",
        "contudo", "entretanto", "portanto", "porem", "todavia",
        "sou", "e", "somos", "sao",
        "estou", "esta", "estamos", "estao",
        "tenho", "tem", "temos", "tem",
        "hei", "ha", "havemos", "hao",
        "posso", "pode", "podemos", "podem",
        "quero", "quer", "queremos", "querem",
        "devo", "deve", "devemos", "devem",
        "nao", "nada", "ninguem",
        "nenhum", "nenhuma",
        "nunca", "jamais",
        "muito", "pouco", "bastante", "demais",
        "mais", "menos",
        "quase", "so", "somente",
        "tambem", "sempre",
        "ja", "ainda", "agora",
    };

    private static readonly HashSet<string> Nl = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "het", "een",
        "deze", "dit", "die", "dat",
        "mijn", "jouw", "zijn", "haar", "onze", "ons", "hun", "uw",
        "je", "zijn", "haar",
        "ik", "jij", "je", "u", "hij", "zij", "ze", "het", "wij", "we",
        "jullie", "ze",
        "mij", "me", "jou", "je", "hem", "haar", "ons", "jullie", "hen", "hun",
        "in", "op", "met", "van", "naar", "uit", "bij", "door",
        "voor", "over", "onder", "tegen", "tussen",
        "tijdens", "na", "langs", "om", "zonder",
        "binnen", "buiten", "via", "per",
        "en", "of", "maar", "want", "dus", "omdat",
        "als", "wanneer", "terwijl", "hoewel",
        "indien", "tenzij", "nadat", "voordat",
        "ben", "bent", "is", "zijn", "was", "waren",
        "heb", "hebt", "heeft", "hebben", "had", "hadden",
        "kan", "kunt", "kunnen",
        "moet", "moeten",
        "mag", "mogen",
        "wil", "wilt", "willen",
        "zal", "zult", "zullen", "zou", "zouden",
        "word", "wordt", "werd", "werden", "geworden",
        "niet", "geen",
        "niets", "niemand", "nooit",
        "heel", "veel", "weinig", "erg",
        "nog", "al", "reeds",
        "pas", "slechts", "maar",
        "net", "even",
        "bijna", "haast",
        "ja", "nee",
        "ook",
    };

    private static readonly Dictionary<string, HashSet<string>> CuratedLists = new(StringComparer.OrdinalIgnoreCase)
    {
        { "eng", En }, { "ita", It }, { "fra", Fr },
        { "deu", De }, { "spa", Es }, { "por", Pt }, { "nld", Nl },
    };

    private static readonly HashSet<string> _curatedIso3s;
    private static HashSet<string>? _yamlIso3s;

    // Detection index: iso3 -> (iso1, name, function_words). Loaded once from the
    // compact compressed `_index.br` resource so language detection never has to
    // touch the large per-language blobs.
    private static Dictionary<string, IndexEntry>? _index;
    private static readonly object _indexLock = new();

    private readonly record struct IndexEntry(string Iso1, string Name, HashSet<string> FunctionWords);

    static FunctionWordProvider()
    {
        _curatedIso3s = new HashSet<string>(CuratedLists.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IndexEntry> GetIndex()
    {
        if (_index != null)
            return _index;
        lock (_indexLock)
        {
            if (_index != null)
                return _index;

            var idx = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
            using var raw = _assembly.GetManifestResourceStream("caveman.core.worddata._index.br");
            if (raw != null)
            {
                using var br = new BrotliStream(raw, CompressionMode.Decompress);
                using var reader = new StreamReader(br);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var fw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 3; i < parts.Length; i++)
                        if (parts[i].Length > 0) fw.Add(parts[i]);
                    idx[parts[0]] = new IndexEntry(parts[1], parts[2], fw);
                }
            }
            _index = idx;
        }
        return _index;
    }

    public FunctionWordProvider() { }

    public bool IsFunctionWord(string word, string iso3)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        var words = GetFunctionWords(iso3);
        return words.Count > 0 && words.Contains(word.Trim().ToLowerInvariant());
    }

    public HashSet<string> GetFunctionWords(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return _empty;
        return _cache.GetOrAdd(iso3, LoadFunctionWords);
    }

    /// <summary>
    /// Returns the cached inflected-form → base-form map for a language (verbs folded in, explicit
    /// lemmas taking precedence). Empty when the language has no lemma data. Shared by the
    /// compressor, summarizer and relevance/dedup so they all normalize words the same way.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetLemmaMap(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return _emptyLemmas;
        return _lemmaCache.GetOrAdd(iso3, BuildLemmaMap);
    }

    private Dictionary<string, string> BuildLemmaMap(string iso3)
    {
        var data = LoadWordData(iso3);
        if (data == null)
            return _emptyLemmas;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Verbs are stored as lemma -> [conjugated forms]; invert into form -> lemma.
        if (data.verbs?.Count > 0)
            foreach (var (lemma, forms) in data.verbs)
            {
                if (string.IsNullOrEmpty(lemma) || forms == null) continue;
                foreach (var f in forms)
                    if (!string.IsNullOrEmpty(f)) map[f] = lemma;
            }

        // Explicit lemmas take precedence over verb-derived mappings.
        if (data.lemmas?.Count > 0)
            foreach (var (form, lemma) in data.lemmas)
                if (!string.IsNullOrEmpty(form) && !string.IsNullOrEmpty(lemma)) map[form] = lemma;

        return map.Count > 0 ? map : _emptyLemmas;
    }

    private HashSet<string> LoadFunctionWords(string iso3)
    {
        if (CuratedLists.TryGetValue(iso3, out var curated))
            return curated;
        return GetIndex().TryGetValue(iso3, out var e) && e.FunctionWords.Count > 0
            ? e.FunctionWords
            : _empty;
    }

    // Per-language blob: brotli-compressed YAML, embedded as `<iso3>.yaml.br`.
    private static Stream? OpenResource(string iso3)
    {
        var raw = _assembly.GetManifestResourceStream($"caveman.core.worddata.{iso3.ToLowerInvariant()}.yaml.br");
        return raw == null ? null : new BrotliStream(raw, CompressionMode.Decompress);
    }

    // Full load (compression time, once per language): function_words + lemmas +
    // verbs + proper_nouns. Decompresses the blob and parses it with the custom
    // streaming YAML parser (tolerant of source quirks like tokens 'tis or a's).
    public WordDataFile? LoadWordData(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return null;

        using var stream = OpenResource(iso3);
        if (stream == null)
            return null;

        return ParseYaml(stream);
    }

    // Parses the worddata YAML text format. Kept public/static so the offline
    // compiler (scripts/compile-worddata) can build the index from the same parser.
    public static WordDataFile ParseYaml(Stream stream)
    {
        var data = new WordDataFile();
        using var reader = new StreamReader(stream);

        const int None = 0, FunctionWords = 1, Lemmas = 2, Verbs = 3, ProperNouns = 4;
        int section = None;
        string? currentVerb = null;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
                continue;

            if (!char.IsWhiteSpace(line[0]))
            {
                var key = line.TrimEnd();
                if (key.StartsWith("iso3:", StringComparison.Ordinal)) { data.iso3 = key.Substring(5).Trim(); section = None; }
                else if (key.StartsWith("iso1:", StringComparison.Ordinal)) { data.iso1 = key.Substring(5).Trim(); section = None; }
                else if (key.StartsWith("name:", StringComparison.Ordinal)) { data.name = key.Substring(5).Trim(); section = None; }
                else if (key == "function_words:") section = FunctionWords;
                else if (key == "lemmas:") section = Lemmas;
                else if (key == "verbs:") { section = Verbs; currentVerb = null; }
                else if (key == "proper_nouns:") section = ProperNouns;
                else section = None;
                continue;
            }

            var t = line.Trim();
            switch (section)
            {
                case FunctionWords:
                    if (t.Length >= 2 && t[0] == '-')
                    {
                        var w = StripQuotes(t.Substring(1).Trim());
                        if (w.Length > 0) data.function_words.Add(w);
                    }
                    break;

                case Lemmas:
                    if (TryParseKeyValue(t, out var lk, out var lv) && lk.Length > 0 && lv.Length > 0)
                        data.lemmas[lk] = lv;
                    break;

                case Verbs:
                    if (t[0] == '-')
                    {
                        if (currentVerb != null)
                        {
                            var f = StripQuotes(t.Substring(1).Trim());
                            if (f.Length > 0) data.verbs[currentVerb].Add(f);
                        }
                    }
                    else if (TryParseKeyValue(t, out var vk, out _) && vk.Length > 0)
                    {
                        currentVerb = vk;
                        if (!data.verbs.ContainsKey(currentVerb))
                            data.verbs[currentVerb] = new List<string>();
                    }
                    break;

                case ProperNouns:
                    if (t.Length >= 2 && t[0] == '-')
                    {
                        var n = StripQuotes(t.Substring(1).Trim());
                        if (n.Length > 0) data.proper_nouns.Add(n);
                    }
                    break;
            }
        }

        return data;
    }

    private static bool TryParseKeyValue(string t, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        if (t.Length == 0) return false;

        int colon;
        if (t[0] == '"')
        {
            int close = ClosingQuote(t, 0);
            if (close < 0) return false;
            colon = t.IndexOf(':', close + 1);
            if (colon < 0) return false;
            key = StripQuotes(t.Substring(0, close + 1));
        }
        else
        {
            colon = t.IndexOf(':');
            if (colon < 0) return false;
            key = StripQuotes(t.Substring(0, colon).Trim());
        }

        value = StripQuotes(t.Substring(colon + 1).Trim());
        return true;
    }

    private static int ClosingQuote(string s, int openIndex)
    {
        for (int i = openIndex + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return -1;
    }

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2)
        {
            if (s[0] == '"' && s[^1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (s[0] == '\'' && s[^1] == '\'')
                return s.Substring(1, s.Length - 2).Replace("''", "'");
        }
        return s;
    }

    /// <summary>
    /// Returns words unique to this language — used by the detector's second pass
    /// to disambiguate texts that share common words with other languages (e.g. "per",
    /// "a", "in" appear in both English and Italian).
    /// Returns an empty set when no exclusive-marker file is available for the language.
    /// </summary>
    public HashSet<string> GetExclusiveMarkers(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return _empty;
        return _exclCache.GetOrAdd(iso3.ToLowerInvariant(), LoadExclusiveMarkers);
    }

    private static HashSet<string> LoadExclusiveMarkers(string iso3)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var raw = _assembly.GetManifestResourceStream(
            $"caveman.core.worddata.{iso3.ToLowerInvariant()}.excl.yaml.br");
        if (raw == null)
            return result;

        using var br = new BrotliStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(br);

        bool inExcl = false;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                inExcl = line.TrimEnd().TrimEnd(':') == "exclusive_markers";
                continue;
            }
            if (!inExcl) continue;
            var t = line.Trim();
            if (t.Length >= 2 && t[0] == '-')
            {
                var w = StripQuotes(t.Substring(1).Trim());
                if (w.Length > 0) result.Add(w);
            }
        }
        return result;
    }

    public HashSet<string> GetAllSupportedIso3()
    {
        var yamls = GetYamlIso3s();
        var all = new HashSet<string>(_curatedIso3s, StringComparer.OrdinalIgnoreCase);
        foreach (var y in yamls)
            all.Add(y);
        return all;
    }

    public IEnumerable<string> GetSupportedLanguages()
    {
        return GetAllSupportedIso3();
    }

    private static HashSet<string> GetYamlIso3s()
    {
        if (_yamlIso3s != null)
            return _yamlIso3s;
        _yamlIso3s = new HashSet<string>(GetIndex().Keys, StringComparer.OrdinalIgnoreCase);
        return _yamlIso3s;
    }
}

public class WordDataFile
{
    public string iso3 { get; set; } = string.Empty;
    public string iso1 { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public List<string> function_words { get; set; } = new();
    public Dictionary<string, string> lemmas { get; set; } = new();
    public Dictionary<string, List<string>> verbs { get; set; } = new();
    public List<string> proper_nouns { get; set; } = new();
}
