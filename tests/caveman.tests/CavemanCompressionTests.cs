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
using caveman.core;
using Mosaik.Core;
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
        public async Task Test_LightCompression_ReducesLength_And_SavesEnergy()
        {
            var result = await _compressor.ApplyNlpLogic(SampleInput, Language.Italian, CavemanCompressionLevel.Light);

            // Verify the text
            Assert.That(result.CompressedText.Length, Is.LessThan(SampleInput.Length));
            Assert.That(result.CompressedText.Contains(" se "), Is.False);
            Assert.That(result.CompressedText.Contains(" sui "), Is.False);

            // Verify the new Green metrics
            Assert.That(result.OriginalTokens, Is.GreaterThan(result.CompressedTokens), "The number of compressed tokens must be lower.");
            Assert.That(result.EstimatedEnergySavedMWh, Is.GreaterThan(0), "The energy savings must be greater than zero.");
            Assert.That(result.EstimatedCO2SavedMg, Is.GreaterThan(0), "The CO2 savings must be calculated.");

            TestContext.WriteLine($"Savings: {result.EstimatedEnergySavedMWh} mWh | Avoided CO2: {result.EstimatedCO2SavedMg} mg");
        }

        [Test]
        public async Task Test_SemanticCompression_KeepsKeywords()
        {
            var result = await _compressor.ApplyNlpLogic(SampleInput, Language.Italian, CavemanCompressionLevel.Semantic);

            string text = result.CompressedText.ToLower();
            Assert.That(text, Does.Contain("voli"));
            Assert.That(text, Does.Contain("roma"));
            Assert.That(text, Does.Contain("domani"));

            // Semantic should save more tokens than Light
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));
        }

        [Test]
        public async Task Test_AggressiveCompression_UsesLemmas()
        {
            string input = "I gatti correvano";

            var result = await _compressor.ApplyNlpLogic(input, Language.Italian, CavemanCompressionLevel.Aggressive);

            string text = result.CompressedText;
            Assert.That(text.Contains("gatto") || text.Contains("correre"), Is.True);
        }

        [TestCase("", Language.Italian)]
        public async Task Test_EdgeCases_HandleEmptyInput(string input, Language lang)
        {
            var result = await _compressor.ApplyNlpLogic(input, lang, CavemanCompressionLevel.Light);

            Assert.That(result.CompressedText, Is.EqualTo(string.Empty));
            Assert.That(result.OriginalTokens, Is.EqualTo(0));
            Assert.That(result.EstimatedEnergySavedMWh, Is.EqualTo(0));
        }
    }
}