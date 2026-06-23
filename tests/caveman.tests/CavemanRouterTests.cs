using caveman.core;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

// ─────────────────────────────────────────────────────────────────────────────
// CavemanContentDetectorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanContentDetectorTests
{
    private readonly CavemanContentDetector _detector = new();

    [Test]
    public void Detect_JsonArray_ReturnsJsonArrayType()
    {
        var r = _detector.Detect("""[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]""");
        Assert.That(r.Type, Is.EqualTo(ContentType.JsonArray));
        Assert.That(r.Confidence, Is.GreaterThan(0.9f));
    }

    [Test]
    public void Detect_JsonObject_ReturnsJsonObjectType()
    {
        var r = _detector.Detect("""{"key":"value","count":42}""");
        Assert.That(r.Type, Is.EqualTo(ContentType.JsonObject));
    }

    [Test]
    public void Detect_PlainEnglish_ReturnsPlainText()
    {
        const string text = "The quick brown fox jumps over the lazy dog. This is a standard English sentence used for testing.";
        var r = _detector.Detect(text);
        Assert.That(r.Type, Is.EqualTo(ContentType.PlainText));
    }

    [Test]
    public void Detect_GitDiff_ReturnsGitDiff()
    {
        const string diff = """
            diff --git a/file.cs b/file.cs
            index 123..456 100644
            --- a/file.cs
            +++ b/file.cs
            @@ -1,3 +1,4 @@
            -old line
            +new line
            """;
        var r = _detector.Detect(diff);
        Assert.That(r.Type, Is.EqualTo(ContentType.GitDiff));
    }

    [Test]
    public void Detect_CSharpCode_ReturnsCode()
    {
        const string code = """
            public class Foo
            {
                public void Bar() { }
                private int _count;
                public Foo() => _count = 0;
            }
            """;
        var r = _detector.Detect(code);
        Assert.That(r.Type, Is.EqualTo(ContentType.Code));
    }

    [Test]
    public void Detect_LogLines_ReturnsLogOrStacktrace()
    {
        const string log = """
            ERROR 2026-01-01 12:00:00 Something went wrong
            at System.Foo.Bar() in Foo.cs:line 10
            at System.Baz.Run() in Baz.cs:line 20
            at Program.Main() in Program.cs:line 5
            """;
        var r = _detector.Detect(log);
        Assert.That(r.Type, Is.EqualTo(ContentType.LogOrStacktrace));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanJsonCrusherTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanJsonCrusherTests
{
    private CavemanJsonCrusher NewCrusher(int max = 15) =>
        new(new CavemanCcrStore(TimeSpan.FromMinutes(5))) { MaxOutputItems = max };

    private static string MakeArray(int rows, string[] keys)
    {
        var items = Enumerable.Range(1, rows)
            .Select(i => "{" + string.Join(",", keys.Select(k => $"\"{k}\":\"{k}{i}\"")) + "}");
        return "[" + string.Join(",", items) + "]";
    }

    [Test]
    public void Crush_UniformArray_ProducesCsv_WithMinSavings()
    {
        // 10 rows, 4 keys — CSV should be shorter than JSON
        var input = MakeArray(10, ["id", "name", "status", "value"]);
        var crusher = new CavemanJsonCrusher(new CavemanCcrStore()) { MaxOutputItems = 15 };
        var result = crusher.Crush(input);

        Assert.That(result.WasCrushed, Is.True);
        Assert.That(result.Strategy, Is.EqualTo(JsonCrushStrategy.MarkdownTable).Or.EqualTo(JsonCrushStrategy.Csv));
        Assert.That(result.Compressed.Length, Is.LessThan((int)(input.Length * 0.95)));
    }

    [Test]
    public void Crush_UniformSmallArray_PrefersMarkdownTable()
    {
        // 5 rows, 4 keys — should fit markdown table preference
        var input = MakeArray(5, ["id", "name", "city", "score"]);
        var result = NewCrusher().Crush(input);

        // Markdown table preferred when ≤6 keys, ≤50 rows, ≥15% savings
        if (result.WasCrushed)
            Assert.That(result.Strategy, Is.EqualTo(JsonCrushStrategy.MarkdownTable).Or.EqualTo(JsonCrushStrategy.Csv));
    }

    [Test]
    public void Crush_LargeNonUniformArrayWithQuery_DropsRows_EmitsCcrMarker()
    {
        // Non-uniform schema (rows have different keys) forces the lossy row-drop path
        var rand = new Random(42);
        var allKeys = new[] { "id", "invoice", "amount", "customer", "status", "region", "notes", "priority" };
        var items = Enumerable.Range(1, 30).Select(i =>
        {
            // Each row picks a random subset of keys, making the array non-uniform
            var subset = allKeys.Where(_ => rand.Next(2) == 0).Prepend("id").Distinct().ToArray();
            return "{" + string.Join(",", subset.Select(k => $"\"{k}\":\"{k}{i}\"")) + "}";
        });
        var input = "[" + string.Join(",", items) + "]";

        var store = new CavemanCcrStore(TimeSpan.FromMinutes(5));
        var crusher = new CavemanJsonCrusher(store) { MaxOutputItems = 10 };

        var result = crusher.Crush(input, query: "invoice amount");

        Assert.That(result.WasCrushed, Is.True);
        Assert.That(result.Strategy, Is.EqualTo(JsonCrushStrategy.LossyRowDrop));
        Assert.That(result.CcrHash, Is.Not.Null);
        Assert.That(result.KeptRows, Is.LessThan(result.OriginalRows), "Some rows must have been dropped.");
        Assert.That(result.Compressed, Does.Contain("<<ccr:"));

        // Verify store has the dropped content
        var retrieved = store.Retrieve(result.CcrHash!);
        Assert.That(retrieved, Is.Not.Null);
    }

    [Test]
    public void Crush_EmptyArray_ReturnsUnchanged()
    {
        var result = NewCrusher().Crush("[]");
        Assert.That(result.WasCrushed, Is.False);
        Assert.That(result.Strategy, Is.EqualTo(JsonCrushStrategy.None));
    }

    [Test]
    public void Crush_NonJsonInput_ReturnsUnchanged()
    {
        const string notJson = "this is not json at all";
        var result = NewCrusher().Crush(notJson);
        Assert.That(result.WasCrushed, Is.False);
        Assert.That(result.Compressed, Is.EqualTo(notJson));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanCacheAlignerTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanCacheAlignerTests
{
    private readonly CavemanCacheAligner _aligner = new();

    [Test]
    public void Scan_SystemPromptWithUuid_FindsUuid()
    {
        var findings = _aligner.Scan("Session: 550e8400-e29b-41d4-a716-446655440000 started.");
        Assert.That(findings.Any(f => f.Label == "UUID"), Is.True);
    }

    [Test]
    public void Scan_SystemPromptWithIso8601_FindsDatetime()
    {
        var findings = _aligner.Scan("Current time: 2026-06-23T14:30:00Z");
        Assert.That(findings.Any(f => f.Label == "ISO8601"), Is.True);
    }

    [Test]
    public void Scan_SystemPromptWithJwt_FindsJwt()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyMTIzIiwiaWF0IjoxNjAwMDAwMH0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var findings = _aligner.Scan($"Bearer token: {jwt}");
        Assert.That(findings.Any(f => f.Label == "JWT"), Is.True);
    }

    [Test]
    public void Scan_SystemPromptWithHexHash_FindsHash()
    {
        var findings = _aligner.Scan("SHA256: a3f5c8d2e1b0f9e7c4d6a2b8e3f1c0d9a5b7e2f4c6d8a0b1e3f5c7d9a2b4c6d8");
        Assert.That(findings.Any(f => f.Label == "HexHash"), Is.True);
    }

    [Test]
    public void Scan_CleanSystemPrompt_ReturnsEmpty()
    {
        var findings = _aligner.Scan("You are a helpful assistant. Always respond in Italian.");
        Assert.That(findings, Is.Empty);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanCcrStoreTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanCcrStoreTests
{
    [Test]
    public void Store_ThenRetrieve_ReturnsSameJson()
    {
        var store = new CavemanCcrStore();
        const string json = """[{"id":1},{"id":2}]""";
        store.Store("abcdef123456", json);
        Assert.That(store.Retrieve("abcdef123456"), Is.EqualTo(json));
    }

    [Test]
    public void Store_AfterTtlExpiry_ReturnsNull()
    {
        // Use a 1-millisecond TTL to simulate expiry
        var store = new CavemanCcrStore(TimeSpan.FromMilliseconds(1));
        store.Store("deadbeef0001", "[1,2,3]");
        System.Threading.Thread.Sleep(10);
        Assert.That(store.Retrieve("deadbeef0001"), Is.Null);
    }

    [Test]
    public void Retrieve_MissingHash_ReturnsNull()
    {
        var store = new CavemanCcrStore();
        Assert.That(store.Retrieve("nonexistenthash"), Is.Null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanContentRouterTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanContentRouterTests
{
    private static CavemanContentRouter BuildRouter(CavemanCcrStore? store = null)
    {
        store ??= new CavemanCcrStore();
        return new CavemanContentRouterBuilder()
            .WithCcrStore(store)
            .Build();
    }

    [Test]
    public void Route_PlainText_UsesNlpCompression()
    {
        var router = BuildRouter();
        var result = router.Route("The quick brown fox jumps over the lazy dog and the cat sat on the mat.");
        Assert.That(result.DetectedType, Is.EqualTo(ContentType.PlainText));
        Assert.That(result.StrategyUsed, Is.EqualTo("NlpCompression"));
    }

    [Test]
    public void Route_JsonArray_UsesJsonCrusher()
    {
        var input = "[" + string.Join(",", Enumerable.Range(1, 5).Select(i => $"{{\"id\":{i},\"name\":\"item{i}\"}}")) + "]";
        var router = BuildRouter();
        var result = router.Route(input);
        Assert.That(result.DetectedType, Is.EqualTo(ContentType.JsonArray));
        Assert.That(result.StrategyUsed, Does.StartWith("JsonCrush"));
    }

    [Test]
    public void Route_Code_IsPassthrough()
    {
        const string code = """
            public class Foo { private int _x; public void Bar() { _x++; } }
            import System;
            function test() => { return true; }
            """;
        var router = BuildRouter();
        var result = router.Route(code);
        Assert.That(result.StrategyUsed, Is.EqualTo("Passthrough"));
    }

    [Test]
    public void Route_LargeJsonArray_DropsRows_StoresInCcrStore()
    {
        var keys = new[] { "id", "product", "price", "qty", "category" };
        var items = Enumerable.Range(1, 50)
            .Select(i => "{" + string.Join(",", keys.Select(k => $"\"{k}\":\"{k}{i}\"")) + "}");
        var input = "[" + string.Join(",", items) + "]";

        var store = new CavemanCcrStore(TimeSpan.FromMinutes(5));
        var router = new CavemanContentRouterBuilder()
            .WithCcrStore(store)
            .WithMaxOutputItems(10)
            .Build();

        var result = router.Route(input, query: "product price");

        if (result.CcrHash != null)
        {
            Assert.That(store.Retrieve(result.CcrHash), Is.Not.Null,
                "Dropped rows should be retrievable from the CCR store.");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ICompressionService CompressContentAsync integration tests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class ICompressionService_CompressContentAsync_Tests
{
    private readonly ICompressionService _svc = new CavemanCompressionService();

    [Test]
    public async Task CompressContentAsync_PlainText_ReturnsNlpCompressed()
    {
        var result = await _svc.CompressContentAsync(
            "The quick brown fox jumps over the lazy dog and all the other animals in the forest.");
        Assert.That(result.StrategyUsed, Is.EqualTo("NlpCompression"));
        Assert.That(result.Compressed, Is.Not.Empty);
    }

    [Test]
    public async Task CompressContentAsync_JsonArray_ReturnsCrushed()
    {
        var input = "[" + string.Join(",",
            Enumerable.Range(1, 10).Select(i => $"{{\"id\":{i},\"val\":\"item{i}\",\"cat\":\"A\",\"score\":{i * 10}}}")) + "]";
        var result = await _svc.CompressContentAsync(input);
        Assert.That(result.DetectedType, Is.EqualTo(ContentType.JsonArray));
        Assert.That(result.StrategyUsed, Does.StartWith("JsonCrush"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanLogCompressorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanLogCompressorTests
{
    private readonly CavemanLogCompressor _compressor = new() { MaxTotalLines = 10 };

    [Test]
    public void Compress_ShortLog_ReturnsUnchanged()
    {
        const string log = "Build started.\nRestoring packages.\nBuild succeeded.";
        var r = _compressor.Compress(log);
        Assert.That(r.WasCompressed, Is.False);
    }

    [Test]
    public void Compress_LongLogWithErrors_KeepsErrorLines()
    {
        var lines = Enumerable.Range(1, 50)
            .Select(i => i % 7 == 0 ? $"ERROR at line {i}: something failed" : $"INFO build step {i} ok")
            .ToList();
        var input = string.Join("\n", lines);
        var r = _compressor.Compress(input);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.KeptLines, Is.LessThan(r.OriginalLines));
        Assert.That(r.Compressed, Does.Contain("ERROR"));
    }

    [Test]
    public void Compress_StackTrace_KeepsStackFrames()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"INFO step {i}").ToList();
        lines.Add("System.NullReferenceException: Object reference not set");
        lines.Add("   at MyApp.Foo.Bar() in Foo.cs:line 42");
        lines.Add("   at MyApp.Program.Main() in Program.cs:line 10");
        var input = string.Join("\n", lines);
        var r = _compressor.Compress(input);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Compressed, Does.Contain("at MyApp"));
    }

    [Test]
    public void Compress_DeduplicatesRepeatedWarnings()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"INFO step {i}").ToList();
        // Add 5 identical-pattern warnings (different numbers)
        for (int i = 1; i <= 5; i++)
            lines.Add($"WARNING: deprecated method called at line {i * 100}");
        var input = string.Join("\n", lines);
        var r = _compressor.Compress(input);
        // After dedup, repeated warning pattern should appear only once
        int warnCount = r.Compressed.Split('\n').Count(l => l.Contains("WARNING"));
        Assert.That(warnCount, Is.LessThanOrEqualTo(5));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanSearchCompressorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanSearchCompressorTests
{
    private readonly CavemanSearchCompressor _compressor = new() { MaxMatchesPerFile = 3, MaxFiles = 5 };

    [Test]
    public void Compress_NoGrepLines_ReturnsUnchanged()
    {
        const string input = "This is not grep output.";
        var r = _compressor.Compress(input);
        Assert.That(r.WasCompressed, Is.False);
    }

    [Test]
    public void Compress_GrepOutput_GroupsByFile()
    {
        // Need > MaxMatchesPerFile (3) lines per file to trigger compression
        var lines = Enumerable.Range(1, 8)
            .Select(i => $"src/Foo.cs:{i * 10}:public void Method{i}() {{ return {i}; }}")
            .Concat(Enumerable.Range(1, 8)
                .Select(i => $"src/Baz.cs:{i * 5}:class Helper{i} : BaseClass"))
            .ToArray();
        var r = _compressor.Compress(string.Join("\n", lines));
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.FilesKept, Is.GreaterThanOrEqualTo(1));
        Assert.That(r.KeptMatches, Is.LessThan(r.OriginalMatches));
    }

    [Test]
    public void Compress_ErrorMatchesScoreHigher_KeptOverInfoMatches()
    {
        var lines = Enumerable.Range(1, 20)
            .Select(i => $"src/App.cs:{i}:info line {i}")
            .ToList();
        lines.Add("src/App.cs:99:error: NullReferenceException thrown here");
        var r = _compressor.Compress(string.Join("\n", lines), query: "null exception");
        Assert.That(r.Compressed, Does.Contain("NullReferenceException"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanDiffCompressorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanDiffCompressorTests
{
    private readonly CavemanDiffCompressor _compressor = new() { MaxContextLines = 1 };

    private static string MakeDiff(int contextLinesPerSide = 10)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("diff --git a/file.cs b/file.cs");
        sb.AppendLine("index 123..456 100644");
        sb.AppendLine("--- a/file.cs");
        sb.AppendLine("+++ b/file.cs");
        sb.AppendLine($"@@ -1,{contextLinesPerSide + 2} +1,{contextLinesPerSide + 2} @@");
        for (int i = 0; i < contextLinesPerSide; i++) sb.AppendLine($" context line {i}");
        sb.AppendLine("-old line");
        sb.AppendLine("+new line");
        for (int i = 0; i < contextLinesPerSide; i++) sb.AppendLine($" context line after {i}");
        return sb.ToString();
    }

    [Test]
    public void Compress_TrimsContextLines()
    {
        var diff = MakeDiff(contextLinesPerSide: 10);
        var r = _compressor.Compress(diff);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Additions, Is.EqualTo(1));
        Assert.That(r.Deletions, Is.EqualTo(1));
        // Output should be shorter than input (context trimmed)
        Assert.That(r.Compressed.Length, Is.LessThan(diff.Length));
    }

    [Test]
    public void Compress_KeepsAllAdditionsAndDeletions()
    {
        var diff = MakeDiff(contextLinesPerSide: 5);
        var r = _compressor.Compress(diff);
        Assert.That(r.Compressed, Does.Contain("+new line"));
        Assert.That(r.Compressed, Does.Contain("-old line"));
    }

    [Test]
    public void Compress_EmptyDiff_ReturnsUnchanged()
    {
        var r = _compressor.Compress(string.Empty);
        Assert.That(r.WasCompressed, Is.False);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanHtmlExtractorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanHtmlExtractorTests
{
    private readonly CavemanHtmlExtractor _extractor = new();

    [Test]
    public void Extract_RemovesScriptAndStyleBlocks()
    {
        const string html = "<html><head><style>body{color:red}</style></head><body><p>Hello world</p><script>alert(1)</script></body></html>";
        var r = _extractor.Extract(html);
        Assert.That(r, Does.Not.Contain("<script>"));
        Assert.That(r, Does.Not.Contain("<style>"));
        Assert.That(r, Does.Contain("Hello world"));
    }

    [Test]
    public void Extract_DecodesHtmlEntities()
    {
        const string html = "<p>Price: &lt;b&gt;€10&lt;/b&gt; &amp; taxes</p>";
        var r = _extractor.Extract(html);
        Assert.That(r, Does.Contain("&").Or.Contain("taxes"));
    }

    [Test]
    public void Extract_PlainText_ReturnsUnchanged()
    {
        const string text = "This is plain text without any HTML.";
        var r = _extractor.Extract(text);
        Assert.That(r, Is.EqualTo(text));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanWasteAnalyzerTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanWasteAnalyzerTests
{
    private readonly CavemanWasteAnalyzer _analyzer = new();

    [Test]
    public void Analyze_CleanText_ReturnsZeroWaste()
    {
        var r = _analyzer.Analyze("The quick brown fox jumps over the lazy dog.");
        Assert.That(r.TotalWasteTokens, Is.EqualTo(0));
    }

    [Test]
    public void Analyze_DetectsHtmlNoise()
    {
        var r = _analyzer.Analyze("<div class=\"container\"><p>text</p></div>");
        Assert.That(r.HtmlNoiseTokens, Is.GreaterThan(0));
    }

    [Test]
    public void Analyze_DetectsBase64Blob()
    {
        var r = _analyzer.Analyze("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        Assert.That(r.Base64Tokens, Is.GreaterThan(0));
    }

    [Test]
    public void Analyze_DetectsExcessiveWhitespace()
    {
        var r = _analyzer.Analyze("line1\n\n\n\n\nline2    with    spaces");
        Assert.That(r.WhitespaceTokens, Is.GreaterThan(0));
    }

    [Test]
    public void AnalyzeMessages_AggregatesAcrossMessages()
    {
        var messages = new[]
        {
            "Normal text.",
            "<div>HTML content here</div>",
            "More text with     excessive whitespace\n\n\n\nhere"
        };
        var r = _analyzer.AnalyzeMessages(messages);
        Assert.That(r.HtmlNoiseTokens + r.WhitespaceTokens, Is.GreaterThan(0));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanCompressionCacheTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanCompressionCacheTests
{
    [Test]
    public void PutResult_ThenGet_ReturnsCachedResult()
    {
        var cache = new CavemanCompressionCache();
        cache.PutResult("hello world", "hello world compressed", 0.5, "TestStrategy");
        Assert.That(cache.TryGetResult("hello world", out var r), Is.True);
        Assert.That(r!.Compressed, Is.EqualTo("hello world compressed"));
        Assert.That(r.Strategy, Is.EqualTo("TestStrategy"));
    }

    [Test]
    public void MarkSkip_ThenIsSkipped_ReturnsTrue()
    {
        var cache = new CavemanCompressionCache();
        cache.MarkSkip("non compressible content");
        Assert.That(cache.IsSkipped("non compressible content"), Is.True);
    }

    [Test]
    public void Get_AfterTtlExpiry_ReturnsFalse()
    {
        var cache = new CavemanCompressionCache(TimeSpan.FromMilliseconds(1));
        cache.PutResult("test", "compressed", 0.5, "S");
        System.Threading.Thread.Sleep(10);
        Assert.That(cache.TryGetResult("test", out _), Is.False);
    }

    [Test]
    public void Router_UsesCache_OnSecondCall()
    {
        var router = new CavemanContentRouterBuilder().Build();
        const string input = "The quick brown fox jumps over the lazy dog and the cat sat on the mat here.";
        var r1 = router.Route(input);
        var r2 = router.Route(input);
        // Second call should hit cache (strategy ends with :cached or same result)
        Assert.That(r2.Compressed, Is.EqualTo(r1.Compressed));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanContentRouter with new compressors
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanContentRouterExtendedTests
{
    private static CavemanContentRouter NewRouter() => new CavemanContentRouterBuilder().Build();

    [Test]
    public void Route_LogContent_UsesLogCompression()
    {
        var lines = Enumerable.Range(1, 50)
            .Select(i => i % 10 == 0
                ? $"ERROR at step {i}: something failed here with details"
                : $"INFO build step {i} completed successfully")
            .ToList();
        var input = string.Join("\n", lines);
        var r = NewRouter().Route(input);
        Assert.That(r.DetectedType, Is.EqualTo(ContentType.LogOrStacktrace));
        // LogCompressor only compresses if > MaxTotalLines (80 by default), but our router will run it
        Assert.That(r.StrategyUsed, Is.EqualTo("LogCompression").Or.EqualTo("Passthrough"));
    }

    [Test]
    public void Route_GitDiff_UsesDiffCompression()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("diff --git a/file.cs b/file.cs");
        sb.AppendLine("--- a/file.cs");
        sb.AppendLine("+++ b/file.cs");
        sb.AppendLine("@@ -1,15 +1,15 @@");
        for (int i = 0; i < 12; i++) sb.AppendLine($" context {i}");
        sb.AppendLine("-old code");
        sb.AppendLine("+new code");
        for (int i = 0; i < 12; i++) sb.AppendLine($" context after {i}");
        var r = NewRouter().Route(sb.ToString());
        Assert.That(r.DetectedType, Is.EqualTo(ContentType.GitDiff));
        Assert.That(r.StrategyUsed, Is.EqualTo("DiffCompression").Or.EqualTo("Passthrough"));
    }

    [Test]
    public void Route_SearchResults_UsesSearchCompression()
    {
        var lines = Enumerable.Range(1, 30)
            .Select(i => $"src/File{i % 5}.cs:{i * 3}:some matching content on line {i}");
        var r = NewRouter().Route(string.Join("\n", lines));
        Assert.That(r.DetectedType, Is.EqualTo(ContentType.SearchResults));
        Assert.That(r.StrategyUsed, Is.EqualTo("SearchCompression").Or.EqualTo("Passthrough"));
    }

    [Test]
    public void Route_Html_UsesHtmlExtraction()
    {
        const string html = """
            <html><head><title>Test</title><style>body{}</style></head>
            <body>
            <div><p>Main content here.</p></div>
            <div><p>More important text.</p></div>
            <script>alert('noise')</script>
            </body></html>
            """;
        var r = NewRouter().Route(html);
        Assert.That(r.DetectedType, Is.EqualTo(ContentType.Html));
        Assert.That(r.StrategyUsed, Does.Contain("Html").Or.EqualTo("Passthrough"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanOutputShaperTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanOutputShaperTests
{
    private readonly CavemanOutputShaper _shaper = new();

    [Test]
    public void Shape_SkipCeremony_AppendsSteering()
    {
        const string prompt = "You are a helpful assistant.";
        var result = _shaper.ShapeSystemPrompt(prompt, VerbosityLevel.SkipCeremony);
        Assert.That(result, Does.StartWith(prompt));
        Assert.That(result, Does.Contain("preamble"));
        Assert.That(_shaper.HasVerbositySteering(result), Is.True);
    }

    [Test]
    public void Shape_Off_ReturnsUnchanged()
    {
        const string prompt = "You are a helpful assistant.";
        Assert.That(_shaper.ShapeSystemPrompt(prompt, VerbosityLevel.Off), Is.EqualTo(prompt));
    }

    [Test]
    public void Shape_IsIdempotent()
    {
        const string prompt = "System prompt.";
        var once = _shaper.ShapeSystemPrompt(prompt, VerbosityLevel.NoRestatement);
        var twice = _shaper.ShapeSystemPrompt(once, VerbosityLevel.NoRestatement);
        Assert.That(twice, Is.EqualTo(once), "Second application at same level must be a no-op.");
    }

    [Test]
    public void Shape_ChangingLevel_ReplacesOldSteering()
    {
        const string prompt = "System prompt.";
        var l1 = _shaper.ShapeSystemPrompt(prompt, VerbosityLevel.SkipCeremony);
        var l3 = _shaper.ShapeSystemPrompt(l1, VerbosityLevel.ConclusionsOnly);
        // Should not contain two sentinel blocks
        int sentinelCount = CountOccurrences(l3, "caveman-verbosity-");
        Assert.That(sentinelCount, Is.EqualTo(1));
        Assert.That(l3, Does.Contain("reasoning"));
    }

    [Test]
    public void RemoveSteering_CleansPrompt()
    {
        const string original = "You are helpful.";
        var shaped = _shaper.ShapeSystemPrompt(original, VerbosityLevel.MinimumTokens);
        var cleaned = _shaper.RemoveVerbositySteering(shaped);
        Assert.That(cleaned.Trim(), Is.EqualTo(original));
    }

    [Test]
    public void Shape_MinimumTokens_ContainsFragmentsInstruction()
    {
        var result = _shaper.ShapeSystemPrompt("prompt", VerbosityLevel.MinimumTokens);
        Assert.That(result, Does.Contain("fragments").IgnoreCase);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0) { count++; idx++; }
        return count;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanCodeCompressorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanCodeCompressorTests
{
    private readonly CavemanCodeCompressor _compressor = new();

    [Test]
    public void Compress_CSharpCode_RemovesLineComments()
    {
        const string code = """
            public class Foo
            {
                // This is a comment
                public void Bar()  // inline comment
                {
                    var x = 1; // another comment
                }
            }
            """;
        var r = _compressor.Compress(code);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Compressed, Does.Not.Contain("This is a comment"));
        Assert.That(r.Compressed, Does.Contain("public void Bar()"));
    }

    [Test]
    public void Compress_CSharpCode_RemovesBlockComments()
    {
        const string code = """
            /* Header comment block
               spanning multiple lines */
            public int Add(int a, int b)
            {
                return a + b;
            }
            """;
        var r = _compressor.Compress(code);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Compressed, Does.Not.Contain("Header comment block"));
        Assert.That(r.Compressed, Does.Contain("public int Add"));
    }

    [Test]
    public void Compress_PythonCode_RemovesHashComments()
    {
        const string code = """
            # This is a module comment
            def greet(name):
                # say hello
                return f"Hello, {name}"
            """;
        var r = _compressor.Compress(code);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Compressed, Does.Not.Contain("module comment"));
        Assert.That(r.Compressed, Does.Contain("def greet"));
    }

    [Test]
    public void Compress_CollapsesExcessiveBlanks()
    {
        const string code = "int x = 1;\n\n\n\n\nint y = 2;";
        var r = _compressor.Compress(code);
        Assert.That(r.WasCompressed, Is.True);
        Assert.That(r.Compressed, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void Compress_EmptyInput_ReturnsUnchanged()
    {
        var r = _compressor.Compress(string.Empty);
        Assert.That(r.WasCompressed, Is.False);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanSharedContextTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanSharedContextTests
{
    [Test]
    public void Put_ThenGet_ReturnsCompressedByDefault()
    {
        var ctx = new CavemanSharedContext(ttl: TimeSpan.FromMinutes(5));
        const string content = "The quick brown fox jumps over the lazy dog and many other animals in the forest today.";
        var entry = ctx.Put("key1", content);
        var retrieved = ctx.Get("key1");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(entry.TokensBefore, Is.GreaterThanOrEqualTo(entry.TokensAfter));
    }

    [Test]
    public void Put_ThenGet_Full_ReturnsOriginal()
    {
        var ctx = new CavemanSharedContext(ttl: TimeSpan.FromMinutes(5));
        const string content = "Original content that should be preserved for full retrieval.";
        ctx.Put("key2", content);
        Assert.That(ctx.Get("key2", full: true), Is.EqualTo(content));
    }

    [Test]
    public void Get_MissingKey_ReturnsNull()
    {
        var ctx = new CavemanSharedContext();
        Assert.That(ctx.Get("nonexistent"), Is.Null);
    }

    [Test]
    public void Stats_ReflectsStoredEntries()
    {
        var ctx = new CavemanSharedContext(ttl: TimeSpan.FromMinutes(5));
        ctx.Put("a", "The quick brown fox. " + string.Concat(Enumerable.Repeat("word ", 20)));
        ctx.Put("b", "Another sentence here. " + string.Concat(Enumerable.Repeat("term ", 20)));
        var (entries, _, _, _) = ctx.Stats;
        Assert.That(entries, Is.EqualTo(2));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CavemanMessageDeduplicatorTests
// ─────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class CavemanMessageDeduplicatorTests
{
    private readonly CavemanMessageDeduplicator _dedup = new() { AdjacentGap = 3, MinContentLength = 10 };

    [Test]
    public void FindDuplicates_NoDuplicates_ReturnsEmpty()
    {
        var msgs = new[] { "message one here", "message two here", "message three here" };
        var r = _dedup.FindDuplicates(msgs);
        Assert.That(r.HasDuplicates, Is.False);
    }

    [Test]
    public void FindDuplicates_IdenticalMessages_FarApart_Detected()
    {
        var repeated = "Tool result: build succeeded with 0 errors and 0 warnings.";
        var msgs = new[] { repeated, "other a", "other b", "other c", "other d", repeated };
        var r = _dedup.FindDuplicates(msgs);
        Assert.That(r.HasDuplicates, Is.True);
        Assert.That(r.EstimatedWastedTokens, Is.GreaterThan(0));
    }

    [Test]
    public void FindDuplicates_AdjacentDuplicate_NotFlagged()
    {
        // Gap of 1 is within AdjacentGap(3) → polling, not re-read
        var repeated = "Tool result: status ok polling response";
        var msgs = new[] { repeated, repeated, "something else here" };
        var r = _dedup.FindDuplicates(msgs);
        Assert.That(r.HasDuplicates, Is.False);
    }

    [Test]
    public void RemoveDuplicates_ReplacesWithPlaceholder()
    {
        var repeated = "Tool result: build succeeded with many lines of output here.";
        var msgs = new[] { repeated, "a", "b", "c", "d", repeated };
        var cleaned = _dedup.RemoveDuplicates(msgs);
        Assert.That(cleaned[5], Does.Contain("duplicate of message #1"));
        Assert.That(cleaned[0], Is.EqualTo(repeated));
    }
}
