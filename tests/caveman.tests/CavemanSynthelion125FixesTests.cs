// -----------------------------------------------------------------------------
// <copyright file="CavemanSynthelion125FixesTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution). See the LICENSE file.
// </copyright>
// <summary>Regression tests for the Synthelion 1.2.5 fixes ported in Caveman 1.4.5: structured values
// (IPs, versions, dates, URLs, e-mails) surviving every compression level, and Chinese/Japanese output
// no longer being longer than its input.</summary>
// -----------------------------------------------------------------------------
using caveman.core;
using caveman.core.entities;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanSynthelion125FixesTests
    {
        private CavemanCompressionService _compressor = null!;

        [OneTimeSetUp]
        public void Setup() => _compressor = new CavemanCompressionService();

        private static readonly CavemanCompressionLevel[] AllLevels =
        {
            CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive,
            CavemanCompressionLevel.Statistical, CavemanCompressionLevel.Syntactic,
        };

        // Every structured value must come through verbatim at every level - previously '.' and ':' were
        // standalone punctuation, so "127.0.0.1" was split into fragments and dropped above Light.
        private static readonly string[] Values =
        {
            "127.0.0.1", "192.168.1.10:8080", "10.0.0.0/24", "2026-10-11", "2026-10-11T10:30:00Z", "v2.3.1", "1.2.3",
            "https://example.com/path?q=1&r=2", "mario.rossi@example.com", "1,234.56", "99.50%",
        };

        [Test]
        public void StructuredValues_SurviveEveryLevel_InEnglish(
            [ValueSource(nameof(Values))] string value,
            [ValueSource(nameof(AllLevels))] CavemanCompressionLevel level)
        {
            var text = $"Please make sure that the server at {value} is reachable before the deployment starts tomorrow morning.";
            var r = _compressor.ApplyCompression(text, "eng", level);
            Assert.That(r.CompressedText, Does.Contain(value), $"{level}: {r.CompressedText}");
        }

        [Test]
        public void StructuredValues_SurviveEveryLevel_InItalian(
            [ValueSource(nameof(Values))] string value,
            [ValueSource(nameof(AllLevels))] CavemanCompressionLevel level)
        {
            var text = $"Verifica che il server all'indirizzo {value} sia raggiungibile prima che inizi la distribuzione di domani mattina.";
            var r = _compressor.ApplyCompression(text, "ita", level);
            Assert.That(r.CompressedText, Does.Contain(value), $"{level}: {r.CompressedText}");
        }

        [Test]
        public void TrailingSentencePunctuation_IsNotAbsorbedIntoTheValue()
        {
            var r = _compressor.ApplyCompression("The host is 10.0.0.1.", "eng", CavemanCompressionLevel.Light);
            Assert.That(r.CompressedText, Does.Contain("10.0.0.1"));
            Assert.That(r.CompressedText, Does.Not.Contain("10.0.0.1."), "the sentence dot is not part of the address");
        }

        [Test]
        public void ValueAttachedToChineseText_IsKeptWhole()
        {
            var r = _compressor.ApplyCompression("服务器地址是127.0.0.1请检查", "zho", CavemanCompressionLevel.Semantic);
            Assert.That(r.CompressedText, Does.Contain("127.0.0.1"));
        }

        [TestCase("zho", "我昨天在北京的一家很大的商店里买了一本非常有意思的书")]
        [TestCase("jpn", "私は昨日東京の大きな店でとても面白い本を買いました")]
        public void CjkOutput_IsNeverLongerThanInput(string iso3, string text)
        {
            var r = _compressor.ApplyCompression(text, iso3, CavemanCompressionLevel.Semantic);
            Assert.That(r.CompressedText.Length, Is.LessThanOrEqualTo(text.Length), r.CompressedText);
            Assert.That(r.CompressedText, Does.Not.Contain("  "));
        }

        [Test]
        public void CjkRun_NextToALatinValue_KeepsABoundary()
        {
            var r = _compressor.ApplyCompression("服务器版本是v2.3.1请升级", "zho", CavemanCompressionLevel.Semantic);
            Assert.That(r.CompressedText, Does.Contain(" v2.3.1").Or.StartWith("v2.3.1"), r.CompressedText);
        }
    }
}
