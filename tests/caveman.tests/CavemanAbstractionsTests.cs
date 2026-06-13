using caveman.core;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanAbstractionsTests
{
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo, ma era un collezionista di ombre. " +
        "Fin da ragazzo aveva scoperto di possedere un dono straordinario. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano. " +
        "Una notte d'inverno il villaggio piombò nel buio più totale. " +
        "Elia liberò le ombre che aveva conservato e illuminò la piazza.";

    [Test]
    public void BothSummarizers_AreISummarizer()
    {
        ISummarizer[] summarizers = { new CavemanSummarizer(), new CavemanTextRank() };
        foreach (var s in summarizers)
        {
            var byCount = s.Summarize(Discourse, 2);
            var byCountLang = s.Summarize(Discourse, 2, "ita");
            var byRatio = s.Summarize(Discourse, 0.3f);

            Assert.That(byCount, Is.Not.Empty);
            Assert.That(byCount.Length, Is.LessThan(Discourse.Length));
            Assert.That(byCountLang, Is.Not.Empty);
            Assert.That(byRatio, Is.Not.Empty);
        }
    }

    [Test]
    public void Parser_IsIConversationParser()
    {
        IConversationParser parser = new CavemanConversationParser();

        var conv = parser.Parse("User: hi\nAssistant: hello");
        Assert.That(conv.Messages, Has.Count.EqualTo(2));

        var ok = parser.TryParse("""[ { "role": "user", "content": "x" } ]""", out var c2);
        Assert.That(ok, Is.True);
        Assert.That(c2.Format, Is.EqualTo("openai-json"));
    }

    // A parser that ignores the input and returns a fixed conversation.
    private sealed class FakeParser : IConversationParser
    {
        public CavemanConversation Parse(string raw) => new()
        {
            Format = "fake",
            Messages = { new CavemanMessage(CavemanRole.User, "hello injected world") }
        };
        public bool TryParse(string raw, out CavemanConversation conversation)
        {
            conversation = Parse(raw);
            return true;
        }
    }

    [Test]
    public void InjectedParser_IsUsedByTextRank()
    {
        var tr = new CavemanTextRank(new FunctionWordProvider(), tokenCounter: null, parser: new FakeParser());

        var result = tr.RankAndSummarizeChatDetailed("whatever input", new ChatSummarizeOptions { ParseConversation = true });

        Assert.That(result.Format, Is.EqualTo("fake"));
        Assert.That(result.Text, Does.Contain("hello injected world"));
    }

    // A compression engine that always returns a sentinel, to prove the seam is used.
    private sealed class FakeCompression : ICompressionService
    {
        public CompressionResult ApplyCompression(string input, string iso3, CavemanCompressionLevel level, CompressionFilter? customFilter = null)
            => new() { CompressedText = "CMP", OriginalTokens = 1, CompressedTokens = 1 };
        public Task<CompressionResult> CompressAsync(string input, CavemanCompressionLevel level, CancellationToken ct = default)
            => Task.FromResult(ApplyCompression(input, "eng", level));
        public Task<CompressionResult> CompressAsync(string input, CavemanCompressionLevel level, CompressionFilter? customFilter, CancellationToken ct = default)
            => Task.FromResult(ApplyCompression(input, "eng", level));
        public Task<CompressionResult[]> CompressBatchAsync(IEnumerable<string> inputs, CavemanCompressionLevel level, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<CompressionResult>());
        public Task<CompressionResult[]> CompressBatchAsync(IEnumerable<string> inputs, CavemanCompressionLevel level, CompressionFilter? customFilter, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<CompressionResult>());
        public string DetectLanguage(string input) => "eng";
        public IReadOnlyDictionary<string, double> DetectLanguageScores(string input) => new Dictionary<string, double>();
        public void ReleaseMemory() { }
    }

    [Test]
    public void CompressionService_IsICompressionService()
    {
        ICompressionService svc = new CavemanCompressionService();
        var r = svc.ApplyCompression("the big red car runs fast", "eng", CavemanCompressionLevel.Light);
        Assert.That(r.CompressedText, Is.Not.Empty);
        Assert.That(svc.DetectLanguage("good morning everyone"), Is.Not.Empty);
        svc.ReleaseMemory();
    }

    [Test]
    public void InjectedCompressionService_IsUsedByTextRank()
    {
        var tr = new CavemanTextRank(new FunctionWordProvider(), tokenCounter: null, parser: null, compressionService: new FakeCompression());
        var result = tr.RankAndSummarizeChat(Discourse, new ChatSummarizeOptions { CompressKeptText = true });
        Assert.That(result, Does.Contain("CMP"));
    }

    [Test]
    public async Task InjectedCompressionService_IsUsedBySummarizer()
    {
        var summarizer = new CavemanSummarizer(new FunctionWordProvider(), new FakeCompression());
        var result = await summarizer.CompressWithSummaryAsync(Discourse, CavemanCompressionLevel.Semantic, 2);
        Assert.That(result.CompressedText, Is.EqualTo("CMP"));
    }

    [Test]
    public void ContextWindow_AcceptsInjectedCompressionService()
    {
        var window = new CavemanContextWindow(100_000, LlmModel.Gpt4, compressionService: new FakeCompression());
        window.Append(CavemanRole.User, "Ciao");
        Assert.That(window.MessageCount, Is.EqualTo(1));
    }

    // A detector that always returns a fixed language.
    private sealed class FakeDetector : ILanguageDetector
    {
        public string Detect(string input) => "deu";
        public IReadOnlyDictionary<string, double> DetectWithScores(string input) => new Dictionary<string, double> { { "deu", 1.0 } };
    }

    [Test]
    public void LanguageDetector_IsILanguageDetector()
    {
        ILanguageDetector d = new CavemanLanguageDetector();
        Assert.That(d.Detect("Vorrei sapere se è possibile avere informazioni"), Is.EqualTo("ita"));
        Assert.That(d.DetectWithScores("the quick brown fox"), Is.Not.Empty);
    }

    [Test]
    public void InjectedDetector_IsUsedByCompressionService()
    {
        var svc = new CavemanCompressionService(null, new FunctionWordProvider(), new FakeDetector());
        Assert.That(svc.DetectLanguage("anything at all"), Is.EqualTo("deu"));
    }

    [Test]
    public void InjectedDetector_IsAcceptedByTextRank()
    {
        var tr = new CavemanTextRank(new FunctionWordProvider(), tokenCounter: null, parser: null,
            compressionService: null, detector: new FakeDetector());
        var res = tr.RankAndSummarizeChat("ciao come stai oggi");
        Assert.That(res, Is.Not.Null);
    }
}
