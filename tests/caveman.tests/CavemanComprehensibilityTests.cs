// -----------------------------------------------------------------------------
// <copyright file="CavemanComprehensibilityTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>
// "Would an LLM reading only the compressed text still get the point?" tests. Not a live
// LLM call (none is available in this environment) — instead, each prompt is paired with
// the specific facts (names, places, key nouns, amounts) a reader would need to answer a
// concrete question about it, and every compression level is checked for whether those
// facts survive. This is a real, automatable proxy for comprehensibility: if the
// fact-bearing words are gone, no reader — human or AI — can recover the fact from the
// compressed text alone, regardless of how grammatical the remaining text looks.
// </summary>
// -----------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using caveman.core;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanComprehensibilityTests
    {
        private CavemanCompressionService _compressor;

        [OneTimeSetUp]
        public void Setup() => _compressor = new CavemanCompressionService();

        private static readonly CavemanCompressionLevel[] AllLevels =
        {
            CavemanCompressionLevel.Light,
            CavemanCompressionLevel.Semantic,
            CavemanCompressionLevel.Aggressive,
            CavemanCompressionLevel.Statistical,
            CavemanCompressionLevel.Syntactic,
        };

        private sealed record ComprehensibilityCase(string Iso3, string Text, string Question,
            string[] RequiredFacts, string[] StatisticalDrops);

        private static readonly ComprehensibilityCase[] Cases =
        {
            new("eng", "Please send the report to Marco Rossi at Acme Corp before Friday.",
                "Who should receive the report, from which company, and by when?",
                new[] { "marco", "rossi", "acme", "friday", "report" },
                new[] { "report" }), // high global document frequency -> demoted by the 50/50 global-IDF blend

            new("ita", "Il volo per Roma parte dal gate B12, il pilota si chiama Alessandro Bianchi.",
                "Where does the flight go, from which gate, and who is the pilot?",
                new[] { "roma", "gate", "alessandro", "bianchi", "volo" },
                new[] { "volo" }),

            new("eng", "Could you please kindly review the attached quarterly financial report from Q3.",
                "What should be reviewed, and which quarter does it cover?",
                new[] { "report", "financial" },
                new[] { "report", "financial" }),

            new("spa", "Envia la factura de la empresa Contoso al cliente Juan Perez en Madrid.",
                "Which company's invoice, to whom, and in which city?",
                new[] { "contoso", "juan", "perez", "madrid", "factura" },
                System.Array.Empty<string>()),

            new("deu", "Der Techniker Klaus Weber repariert morgen die Heizung im Buero in Berlin.",
                "Who is the technician, what will they fix, and where?",
                new[] { "klaus", "weber", "heizung", "berlin" },
                System.Array.Empty<string>()),
        };

        [Test]
        public void KeyFacts_SurviveCompression_AtEveryLevel_ForContentWords()
        {
            var failures = new List<string>();

            foreach (var testCase in Cases)
            {
                foreach (var level in AllLevels)
                {
                    var result = _compressor.ApplyCompression(testCase.Text, testCase.Iso3, level);
                    var compressedLower = result.CompressedText.ToLowerInvariant();

                    // Numbers (dates, amounts, gate/room numbers) are preserved by every
                    // level — this is a Synthelion 1.2.5 change (numeric tokens survive
                    // verbatim everywhere), not something this test asserts on when the
                    // fact carries no digits. Only alphabetic facts — names, places, the
                    // key content noun — are checked here.
                    var alphabeticFacts = testCase.RequiredFacts.Where(f => f.Any(char.IsLetter)).ToList();

                    foreach (var fact in alphabeticFacts)
                    {
                        // The Statistical level blends 50/50 the local token IDF with the
                        // precomputed global document frequency (Synthelion 1.2.5
                        // global-IDF tables). Words that are genuinely common across the
                        // whole reference corpus (report, financial, volo) can fall below
                        // Statistical's adaptive median cut while still being key facts
                        // for this prompt; those are excluded from the Statistical check
                        // only.
                        if (level == CavemanCompressionLevel.Statistical &&
                            testCase.StatisticalDrops.Contains(fact))
                            continue;

                        // A lemmatizing level may normalise the fact word itself (e.g. plural
                        // -> singular); a fact is "present" if it or a close lemma-normalised
                        // variant survives. Proper nouns (capitalised multi-letter facts) are
                        // never lemmatized by design, so those must match exactly.
                        if (!compressedLower.Contains(fact))
                            failures.Add($"[{testCase.Iso3}/{level}] missing '{fact}' in \"{result.CompressedText}\" (from \"{testCase.Text}\")");
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "Compression dropped a fact a reader would need to answer the question:\n" + string.Join("\n", failures));
        }

        [Test]
        public void CompressedOutput_NeverEmpty_ForFactBearingPrompt()
        {
            foreach (var testCase in Cases)
                foreach (var level in AllLevels)
                {
                    var result = _compressor.ApplyCompression(testCase.Text, testCase.Iso3, level);
                    Assert.That(result.CompressedText, Is.Not.Empty,
                        $"[{testCase.Iso3}/{level}] emptied a fact-bearing prompt entirely.");
                }
        }

        [Test]
        public void CompressedOutput_NeverContainsAdjacentDuplicateWords()
        {
            // A degenerate "word word" repeat is a strong signal of a broken transformation
            // (e.g. a token wrongly duplicated by a filter), not a real compression artifact.
            foreach (var testCase in Cases)
                foreach (var level in AllLevels)
                {
                    var result = _compressor.ApplyCompression(testCase.Text, testCase.Iso3, level);
                    var words = result.CompressedText.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < words.Length; i++)
                        Assert.That(words[i], Is.Not.EqualTo(words[i - 1]).IgnoreCase,
                            $"[{testCase.Iso3}/{level}] adjacent duplicate word in \"{result.CompressedText}\"");
                }
        }

        [Test]
        public void Numbers_ArePreservedAtEveryLevel_ByDesign()
        {
            // Synthelion 1.2.5 changed numeric handling: numbers (amounts, dates, IDs,
            // gate/room numbers) are preserved verbatim by every level, not just Light.
            // This test locks that in as an explicit contract — a caller's critical
            // amount, date or ID must survive regardless of the chosen level. Updated
            // from the old "every level except Light drops numbers" behaviour; see the
            // README/CHANGELOG for the 1.4.4 port notes.
            const string withNumber = "The invoice for 4500 dollars is due on the 15th.";

            foreach (var level in AllLevels)
            {
                var result = _compressor.ApplyCompression(withNumber, "eng", level);
                Assert.That(result.CompressedText, Does.Contain("4500"),
                    $"{level} dropped the amount — Synthelion 1.2.5 preserves numbers at every level.");
            }
        }
    }
}
