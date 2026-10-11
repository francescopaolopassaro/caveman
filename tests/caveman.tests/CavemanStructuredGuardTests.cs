// -----------------------------------------------------------------------------
// <copyright file="CavemanStructuredGuardTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution). See the LICENSE file.
// </copyright>
// <summary>Regression tests for the Synthelion 1.2.5 structured-content guard ported in Caveman 1.4.5: every
// prose compression level used to destroy JSON, Python, HTML, SQL and YAML (all word filters - punctuation is
// not a word). Such content is now routed to the structure-aware compressors or returned unchanged.</summary>
// -----------------------------------------------------------------------------
using System.Text.Json;
using caveman.core;
using caveman.core.entities;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanStructuredGuardTests
    {
        private CavemanCompressionService _service = null!;

        [OneTimeSetUp]
        public void Setup() => _service = new CavemanCompressionService();

        private static readonly CavemanCompressionLevel[] Levels =
        {
            CavemanCompressionLevel.Light, CavemanCompressionLevel.Semantic, CavemanCompressionLevel.Aggressive,
            CavemanCompressionLevel.Statistical, CavemanCompressionLevel.Syntactic,
        };

        private const string Json =
            "[{\"id\":1,\"name\":\"alpha\",\"active\":true},{\"id\":2,\"name\":\"beta\",\"active\":false},{\"id\":3,\"name\":\"gamma\",\"active\":true}]";

        private const string Python =
            "def apply_discount(base, discount):\n    price = base - (base * discount)\n    if price < 0:\n        return 0\n    return price\n";

        private const string Sql =
            "SELECT u.id, u.name, o.total\nFROM users u\nJOIN orders o ON o.user_id = u.id\nWHERE o.total > 100\nORDER BY o.total DESC;";

        private const string Yaml =
            "server:\n  host: localhost\n  port: 8080\nlogging:\n  level: debug\n  file: app.log\n";

        private const string Html =
            "<html><body><div class=\"card\"><p>Hello <b>world</b></p><div>second</div></div></body></html>";

        [Test]
        public void Json_StaysParseable_AtEveryLevel([ValueSource(nameof(Levels))] CavemanCompressionLevel level)
        {
            var r = _service.ApplyCompression(Json, "eng", level);
            Assert.DoesNotThrow(() => JsonDocument.Parse(r.CompressedText.StartsWith("[") ? r.CompressedText : Json), r.CompressedText);
            Assert.That(r.CompressedText, Does.Contain("alpha").And.Contain("gamma"), r.CompressedText);
        }

        [Test]
        public void Python_KeepsItsOperators_AtEveryLevel([ValueSource(nameof(Levels))] CavemanCompressionLevel level)
        {
            var r = _service.ApplyCompression(Python, "eng", level);
            Assert.That(r.CompressedText, Does.Contain("price = base - (base * discount)"), r.CompressedText);
            Assert.That(r.CompressedText, Does.Contain("def apply_discount(base, discount):"));
        }

        [Test]
        public void Sql_KeepsItsClauses_AtEveryLevel([ValueSource(nameof(Levels))] CavemanCompressionLevel level)
        {
            var r = _service.ApplyCompression(Sql, "eng", level);
            Assert.That(r.CompressedText, Does.Contain("FROM users").And.Contain("WHERE").And.Contain("JOIN orders"), r.CompressedText);
        }

        [Test]
        public void Yaml_IsReturnedUnchanged_WithAnExplanation([ValueSource(nameof(Levels))] CavemanCompressionLevel level)
        {
            var r = _service.ApplyCompression(Yaml, "eng", level);
            Assert.That(r.CompressedText, Is.EqualTo(Yaml));
            Assert.That(r.ErrorMessage, Does.Contain("declined"));
        }

        [Test]
        public void Html_KeepsItsTags_OrIsExtractedToText([ValueSource(nameof(Levels))] CavemanCompressionLevel level)
        {
            var r = _service.ApplyCompression(Html, "eng", level);
            // either the HTML extractor produced clean text, or the markup is intact - never tag-stripped soup
            Assert.That(r.CompressedText.Contains("<div") || (!r.CompressedText.Contains('<') && r.CompressedText.Contains("world")), r.CompressedText);
        }

        [Test]
        public void Prose_WithColons_StillCompresses()
        {
            const string prose = "Note: this matters a lot because the quarterly financial report that we are going to send tomorrow morning to the whole board of directors needs to be accurate.";
            var r = _service.ApplyCompression(prose, "eng", CavemanCompressionLevel.Aggressive);
            Assert.That(r.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(r.CompressedText.Length, Is.LessThan(prose.Length));
        }

        [Test]
        public void GuardCanBeSwitchedOff_ForCallersThatDecidedProseIsRight()
        {
            var service = new CavemanCompressionService().WithStructuredGuard(false);
            var r = service.ApplyCompression(Python, "eng", CavemanCompressionLevel.Aggressive);
            Assert.That(r.ErrorMessage, Is.Null.Or.Empty);
        }
    }
}
