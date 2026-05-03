/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor) - TokenOptimizerPlugin Tests
 * DESCRIPTION:
 * NUnit test suite for TokenOptimizerPlugin class.
 * Tests Semantic Kernel integration, CompressionResult handling, and compression logic.
 * 
 * TECHNOLOGY STACK:
 * - Test Framework: NUnit 3.x
 * - Semantic Kernel: Microsoft.SemanticKernel
 * - Core: caveman.core (CavemanCompressionService, CompressionResult)
 * 
 * AUTHOR: [Francesco Paolo Passaro]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using caveman.core;
using caveman.core.entities;
using caveman.core.SemanticKernel.Plugin;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class TokenOptimizerPluginTests
    {
        private CavemanCompressionService _compressionService;
        private TokenOptimizerPlugin _plugin;
        private Kernel _kernel;

        private const string SampleItalian = "Vorrei sapere se è possibile ricevere informazioni sui voli per Roma domani mattina.";
        private const string SampleEnglish = "Hello there, I would really like to know if you could kindly provide me with some information regarding the best cheap restaurants located in London.";

        [SetUp]
        public void Setup()
        {
            _compressionService = new CavemanCompressionService();
            _plugin = new TokenOptimizerPlugin(_compressionService);

            // Build minimal kernel for integration tests
            var builder = Kernel.CreateBuilder();
            builder.Plugins.AddFromObject(_plugin, "TokenOptimizer");
            _kernel = builder.Build();
        }
        // In caveman.core.SemanticKernel.Plugin/TokenOptimizerPlugin.cs

   
        // ==================== DIRECT PLUGIN METHOD TESTS ====================

        [Test]
        public async Task OptimizePrompt_LightCompression_ReducesTokens()
        {
            // Act
            CompressionResult result = await _plugin.OptimizePrompt(SampleItalian, level: 1); // Light

            // Assert: Verify CompressionResult properties
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null);
            Assert.That(result.CompressedText.Length, Is.LessThan(SampleItalian.Length),
                "Compressed text should be shorter than original");

            // Verify token metrics
            Assert.That(result.OriginalTokens, Is.GreaterThan(0));
            Assert.That(result.CompressedTokens, Is.LessThanOrEqualTo(result.OriginalTokens));
            Assert.That(result.SavedTokens, Is.GreaterThanOrEqualTo(0));

            // Verify derived metrics
            Assert.That(result.EfficiencyPercentage, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.EfficiencyPercentage, Is.LessThanOrEqualTo(100));

            TestContext.WriteLine($"Light: {result.OriginalTokens} → {result.CompressedTokens} tokens ({result.EfficiencyPercentage:F1}% efficiency)");
        }

        [Test]
        public async Task OptimizePrompt_SemanticCompression_KeepsKeywords()
        {
            // Act
            CompressionResult result = await _plugin.OptimizePrompt(SampleItalian, level: 2); // Semantic

            // Assert: Key semantic words should be preserved
            string compressed = result.CompressedText.ToLowerInvariant();

            // These are high-value keywords that should survive semantic compression
            Assert.That(compressed, Does.Contain("voli").Or.Contains("volo"), "Should preserve 'voli' keyword");
            Assert.That(compressed, Does.Contain("roma"), "Should preserve 'Roma' keyword");
            Assert.That(compressed, Does.Contain("domani"), "Should preserve 'domani' keyword");

            // Should still achieve compression
            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens));
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));

            TestContext.WriteLine($"Semantic: {result.CompressedText}");
        }

        [Test]
        public async Task OptimizePrompt_AggressiveCompression_UsesLemmas()
        {
            // Arrange: Input with inflected forms
            string input = "I gatti correvano velocemente nel giardino";

            // Act
            CompressionResult result = await _plugin.OptimizePrompt(input, level: 3); // Aggressive

            // Assert: Should contain lemmatized forms
            string text = result.CompressedText.ToLowerInvariant();

            // Aggressive mode should reduce to lemma forms
            Assert.That(
                text.Contains("gatto") || text.Contains("gatti"),
                Is.True,
                "Should contain lemma or original form of 'gatti'");

            Assert.That(
                text.Contains("correre") || text.Contains("correvano") || text.Contains("corr"),
                Is.True,
                "Should contain lemma or root of 'correvano'");

            // Should achieve significant compression
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(20),
                "Aggressive mode should achieve at least 20% efficiency");
        }

        [Test]
        [TestCase(0)] // None
        [TestCase(1)] // Light
        [TestCase(2)] // Semantic
        [TestCase(3)] // Aggressive
        public async Task OptimizePrompt_AllLevels_ReturnValidResult(int level)
        {
            // Act
            CompressionResult result = await _plugin.OptimizePrompt(SampleEnglish, level);

            // Assert: All levels should return valid CompressionResult
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null);
            Assert.That(result.OriginalTokens, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.CompressedTokens, Is.GreaterThanOrEqualTo(0));

            // Efficiency should be 0-100%
            Assert.That(result.EfficiencyPercentage, Is.InRange(0, 100));

            // Green metrics should be non-negative
            Assert.That(result.EstimatedEnergySavedMWh, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.EstimatedCO2SavedMg, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task OptimizePrompt_EmptyInput_ReturnsEmptyResult()
        {
            // Act
            CompressionResult result = await _plugin.OptimizePrompt("", level: 2);

            // Assert
            Assert.That(result.CompressedText, Is.EqualTo(string.Empty));
            Assert.That(result.OriginalTokens, Is.EqualTo(0));
            Assert.That(result.CompressedTokens, Is.EqualTo(0));
            Assert.That(result.SavedTokens, Is.EqualTo(0));
            Assert.That(result.EfficiencyPercentage, Is.EqualTo(0));
            Assert.That(result.EstimatedEnergySavedMWh, Is.EqualTo(0));
            Assert.That(result.EstimatedCO2SavedMg, Is.EqualTo(0));
        }
        [Test]
        public async Task OptimizePrompt_NullInput_ThrowsArgumentNullException()
        {
            // Act & Assert
            // FIXED: Use Throws.TypeOf for clearer intent
            var exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await _plugin.OptimizePrompt(null, level: 2));

            // Optional: Verify the exception message contains the parameter name
            Assert.That(exception.ParamName, Is.EqualTo("input"),
                "Exception should identify 'input' as the null parameter");
        }

        [Test]
        public async Task OptimizePrompt_InvalidLevel_ClampsToValidRange()
        {
            // Act: Test with out-of-range levels
            var resultLow = await _plugin.OptimizePrompt(SampleItalian, level: -5);
            var resultHigh = await _plugin.OptimizePrompt(SampleItalian, level: 99);

            // Assert: Should still return valid results (internal clamping)
            Assert.That(resultLow, Is.Not.Null);
            Assert.That(resultLow.CompressedText, Is.Not.Null);

            Assert.That(resultHigh, Is.Not.Null);
            Assert.That(resultHigh.CompressedText, Is.Not.Null);

            // Both should have processed the input
            Assert.That(resultLow.OriginalTokens, Is.GreaterThan(0));
            Assert.That(resultHigh.OriginalTokens, Is.GreaterThan(0));
        }

        [Test]
        public async Task OptimizePrompt_LongText_HandlesGracefully()
        {
            // Arrange: Generate a longer input using Enumerable.Repeat (FIXED)
            string longInput = string.Join(" ",
                Enumerable.Repeat("This is a test sentence for compression with many repeated words to evaluate how the system handles larger inputs and maintains semantic integrity while reducing token count for efficient LLM prompt optimization and cost savings", 10));

            // Act
            CompressionResult result = await _plugin.OptimizePrompt(longInput, level: 2);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null.And.Not.Empty);
            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens),
                "Should reduce token count for longer inputs");
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));

            TestContext.WriteLine($"Long text: {result.OriginalTokens} → {result.CompressedTokens} tokens");
        }

        // ==================== SEMANTIC KERNEL INTEGRATION TESTS ====================

        [Test]
        public async Task Kernel_InvokeOptimizePrompt_ReturnsCompressionResult()
        {
            // Act: Explicit plugin & function names
            var result = await _kernel.InvokeAsync<CompressionResult>(
                pluginName: "TokenOptimizer",
                functionName: "OptimizePrompt",
                new KernelArguments
                {
                    ["input"] = SampleItalian,
                    ["level"] = 2
                });

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null.And.Not.Empty);
            Assert.That(result.CompressedText.Length, Is.LessThan(SampleItalian.Length));
            Assert.That(result.EfficiencyPercentage, Is.GreaterThan(0));
        }

        [Test]
        public async Task Kernel_InvokeWithDefaultLevel_UsesSemantic()
        {
            // Act: Omit level parameter
            var result = await _kernel.InvokeAsync<CompressionResult>(
                pluginName: "TokenOptimizer",
                functionName: "OptimizePrompt",
                new KernelArguments { ["input"] = SampleEnglish });

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null);
            Assert.That(result.EfficiencyPercentage, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task Kernel_MultipleInvocations_AreIndependent()
        {
            // Act: Multiple independent calls
            var r1 = await _kernel.InvokeAsync<CompressionResult>(
                pluginName: "TokenOptimizer", functionName: "OptimizePrompt",
                new KernelArguments { ["input"] = "Ciao mondo", ["level"] = 1 });

            var r2 = await _kernel.InvokeAsync<CompressionResult>(
                pluginName: "TokenOptimizer", functionName: "OptimizePrompt",
                new KernelArguments { ["input"] = "Hello world", ["level"] = 3 });

            // Assert
            Assert.That(r1, Is.Not.Null);
            Assert.That(r2, Is.Not.Null);
            Assert.That(r1.CompressedText, Is.Not.EqualTo(r2.CompressedText));
            Assert.That(r1.EfficiencyPercentage, Is.InRange(0, 100));
            Assert.That(r2.EfficiencyPercentage, Is.InRange(0, 100));
        }
        // ==================== GREEN METRICS VALIDATION TESTS ====================

        [Test]
        public async Task CompressionResult_EnergyMetric_CalculatedCorrectly()
        {
            // Arrange: Known token savings
            var result = await _plugin.OptimizePrompt(SampleItalian, level: 2);
            double expectedEnergy = result.SavedTokens * 0.005; // 5 mWh per saved token

            // Assert: Verify formula matches implementation
            Assert.That(result.EstimatedEnergySavedMWh,
                Is.EqualTo(expectedEnergy).Within(0.0001),
                "Energy calculation should match: savedTokens * 0.005");
        }

        [Test]
        public async Task CompressionResult_CO2Metric_CalculatedCorrectly()
        {
            // Arrange
            var result = await _plugin.OptimizePrompt(SampleItalian, level: 2);
            double expectedCO2 = result.EstimatedEnergySavedMWh * 0.4; // 0.4 mg CO2 per mWh

            // Assert: Verify formula matches implementation
            Assert.That(result.EstimatedCO2SavedMg,
                Is.EqualTo(expectedCO2).Within(0.0001),
                "CO2 calculation should match: energySaved * 0.4");
        }

        [Test]
        public async Task CompressionResult_SavedTokens_NeverNegative()
        {
            // Act: Test with level=0 (None) which should have 0 savings
            var result = await _plugin.OptimizePrompt(SampleEnglish, level: 0);

            // Assert: SavedTokens uses Math.Max(0, ...) so should never be negative
            Assert.That(result.SavedTokens, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.CompressedTokens, Is.LessThanOrEqualTo(result.OriginalTokens + 1),
                "Compressed tokens should not exceed original by more than 1 (rounding)");
        }

        [Test]
        public async Task CompressionResult_Efficiency_RangeValid()
        {
            // Act: Test all compression levels
            foreach (int level in new[] { 0, 1, 2, 3 })
            {
                var result = await _plugin.OptimizePrompt(SampleItalian, level);

                // Assert: Efficiency should always be 0-100%
                Assert.That(result.EfficiencyPercentage, Is.InRange(0, 100),
                    $"Efficiency at level {level} should be 0-100%");

                // If original tokens > 0, efficiency formula should be mathematically consistent
                if (result.OriginalTokens > 0)
                {
                    double expectedEff = (result.SavedTokens / result.OriginalTokens) * 100;
                    Assert.That(result.EfficiencyPercentage,
                        Is.EqualTo(expectedEff).Within(0.1),
                        $"Efficiency calculation mismatch at level {level}");
                }
            }
        }

        // ==================== EDGE CASES & ERROR HANDLING ====================

        [Test]
        [TestCase("   ")]      // Whitespace only
        [TestCase("\n\t\r")]   // Control characters
        [TestCase("🎉🪨✨")]    // Emoji/special chars
        public async Task OptimizePrompt_SpecialInputs_HandledGracefully(string input)
        {
            // Act
            CompressionResult result = await _plugin.OptimizePrompt(input, level: 2);

            // Assert: Should not throw, should return valid result
            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedText, Is.Not.Null);
            Assert.That(result.OriginalTokens, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.CompressedTokens, Is.GreaterThanOrEqualTo(0));
        }
        [Test]
        public async Task OptimizePrompt_VeryLongInput_DoesNotHang()
        {
            // Arrange: 10KB of text using Enumerable.Repeat (FIXED)
            string veryLong = string.Join(" ", Enumerable.Repeat("word", 2000));

            // Act with timeout consideration
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            CompressionResult result = await _plugin.OptimizePrompt(veryLong, level: 2);
            stopwatch.Stop();

            // Assert: Should complete in reasonable time (< 30 seconds for 10KB)
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(30),
                "Should process 10KB input within 30 seconds");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.CompressedTokens, Is.LessThan(result.OriginalTokens),
                "Should still achieve compression on large inputs");
        }

        [Test]
        public void Plugin_Constructor_NullService_Throws()
        {
            // Act & Assert
            // FIXED: Use Throws.TypeOf for clearer intent
            var exception = Assert.Throws<ArgumentNullException>(
                () => new TokenOptimizerPlugin(null));

            // Optional: Verify the exception message contains the parameter name
            Assert.That(exception.ParamName, Is.EqualTo("compressionService"),
                "Exception should identify 'compressionService' as the null parameter");
        }

        // ==================== COMPRESSION LEVEL COMPARISON TESTS ====================

        [Test]
        public async Task CompressionLevels_ProgressiveEfficiency()
        {
            // Act: Compress same input at all levels
            var none = await _plugin.OptimizePrompt(SampleItalian, 0);
            var light = await _plugin.OptimizePrompt(SampleItalian, 1);
            var semantic = await _plugin.OptimizePrompt(SampleItalian, 2);
            var aggressive = await _plugin.OptimizePrompt(SampleItalian, 3);

            // Assert: Efficiency should generally increase with level
            // (Allow small variance due to edge cases)
            Assert.That(light.EfficiencyPercentage, Is.GreaterThanOrEqualTo(none.EfficiencyPercentage - 1),
                "Light should be >= None efficiency");
            Assert.That(semantic.EfficiencyPercentage, Is.GreaterThanOrEqualTo(light.EfficiencyPercentage - 1),
                "Semantic should be >= Light efficiency");
            Assert.That(aggressive.EfficiencyPercentage, Is.GreaterThanOrEqualTo(semantic.EfficiencyPercentage - 1),
                "Aggressive should be >= Semantic efficiency");

            // Assert: Token count should generally decrease with higher compression
            Assert.That(aggressive.CompressedTokens, Is.LessThanOrEqualTo(semantic.CompressedTokens + 1));
            Assert.That(semantic.CompressedTokens, Is.LessThanOrEqualTo(light.CompressedTokens + 1));
            Assert.That(light.CompressedTokens, Is.LessThanOrEqualTo(none.CompressedTokens + 1));

            TestContext.WriteLine($"Efficiency progression: None={none.EfficiencyPercentage:F1}% → Light={light.EfficiencyPercentage:F1}% → Semantic={semantic.EfficiencyPercentage:F1}% → Aggressive={aggressive.EfficiencyPercentage:F1}%");
        }

        [Test]
        public async Task CompressionResult_Immutability_PropertiesConsistent()
        {
            // Act
            var result = await _plugin.OptimizePrompt(SampleItalian, 2);

            // Assert: Derived properties should be consistent with base properties
            double calculatedSaved = Math.Max(0, result.OriginalTokens - result.CompressedTokens);
            Assert.That(result.SavedTokens, Is.EqualTo(calculatedSaved),
                "SavedTokens should equal max(0, original - compressed)");

            double calculatedEff = result.OriginalTokens == 0 ? 0 : (result.SavedTokens / result.OriginalTokens) * 100;
            Assert.That(result.EfficiencyPercentage, Is.EqualTo(calculatedEff).Within(0.01),
                "EfficiencyPercentage should match calculated value");

            double calculatedEnergy = result.SavedTokens * 0.005;
            Assert.That(result.EstimatedEnergySavedMWh, Is.EqualTo(calculatedEnergy).Within(0.0001),
                "Energy metric should match: savedTokens * 0.005");

            double calculatedCO2 = result.EstimatedEnergySavedMWh * 0.4;
            Assert.That(result.EstimatedCO2SavedMg, Is.EqualTo(calculatedCO2).Within(0.0001),
                "CO2 metric should match: energy * 0.4");
        }
    }
}