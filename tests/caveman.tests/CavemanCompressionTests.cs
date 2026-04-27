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
using Catalyst;
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
        public async Task Test_LightCompression_ReducesLength()
        {
            // Nota: Usa il nome esatto del tuo metodo (ApplyNlpLogic o CompressAsync)
            var result = await _compressor.ApplyNlpLogic(SampleInput, Language.Italian, CavemanCompressionLevel.Light);

            Assert.That(result.Length, Is.LessThan(SampleInput.Length));
            // Sostituzione IsFalse -> Assert.That(..., Is.False)
            Assert.That(result.Contains(" se "), Is.False);
            Assert.That(result.Contains(" sui "), Is.False);
        }

        [Test]
        public async Task Test_SemanticCompression_KeepsKeywords()
        {
            var result = await _compressor.ApplyNlpLogic(SampleInput, Language.Italian, CavemanCompressionLevel.Semantic);

            // Correzione errore NUnit2024: usa Does.Contain per le stringhe
            Assert.That(result.ToLower(), Does.Contain("voli"));
            Assert.That(result.ToLower(), Does.Contain("roma"));
            Assert.That(result.ToLower(), Does.Contain("domani"));
        }

        [Test]
        public async Task Test_AggressiveCompression_UsesLemmas()
        {
            string input = "I gatti correvano";

            var result = await _compressor.ApplyNlpLogic(input, Language.Italian, CavemanCompressionLevel.Aggressive);

            // IsTrue -> Assert.That(..., Is.True)
            Assert.That(result.Contains("gatto") || result.Contains("correre"), Is.True);
        }

        [TestCase("", Language.Italian)]
        public async Task Test_EdgeCases_HandleEmptyInput(string input, Language lang)
        {
            var result = await _compressor.ApplyNlpLogic(input, lang, CavemanCompressionLevel.Light);

            // AreEqual -> Assert.That(..., Is.EqualTo(...))
            Assert.That(result, Is.EqualTo(string.Empty));
        }
    }
}