using caveman.core;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanAsyncPresetTraceTests
{
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre. " +
        "Fin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario e lo coltivava ogni giorno con grande pazienza. " +
        "Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti. " +
        "Non rubava le ombre per fare del male, ma per preservare i ricordi felici di tutti gli abitanti del paese. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone solitario.";

    private CavemanTextRank _textRank;

    [SetUp]
    public void Setup() => _textRank = new CavemanTextRank();

    [Test]
    public async Task Async_MatchesSync()
    {
        var sync = _textRank.RankAndSummarizeChat(Discourse);
        var async = await _textRank.RankAndSummarizeChatAsync(Discourse);
        Assert.That(async, Is.EqualTo(sync));
    }

    [Test]
    public void Async_HonorsCancellation()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.That(async () => await _textRank.RankAndSummarizeChatAsync(Discourse, null, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Preset_AgentMemory_FitsBudget()
    {
        // Long discourses first (outside the recency window of 4), short recent turns last.
        string M(string role, string content) =>
            $"{{ \"role\": \"{role}\", \"content\": {System.Text.Json.JsonSerializer.Serialize(content)} }}";
        var json = "[ " + string.Join(", ",
            M("user", Discourse),
            M("assistant", Discourse),
            M("user", "Domanda breve uno."),
            M("assistant", "Risposta breve uno."),
            M("user", "Domanda breve due."),
            M("assistant", "Ok, grazie.")) + " ]";

        var result = _textRank.RankAndSummarizeChatDetailed(json, ChatSummarizeOptions.AgentMemory(maxTokens: 80, model: LlmModel.Gpt4));

        Assert.That(result.WithinBudget, Is.True);
        Assert.That(result.CompressedTokens, Is.LessThanOrEqualTo(80));
    }

    [Test]
    public void Preset_CodingChat_KeepsCode()
    {
        var chat = "Soluzione:\n\n```csharp\nConsole.WriteLine(42);\n```";
        var result = _textRank.RankAndSummarizeChat(chat, ChatSummarizeOptions.CodingChat());
        Assert.That(result, Does.Contain("Console.WriteLine"));
    }

    [Test]
    public void Trace_IsPopulated_WhenEnabled()
    {
        var chat = string.Join("\n\n", "## I.5 - Stemma, gonfalone, sigillo", Discourse);
        var result = _textRank.RankAndSummarizeChatDetailed(chat, new ChatSummarizeOptions { CollectTrace = true });

        Assert.That(result.Trace, Is.Not.Empty);
        Assert.That(result.Trace.Any(t => t.Action == "summarized"), Is.True);
        Assert.That(result.Trace.Any(t => t.Action == "kept"), Is.True);
    }

    [Test]
    public void Trace_IsEmpty_ByDefault()
    {
        var result = _textRank.RankAndSummarizeChatDetailed(Discourse);
        Assert.That(result.Trace, Is.Empty);
    }
}
