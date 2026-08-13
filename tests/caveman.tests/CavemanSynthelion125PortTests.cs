// -----------------------------------------------------------------------------
// <copyright file="CavemanSynthelion125PortTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Regression tests for the Synthelion 1.2.5 port: numeric preservation across every
// compression level, the global-IDF provider and its 50/50 Statistical blend / Aggressive
// ubiquitous-word gate, and the SQL-aware compressor plus its detection/routing wiring.</summary>
// -----------------------------------------------------------------------------
using caveman.core;
using caveman.core.entities;
using caveman.core.services;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanSynthelion125PortTests
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
        // Numeric tokens must survive every compression level (Synthelion 1.2.5 fix:
        // ports, amounts, percentages, versions, dates and temperatures were silently
        // discarded by Semantic/Aggressive/Statistical/Syntactic filtering).
        // ------------------------------------------------------------------

        [TestCase("The server listens on port 8080.", "8080")]
        [TestCase("The invoice total is $99.50 with 20% VAT.", "99.50")]
        [TestCase("Support PHP version 8.3 until 2026.", "8.3")]
        [TestCase("The temperature fell to -5 degrees.", " 5 ")]
        [TestCase("Release scheduled for 2026-08-12.", "2026")]
        public void Test_AllLevels_PreserveNumericTokens(string input, string expectedFragment)
        {
            foreach (var level in AllLevels)
            {
                var result = _compressor.ApplyCompression(input, "eng", level);
                Assert.That(result.CompressedText, Does.Contain(expectedFragment),
                    $"{level} dropped numeric '{expectedFragment}'. Got: '{result.CompressedText}'");
            }
        }

        [Test]
        public void Test_Statistical_PreservesNumbersInNoScoreFallback()
        {
            // A number-only prompt has no scoring candidates at all; the no-scores fallback
            // must still keep the numbers rather than filter them out.
            foreach (var level in AllLevels)
            {
                var result = _compressor.ApplyCompression("404", "eng", level);
                Assert.That(result.CompressedText, Does.Contain("404"),
                    $"{level} dropped the only content ('404'). Got: '{result.CompressedText}'");
            }
        }

        // ------------------------------------------------------------------
        // Global IDF provider: ships the same 56 per-language tables as Synthelion
        // 1.2.5; a language without a table falls back to local-only scoring.
        // ------------------------------------------------------------------

        [Test]
        public void Test_GlobalIdf_HasEngTable_WithSaneCorpusSize()
        {
            var provider = new CavemanGlobalIdfProvider();
            Assert.That(provider.HasData("eng"), Is.True,
                "Expected a shipped eng.idf.br embedded resource to load.");
            Assert.That(provider.GetCorpusSize("eng"), Is.GreaterThan(0));
            Assert.That(CavemanGlobalIdfProvider.GetShippedLanguages(), Does.Contain("eng"));
        }

        [Test]
        public void Test_GlobalIdf_MissingLanguage_HasNoData()
        {
            var provider = new CavemanGlobalIdfProvider();
            Assert.That(provider.HasData("hat"), Is.False);
            Assert.That(provider.GetCorpusSize("hat"), Is.Zero);
            Assert.That(provider.GetDocumentFrequency("any", "hat"), Is.Zero);
        }

        [Test]
        public void Test_GlobalIdf_Preload_WarmsCacheWithoutThrowing()
        {
            var provider = new CavemanGlobalIdfProvider();
            Assert.DoesNotThrow(() => provider.Preload(new[] { "eng", "ita" }));
            Assert.That(provider.HasData("ita"), Is.True);
        }

        [Test]
        public void Test_GlobalIdf_ProviderEqual_FallbackBackwardCompatible()
        {
            // A language with no table must produce a Statistical compression identical to
            // the local-only behaviour — no exception, no empty output.
            var result = _compressor.ApplyCompression(
                "Bonjou tout moun, kijan ou ye jodi a.", "hat", CavemanCompressionLevel.Statistical);
            Assert.That(result.CompressedText, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // SQL-aware compression (Synthelion 1.2.5 SqlCompressor port, dependency-free).
        // ------------------------------------------------------------------

        [Test]
        public void Test_SqlCompressor_CollapsesWhitespace_PreservingLiteralsAndComments()
        {
            const string input =
                "SELECT  id,   name -- user's display name\n" +
                "FROM     users\n" +
                "WHERE    status = 'active   user'  AND age  >  18;\n";
            var result = new CavemanSqlCompressor().Compress(input);

            Assert.That(result.WasCompressed, Is.True);
            // Collapsed outside literals: no double spaces / newlines around keywords.
            Assert.That(result.Compressed, Does.Not.Contain("\n"));
            Assert.That(result.Compressed, Does.Not.Contain("'  AND"));
            // The literal (incl. its inner double space) and the comment survive intact.
            Assert.That(result.Compressed, Does.Contain("'active   user'"));
            Assert.That(result.Compressed, Does.Contain("-- user's display name"));
            // Keywords preserved.
            Assert.That(result.Compressed, Does.Contain("SELECT"));
            Assert.That(result.Compressed, Does.Contain("FROM"));
        }

        [Test]
        public void Test_SqlCompressor_StripComments_RemovesCommentsButNotLiterals()
        {
            const string input =
                "/* header comment */ SELECT 1 -- trailing\nUNION ALL SELECT 2";
            var result = new CavemanSqlCompressor().Compress(input, stripComments: true);

            Assert.That(result.Compressed, Does.Not.Contain("header comment"));
            Assert.That(result.Compressed, Does.Not.Contain("trailing"));
            Assert.That(result.Compressed, Does.Contain("SELECT"));
            Assert.That(result.Compressed, Does.Contain("UNION"));
        }

        [Test]
        public void Test_SqlCompressor_FoldValues_DropsSurplusTuplesAndWritesCcrHash()
        {
            var tuples = string.Join(",\n", Enumerable.Range(1, 20).Select(i => $"({i})"));
            var input = $"INSERT INTO t (id) VALUES\n{tuples};";
            var compressor = new CavemanSqlCompressor();
            var result = compressor.Compress(input, foldValues: true, maxValues: 8);

            Assert.That(result.WasCompressed, Is.True);
            Assert.That(result.TuplesDropped, Is.EqualTo(12));
            Assert.That(result.CcrHash, Is.Not.Null.And.Length.EqualTo(12));
            Assert.That(result.Compressed, Does.Contain("(1)"));
            Assert.That(result.Compressed, Does.Contain("(8)"));
            Assert.That(result.Compressed, Does.Not.Contain("(9)"));
            Assert.That(result.Compressed, Does.Contain("12 value tuples dropped"));
        }

        [Test]
        public void Test_SqlCompressor_FoldValues_RespectsParensAndStringsInsideTuples()
        {
            var input = "INSERT INTO t (a) VALUES (1, 'x,y'), (2, 'nested (paren)'), (3, 4), (5, 6), (7, 8), (9, 10);";
            var result = new CavemanSqlCompressor().Compress(input, foldValues: true, maxValues: 2);

            Assert.That(result.TuplesDropped, Is.EqualTo(4));
            // Tuple 1 and 2 kept complete, commas inside literals not split.
            Assert.That(result.Compressed, Does.Contain("(1, 'x,y')"));
            Assert.That(result.Compressed, Does.Contain("(2, 'nested (paren)')"));
        }

        [Test]
        public void Test_SqlDetector_RoutesStatements_NotProse()
        {
            var detector = new CavemanContentDetector();
            Assert.That(detector.Detect("SELECT id, name FROM users WHERE age > 18;").Type,
                Is.EqualTo(ContentType.Sql));
            Assert.That(detector.Detect("INSERT INTO logs (msg) VALUES ('hello');").Type,
                Is.EqualTo(ContentType.Sql));
            Assert.That(detector.Detect("Create a plan for our customers and deliver it on time.").Type,
                Is.Not.EqualTo(ContentType.Sql));
            Assert.That(detector.Detect("Show me the results from last week.").Type,
                Is.Not.EqualTo(ContentType.Sql));
        }

        [Test]
        public void Test_ContentRouter_RoutesSqlToSqlCompression()
        {
            var router = new CavemanContentRouter();
            var result = router.Route("SELECT  id,   name\nFROM     users\nWHERE    status = 'ok';");

            Assert.That(result.DetectedType, Is.EqualTo(ContentType.Sql));
            Assert.That(result.StrategyUsed, Is.EqualTo("SqlCompression"));
            Assert.That(result.Compressed, Does.Not.Contain("\n"));
        }
    }
}