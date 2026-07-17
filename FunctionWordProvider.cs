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
    private static readonly ConcurrentDictionary<string, HashSet<string>> _genericCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _posCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _emptyLemmas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _empty = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Assembly _assembly = typeof(FunctionWordProvider).Assembly;


    // Languages with a hand-curated {iso3}.fw.yaml.br resource (precise grammatical
    // function words only — articles, pronouns, prepositions, conjunctions, auxiliaries).
    // Loaded from data, not embedded in code: see LoadFunctionWords.
    private static readonly HashSet<string> _curatedIso3s = new(
        new[] { "eng", "ita", "fra", "deu", "spa", "por", "nld" },
        StringComparer.OrdinalIgnoreCase);

    private static HashSet<string>? _yamlIso3s;

    // Detection index: iso3 -> (iso1, name, function_words). Loaded once from the
    // compact compressed `_index.br` resource so language detection never has to
    // touch the large per-language blobs.
    private static Dictionary<string, IndexEntry>? _index;
    private static readonly object _indexLock = new();

    private readonly record struct IndexEntry(string Iso1, string Name, HashSet<string> FunctionWords);

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
        if (_curatedIso3s.Contains(iso3))
        {
            var curated = LoadCuratedFunctionWords(iso3);
            if (curated.Count > 0)
                return curated;
        }
        return GetIndex().TryGetValue(iso3, out var e) && e.FunctionWords.Count > 0
            ? e.FunctionWords
            : _empty;
    }

    // Curated languages: hand-picked grammatical function words only (articles, pronouns,
    // prepositions, conjunctions, auxiliaries) from {iso3}.fw.yaml.br — more precise than the
    // broad YAML stop-word lists used for the other (non-curated) languages.
    private static HashSet<string> LoadCuratedFunctionWords(string iso3)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var raw = _assembly.GetManifestResourceStream(
            $"caveman.core.worddata.{iso3.ToLowerInvariant()}.fw.yaml.br");
        if (raw == null)
            return result;

        using var br = new BrotliStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(br);

        bool inFw = false;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                inFw = line.TrimEnd().TrimEnd(':') == "function_words";
                continue;
            }
            if (!inFw) continue;
            var t = line.Trim();
            if (t.Length >= 2 && t[0] == '-')
            {
                var w = StripQuotes(t.Substring(1).Trim());
                if (w.Length > 0) result.Add(w);
            }
        }
        return result;
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

    /// <summary>
    /// Returns a word → Universal POS tag lookup (NOUN, VERB, ADJ, ADP, DET, …) for this
    /// language: the most frequent tag Universal Dependencies treebanks observed for each
    /// form, generated offline by <c>scripts/import-ud-lemmas</c> from the same UD source
    /// data already used for lemmas/verbs (a classic frequency-baseline tagger — no model,
    /// no inference, just a dictionary lookup). Returns an empty dictionary when no
    /// <c>{iso3}.pos.yaml.br</c> resource is available for the language.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetPosTags(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return _emptyLemmas;
        return _posCache.GetOrAdd(iso3.ToLowerInvariant(), LoadPosTags);
    }

    /// <summary>Looks up the most frequent Universal POS tag for a single word; null if unknown.</summary>
    public string? GetPosTag(string word, string iso3)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;
        var tags = GetPosTags(iso3);
        return tags.TryGetValue(word.Trim().ToLowerInvariant(), out var tag) ? tag : null;
    }

    private static Dictionary<string, string> LoadPosTags(string iso3)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var raw = _assembly.GetManifestResourceStream(
            $"caveman.core.worddata.{iso3.ToLowerInvariant()}.pos.yaml.br");
        if (raw == null)
            return result;

        using var br = new BrotliStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(br);

        bool inPos = false;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                inPos = line.TrimEnd().TrimEnd(':') == "pos";
                continue;
            }
            if (!inPos) continue;
            if (TryParseKeyValue(line.Trim(), out var word, out var tag) && word.Length > 0 && tag.Length > 0)
                result[word] = tag;
        }
        return result;
    }

    /// <summary>
    /// Returns generic/descriptive words for this language (e.g. "want", "know", "time")
    /// that aggressive-mode compression strips in addition to function words. Loaded from
    /// the per-language <c>{iso3}.generic.yaml.br</c> resource when curated. For every other
    /// language, derived from that language's own verb data instead (see
    /// <see cref="DeriveGenericWordsFromVerbRichness"/>) rather than falling back to nothing.
    /// </summary>
    public HashSet<string> GetGenericWords(string iso3)
    {
        if (string.IsNullOrEmpty(iso3))
            return _empty;
        return _genericCache.GetOrAdd(iso3.ToLowerInvariant(), key =>
        {
            var curated = LoadGenericWords(key);
            return curated.Count > 0 ? curated : DeriveGenericWordsFromVerbRichness(key);
        });
    }

    // Languages without a hand-curated generic-word list still deserve some aggressive-mode
    // pruning depth, but guessing translations for 40+ languages risks wrongly dropping real
    // content — the exact false-positive class this whole worddata layer exists to avoid. So
    // instead of a hardcoded per-language list, this ranks each language's *own* verb lemmas
    // by how many conjugated forms the worddata attests for them. Basic, high-frequency verbs
    // ("be", "have", "go", "want", "say", …) are consistently the most richly inflected/attested
    // ones in UD-derived data — verified against Polish worddata, where the top-ranked lemmas
    // by form count are "być" (be), "mieć" (have), "mówić" (say), "iść" (go), "chcieć" (want):
    // exactly the category curated generic-word lists target, but derived from data instead of
    // a translated word list.
    private HashSet<string> DeriveGenericWordsFromVerbRichness(string iso3)
    {
        var data = LoadWordData(iso3);
        if (data?.verbs == null || data.verbs.Count < 10)
            return _empty; // too little verb data to rank meaningfully

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (lemma, forms) in data.verbs
                     .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Value?.Count >= 4)
                     .OrderByDescending(kv => kv.Value.Count)
                     .Take(25))
        {
            result.Add(lemma);
            foreach (var f in forms)
                if (!string.IsNullOrEmpty(f)) result.Add(f);
        }
        return result;
    }

    private static HashSet<string> LoadGenericWords(string iso3)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var raw = _assembly.GetManifestResourceStream(
            $"caveman.core.worddata.{iso3.ToLowerInvariant()}.generic.yaml.br");
        if (raw == null)
            return result;

        using var br = new BrotliStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(br);

        bool inGeneric = false;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                inGeneric = line.TrimEnd().TrimEnd(':') == "generic_words";
                continue;
            }
            if (!inGeneric) continue;
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
