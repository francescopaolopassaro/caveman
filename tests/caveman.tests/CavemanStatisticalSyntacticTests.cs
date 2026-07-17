// -----------------------------------------------------------------------------
// <copyright file="CavemanStatisticalSyntacticTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>
// Quality/regression tests for the Statistical (TF-IDF) and Syntactic compression
// levels. These levels exist to compress a prompt while staying comprehensible, so the
// tests below assert that guarantee directly: never crash, never return nothing for a
// prompt that had content, never drop the sentence's main verb, and never let a kept
// grammatical word get corrupted by lemmatization.
// </summary>
// -----------------------------------------------------------------------------
using System;
using System.Linq;
using caveman.core;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanStatisticalSyntacticTests
    {
        private CavemanCompressionService _compressor;

        [OneTimeSetUp]
        public void Setup()
        {
            _compressor = new CavemanCompressionService();
        }

        private static readonly CavemanCompressionLevel[] AllLevels =
        {
            CavemanCompressionLevel.Light,
            CavemanCompressionLevel.Semantic,
            CavemanCompressionLevel.Aggressive,
            CavemanCompressionLevel.Statistical,
            CavemanCompressionLevel.Syntactic,
        };

        // ------------------------------------------------------------------
        // Never crash, regardless of level or how degenerate the input is.
        // ------------------------------------------------------------------

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("!!! ??? ...")]
        [TestCase("This is that which it was.")] // all function words, no content
        public void Test_NeverThrows_OnDegenerateInput(string input)
        {
            foreach (var level in AllLevels)
                Assert.DoesNotThrow(() => _compressor.ApplyCompression(input, "eng", level),
                    $"Level {level} threw on input '{input}'");
        }

        [Test]
        public void Test_NeverThrows_OnUnsupportedLanguage()
        {
            // "hat" (Haitian Creole) has no curated word data at all.
            const string input = "Bonjou tout moun, kijan ou ye jodi a.";
            foreach (var level in AllLevels)
                Assert.DoesNotThrow(() => _compressor.ApplyCompression(input, "hat", level));
        }

        // ------------------------------------------------------------------
        // Safety floor: a prompt that had real content must never compress to
        // nothing. This is the concrete failure mode a fully-dropped compression
        // would produce — the worst possible comprehensibility outcome.
        // ------------------------------------------------------------------

        [TestCase(CavemanCompressionLevel.Statistical)]
        [TestCase(CavemanCompressionLevel.Syntactic)]
        [TestCase(CavemanCompressionLevel.Aggressive)]
        public void Test_NeverEmptiesContentBearingInput(CavemanCompressionLevel level)
        {
            // "Vai." ("Go.") is a one-word imperative whose only content word is
            // curated as a generic/filler verb — exactly the shape that used to
            // compress Aggressive mode down to an empty string.
            var result = _compressor.ApplyCompression("Vai.", "ita", level);
            Assert.That(result.CompressedText, Is.Not.Empty,
                $"{level} emptied a content-bearing prompt entirely.");
        }

        [Test]
        public void Test_Statistical_SafetyFloor_PerSentence()
        {
            // Three short sentences; none should vanish even if none of their words
            // individually clears the adaptive TF-IDF threshold.
            var result = _compressor.ApplyCompression(
                "Ciao. Grazie mille. A presto.", "ita", CavemanCompressionLevel.Statistical);
            Assert.That(result.CompressedText, Is.Not.Empty);
        }

        // ------------------------------------------------------------------
        // Regression test: an "-are" suffix in RomanceAdjectiveSuffixes used to
        // misclassify every Italian first-conjugation infinitive verb as an
        // adjective and silently drop it — deleting the sentence's actual
        // instruction. This is the user-supplied example that exposed the bug.
        // ------------------------------------------------------------------

        [TestCase(CavemanCompressionLevel.Aggressive)]
        [TestCase(CavemanCompressionLevel.Statistical)]
        [TestCase(CavemanCompressionLevel.Syntactic)]
        public void Test_MainVerbSurvives_ItalianHedgeSentence(CavemanCompressionLevel level)
        {
            const string input =
                "Ti chiedo cortesemente di analizzare con estrema attenzione questo report finanziario molto dettagliato.";
            var result = _compressor.ApplyCompression(input, "ita", level);

            Assert.That(result.CompressedText.ToLowerInvariant(), Does.Contain("analizzare"),
                $"{level} dropped the sentence's main verb: '{result.CompressedText}'");
            Assert.That(result.CompressedText.ToLowerInvariant(), Does.Contain("report"));
            Assert.That(result.CompressedText.ToLowerInvariant(), Does.Contain("finanziario"));
        }

        [Test]
        public void Test_ItalianDashAreVerbsAreNotTreatedAsAdjectives()
        {
            // A battery of common -are verbs that must never be pruned as
            // "descriptive" (the suffix collision covered eng/spa/fra/por too,
            // but -are is specifically the Italian infinitive ending).
            foreach (var verb in new[] { "analizzare", "parlare", "mandare", "lavorare" })
            {
                var result = _compressor.ApplyCompression($"Devi {verb} questo documento.", "ita",
                    CavemanCompressionLevel.Aggressive);
                Assert.That(result.CompressedText.ToLowerInvariant(), Does.Contain(verb),
                    $"'{verb}' was pruned as if it were a Romance adjective.");
            }
        }

        // ------------------------------------------------------------------
        // Kept function words (Syntactic's "grammatical glue") must survive
        // verbatim, never lemmatized into an unrelated word.
        // ------------------------------------------------------------------

        [Test]
        public void Test_Syntactic_KeepsGrammaticalGlueVerbatim_NotLemmatized()
        {
            var result = _compressor.ApplyCompression("Send the invoice to Marco.", "eng",
                CavemanCompressionLevel.Syntactic);

            // Regression guard: a prior version lemmatized a surviving function word
            // ("to") through the verb/lemma map and it corrupted into "do".
            Assert.That(result.CompressedText, Does.Not.Contain(" do "));
            Assert.That(result.CompressedText, Does.Contain("Send"));
            Assert.That(result.CompressedText, Does.Contain("invoice"));
            Assert.That(result.CompressedText, Does.Contain("Marco"));
        }

        [Test]
        public void Test_Syntactic_PreservesProperNouns()
        {
            var result = _compressor.ApplyCompression(
                "Marco ha inviato il documento a Roma ieri sera.", "ita", CavemanCompressionLevel.Syntactic);
            Assert.That(result.CompressedText, Does.Contain("Marco"));
            Assert.That(result.CompressedText, Does.Contain("Roma"));
        }

        // ------------------------------------------------------------------
        // Statistical: cross-language false positives (the same bug class as the
        // generic-words fix) must not resurface — a common word from another
        // language's standard corpus must not out-score real content.
        // ------------------------------------------------------------------

        [Test]
        public void Test_Statistical_DropsCuratedFunctionAndGenericWords()
        {
            var result = _compressor.ApplyCompression(
                "Il gatto nero dorme sul tavolo della cucina.", "ita", CavemanCompressionLevel.Statistical);
            var text = result.CompressedText.ToLowerInvariant();

            // Pure grammatical glue should never outrank the actual subject/object.
            Assert.That(text, Does.Not.Contain(" il "));
            Assert.That(text, Does.Contain("gatto"));
        }

        // ------------------------------------------------------------------
        // General compression sanity across every level: token count must never
        // increase (a "compressor" that expands the prompt has failed by definition).
        // ------------------------------------------------------------------

        [TestCase("Vorrei sapere se è possibile ricevere informazioni sui voli per Roma domani mattina.", "ita")]
        [TestCase("Could you please kindly review the attached quarterly financial report before tomorrow.", "eng")]
        [TestCase("Quisiera pedirte amablemente que revises con mucho cuidado este informe financiero.", "spa")]
        public void Test_AllLevels_NeverExpandTokenCount(string input, string iso3)
        {
            foreach (var level in AllLevels)
            {
                var result = _compressor.ApplyCompression(input, iso3, level);
                Assert.That(result.CompressedTokens, Is.LessThanOrEqualTo(result.OriginalTokens),
                    $"{level} expanded '{input}' from {result.OriginalTokens} to {result.CompressedTokens} tokens.");
            }
        }

        // ------------------------------------------------------------------
        // Syntactic: POS-gated hedge-clause elision (FunctionWordProvider.GetPosTags,
        // a frequency-baseline tagger built offline from Universal Dependencies). A first
        // attempt at eliding a leading "matrix" clause based on naive verb-form membership
        // broke on verb/preposition homographs and was rolled back; with real POS tags the
        // same idea is safe. These tests lock in that safety.
        // ------------------------------------------------------------------

        [Test]
        public void Test_Syntactic_PosGated_CoordinationIsNeverMistakenForHedgeClause()
        {
            // Two independent, coordinated clauses ("bought X and ate Y") must NEVER be
            // treated as a matrix-verb + hedge-clause pair — both objects must survive.
            var it = _compressor.ApplyCompression("Ho comprato il pane e mangiato la torta.", "ita",
                CavemanCompressionLevel.Syntactic);
            Assert.That(it.CompressedText, Does.Contain("pane"),
                $"Coordination wrongly elided the first clause's object: '{it.CompressedText}'");

            var en = _compressor.ApplyCompression("I bought bread and ate cake.", "eng",
                CavemanCompressionLevel.Syntactic);
            Assert.That(en.CompressedText, Does.Contain("bread"),
                $"Coordination wrongly elided the first clause's object: '{en.CompressedText}'");
            Assert.That(en.CompressedText, Does.Contain("cake"));
        }

        [Test]
        public void Test_Syntactic_PosGated_ElidesHedgeClause_KeepsMainVerbAndObject()
        {
            var result = _compressor.ApplyCompression(
                "Could you please kindly review the attached quarterly financial report before tomorrow.",
                "eng", CavemanCompressionLevel.Syntactic);

            Assert.That(result.CompressedText, Does.Contain("review"));
            Assert.That(result.CompressedText.ToLowerInvariant(), Does.Contain("report"));
        }

        [Test]
        public void Test_Syntactic_PosGated_NeverMisfiresOnPrepositionVerbHomograph()
        {
            // "entro" is the Italian preposition "by/within" far more often than it is the
            // 1st-person verb "I enter" — a naive verb-form lookup used to misdetect it as a
            // second verb and elide the whole preceding clause ("Il cliente aspetta una
            // risposta" was dropped entirely in that failure). With real POS tags "entro" is
            // tagged ADP, so there is only one real VERB in the sentence and no elision
            // fires — the client/subject and the verb must both survive intact. (Whether
            // "entro" itself gets lemma-normalized to "entrare" is a separate, pre-existing
            // lemma-map ambiguity shared by every compression level, not what this guards.)
            var result = _compressor.ApplyCompression(
                "Il cliente aspetta una risposta entro venerdì.", "ita", CavemanCompressionLevel.Syntactic);

            var text = result.CompressedText.ToLowerInvariant();
            Assert.That(text, Does.Contain("cliente"),
                $"The subject clause was wrongly elided as a hedge clause: '{result.CompressedText}'");
            Assert.That(text, Does.Contain("venerdì"));
        }

        [Test]
        public void Test_Syntactic_NoPosData_DoesNotCrash_FallsBackGracefully()
        {
            // Kannada has no mapped UD treebank, so GetPosTags("kan") is empty — elision
            // must silently no-op rather than throw or misbehave.
            Assert.DoesNotThrow(() =>
                _compressor.ApplyCompression("ಇದು ಒಂದು ಪರೀಕ್ಷೆ.", "kan", CavemanCompressionLevel.Syntactic));
        }
    }
}
