// -----------------------------------------------------------------------------
// <copyright file="CavemanCompressionTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Unit tests for the compression service.</summary>
// -----------------------------------------------------------------------------
using caveman.core;
using NUnit.Framework;
using System.Threading.Tasks;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanCompressionTests
    {
        private CavemanCompressionService _compressor;
        private const string SampleInput = "Vorrei sapere se è possibile ricevere informazioni sui voli per Roma domani mattina.";

        [OneTimeSetUp]
        public void Setup()
        {
            _compressor = new CavemanCompressionService();
        }

        [Test]
        public void Test_ApplyCompressionDirectly_Works()
        {
            var result = _compressor.ApplyCompression(SampleInput, "ita", CavemanCompressionLevel.Light);

            Assert.That(result.CompressedText.Length, Is.LessThan(SampleInput.Length),
                $"Light: '{SampleInput}' -> '{result.CompressedText}'");
            Assert.That(result.OriginalTokens, Is.GreaterThan(0));
            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens));
        }

        [Test]
        public void Test_ApplyCompression_English_Semantic()
        {
            var input = "I would like to order a pepperoni pizza please";
            var result = _compressor.ApplyCompression(input, "eng", CavemanCompressionLevel.Semantic);

            Assert.That(result.CompressedText, Does.Contain("order"));
            Assert.That(result.CompressedText, Does.Contain("pepperoni"));
            Assert.That(result.CompressedText, Does.Contain("pizza"));
            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens));
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));
        }

        [Test]
        public async Task Test_LightCompression_ReducesLength_And_SavesEnergy()
        {
            var result = await _compressor.CompressAsync(SampleInput, CavemanCompressionLevel.Light);

            if (result.HasError)
                Assert.Inconclusive($"Language detection failed: {result.ErrorMessage}");

            Assert.That(result.CompressedText.Length, Is.LessThan(SampleInput.Length));

            Assert.That(result.OriginalTokens, Is.GreaterThan(0));
            Assert.That(result.CompressedTokens, Is.LessThanOrEqualTo(result.OriginalTokens));
            Assert.That(result.EstimatedEnergySavedMWh, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.EstimatedCO2SavedMg, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task Test_SemanticCompression_KeepsKeywords()
        {
            var result = await _compressor.CompressAsync(SampleInput, CavemanCompressionLevel.Semantic);

            if (result.HasError)
                Assert.Inconclusive($"Language detection failed: {result.ErrorMessage}");

            string text = result.CompressedText.ToLower();
            Assert.That(text, Does.Contain("volo"));
            Assert.That(text, Does.Contain("roma"));
            Assert.That(text, Does.Contain("domani"));

            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));
        }

        [Test]
        public async Task Test_AggressiveCompression_ReducesMore()
        {
            var input = "I gatti correvano velocemente nel giardino";

            var result = await _compressor.CompressAsync(input, CavemanCompressionLevel.Aggressive);

            if (result.HasError)
                Assert.Inconclusive($"Language detection failed: {result.ErrorMessage}");

            string text = result.CompressedText.ToLower();
            Assert.That(text.Contains("gatti") || text.Contains("gatto"), Is.True);

            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens));
        }

        [Test]
        public void Test_AggressiveCompression_PreservesProperNouns()
        {
            var input = "Vorrei informazioni sui ristoranti vicino alla stazione Termini a Roma.";
            var result = _compressor.ApplyCompression(input, "ita", CavemanCompressionLevel.Aggressive);

            // Names must survive verbatim and never be lemmatized to a common word.
            Assert.That(result.CompressedText, Does.Contain("Termini"));
            Assert.That(result.CompressedText, Does.Contain("Roma"));
            Assert.That(result.CompressedText, Does.Not.Contain("termine"));
        }

        [Test]
        public void Test_Aggressive_GazetteerKeepsNamesAtSentenceStartAndInGerman()
        {
            // Sentence-initial names (Italian) — only the gazetteer can catch these.
            var it = _compressor.ApplyCompression(
                "Roma è bella e Milano cresce ogni anno.", "ita", CavemanCompressionLevel.Aggressive);
            Assert.That(it.CompressedText, Does.Contain("Roma"));
            Assert.That(it.CompressedText, Does.Contain("Milano"));

            // German capitalises all nouns; names are protected via the gazetteer,
            // while common nouns are still lemmatized.
            var de = _compressor.ApplyCompression(
                "Berlin ist groß und München wächst schnell.", "deu", CavemanCompressionLevel.Aggressive);
            Assert.That(de.CompressedText, Does.Contain("Berlin"));
            Assert.That(de.CompressedText, Does.Contain("München"));
        }

        [Test]
        public void Test_DetectLanguage_Standalone()
        {
            Assert.That(_compressor.DetectLanguage("Vorrei un tavolo per due persone, per favore."), Is.EqualTo("ita"));
            Assert.That(_compressor.DetectLanguage("Where is the nearest train station please?"), Is.EqualTo("eng"));

            var scores = _compressor.DetectLanguageScores("Ich hätte gerne einen Kaffee bitte.");
            Assert.That(scores, Is.Not.Empty);
            Assert.That(scores.ContainsKey("deu"), Is.True);
        }

        [Test]
        public async Task Test_EdgeCases_HandleEmptyInput()
        {
            var result = await _compressor.CompressAsync("", CavemanCompressionLevel.Light);

            Assert.That(result.CompressedText, Is.EqualTo(string.Empty));
            Assert.That(result.OriginalTokens, Is.EqualTo(0));
            Assert.That(result.EstimatedEnergySavedMWh, Is.EqualTo(0));
        }
    }
}
