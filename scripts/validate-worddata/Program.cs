// -----------------------------------------------------------------------------
// <copyright file="Program.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Developer script: smoke-test that loads every language and exercises compression.</summary>
// -----------------------------------------------------------------------------
using System.Diagnostics;
using caveman.core;

// Smoke-test: load every supported language with the custom streaming parser,
// verify function_words + lemmas + verbs parse, and that verb forms invert into
// the lemma map used by compression. Reports timings and any anomalies.

var provider = new FunctionWordProvider();
var iso3s = provider.GetAllSupportedIso3().OrderBy(x => x).ToList();

Console.WriteLine($"Supported languages: {iso3s.Count}\n");
Console.WriteLine($"{"iso3",-5} {"fw",7} {"lemmas",8} {"verbs",7} {"load(ms)",9}  notes");

int failures = 0;
var swAll = Stopwatch.StartNew();
foreach (var iso3 in iso3s)
{
    try
    {
        var fw = provider.GetFunctionWords(iso3);

        var sw = Stopwatch.StartNew();
        var data = provider.LoadWordData(iso3);
        sw.Stop();

        int lemmas = data?.lemmas?.Count ?? 0;
        int verbs = data?.verbs?.Count ?? 0;

        var notes = new List<string>();
        if (fw.Count == 0) notes.Add("NO function_words");
        // spot-check verb inversion: a verb form should resolve to its lemma
        if (data?.verbs is { Count: > 0 })
        {
            var sample = data.verbs.First(v => v.Value.Count > 0);
            // verb forms are stored lemma -> [forms]; the form must differ from lemma
            if (!sample.Value.Any(f => !string.Equals(f, sample.Key, StringComparison.OrdinalIgnoreCase)))
                notes.Add("verb forms == lemma?");
        }

        Console.WriteLine($"{iso3,-5} {fw.Count,7} {lemmas,8} {verbs,7} {sw.ElapsedMilliseconds,9}  {string.Join("; ", notes)}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"{iso3,-5} FAILED: {ex.Message}");
    }
}
swAll.Stop();

Console.WriteLine();

// End-to-end compression smoke test on a few languages.
var compressor = new CavemanCompressionService();
var samples = new (string iso3, string text)[]
{
    ("eng", "The researchers were studying the effects of the medication on patients."),
    ("ita", "I ricercatori stavano studiando gli effetti del farmaco sui pazienti."),
    ("deu", "Die Forscher untersuchten die Auswirkungen des Medikaments auf die Patienten."),
    ("rus", "Исследователи изучали влияние лекарства на пациентов в больнице."),
    // proper-noun regression: "Termini" and "Roma" must survive verbatim.
    ("ita", "Vorrei informazioni sui ristoranti economici vicino alla stazione Termini a Roma."),
    // gazetteer: names at sentence start must survive (not just mid-sentence).
    ("ita", "Roma è bella e Milano cresce ogni anno."),
    // gazetteer: German names survive even though German capitalises all nouns.
    ("deu", "Berlin ist groß und München wächst schnell."),
};
foreach (var (iso3, text) in samples)
{
    var r = compressor.ApplyCompression(text, iso3, CavemanCompressionLevel.Aggressive);
    Console.WriteLine($"[{iso3}] {r.OriginalTokens}->{r.CompressedTokens}: {r.CompressedText}");
}

Console.WriteLine($"\nDone. {iso3s.Count - failures}/{iso3s.Count} parsed, {failures} failures. Total load {swAll.ElapsedMilliseconds} ms.");
return failures == 0 ? 0 : 1;
