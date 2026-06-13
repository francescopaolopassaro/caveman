using System.Text.Json;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanAgentToolsTests
{
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre. " +
        "Fin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario e lo coltivava ogni giorno con grande pazienza. " +
        "Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti. " +
        "Non rubava le ombre per fare del male, ma per preservare i ricordi felici di tutti gli abitanti del paese. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone solitario.";

    private static string Json(string s) => JsonSerializer.Serialize(s);

    // ---- CavemanContextWindow ----

    [Test]
    public void ContextWindow_StaysWithinBudget_AndKeepsRecentTurn()
    {
        var window = new CavemanContextWindow(maxTokens: 120) { KeepLastTurns = 1 };

        window.Append(CavemanRole.User, Discourse);
        window.Append(CavemanRole.Assistant, Discourse);
        window.Append(CavemanRole.User, Discourse);
        window.Append(CavemanRole.Assistant, "Va bene, grazie mille per la spiegazione.");

        Assert.That(window.TokenCount, Is.LessThanOrEqualTo(120));
        Assert.That(window.Render(), Does.Contain("Va bene, grazie mille per la spiegazione."));
        Assert.That(window.MessageCount, Is.GreaterThan(0));
    }

    [Test]
    public void ContextWindow_BelowBudget_KeepsEverythingVerbatim()
    {
        var window = new CavemanContextWindow(maxTokens: 100_000);
        window.Append(CavemanRole.User, "Ciao!");
        window.Append(CavemanRole.Assistant, "Salve, come posso aiutarti?");

        Assert.That(window.MessageCount, Is.EqualTo(2));
        Assert.That(window.Render(), Does.Contain("Salve, come posso aiutarti?"));
    }

    // ---- CavemanMemoryExtractor ----

    [Test]
    public void MemoryExtractor_ProducesSummaryAndKeywords()
    {
        var note = new CavemanMemoryExtractor().Extract(Discourse, maxSentences: 3, maxKeywords: 8);

        Assert.That(note.Summary, Is.Not.Empty);
        Assert.That(note.Summary.Length, Is.LessThan(Discourse.Length));
        Assert.That(note.Keywords, Is.Not.Empty);
        // "Elia" is a capitalized, recurring name — it should surface as a key term.
        Assert.That(note.Keywords, Does.Contain("Elia"));
    }

    // ---- CavemanRelevanceFilter ----

    [Test]
    public void RelevanceFilter_KeepsRelevantBlock_DropsIrrelevant()
    {
        var text = string.Join("\n\n",
            "Il gatto dorme sul divano tutto il giorno e la notte caccia i topi nel giardino.",
            "La ricetta della carbonara richiede uova, guanciale, pecorino e pepe nero macinato.",
            "Le automobili elettriche riducono le emissioni ma richiedono colonnine di ricarica.");

        var focused = new CavemanRelevanceFilter().Focus(text, "Come si prepara la carbonara con il guanciale?", topK: 1);

        Assert.That(focused, Does.Contain("carbonara"));
        Assert.That(focused, Does.Not.Contain("gatto"));
        Assert.That(focused, Does.Not.Contain("automobili"));
    }

    // ---- New 1.2.1 options ----

    [Test]
    public void KeepSystemVerbatim_KeepsSystemPromptUnsummarized()
    {
        var json = $"[ {{ \"role\": \"system\", \"content\": {Json(Discourse)} }}, {{ \"role\": \"user\", \"content\": \"Ciao\" }} ]";
        var textRank = new CavemanTextRank();

        var kept = textRank.RankAndSummarizeChat(json, new ChatSummarizeOptions { ParseConversation = true, KeepSystemVerbatim = true });
        var summarized = textRank.RankAndSummarizeChat(json, new ChatSummarizeOptions { ParseConversation = true, KeepSystemVerbatim = false });

        Assert.That(kept, Does.Contain(Discourse));
        Assert.That(summarized, Does.Not.Contain(Discourse));
    }

    [Test]
    public void Detailed_ReturnsStructuredConversation_RoundTrips()
    {
        var json = $"[ {{ \"role\": \"user\", \"content\": {Json(Discourse)} }}, {{ \"role\": \"assistant\", \"content\": \"Capito, grazie.\" }} ]";

        var result = new CavemanTextRank().RankAndSummarizeChatDetailed(json, new ChatSummarizeOptions { ParseConversation = true });

        Assert.That(result.Conversation.Messages, Has.Count.EqualTo(2));
        Assert.That(result.Conversation.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        var roundTrip = result.Conversation.ToMessagesJson();
        Assert.That(roundTrip, Does.Contain("\"role\""));
        Assert.That(roundTrip, Does.Contain("assistant"));
    }

    [Test]
    public void Budget_DropsOldestTurns_AndLeavesTruncationMarker()
    {
        // Five long discourses followed by a short, recent turn that is cheap to keep verbatim.
        var msgs = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var role = i % 2 == 0 ? "user" : "assistant";
                var content = i < 5 ? Discourse : "Ok, grazie.";
                return $"{{ \"role\": \"{role}\", \"content\": {Json(content)} }}";
            });
        var json = "[ " + string.Join(", ", msgs) + " ]";

        var result = new CavemanTextRank().RankAndSummarizeChatDetailed(json, new ChatSummarizeOptions
        {
            ParseConversation = true,
            KeepLastTurnsVerbatim = 1,
            MaxTokens = 80
        });

        Assert.That(result.DroppedBlocks, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Text, Does.Contain("[…]"));
        Assert.That(result.WithinBudget, Is.True);
    }

}
