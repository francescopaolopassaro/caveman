// -----------------------------------------------------------------------------
// <copyright file="Program.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Developer script: imports lemmas and verb forms from Universal Dependencies treebanks into the worddata YAML files.</summary>
// -----------------------------------------------------------------------------
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// import-ud-lemmas
// Builds `form -> lemma` dictionaries from Universal Dependencies treebanks
// (https://universaldependencies.org/) and merges them into the `lemmas:`
// section of caveman's worddata/<iso3>.yaml files.
//
// Usage:
//   dotnet run -- <iso3> [<iso3> ...]     import specific languages
//   dotnet run -- --all                   import every mapped language
//   dotnet run -- --list                  list mapped languages and exit
//
// Source data is licensed per UD treebank (mostly CC-BY-SA / CC-BY).
// Attribution (NOTICE/CREDITS) is handled separately.
// ---------------------------------------------------------------------------

var repoRoot = @"C:\Sorgenti\Personal\caveman";
var worddataDir = Path.Combine(repoRoot, "worddata");
var tmpRoot = Path.Combine(Path.GetTempPath(), "caveman-ud-lemmas");

// Only content POS carry useful lemma normalization; function words are already
// handled by function_words, and PROPN/NUM lemmas are mostly identity noise.
var keepPos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NOUN", "VERB", "ADJ", "ADV", "AUX" };

// Skip individual .conllu files larger than this (e.g. the huge German-HDT train).
const double MaxFileMB = 80;

// caveman iso3 -> UD language folder name (the part in `UD_<Name>-<Treebank>`).
var langMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["afr"] = "Afrikaans", ["ara"] = "Arabic", ["ben"] = "Bengali", ["bul"] = "Bulgarian",
    ["cat"] = "Catalan", ["ces"] = "Czech", ["dan"] = "Danish", ["deu"] = "German",
    ["ell"] = "Greek", ["eng"] = "English", ["est"] = "Estonian", ["eus"] = "Basque",
    ["fas"] = "Persian", ["fin"] = "Finnish", ["fra"] = "French", ["gle"] = "Irish",
    ["glg"] = "Galician", ["heb"] = "Hebrew", ["hin"] = "Hindi", ["hrv"] = "Croatian",
    ["hun"] = "Hungarian", ["hye"] = "Armenian", ["ind"] = "Indonesian", ["isl"] = "Icelandic",
    ["ita"] = "Italian", ["jpn"] = "Japanese", ["kor"] = "Korean", ["lat"] = "Latin",
    ["lav"] = "Latvian", ["lit"] = "Lithuanian", ["mar"] = "Marathi", ["nld"] = "Dutch",
    ["nor"] = "Norwegian", ["pol"] = "Polish", ["por"] = "Portuguese", ["ron"] = "Romanian",
    ["rus"] = "Russian", ["slk"] = "Slovak", ["slv"] = "Slovenian", ["spa"] = "Spanish",
    ["sqi"] = "Albanian", ["srp"] = "Serbian", ["swe"] = "Swedish", ["tha"] = "Thai",
    ["tur"] = "Turkish", ["ukr"] = "Ukrainian", ["urd"] = "Urdu", ["vie"] = "Vietnamese",
    ["zho"] = "Chinese", ["kan"] = "Kannada", ["bel"] = "Belarusian", ["kaz"] = "Kazakh",
    ["mkd"] = "Macedonian", ["tel"] = "Telugu", ["tam"] = "Tamil",
};

var flags = args.Where(a => a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToHashSet();
var requested = args.Where(a => !a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToList();

if (flags.Contains("--list") || (requested.Count == 0 && !flags.Contains("--all")))
{
    Console.WriteLine("Mapped languages (iso3 -> UD name):");
    foreach (var kv in langMap.OrderBy(k => k.Key))
        Console.WriteLine($"  {kv.Key} -> {kv.Value}");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -- <iso3> [<iso3> ...] | --all");
    return;
}

var targets = flags.Contains("--all")
    ? langMap.Keys.OrderBy(k => k).ToList()
    : requested.Where(langMap.ContainsKey).ToList();

foreach (var bad in requested.Where(r => !langMap.ContainsKey(r)))
    Console.WriteLine($"WARN: '{bad}' is not mapped, skipping.");

if (targets.Count == 0) { Console.WriteLine("Nothing to do."); return; }

Directory.CreateDirectory(tmpRoot);

// --- list all UD org repos once (cheap: ~3 API calls) ----------------------
var allRepos = await ListUdReposAsync();
Console.WriteLine($"UD org repos discovered: {allRepos.Count}");

foreach (var iso3 in targets)
{
    var langName = langMap[iso3];
    var repos = allRepos
        .Where(r => r.StartsWith($"UD_{langName}-", StringComparison.Ordinal))
        .OrderBy(r => r, StringComparer.Ordinal)
        .ToList();

    if (repos.Count == 0)
    {
        Console.WriteLine($"[{iso3}] no UD treebank for '{langName}', skipping.");
        continue;
    }

    Console.WriteLine($"[{iso3}] {langName}: {repos.Count} treebank(s): {string.Join(", ", repos)}");

    // form -> (lemma -> count)
    var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    // verb lemma -> set of inflected forms (VERB + AUX only)
    var verbForms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    // per-form PROPN vs non-PROPN occurrence counts (for a high-precision gazetteer)
    var propnCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var nonPropnCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var repo in repos)
    {
        var dir = Path.Combine(tmpRoot, repo);
        if (!CloneIfNeeded(repo, dir)) continue;

        foreach (var file in Directory.GetFiles(dir, "*.conllu", SearchOption.AllDirectories))
        {
            var sizeMB = new FileInfo(file).Length / 1_048_576.0;
            if (sizeMB > MaxFileMB)
            {
                Console.WriteLine($"    skip {Path.GetFileName(file)} ({sizeMB:F0} MB > {MaxFileMB} MB)");
                continue;
            }
            ParseConllu(file, keepPos, counts, verbForms, propnCounts, nonPropnCounts);
        }
    }

    // Keep only forms that occur as a name at least as often as not — so a common
    // word that is occasionally tagged PROPN does not pollute the gazetteer.
    var properNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (form, pc) in propnCounts)
        if (pc >= nonPropnCounts.GetValueOrDefault(form))
            properNouns.Add(form);

    if (counts.Count == 0 && properNouns.Count == 0)
    {
        Console.WriteLine($"[{iso3}] no usable data extracted.");
        continue;
    }

    // pick the most frequent lemma per form (tie: shorter, then alphabetical)
    var udLemmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (form, lemmas) in counts)
    {
        var best = lemmas
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.Length)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .First().Key;
        if (best.Equals(form, StringComparison.OrdinalIgnoreCase))
            continue; // dominant lemma is the form itself -> not an inflection
        udLemmas[form] = best;
    }

    // Every form UD actually observed: for these, UD's verdict overrides any
    // pre-existing entry (so previously-imported noise is corrected/dropped).
    var udSaw = new HashSet<string>(counts.Keys, StringComparer.OrdinalIgnoreCase);

    WriteYaml(iso3, udLemmas, udSaw, verbForms, properNouns);
}

Console.WriteLine("Done.");
return;

// ---------------------------------------------------------------------------

async Task<List<string>> ListUdReposAsync()
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("caveman-ud-import");
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrEmpty(token))
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

    var names = new List<string>();
    for (int page = 1; page <= 10; page++)
    {
        var url = $"https://api.github.com/orgs/UniversalDependencies/repos?per_page=100&page={page}";
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GitHub API {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var batch = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();
        names.AddRange(batch);
        if (batch.Count < 100) break;
    }
    return names;
}

bool CloneIfNeeded(string repo, string dir)
{
    if (Directory.Exists(Path.Combine(dir, ".git")))
    {
        Console.WriteLine($"    using cached {repo}");
        return true;
    }
    Console.WriteLine($"    cloning {repo} ...");
    var psi = new ProcessStartInfo("git",
        $"clone --depth 1 --single-branch https://github.com/UniversalDependencies/{repo}.git \"{dir}\"")
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    };
    var p = Process.Start(psi)!;
    p.WaitForExit();
    if (p.ExitCode != 0)
    {
        Console.WriteLine($"    clone FAILED: {p.StandardError.ReadToEnd().Trim()}");
        return false;
    }
    return true;
}

void ParseConllu(
    string file,
    HashSet<string> keep,
    Dictionary<string, Dictionary<string, int>> counts,
    Dictionary<string, HashSet<string>> verbForms,
    Dictionary<string, int> propnCounts,
    Dictionary<string, int> nonPropnCounts)
{
    foreach (var line in File.ReadLines(file))
    {
        if (line.Length == 0 || line[0] == '#') continue;
        var cols = line.Split('\t');
        if (cols.Length < 4) continue;

        var id = cols[0];
        if (id.Contains('-') || id.Contains('.')) continue; // multiword range / empty node

        var form = cols[1];
        var lemma = cols[2];
        var upos = cols[3];

        if (form.Length < 2) continue;
        if (!form.Any(char.IsLetter)) continue;

        var f = form.ToLowerInvariant();

        // Track how often each form occurs as a name vs. anything else, so the
        // gazetteer can keep only forms that are *predominantly* proper nouns.
        if (upos.Equals("PROPN", StringComparison.OrdinalIgnoreCase))
        {
            propnCounts[f] = propnCounts.GetValueOrDefault(f) + 1;
            continue; // names are never fed to the lemma/verb maps
        }
        nonPropnCounts[f] = nonPropnCounts.GetValueOrDefault(f) + 1;

        if (!keep.Contains(upos)) continue;
        if (lemma.Length == 0 || lemma == "_") continue;

        var l = lemma.ToLowerInvariant();
        if (l.Length == 0 || l.Contains(' ')) continue;

        // Identity observations are counted too: if a form is overwhelmingly its
        // own lemma, a rare mis-tagged lemma must not win (e.g. "на" -> "напрочь").
        if (!counts.TryGetValue(f, out var inner))
            counts[f] = inner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        inner[l] = inner.GetValueOrDefault(l) + 1;

        if (!f.Equals(l, StringComparison.OrdinalIgnoreCase) &&
            (upos.Equals("VERB", StringComparison.OrdinalIgnoreCase) ||
             upos.Equals("AUX", StringComparison.OrdinalIgnoreCase)))
        {
            if (!verbForms.TryGetValue(l, out var set))
                verbForms[l] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(f);
        }
    }
}

void WriteYaml(string iso3, Dictionary<string, string> udLemmas, HashSet<string> udSaw, Dictionary<string, HashSet<string>> udVerbs, HashSet<string> udProperNouns)
{
    var path = Path.Combine(worddataDir, $"{iso3}.yaml");
    if (!File.Exists(path))
    {
        Console.WriteLine($"[{iso3}] {iso3}.yaml not found, skipping merge.");
        return;
    }

    var lines = File.ReadAllLines(path).ToList();
    var topKey = new Regex(@"^[A-Za-z_][\w]*:\s*$");

    int functionWordsLine = lines.FindIndex(l => l.TrimEnd() == "function_words:");
    int lemmasLine = lines.FindIndex(l => l.TrimEnd() == "lemmas:");
    int verbsLine = lines.FindIndex(l => l.TrimEnd() == "verbs:");
    int properNounsLine = lines.FindIndex(l => l.TrimEnd() == "proper_nouns:");

    // Stopwords must never appear as lemma keys: a function word with a noisy UD
    // lemma (e.g. "on" -> "online") would otherwise survive compression.
    var functionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (functionWordsLine >= 0)
    {
        for (int i = functionWordsLine + 1; i < lines.Count; i++)
        {
            if (topKey.IsMatch(lines[i])) break;
            var t = lines[i];
            if (t.Length == 0 || !char.IsWhiteSpace(t[0])) continue;
            var trimmed = t.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '-') continue;
            var w = Unquote(trimmed[1..].Trim());
            if (w.Length > 0) functionWords.Add(w.ToLowerInvariant());
        }
    }

    // Everything before the first data section (i.e. iso3/iso1/name/function_words) is kept verbatim.
    int sectionStart = new[] { lemmasLine, verbsLine, properNounsLine }.Where(i => i >= 0).DefaultIfEmpty(lines.Count).Min();

    // --- parse existing lemmas (de-duplicates: last definition wins) ---
    var existingLemmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (lemmasLine >= 0)
    {
        for (int i = lemmasLine + 1; i < lines.Count; i++)
        {
            if (topKey.IsMatch(lines[i])) break;
            var t = lines[i];
            if (t.Length == 0 || !char.IsWhiteSpace(t[0])) continue;
            var trimmed = t.Trim();
            if (trimmed.StartsWith("-")) continue;
            int sep = trimmed.IndexOf(':');
            if (sep <= 0) continue;
            var k = Unquote(trimmed[..sep].Trim());
            var v = Unquote(trimmed[(sep + 1)..].Trim());
            if (k.Length > 0 && v.Length > 0) existingLemmas[k] = v;
        }
    }

    // --- parse existing verbs (lemma -> forms), unioned on re-run ---
    var existingVerbs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    if (verbsLine >= 0)
    {
        string? cur = null;
        for (int i = verbsLine + 1; i < lines.Count; i++)
        {
            if (topKey.IsMatch(lines[i])) break;
            var t = lines[i];
            if (t.Length == 0 || !char.IsWhiteSpace(t[0])) continue;
            var trimmed = t.Trim();
            if (trimmed.StartsWith("-"))
            {
                var v = Unquote(trimmed[1..].Trim());
                if (cur != null && v.Length > 0) existingVerbs[cur].Add(v);
            }
            else
            {
                int sep = trimmed.IndexOf(':');
                if (sep <= 0) continue;
                cur = Unquote(trimmed[..sep].Trim());
                if (!existingVerbs.ContainsKey(cur))
                    existingVerbs[cur] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    // --- parse existing proper_nouns (list), unioned on re-run ---
    var existingProperNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (properNounsLine >= 0)
    {
        for (int i = properNounsLine + 1; i < lines.Count; i++)
        {
            if (topKey.IsMatch(lines[i])) break;
            var t = lines[i];
            if (t.Length == 0 || !char.IsWhiteSpace(t[0])) continue;
            var trimmed = t.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '-') continue;
            var w = Unquote(trimmed[1..].Trim());
            if (w.Length > 0) existingProperNouns.Add(w.ToLowerInvariant());
        }
    }

    int keptLemmas = existingLemmas.Count;
    int keptVerbs = existingVerbs.Count;
    int keptProperNouns = existingProperNouns.Count;

    // Merge lemmas: keep existing entries only for forms UD never observed
    // (preserves curated seeds for out-of-UD words); for everything UD saw,
    // UD's verdict wins (a kept lemma, or omission when identity-dominant).
    var mergedLemmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in existingLemmas)
        if (!udSaw.Contains(k)) mergedLemmas[k] = v;
    foreach (var (k, v) in udLemmas) mergedLemmas[k] = v;

    // Drop any lemma key that is a function word (defense-in-depth).
    int removedStopwords = 0;
    foreach (var fw in functionWords)
        if (mergedLemmas.Remove(fw)) removedStopwords++;

    int addedLemmas = mergedLemmas.Count - existingLemmas.Count;

    // Merge verbs: union of forms per lemma.
    var mergedVerbs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var (lemma, forms) in existingVerbs)
        mergedVerbs[lemma] = new HashSet<string>(forms, StringComparer.OrdinalIgnoreCase);
    foreach (var (lemma, forms) in udVerbs)
    {
        if (!mergedVerbs.TryGetValue(lemma, out var set))
            mergedVerbs[lemma] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.UnionWith(forms);
    }
    int addedVerbs = mergedVerbs.Count - existingVerbs.Count;

    // Proper nouns come entirely from UD (no curated source), so regenerate them
    // fresh each run instead of unioning — this lets the high-precision filter drop
    // previously-imported imprecise entries. Never treat a function word as a name.
    var mergedProperNouns = new HashSet<string>(udProperNouns, StringComparer.OrdinalIgnoreCase);
    mergedProperNouns.ExceptWith(functionWords);
    int addedProperNouns = mergedProperNouns.Count - existingProperNouns.Count;

    // --- rebuild file: header + lemmas block + verbs block + proper_nouns block ---
    var result = new List<string>(lines.Take(sectionStart).Select(l => l.TrimEnd()));
    while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);
    result.Add("");

    result.Add("lemmas:");
    foreach (var kv in mergedLemmas.OrderBy(k => k.Key, StringComparer.Ordinal))
        result.Add($"  {Quote(kv.Key)}: {Quote(kv.Value)}");

    result.Add("");
    result.Add("verbs:");
    foreach (var kv in mergedVerbs.OrderBy(k => k.Key, StringComparer.Ordinal))
    {
        if (kv.Value.Count == 0) continue;
        result.Add($"  {Quote(kv.Key)}:");
        foreach (var form in kv.Value.OrderBy(f => f, StringComparer.Ordinal))
            result.Add($"    - {Quote(form)}");
    }

    result.Add("");
    result.Add("proper_nouns:");
    foreach (var name in mergedProperNouns.OrderBy(n => n, StringComparer.Ordinal))
        result.Add($"  - {Quote(name)}");

    File.WriteAllLines(path, result, new UTF8Encoding(false));

    var sizeKB = new FileInfo(path).Length / 1024.0;
    Console.WriteLine($"[{iso3}] lemmas: {keptLemmas}+{addedLemmas}={mergedLemmas.Count} (-{removedStopwords} sw); " +
                      $"verbs: {keptVerbs}+{addedVerbs}={mergedVerbs.Count}; " +
                      $"names: {keptProperNouns}+{addedProperNouns}={mergedProperNouns.Count} ({sizeKB:F0} KB)");
}

static string Quote(string s) =>
    "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

static string Unquote(string s)
{
    if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        return s[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
        return s[1..^1].Replace("''", "'");
    return s;
}
