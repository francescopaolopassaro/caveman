// -----------------------------------------------------------------------------
// <copyright file="CavemanServicesTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Unit tests for the developer services.</summary>
// -----------------------------------------------------------------------------
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanContextCompressorTests
{
    [Test]
    public async Task CompressFileAsync_NonExistentFile_ReturnsError()
    {
        var svc = new CavemanContextCompressor();
        var result = await svc.CompressFileAsync("Z:\\nonexistent\\file.md");
        Assert.That(result.HasError, Is.True);
        Assert.That(result.ErrorMessage, Does.Contain("not found"));
    }

    [Test]
    public async Task CompressFileAsync_EmptyFile_ReturnsEmpty()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var svc = new CavemanContextCompressor();
            var result = await svc.CompressFileAsync(tmp);
            Assert.That(result.HasError, Is.False);
            Assert.That(result.CompressedContent, Is.EqualTo(string.Empty));
        }
        finally { File.Delete(tmp); }
    }

    [Test]
    public async Task CompressDirectoryAsync_NoContextFiles_ReturnsEmpty()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        try
        {
            var svc = new CavemanContextCompressor();
            var results = await svc.CompressDirectoryAsync(tmp);
            Assert.That(results, Is.Empty);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Test]
    public async Task GenerateCompressedContextAsync_NullDirectory_ReturnsEmpty()
    {
        var svc = new CavemanContextCompressor();
        var result = await svc.GenerateCompressedContextAsync("Z:\\nonexistent");
        Assert.That(result, Is.EqualTo(string.Empty));
    }
}

[TestFixture]
public class CavemanCommitGeneratorTests
{
    private CavemanCommitGenerator _gen = null!;

    [SetUp]
    public void Setup() => _gen = new CavemanCommitGenerator();

    [Test]
    public void GenerateFromDiff_EmptyInput_ReturnsChore()
    {
        var result = _gen.GenerateFromDiff("");
        Assert.That(result.Type, Is.EqualTo("chore"));
        Assert.That(result.FullMessage, Does.Contain("empty"));
    }

    [Test]
    public void GenerateFromDiff_ChangedCsFile_DetectsFeat()
    {
        var diff = @"
--- a/src/Services/UserService.cs
+++ b/src/Services/UserService.cs
@@ -10,6 +10,8 @@
 public async Task<User> GetUserAsync(int id)
 {
+    var user = await _db.Users.FindAsync(id);
+    return user;
 }";
        var result = _gen.GenerateFromDiff(diff);
        Assert.That(result.FullMessage, Does.Contain("user"));
        Assert.That(result.FullMessage.Length, Is.LessThanOrEqualTo(50));
    }

    [Test]
    public void GenerateFromDiff_BugfixDetected()
    {
        var diff = @"
--- a/src/Utils/Validator.cs
+++ b/src/Utils/Validator.cs
@@ -5,6 +5,7 @@
 public void Validate(string input)
 {
-    if (input == null) throw;
+    if (string.IsNullOrEmpty(input)) return;
+    // fixed null bug
 }";
        var result = _gen.GenerateFromDiff(diff);
        Assert.That(result.Type, Is.EqualTo("fix"));
    }

    [Test]
    public void SubjectLength_Under50Chars()
    {
        var diff = "+++ b/x.cs\n@@ -1 +1 @@\n+public class VeryLongClassNameWithManyWordsForTesting";
        for (int i = 0; i < 10; i++)
        {
            var result = _gen.GenerateFromDiff(diff);
            Assert.That(result.FullMessage.Length, Is.LessThanOrEqualTo(50),
                $"Commit too long at iteration {i}: '{result.FullMessage}' ({result.FullMessage.Length})");
        }
    }

    [Test]
    public void GenerateFromDiff_NullInput_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _gen.GenerateFromDiff(null!));
    }
}

[TestFixture]
public class CavemanReviewServiceTests
{
    private CavemanReviewService _reviewer = null!;

    [SetUp]
    public void Setup() => _reviewer = new CavemanReviewService();

    [Test]
    public void ReviewDiff_EmptyDiff_ReturnsEmpty()
    {
        var result = _reviewer.ReviewDiff("");
        Assert.That(result.TotalIssues, Is.EqualTo(0));
    }

    [Test]
    public void ReviewDiff_NullDiff_ReturnsEmpty()
    {
        var result = _reviewer.ReviewDiff(null!);
        Assert.That(result.TotalIssues, Is.EqualTo(0));
    }

    [Test]
    public void ReviewDiff_DetectsTodo()
    {
        var diff = @"+++ b/a.cs
@@ -1 +1 @@
+// TODO: implement pagination";
        var result = _reviewer.ReviewDiff(diff);
        Assert.That(result.TotalIssues, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Comments[0].Severity, Is.EqualTo("info"));
    }

    [Test]
    public void ReviewDiff_DetectsNullCheck()
    {
        var diff = @"+++ b/a.cs
@@ -1 +1 @@
+if (user == null) return null;";
        var result = _reviewer.ReviewDiff(diff);
        Assert.That(result.TotalIssues, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void ReviewDiff_DetectsSecurityPattern()
    {
        var diff = @"+++ b/a.cs
@@ -1 +1 @@
+var password = config[""db_password""];";
        var result = _reviewer.ReviewDiff(diff);
        var securityIssues = result.Comments.Where(c => c.Severity == "critical");
        Assert.That(securityIssues, Is.Not.Empty);
    }

    [Test]
    public void ReviewDiff_LongLine_Warning()
    {
        var diff = @"+++ b/a.cs
@@ -1 +1 @@
+" + new string('x', 250);
        var result = _reviewer.ReviewDiff(diff);
        Assert.That(result.Comments, Has.Some.Matches<ReviewComment>(c => c.Severity == "warning"));
    }

    [Test]
    public void ReviewDiff_ValidCode_NoIssues()
    {
        var diff = @"+++ b/a.cs
@@ -0,0 +1 @@
+return Ok();";
        var result = _reviewer.ReviewDiff(diff);
        Assert.That(result.TotalIssues, Is.EqualTo(0));
    }

    [Test]
    public void ReviewDiff_ChangedFiles_Counted()
    {
        var diff = @"+++ b/a.cs
@@ -1 +1 @@
+var x = 1;
--- a/b.cs
+++ b/b.cs
@@ -1 +1 @@
-var y = 1;
+var y = 2;";
        var result = _reviewer.ReviewDiff(diff);
        Assert.That(result.ChangedFiles, Is.GreaterThan(0));
        Assert.That(result.Additions + result.Deletions, Is.GreaterThan(0));
    }
}

[TestFixture]
public class CavemanStatsTrackerTests
{
    [Test]
    public void TrackResult_AddsToCounters()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackResult(new caveman.core.entities.CompressionResult
        {
            CompressedText = "test",
            OriginalTokens = 100,
            CompressedTokens = 60
        });

        Assert.That(stats.SessionRequests, Is.EqualTo(1));
        Assert.That(stats.SessionTokensSaved, Is.EqualTo(40));
        Assert.That(stats.SessionSavingsPercent, Is.EqualTo(40.0));
    }

    [Test]
    public void TrackResult_WithError_Ignored()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackResult(new caveman.core.entities.CompressionResult
        {
            CompressedText = "test",
            OriginalTokens = 100,
            CompressedTokens = 60,
            ErrorMessage = "fail"
        });

        Assert.That(stats.SessionRequests, Is.EqualTo(0));
    }

    [Test]
    public void TrackResult_ZeroTokens_Ignored()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackResult(new caveman.core.entities.CompressionResult
        {
            CompressedText = "test",
            OriginalTokens = 0,
            CompressedTokens = 0
        });

        Assert.That(stats.SessionRequests, Is.EqualTo(0));
    }

    [Test]
    public void TrackBatch_MultipleResults_Accumulates()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackBatch([
            new caveman.core.entities.CompressionResult { CompressedText = "a", OriginalTokens = 100, CompressedTokens = 50 },
            new caveman.core.entities.CompressionResult { CompressedText = "b", OriginalTokens = 200, CompressedTokens = 100 }
        ]);

        Assert.That(stats.SessionTokensSaved, Is.EqualTo(150));
        Assert.That(stats.SessionRequests, Is.EqualTo(2));
    }

    [Test]
    public void ResetSession_ClearsCounters()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackResult(new caveman.core.entities.CompressionResult
        {
            CompressedText = "test", OriginalTokens = 100, CompressedTokens = 60
        });
        stats.ResetSession();

        Assert.That(stats.SessionTokensSaved, Is.EqualTo(0));
        Assert.That(stats.SessionRequests, Is.EqualTo(0));
    }

    [Test]
    public void FormatReports_DoNotThrow()
    {
        var stats = new CavemanStatsTracker(statsFilePath: null);
        stats.TrackResult(new caveman.core.entities.CompressionResult
        {
            CompressedText = "test", OriginalTokens = 100, CompressedTokens = 60
        });

        Assert.DoesNotThrow(() => stats.FormatSessionReport());
        Assert.DoesNotThrow(() => stats.FormatLifetimeReport());
        Assert.DoesNotThrow(() => stats.FormatFullReport());
    }

    [Test]
    public void SaveAndLoad_PreservesData()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var stats1 = new CavemanStatsTracker(statsFilePath: tmp);
            stats1.TrackResult(new caveman.core.entities.CompressionResult
            {
                CompressedText = "test", OriginalTokens = 100, CompressedTokens = 60
            });

            var stats2 = new CavemanStatsTracker(statsFilePath: tmp);
            Assert.That(stats2.LifetimeTokensSaved, Is.EqualTo(40));
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

[TestFixture]
public class CavecrewServiceTests
{
    private CavecrewService _crew = null!;

    [SetUp]
    public void Setup() => _crew = new CavecrewService();

    [Test]
    public async Task InvestigateAsync_NonExistentPath_ReturnsError()
    {
        var result = await _crew.InvestigateAsync("Z:\\nope");
        Assert.That(result.Summary, Does.Contain("not found"));
        Assert.That(result.Agent, Is.EqualTo("cavecrew-investigator"));
    }

    [Test]
    public async Task InvestigateAsync_WithValidFile_FindsSymbols()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        try
        {
            var file = Path.Combine(tmp, "test.cs");
            await File.WriteAllTextAsync(file, @"
public class MyService
{
    public async Task<string> GetDataAsync(int id)
    {
        return ""data"";
    }

    public string Name { get; set; }
}");
            var result = await _crew.InvestigateAsync(tmp);
            Assert.That(result.Summary, Does.Contain("Mapped"));
            Assert.That(result.Summary, Does.Contain("symbols"));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Test]
    public async Task BuildAsync_WithExistingFile_FindsSymbols()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        try
        {
            var file = Path.Combine(tmp, "test.cs");
            await File.WriteAllTextAsync(file, "public class Handler { public void Execute() {} }");
            var result = await _crew.BuildAsync("fix handler execute", new List<string> { file });
            Assert.That(result.Agent, Is.EqualTo("cavecrew-builder"));
            Assert.That(result.Summary, Does.Contain("fix handler execute"));
            Assert.That(result.Details.Count, Is.GreaterThanOrEqualTo(2));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Test]
    public void Review_EmptyDiff_ReturnsNoIssues()
    {
        var result = _crew.Review("");
        Assert.That(result.Agent, Is.EqualTo("cavecrew-reviewer"));
        Assert.That(result.Summary, Does.Contain("No diff"));
    }

    [Test]
    public void Review_WithDiff_ReturnsIssues()
    {
        var result = _crew.Review(@"+++ b/a.cs
@@ -1 +1 @@
+// TODO: add pagination");
        Assert.That(result.Details, Has.Some.Contains("todo"));
    }
}

[TestFixture]
public class CavemanSafetyGuardTests
{
    private CavemanSafetyGuard _safety = null!;

    [SetUp]
    public void Setup() => _safety = new CavemanSafetyGuard();

    [Test]
    public void NormalMessage_ShouldCompress()
    {
        Assert.That(_safety.ShouldCompress("Please refactor the API endpoint"), Is.True);
    }

    [Test]
    public void SecurityPattern_DisablesCompression()
    {
        var verdict = _safety.Check("Critical security vulnerability found in auth module");
        Assert.That(verdict.Level, Is.EqualTo(SafetyLevel.Critical));
        Assert.That(verdict.ShouldCompress, Is.False);
    }

    [Test]
    public void DestructiveCommand_DisablesCompression()
    {
        var verdict = _safety.Check("WARNING: rm -rf /etc");
        Assert.That(verdict.Level, Is.EqualTo(SafetyLevel.Critical));
    }

    [Test]
    public void WarningPattern_ReturnsWarning()
    {
        var verdict = _safety.Check("This API is deprecated, please migrate");
        Assert.That(verdict.Level, Is.EqualTo(SafetyLevel.Warning));
    }

    [Test]
    public void EmptyMessage_Normal()
    {
        Assert.That(_safety.Check("").Level, Is.EqualTo(SafetyLevel.Normal));
        Assert.That(_safety.Check(null!).Level, Is.EqualTo(SafetyLevel.Normal));
    }

    [Test]
    public void SqlInjection_Rce_Patterns_Detected()
    {
        Assert.That(_safety.Check("sql injection vulnerability").Level, Is.EqualTo(SafetyLevel.Critical));
        Assert.That(_safety.Check("remote code execution via RCE").Level, Is.EqualTo(SafetyLevel.Critical));
        Assert.That(_safety.Check("private key exposed in repo").Level, Is.EqualTo(SafetyLevel.Critical));
    }

    // ---- Word-boundary hardening: no more substring false positives ----

    [Test]
    [TestCase("We run the app on Windows servers.")]   // "dos" inside "Windows"
    [TestCase("Our e-commerce platform is live.")]      // "rce" inside "commerce"
    [TestCase("Download it from the source folder.")]   // "rce" inside "source"
    [TestCase("Please increase the dose to two pills.")] // "dos" inside "dose"
    [TestCase("See the reproduction notes below.")]     // "production" inside "reproduction"
    public void NoFalsePositive_OnSubstrings(string message)
    {
        Assert.That(_safety.Check(message).Level, Is.EqualTo(SafetyLevel.Normal), message);
    }

    [Test]
    [TestCase("This is a DDoS attack.")]
    [TestCase("Found an XSS vulnerability.")]
    [TestCase("The certificate uses TLS now.")]
    public void StillDetects_StandaloneAcronyms(string message)
    {
        Assert.That(_safety.Check(message).Level, Is.EqualTo(SafetyLevel.Critical), message);
    }

    [Test]
    public void ExtraPatterns_AreHonored_AndBoundaryAware()
    {
        var guard = new CavemanSafetyGuard(
            extraCriticalPatterns: new[] { "gdpr" },
            extraWarningPatterns: new[] { "wip" });

        Assert.That(guard.Check("This handles GDPR data.").Level, Is.EqualTo(SafetyLevel.Critical));
        Assert.That(guard.Check("It is still a wip feature.").Level, Is.EqualTo(SafetyLevel.Warning));
        Assert.That(guard.Check("the gdprx report").Level, Is.EqualTo(SafetyLevel.Normal)); // boundary: not inside a larger word
    }
}

[TestFixture]
public class ModelTokenizerTests
{
    private ModelTokenizer _tok = null!;

    [SetUp]
    public void Setup() => _tok = new ModelTokenizer();

    [Test]
    public void CountTokens_EmptyText_ReturnsZero()
    {
        Assert.That(_tok.CountTokens(""), Is.EqualTo(0));
        Assert.That(_tok.CountTokens(null!), Is.EqualTo(0));
        Assert.That(_tok.CountTokens("   "), Is.EqualTo(0));
    }

    [Test]
    public void CountTokens_ShortText_Positive()
    {
        var count = _tok.CountTokens("Hello world");
        Assert.That(count, Is.GreaterThan(0));
    }

    [Test]
    public void CountTokens_LongerText_MoreTokens()
    {
        var short_ = _tok.CountTokens("Hello world");
        var long_ = _tok.CountTokens("Hello world this is a much longer sentence with many words");
        Assert.That(long_, Is.GreaterThan(short_));
    }

    [Test]
    public void CountTokens_DifferentModels_DifferentCounts()
    {
        var text = "This is a sample text for testing tokenization across models";
        var gpt4 = _tok.CountTokens(text, LlmModel.Gpt4);
        var llama3 = _tok.CountTokens(text, LlmModel.Llama3);
        Assert.That(llama3, Is.GreaterThanOrEqualTo(gpt4));
    }

    [Test]
    public void CountAllModels_ReturnsAllFive()
    {
        var (gpt4, gpt35, llama3, gemma3, claude3) = _tok.CountAllModels("test text here");
        Assert.That(gpt4, Is.GreaterThan(0));
        Assert.That(gpt35, Is.GreaterThan(0));
        Assert.That(llama3, Is.GreaterThan(0));
        Assert.That(gemma3, Is.GreaterThan(0));
        Assert.That(claude3, Is.GreaterThan(0));
    }

    [Test]
    public void ModelName_ReturnsKnownNames()
    {
        Assert.That(_tok.ModelName(LlmModel.Gpt4), Does.Contain("gpt-4"));
        Assert.That(_tok.ModelName(LlmModel.Claude3), Does.Contain("claude"));
    }
}
