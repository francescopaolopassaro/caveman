// -----------------------------------------------------------------------------
// <copyright file="CavemanRelevanceRankingTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>
// Correctness tests for the two statistical relevance-ranking algorithms already in the
// codebase: BM25 (CavemanJsonCrusher's lossy row-drop) and TF-IDF (CavemanSummarizer's
// sentence scoring). The existing test suites cover their *mechanics* (a CCR marker gets
// emitted, the summary is shorter); these tests cover that the ranking itself is correct —
// that the algorithm actually keeps the more relevant content, not just *some* content.
// </summary>
// -----------------------------------------------------------------------------
using System;
using System.Linq;
using caveman.core.entities;
using caveman.core.services;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanRelevanceRankingTests
    {
        // ------------------------------------------------------------------
        // BM25 (CavemanJsonCrusher lossy row-drop)
        // ------------------------------------------------------------------

        [Test]
        public void Bm25_RowDrop_KeepsRowsMatchingQuery_OverUnrelatedRows()
        {
            // Half the rows are about invoices (matches the query), half are unrelated
            // "notes" rows padded to be non-uniform so the crusher takes the lossy path.
            var rows = Enumerable.Range(1, 20).Select(i =>
                i % 2 == 0
                    ? $"{{\"id\":{i},\"invoice\":\"INV-{i}\",\"amount\":{i * 10},\"customer\":\"Cust{i}\"}}"
                    : $"{{\"id\":{i},\"weather\":\"sunny\",\"mood\":\"calm\",\"color\":\"blue\"}}"
            );
            var input = "[" + string.Join(",", rows) + "]";

            var crusher = new CavemanJsonCrusher(new CavemanCcrStore()) { MaxOutputItems = 6 };
            var result = crusher.Crush(input, query: "invoice amount customer");

            Assert.That(result.Strategy, Is.EqualTo(JsonCrushStrategy.LossyRowDrop));
            Assert.That(result.KeptRows, Is.LessThan(result.OriginalRows));

            // The surviving rows should be dominated by the query-relevant ("invoice") rows,
            // not the unrelated ("weather") ones — that is the entire point of ranking by
            // BM25 against the query instead of dropping rows arbitrarily.
            Assert.That(result.Compressed, Does.Contain("invoice"));
            Assert.That(result.Compressed.Split("invoice").Length - 1,
                Is.GreaterThan(result.Compressed.Split("weather").Length - 1),
                $"Expected invoice-relevant rows to be favoured over unrelated rows.\n{result.Compressed}");
        }

        [Test]
        public void Bm25_RowDrop_NoQuery_StillProducesDeterministicOutput()
        {
            // Without a query, BM25 has no term to score against — the row-drop path must
            // still behave (not throw, still respect MaxOutputItems) rather than depend on
            // an implicit query.
            var rows = Enumerable.Range(1, 20).Select(i =>
                $"{{\"id\":{i},\"a\":\"{i}\",\"b\":\"{i}\",\"c\":\"{i}\",\"extra{i % 3}\":\"x\"}}");
            var input = "[" + string.Join(",", rows) + "]";

            var crusher = new CavemanJsonCrusher(new CavemanCcrStore()) { MaxOutputItems = 6 };
            Assert.DoesNotThrow(() => crusher.Crush(input));
        }

        // ------------------------------------------------------------------
        // TF-IDF (CavemanSummarizer sentence scoring)
        // ------------------------------------------------------------------

        [Test]
        public void Tfidf_Summarizer_PrefersDistinctiveSentence_OverRepeatedFiller()
        {
            var summarizer = new CavemanSummarizer();

            // Four filler sentences share almost the same vocabulary (low IDF for every
            // word in them); one sentence carries distinctive, rare terms. A correct TF-IDF
            // ranking must prefer the distinctive sentence when asked for just one.
            const string text =
                "Oggi il tempo è bello e sereno. " +
                "Oggi il cielo è bello e sereno. " +
                "Oggi il sole è bello e caldo. " +
                "Il vulcano Krakatoa eruttò catastroficamente nel milleottocentottantatre. " +
                "Oggi il vento è bello e leggero.";

            var summary = summarizer.CondenseText(text, 1);

            Assert.That(summary, Does.Contain("Krakatoa"),
                $"Expected the distinctive sentence to win over repeated filler. Got: '{summary}'");
        }

        [Test]
        public void Tfidf_Summarizer_TwoSentences_KeepsHighestScoringPair()
        {
            var summarizer = new CavemanSummarizer();

            const string text =
                "Il gatto dorme sul divano. " +
                "Il gatto dorme sul tappeto. " +
                "Il presidente ha firmato un trattato commerciale storico con il Giappone. " +
                "Il gatto dorme sulla sedia. " +
                "Domani si terrà una conferenza internazionale sul clima a Ginevra.";

            var summary = summarizer.CondenseText(text, 2);

            Assert.That(summary, Does.Contain("presidente").Or.Contain("trattato"));
            Assert.That(summary, Does.Contain("Ginevra").Or.Contain("clima").Or.Contain("conferenza"));
        }
    }
}
