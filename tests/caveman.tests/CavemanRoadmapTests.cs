using System.Text.Json;
using caveman.core;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanRoadmapTests
{
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre. " +
        "Fin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario e lo coltivava ogni giorno con grande pazienza. " +
        "Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti. " +
        "Non rubava le ombre per fare del male, ma per preservare i ricordi felici di tutti gli abitanti del paese. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone solitario.";

    // A token counter that returns character count, to prove injection works.
    private sealed class CharCounter : ITokenCounter
    {
        public int CountTokens(string text, LlmModel model = LlmModel.Gpt4) => text?.Length ?? 0;
    }

    // ---- ITokenCounter injection ----

    [Test]
    public void InjectedTokenCounter_DrivesMetrics()
    {
        var textRank = new CavemanTextRank(new FunctionWordProvider(), new CharCounter());
        var result = textRank.RankAndSummarizeChatDetailed("hello world");
        Assert.That(result.OriginalTokens, Is.EqualTo("hello world".Length));
    }

    // ---- Cost estimate (USD + EUR) ----

    [Test]
    public void CostEstimate_PopulatesUsdAndEur()
    {
        var result = new CavemanTextRank().RankAndSummarizeChatDetailed(Discourse, new ChatSummarizeOptions
        {
            TokenModel = LlmModel.Gpt4,
            UsdToEurRate = 0.9m
        });

        Assert.That(result.SavedTokens, Is.GreaterThan(0));
        Assert.That(result.EstimatedSavedUsd, Is.GreaterThan(0m));
        Assert.That(result.EstimatedSavedEur, Is.EqualTo(result.EstimatedSavedUsd * 0.9m).Within(0.0001m));
    }

    [Test]
    public void CostEstimate_CustomPrice_IsUsed()
    {
        var result = new CavemanTextRank().RankAndSummarizeChatDetailed(Discourse, new ChatSummarizeOptions
        {
            UsdPer1KTokens = 1.0m,   // $1 per 1K tokens
            UsdToEurRate = 1.0m
        });
        Assert.That(result.EstimatedSavedUsd, Is.EqualTo(result.SavedTokens / 1000m).Within(0.0001m));
    }

    // ---- Lemmatized relevance ----

    [Test]
    public void Relevance_Score_MatchesInflectedVariants()
    {
        var filter = new CavemanRelevanceFilter();
        var hit = filter.Score("Il gatto dorme sul divano.", "gatti", "ita");
        var miss = filter.Score("La macchina corre veloce.", "gatti", "ita");
        Assert.That(hit, Is.GreaterThan(0), "lemmatization should match gatti↔gatto");
        Assert.That(hit, Is.GreaterThan(miss));
    }

    // ---- Must-keep pinning ----

    [Test]
    public void KeepNumbersAndDates_PinsBlockVerbatim()
    {
        var block = Discourse + " Il totale ammonta esattamente a 12345 euro.";
        var summarized = new CavemanTextRank().RankAndSummarizeChat(block);
        var pinned = new CavemanTextRank().RankAndSummarizeChat(block, new ChatSummarizeOptions { KeepNumbersAndDates = true });

        Assert.That(summarized, Does.Not.Contain("12345").And.Not.Contain(block));
        Assert.That(pinned, Does.Contain("12345"));
        Assert.That(pinned, Does.Contain(block.Trim()));
    }

    [Test]
    public void MustKeepPattern_PinsMatchingBlock()
    {
        var block = Discourse + " Riferimento pratica CODICE-42 da non perdere.";
        var pinned = new CavemanTextRank().RankAndSummarizeChat(block,
            new ChatSummarizeOptions { MustKeepPatterns = { @"CODICE-\d+" } });
        Assert.That(pinned, Does.Contain("CODICE-42"));
    }

    // ---- Parser fidelity ----

    [Test]
    public void Parser_PreservesToolCalls()
    {
        var json = """
        [ { "role": "assistant", "content": "Cerco il meteo.",
            "tool_calls": [ { "function": { "name": "get_weather", "arguments": "{\"city\":\"Roma\"}" } } ] } ]
        """;
        var conv = new CavemanConversationParser().Parse(json);
        Assert.That(conv.Messages, Has.Count.EqualTo(1));
        Assert.That(conv.Messages[0].Content, Does.Contain("[tool_call: get_weather"));
    }

    [Test]
    public void Parser_AddsPlaceholderForNonTextContentBlocks()
    {
        var json = """
        [ { "role": "user", "content": [ { "type": "text", "text": "guarda qui" }, { "type": "image" } ] } ]
        """;
        var conv = new CavemanConversationParser().Parse(json);
        Assert.That(conv.Messages[0].Content, Does.Contain("guarda qui"));
        Assert.That(conv.Messages[0].Content, Does.Contain("[image]"));
    }

    // ---- Persistence: ContextWindow ----

    [Test]
    public void ContextWindow_SaveLoad_RoundTrips()
    {
        var window = new CavemanContextWindow(100_000) { SessionId = "s1", KeepLastTurns = 3 };
        window.Append(CavemanRole.System, "Sei un assistente.");
        window.Append(CavemanRole.User, "Ciao");
        window.Append(CavemanRole.Assistant, "Salve!");

        var json = window.Save();
        var restored = CavemanContextWindow.Load(json);

        Assert.That(restored.SessionId, Is.EqualTo("s1"));
        Assert.That(restored.MessageCount, Is.EqualTo(3));
        Assert.That(restored.Render(), Does.Contain("Salve!"));
    }

    [Test]
    public void ContextWindow_DeduplicateOnAppend_SkipsRepeats()
    {
        var window = new CavemanContextWindow(100_000) { DeduplicateOnAppend = true };
        window.Append(CavemanRole.System, "Stesso prompt di sistema.");
        window.Append(CavemanRole.User, "Domanda.");
        window.Append(CavemanRole.System, "Stesso prompt di sistema.");   // duplicate

        Assert.That(window.MessageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task FileConversationStore_SaveLoadDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveman_store_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileConversationStore(dir);
            var window = new CavemanContextWindow(100_000) { SessionId = "agent-1" };
            window.Append(CavemanRole.User, "Ricordami questo.");
            await window.SaveAsync(store);

            var loaded = await CavemanContextWindow.LoadAsync(store, "agent-1");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Render(), Does.Contain("Ricordami questo."));

            await store.DeleteAsync("agent-1");
            Assert.That(await store.LoadAsync("agent-1"), Is.Null);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task InMemoryConversationStore_Works()
    {
        var store = new InMemoryConversationStore();
        var state = new ConversationState { SessionId = "x", MaxTokens = 1000, Model = LlmModel.Gpt4 };
        state.Turns.Add(new PersistedTurn { Message = new CavemanMessage(CavemanRole.User, "hi") });
        await store.SaveAsync("x", state);

        var back = await store.LoadAsync("x");
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.Turns, Has.Count.EqualTo(1));
        Assert.That(back.Turns[0].Message.Content, Is.EqualTo("hi"));
    }

    // ---- Persistence: MemoryStore + recall ----

    [Test]
    public void MemoryStore_RecallsRelevant_AndRoundTrips()
    {
        var store = new CavemanMemoryStore();
        store.Remember(new MemoryNote { Summary = "The user prefers dark mode and concise answers.", Keywords = { "dark", "mode" } });
        store.Remember(new MemoryNote { Summary = "The capital of France is Paris.", Keywords = { "Paris", "France" } });
        store.Remember(new MemoryNote { Summary = "The project deadline is next Friday.", Keywords = { "deadline" } });

        var recalled = store.Recall("What is the capital of France?", topK: 1);
        Assert.That(recalled, Has.Count.EqualTo(1));
        Assert.That(recalled[0].Summary, Does.Contain("Paris"));

        var json = store.Save();
        var store2 = new CavemanMemoryStore();
        store2.Load(json);
        Assert.That(store2.Count, Is.EqualTo(3));
    }
}
