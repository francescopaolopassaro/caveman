// -----------------------------------------------------------------------------
// <copyright file="CavemanNegationAndZhTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>
// Regression tests for the 1.4.2 quality pass (ported from Synthelion's Python
// test_negation_and_zh.py, found via real-text quality review):
//   - Negation particles ("non"/"not"/"ne...pas"/"no"/"nicht"/"não"/"不") were being
//     stripped as ordinary function words at every compression level, silently
//     inverting sentence meaning.
//   - Italian "subito" (adverb, "immediately") was mis-lemmatised to "subire"
//     (verb, "to undergo") via a UD homograph-contaminated lemma entry.
//   - Chinese had no word segmentation at all, so compression and language
//     detection both silently no-op'd for Chinese beyond punctuation stripping.
//   - Language-detection data contamination: "john" in the Italian index and
//     "ha"/"e" in the French index, and "ate" doubling as a Portuguese
//     exclusive marker collided with English's "ate".
// </summary>
// -----------------------------------------------------------------------------
using caveman.core;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanNegationAndZhTests
    {
        private CavemanCompressionService _compressor;
        private CavemanLanguageDetector _detector;

        [OneTimeSetUp]
        public void Setup()
        {
            _compressor = new CavemanCompressionService();
            _detector = new CavemanLanguageDetector();
        }

        private string Compressed(string text, string iso3, CavemanCompressionLevel level) =>
            _compressor.ApplyCompression(text, iso3, level).CompressedText;

        [Test]
        public void ItalianNonSurvivesAllLevels()
        {
            const string text = "quando nelle istituzioni non c'era alcuna sensibilità";
            foreach (var level in new[] { CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive })
            {
                Assert.That(Compressed(text, "ita", level).ToLowerInvariant(), Does.Contain("non"), level.ToString());
            }
        }

        [Test]
        public void EnglishNotSurvivesAllLevels()
        {
            const string text = "I do not think this is correct and you should not do that";
            foreach (var level in new[] { CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive })
            {
                Assert.That(Compressed(text, "eng", level).ToLowerInvariant(), Does.Contain("not"), level.ToString());
            }
        }

        [Test]
        public void FrenchNegationSurvives()
        {
            var outText = Compressed("Je ne sais pas si cela est vrai", "fra", CavemanCompressionLevel.Semantic).ToLowerInvariant();
            Assert.That(outText, Does.Contain("ne"));
            Assert.That(outText, Does.Contain("pas"));
        }

        [Test]
        public void SpanishNoSurvives()
        {
            var outText = Compressed("No creo que esto sea correcto", "spa", CavemanCompressionLevel.Aggressive).ToLowerInvariant();
            Assert.That(outText, Does.Contain("no"));
        }

        [Test]
        public void GermanNichtSurvives()
        {
            var outText = Compressed("Ich weiss nicht ob das richtig ist", "deu", CavemanCompressionLevel.Aggressive).ToLowerInvariant();
            Assert.That(outText, Does.Contain("nicht"));
        }

        [Test]
        public void PortugueseNegationSurvives()
        {
            var outText = Compressed("Não acho que isso seja correto", "por", CavemanCompressionLevel.Semantic).ToLowerInvariant();
            Assert.That(outText, Does.Contain("não").Or.Contain("nao"));
        }

        [Test]
        public void ChineseBuSurvives()
        {
            const string text = "我不喜欢这个方法，因为它没有考虑到否定词。";
            foreach (var level in new[] { CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive })
            {
                Assert.That(Compressed(text, "zho", level), Does.Contain("不"), level.ToString());
            }
        }

        [Test]
        public void ChineseNegationCompoundSurvivesAggressive()
        {
            // "不是" ("is not") is itself a dictionary word the segmenter merges into one
            // token -- must still be recognised as negation-protected, not just bare "不".
            var outText = Compressed("别管他，这不是我们的问题。", "zho", CavemanCompressionLevel.Aggressive);
            Assert.That(outText, Does.Contain("不是"));
        }

        [Test]
        public void SubitoNotLemmatisedToSubire()
        {
            var outText = Compressed(
                "In inglese invece il nome fece riferimento fin da subito agli human rights",
                "ita", CavemanCompressionLevel.Semantic).ToLowerInvariant();
            Assert.That(outText, Does.Contain("subito"));
            Assert.That(outText, Does.Not.Contain("subire"));
        }

        [Test]
        public void SubitoStableAcrossLevels()
        {
            const string text = "Fin da subito abbiamo capito il problema.";
            foreach (var level in new[] { CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive })
            {
                Assert.That(Compressed(text, "ita", level).ToLowerInvariant(), Does.Not.Contain("subire"), level.ToString());
            }
        }

        [Test]
        public void DetectsChineseNotEnglish()
        {
            const string text = "这个系统不支持中文的否定词处理，所以我们需要检查一下。";
            Assert.That(_detector.Detect(text), Is.EqualTo("zho"));
        }

        [Test]
        public void ChineseCompressionActuallySegmentsWords()
        {
            var outText = Compressed("我们不能接受这个方案，因为它不安全。", "zho", CavemanCompressionLevel.Semantic);
            // more than one token in the output proves segmentation happened (the pre-fix
            // behaviour collapsed the whole sentence into one blob).
            Assert.That(outText.Split(' ').Length, Is.GreaterThan(1));
        }

        [Test]
        public void MarcoSentenceDetectsItalianNotFrench()
        {
            Assert.That(_detector.Detect("Marco ha comprato il pane e ha mangiato la torta."), Is.EqualTo("ita"));
        }

        [Test]
        public void JohnSentenceDetectsEnglishNotItalian()
        {
            Assert.That(_detector.Detect("John bought bread and ate cake yesterday."), Is.EqualTo("eng"));
        }
    }
}
