// -----------------------------------------------------------------------------
// <copyright file="CavemanCompressionEnhancementsTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>
// Tests for two compression additions: log-line folding for short/repetitive logs
// (CavemanLogCompressor) and opt-in function-body skeletonization (CavemanCodeCompressor).
// </summary>
// -----------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using caveman.core;
using caveman.core.services;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanLogFoldingTests
    {
        [Test]
        public void Fold_RepeatedPassingTestLines_CollapsesEvenBelowMaxTotalLines()
        {
            // 10 lines total, well under the 80-line MaxTotalLines gate — the old
            // implementation would return this unchanged. Structural repetition alone
            // should still be worth folding. Uses a 4+-digit timestamp (the same "noise"
            // class the existing digit-normalisation already targets) rather than a small
            // distinguishing number, since a short number could be meaningful identity
            // (e.g. which test ran) and folding must not silently discard that.
            var lines = Enumerable.Range(1000, 8).Select(i => $"PASS suite duration {i} ms");
            var log = string.Join("\n", new[] { "Running suite..." }.Concat(lines).Append("8 passed"));

            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.WasCompressed, Is.True);
            Assert.That(result.Compressed, Does.Contain("repeated 8x"));
            Assert.That(result.Compressed.Length, Is.LessThan(log.Length));
        }

        [Test]
        public void Fold_NormalizesLongIdsBeforeComparing_SoNearIdenticalLinesStillFold()
        {
            // 5+ digit worker IDs are exactly the "noise, not identity" class the existing
            // DigitNoise regex already normalises for warning dedup — folding reuses it.
            var lines = Enumerable.Range(10000, 5).Select(i => $"INFO compiling module core worker id {i}");
            var log = string.Join("\n", lines);

            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.WasCompressed, Is.True);
            Assert.That(result.Compressed, Does.Contain("repeated 5x"));
        }

        [Test]
        public void Fold_SmallDistinguishingNumbers_AreNotTreatedAsNoise()
        {
            // A short number can be meaningful identity (which test ran), not noise — the
            // existing DigitNoise threshold (4+ digits) intentionally leaves 1-3 digit
            // numbers alone, so these lines must NOT fold into each other and lose which
            // test is which.
            const string log = "PASS test_1 (1ms)\nPASS test_2 (2ms)\nPASS test_3 (3ms)";
            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.Compressed, Does.Not.Contain("repeated"));
            Assert.That(result.Compressed, Does.Contain("test_1"));
            Assert.That(result.Compressed, Does.Contain("test_2"));
            Assert.That(result.Compressed, Does.Contain("test_3"));
        }

        [Test]
        public void Fold_TwoRepeatsOnly_DoesNotFold_BelowMinFoldRun()
        {
            // MinFoldRun defaults to 3 — two repeats is common/harmless prose and should
            // survive verbatim rather than being folded on a hair-trigger.
            const string log = "INFO step one\nINFO step one\nINFO step two";
            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.Compressed, Does.Not.Contain("repeated"));
        }

        [Test]
        public void Fold_NoRepetition_ReturnsUnchanged()
        {
            const string log = "INFO one\nINFO two\nINFO three";
            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.WasCompressed, Is.False);
            Assert.That(result.Compressed, Is.EqualTo(log));
        }

        [Test]
        public void Fold_LongLogWithRepeats_StillAppliesSeverityBasedSelectionOnTop()
        {
            var passLines = Enumerable.Range(1, 100).Select(i => $"PASS test_{i} (1ms)");
            var log = string.Join("\n", new[] { "ERROR something broke" }.Concat(passLines));

            var compressor = new CavemanLogCompressor();
            var result = compressor.Compress(log);

            Assert.That(result.WasCompressed, Is.True);
            Assert.That(result.Compressed, Does.Contain("ERROR something broke"));
            Assert.That(result.Compressed.Length, Is.LessThan(log.Length));
        }

        [Test]
        public void FuzzyFold_Off_LeavesTemplatedVariationUnfolded()
        {
            const string log =
                "User alice logged in from 192.168.1.5 at session start\n" +
                "User bob logged in from 10.0.0.9 at session start\n" +
                "User carol logged in from 172.16.0.3 at session start";

            var strict = new CavemanLogCompressor();
            var result = strict.Compress(log);

            Assert.That(result.WasCompressed, Is.False,
                "Default (exact-match) folding must not group lines that differ in wording.");
        }

        [Test]
        public void FuzzyFold_On_FoldsNearDuplicateTemplatedLines()
        {
            const string log =
                "User alice logged in from 192.168.1.5 at session start\n" +
                "User bob logged in from 10.0.0.9 at session start\n" +
                "User carol logged in from 172.16.0.3 at session start\n" +
                "ERROR database connection refused";

            var fuzzy = new CavemanLogCompressor { FuzzyFold = true };
            var result = fuzzy.Compress(log);

            Assert.That(result.WasCompressed, Is.True);
            Assert.That(result.Compressed, Does.Contain("repeated 3x"));
            Assert.That(result.Compressed, Does.Contain("ERROR database connection refused"));
        }

        [Test]
        public void FuzzyFold_On_NeverFoldsUnrelatedLines()
        {
            const string log =
                "User alice logged in from 192.168.1.5 at session start\n" +
                "Payment of $42.50 processed for order #1183\n" +
                "Disk usage at 87 percent on volume /var/log";

            var fuzzy = new CavemanLogCompressor { FuzzyFold = true };
            var result = fuzzy.Compress(log);

            Assert.That(result.WasCompressed, Is.False,
                "Unrelated lines must never be folded together regardless of FuzzyFold.");
        }
    }

    [TestFixture]
    public class CavemanSimHashTests
    {
        [Test]
        public void Compute_IdenticalText_ZeroDistance()
        {
            const string text = "the quick brown fox jumps over the lazy dog";
            Assert.That(CavemanSimHash.HammingDistance(
                CavemanSimHash.Compute(text), CavemanSimHash.Compute(text)), Is.EqualTo(0));
        }

        [Test]
        public void Compute_NearDuplicateText_SmallDistance_UnrelatedText_LargeDistance()
        {
            const string a = "User alice logged in from 192.168.1.5 at session start";
            const string b = "User bob logged in from 10.0.0.9 at session start";
            const string c = "The quarterly financial report was uploaded successfully";

            int nearDupDistance = CavemanSimHash.HammingDistance(CavemanSimHash.Compute(a), CavemanSimHash.Compute(b));
            int unrelatedDistance = CavemanSimHash.HammingDistance(CavemanSimHash.Compute(a), CavemanSimHash.Compute(c));

            Assert.That(nearDupDistance, Is.LessThan(unrelatedDistance),
                "A near-duplicate (same template, different values) must score closer than unrelated text.");
        }

        [Test]
        public void Compute_EmptyOrWhitespace_ReturnsZero()
        {
            Assert.That(CavemanSimHash.Compute(""), Is.EqualTo(0UL));
            Assert.That(CavemanSimHash.Compute("   "), Is.EqualTo(0UL));
        }

        [Test]
        public void AreNearDuplicates_RespectsMaxDistance()
        {
            const string a = "User alice logged in from 192.168.1.5 at session start";
            const string c = "The quarterly financial report was uploaded successfully";

            Assert.That(CavemanSimHash.AreNearDuplicates(a, c, maxDistance: 3), Is.False);
        }
    }

    [TestFixture]
    public class CavemanCodeSkeletonizationTests
    {
        private readonly CavemanCodeCompressor _compressor = new();

        [Test]
        public void Skeletonize_DefaultOff_BehavesExactlyAsBefore()
        {
            const string code = "public class C { public void M() { var x = 1; var y = 2; return; } }";
            var withDefault = _compressor.Compress(code);
            var withExplicitFalse = _compressor.Compress(code, skeletonize: false);

            Assert.That(withDefault.Compressed, Is.EqualTo(withExplicitFalse.Compressed));
            Assert.That(withDefault.FunctionsSkeletonized, Is.EqualTo(0));
        }

        [Test]
        public void Skeletonize_CSharp_ReplacesLargeBody_KeepsSignature()
        {
            const string code = @"
public class AuthService
{
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        var user = await _repo.FindByUsernameAsync(username);
        if (user == null) { return false; }
        return user.VerifyPassword(password);
    }
}";
            var r = _compressor.Compress(code, skeletonize: true);

            Assert.That(r.FunctionsSkeletonized, Is.EqualTo(1));
            Assert.That(r.Compressed, Does.Contain("AuthenticateAsync(string username, string password)"));
            Assert.That(r.Compressed, Does.Not.Contain("FindByUsernameAsync"));
            Assert.That(r.Compressed, Does.Not.Contain("VerifyPassword"));
        }

        [Test]
        public void Skeletonize_CSharp_NestedBraces_StayBalanced()
        {
            const string code = @"
public class P
{
    public void Weird()
    {
        var s = ""unbalanced { brace in string"";
        var c = '{';
        if (s.Length > 0)
        {
            for (int i = 0; i < 10; i++) { Console.WriteLine(i); }
        }
        return;
    }
}";
            var r = _compressor.Compress(code, skeletonize: true);

            int opens = r.Compressed.Count(c => c == '{');
            int closes = r.Compressed.Count(c => c == '}');
            Assert.That(opens, Is.EqualTo(closes), $"Unbalanced braces in output:\n{r.Compressed}");
        }

        [Test]
        public void Skeletonize_CSharp_TrivialBodies_AreLeftAlone()
        {
            const string code = "public class C { public void Empty() {} public void Tiny() { return; } }";
            var r = _compressor.Compress(code, skeletonize: true);

            Assert.That(r.FunctionsSkeletonized, Is.EqualTo(0));
        }

        [Test]
        public void Skeletonize_Python_CollapsesMethodBody_KeepsClassAndOtherMethods()
        {
            const string code = "class Auth:\n" +
                                 "    def __init__(self, repo):\n" +
                                 "        self.repo = repo\n" +
                                 "\n" +
                                 "    def authenticate(self, username, password):\n" +
                                 "        user = self.repo.find(username)\n" +
                                 "        if user is None:\n" +
                                 "            return False\n" +
                                 "        return user.verify(password)\n" +
                                 "\n" +
                                 "    def add(self, a, b):\n" +
                                 "        return a + b\n";

            var r = _compressor.Compress(code, skeletonize: true);

            // The class container and every method signature must survive.
            Assert.That(r.Compressed, Does.Contain("class Auth:"));
            Assert.That(r.Compressed, Does.Contain("def __init__(self, repo):"));
            Assert.That(r.Compressed, Does.Contain("def authenticate(self, username, password):"));
            Assert.That(r.Compressed, Does.Contain("def add(self, a, b):"));

            // Only the multi-statement method body was collapsed.
            Assert.That(r.Compressed, Does.Not.Contain("self.repo.find"));
            Assert.That(r.Compressed, Does.Contain("..."));

            // The trivial one-line bodies (__init__, add) must survive verbatim — this is
            // also the regression guard for the bug where "class" itself was treated as a
            // collapsible def and swallowed all three methods into a single "...".
            Assert.That(r.Compressed, Does.Contain("self.repo = repo"));
            Assert.That(r.Compressed, Does.Contain("return a + b"));
        }
    }

    [TestFixture]
    public class CavemanTopicSegmenterTests
    {
        private readonly CavemanTopicSegmenter _segmenter = new(new FunctionWordProvider());

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("This is one sentence.")]
        [TestCase("First sentence here. Second sentence here.")]
        public void Segment_DegenerateInput_NeverThrows_ReturnsAtMostOneSegment(string text)
        {
            List<TopicSegment> segments = null!;
            Assert.DoesNotThrow(() => segments = _segmenter.Segment(text, "eng"));
            Assert.That(segments.Count, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Segment_MultiTopicDocument_FindsMoreThanOneSegment()
        {
            const string doc =
                "The stock market rallied sharply today as technology shares surged across every major index. " +
                "Investors were optimistic about strong earnings reports from major software companies this quarter. " +
                "Analysts raised their price targets for several leading technology firms after the announcement. " +
                "Trading volume was significantly higher than the monthly average throughout the session. " +
                "The central bank hinted at possible interest rate cuts later this year. " +
                "Currency markets reacted immediately with the dollar weakening against major peers. " +
                "Corporate bond yields also fell as investors priced in looser monetary policy. " +
                "Several major banks upgraded their outlook for the technology sector. " +
                "Meanwhile, a powerful storm system is moving steadily across the central plains this week. " +
                "Meteorologists warn of heavy rainfall and potential flash flooding in several low-lying counties. " +
                "Residents in affected areas have been urged to prepare emergency supplies and evacuation routes. " +
                "The storm is expected to weaken gradually as it moves further east by Thursday evening. " +
                "Local officials have opened emergency shelters in three counties ahead of the worst conditions. " +
                "Power companies are pre-positioning repair crews anticipating widespread outages from high winds. " +
                "Schools in the affected region have already announced closures for tomorrow. " +
                "The national weather service upgraded the storm warning to its highest level overnight.";

            var segments = _segmenter.Segment(doc, "eng");

            Assert.That(segments.Count, Is.GreaterThan(1),
                "A document with two clearly distinct topics should not collapse to one segment.");
            // Segments must be contiguous and cover every sentence exactly once.
            Assert.That(segments[0].StartSentence, Is.EqualTo(0));
            for (int i = 1; i < segments.Count; i++)
                Assert.That(segments[i].StartSentence, Is.EqualTo(segments[i - 1].EndSentence));
        }

        [Test]
        public void Segment_SingleTopicDocument_StaysAsOneOrFewSegments()
        {
            const string doc =
                "The cat sat quietly on the warm windowsill in the afternoon sun. " +
                "It watched birds hopping across the garden fence with great interest. " +
                "Later the cat stretched, yawned, and curled up for a long nap. " +
                "When it woke, the same cat wandered back to the same windowsill again.";

            var segments = _segmenter.Segment(doc, "eng");

            // A short, single-topic passage should not be shattered into many tiny segments.
            Assert.That(segments.Count, Is.LessThanOrEqualTo(2));
        }
    }

    [TestFixture]
    public class CavemanSummarizerTopicAwareTests
    {
        private readonly CavemanSummarizer _summarizer = new(new FunctionWordProvider());

        private const string TwoTopicDoc =
            "The stock market rallied sharply today as technology shares surged across every major index. " +
            "Investors were optimistic about strong earnings reports from major software companies this quarter. " +
            "Analysts raised their price targets for several leading technology firms after the announcement. " +
            "Trading volume was significantly higher than the monthly average throughout the session. " +
            "The central bank hinted at possible interest rate cuts later this year. " +
            "Currency markets reacted immediately with the dollar weakening against major peers. " +
            "Corporate bond yields also fell as investors priced in looser monetary policy. " +
            "Several major banks upgraded their outlook for the technology sector. " +
            "Meanwhile, a powerful storm system is moving steadily across the central plains this week. " +
            "Meteorologists warn of heavy rainfall and potential flash flooding in several low-lying counties. " +
            "Residents in affected areas have been urged to prepare emergency supplies and evacuation routes. " +
            "The storm is expected to weaken gradually as it moves further east by Thursday evening. " +
            "Local officials have opened emergency shelters in three counties ahead of the worst conditions. " +
            "Power companies are pre-positioning repair crews anticipating widespread outages from high winds. " +
            "Schools in the affected region have already announced closures for tomorrow. " +
            "The national weather service upgraded the storm warning to its highest level overnight.";

        [Test]
        public void CondenseTextTopicAware_CoversMoreTopicsThanPlainCondense()
        {
            var plain = _summarizer.CondenseText(TwoTopicDoc, 4, "eng");
            var topicAware = _summarizer.CondenseTextTopicAware(TwoTopicDoc, 4, "eng");

            bool plainHasWeather = plain.Contains("storm") || plain.Contains("weather") || plain.Contains("flooding");
            bool topicAwareHasWeather = topicAware.Contains("storm") || topicAware.Contains("weather") || topicAware.Contains("flooding");
            bool topicAwareHasMarket = topicAware.Contains("market") || topicAware.Contains("stock") || topicAware.Contains("technology");

            // Plain TF-IDF scoring over the whole document can let one topic's denser
            // vocabulary dominate and starve the other entirely (observed: it drops weather
            // completely on this document). Topic-aware allocation must not have that failure
            // mode — it should cover at least as many distinct topics as plain, and here
            // specifically must include both topics it was given a budget for.
            Assert.That(topicAwareHasWeather, Is.True, $"Topic-aware summary dropped a whole topic: '{topicAware}'");
            Assert.That(topicAwareHasMarket, Is.True, $"Topic-aware summary dropped a whole topic: '{topicAware}'");
            Assert.That(topicAwareHasWeather || !plainHasWeather, Is.True,
                "Topic-aware coverage should never be strictly worse than plain coverage on the same budget.");
        }

        [Test]
        public void CondenseTextTopicAware_SingleTopicDocument_FallsBackToPlainCondense()
        {
            const string singleTopic =
                "The cat sat quietly on the warm windowsill in the afternoon sun. " +
                "It watched birds hopping across the garden fence with great interest. " +
                "Later the cat stretched, yawned, and curled up for a long nap. " +
                "When it woke, the same cat wandered back to the same windowsill again.";

            var plain = _summarizer.CondenseText(singleTopic, 2, "eng");
            var topicAware = _summarizer.CondenseTextTopicAware(singleTopic, 2, "eng");

            Assert.That(topicAware, Is.EqualTo(plain));
        }

        [Test]
        public void CondenseTextTopicAware_EmptyInput_ReturnsEmpty()
        {
            Assert.That(_summarizer.CondenseTextTopicAware("", 3, "eng"), Is.Empty);
        }
    }

    [TestFixture]
    public class CavemanRetrieverTests
    {
        private static readonly string[] Documents =
        {
            "Electric car battery range improved significantly this year for most manufacturers",
            "Battery technology advances are extending electric car range across the industry",
            "Tesla and Rivian both improved battery range in their latest vehicle models",
            "The weather today is sunny with mild temperatures across the region",
            "Local bakery introduces a new sourdough recipe for the weekend market",
        };

        private readonly CavemanRetriever _retriever = new();

        [Test]
        public void Retrieve_PlainBm25_OnlyFindsLiteralMatches()
        {
            var results = _retriever.Retrieve(Documents, "car", 5);

            Assert.That(results.Select(r => r.Index), Is.EquivalentTo(new[] { 0, 1 }),
                "Plain BM25 must only surface documents literally containing the query term.");
        }

        [Test]
        public void RetrieveWithFeedback_Rm3_SurfacesRelevantDocumentWithoutLiteralQueryTerm()
        {
            var results = _retriever.RetrieveWithFeedback(Documents, "car", 5);

            var indexes = results.Select(r => r.Index).ToList();
            Assert.That(indexes, Does.Contain(0));
            Assert.That(indexes, Does.Contain(1));
            Assert.That(indexes, Does.Contain(2),
                "RM3 should surface the Tesla/Rivian document via expansion terms (battery, range) even though it never says 'car'.");

            // Document 2 (genuinely relevant, found via expansion) must rank strictly above
            // document 4 (unrelated bakery text) if the latter appears at all.
            var doc2 = results.First(r => r.Index == 2);
            var doc4 = results.FirstOrDefault(r => r.Index == 4);
            if (doc4.Document != null)
                Assert.That(doc2.Score, Is.GreaterThan(doc4.Score));
        }

        [Test]
        public void RetrieveWithFeedback_NeverSurfacesTrulyUnrelatedDocumentAboveGenuineMatches()
        {
            var results = _retriever.RetrieveWithFeedback(Documents, "car", 5);
            var scores = results.ToDictionary(r => r.Index, r => r.Score);

            // Weather (index 3) must never outrank the car-battery-range cluster.
            if (scores.TryGetValue(3, out var weatherScore))
            {
                Assert.That(weatherScore, Is.LessThan(scores[0]));
                Assert.That(weatherScore, Is.LessThan(scores[1]));
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Retrieve_EmptyQuery_ReturnsEmpty(string query)
        {
            Assert.That(_retriever.Retrieve(Documents, query, 5), Is.Empty);
            Assert.That(_retriever.RetrieveWithFeedback(Documents, query, 5), Is.Empty);
        }

        [Test]
        public void Retrieve_NoMatchingDocuments_ReturnsEmpty_NeverThrows()
        {
            List<RetrievalResult> plain = null!;
            List<RetrievalResult> feedback = null!;
            Assert.DoesNotThrow(() => plain = _retriever.Retrieve(Documents, "xyzzyplugh", 5));
            Assert.DoesNotThrow(() => feedback = _retriever.RetrieveWithFeedback(Documents, "xyzzyplugh", 5));
            Assert.That(plain, Is.Empty);
            Assert.That(feedback, Is.Empty);
        }

        [Test]
        public void Retrieve_EmptyDocumentList_ReturnsEmpty_NeverThrows()
        {
            Assert.DoesNotThrow(() => _retriever.Retrieve(Array.Empty<string>(), "car", 5));
        }
    }
}
