using caveman.core;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanBuilderTests
{
    private sealed class FakeParser : IConversationParser
    {
        public CavemanConversation Parse(string raw) => new()
        {
            Format = "fake",
            Messages = { new CavemanMessage(CavemanRole.User, "built via builder") }
        };
        public bool TryParse(string raw, out CavemanConversation conversation)
        {
            conversation = Parse(raw);
            return true;
        }
    }

    [Test]
    public void TextRankBuilder_BuildsWorkingInstance()
    {
        var tr = CavemanTextRank.CreateBuilder().Build();
        Assert.That(tr.RankAndSummarize("Ciao. Come stai. Tutto bene.", 1), Is.Not.Null);
    }

    [Test]
    public void TextRankBuilder_AppliesInjectedSeams()
    {
        var tr = CavemanTextRank.CreateBuilder()
            .WithWordProvider(new FunctionWordProvider())
            .WithParser(new FakeParser())
            .Build();

        var result = tr.RankAndSummarizeChatDetailed("anything", new ChatSummarizeOptions { ParseConversation = true });
        Assert.That(result.Format, Is.EqualTo("fake"));
        Assert.That(result.Text, Does.Contain("built via builder"));
    }

    [Test]
    public void ContextWindowBuilder_BuildsConfiguredWindow()
    {
        var window = CavemanContextWindow.CreateBuilder()
            .WithMaxTokens(100_000)
            .WithModel(LlmModel.Gpt4)
            .WithKeepLastTurns(2)
            .WithSessionId("s1")
            .WithDeduplicateOnAppend()
            .Build();

        Assert.That(window.MaxTokens, Is.EqualTo(100_000));
        Assert.That(window.KeepLastTurns, Is.EqualTo(2));
        Assert.That(window.SessionId, Is.EqualTo("s1"));

        window.Append(CavemanRole.User, "stesso");
        window.Append(CavemanRole.User, "stesso");   // dedup on append
        Assert.That(window.MessageCount, Is.EqualTo(1));
    }

    [Test]
    public void ContextWindowBuilder_RequiresMaxTokens()
    {
        Assert.That(() => CavemanContextWindow.CreateBuilder().Build(),
            Throws.InstanceOf<InvalidOperationException>());
    }
}
