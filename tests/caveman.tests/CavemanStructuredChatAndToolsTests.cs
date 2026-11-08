using System.Text.Json;
using System.Xml.Linq;
using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

/// <summary>Structured content is compressed (never removed) in chats; tool calls of every provider survive untouched.</summary>
[TestFixture]
public class CavemanStructuredChatAndToolsTests
{
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre. " +
        "Fin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario e lo coltivava ogni giorno con grande pazienza. " +
        "Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti, conservandola in barattoli di vetro. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone solitario.";

    private CavemanTextRank _rank = null!;

    [SetUp]
    public void Setup() => _rank = new CavemanTextRank();

    // ── StructuredCompressor ────────────────────────────────────────────────

    [Test]
    public void Compress_PrettyJson_StaysValidAndIdenticalInMeaning()
    {
        var json = "{\n  \"city\": \"Roma\",\n  \"temp\": 30.50,\n  \"tags\": [ \"a\", \"b\" ],\n  \"ok\": true\n}";
        var c = new CavemanStructuredCompressor().Compress(json, "json");

        Assert.That(c.Length, Is.LessThan(json.Length));
        using var a = JsonDocument.Parse(json);
        using var b = JsonDocument.Parse(c);
        Assert.That(b.RootElement.GetProperty("city").GetString(), Is.EqualTo(a.RootElement.GetProperty("city").GetString()));
        Assert.That(c, Does.Not.Contain("\n").And.Not.Contain("  "), "minified");
        Assert.That(b.RootElement.GetProperty("temp").GetRawText(), Is.EqualTo("30.50"), "numbers must be copied verbatim");
        Assert.That(b.RootElement.GetProperty("tags").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public void Compress_Xml_StaysWellFormedWithoutCommentsAndIndentation()
    {
        var xml = "<?xml version=\"1.0\"?>\n<root>\n  <!-- remove me -->\n  <item id=\"1\">\n    <name>Alpha</name>\n  </item>\n</root>";
        var c = new CavemanStructuredCompressor().Compress(xml, "xml");

        Assert.That(c, Does.Not.Contain("remove me"));
        Assert.That(c, Does.StartWith("<?xml version=\"1.0\"?>"));
        var doc = XDocument.Parse(c);
        Assert.That(doc.Root!.Element("item")!.Attribute("id")!.Value, Is.EqualTo("1"));
        Assert.That(c.Length, Is.LessThan(xml.Length));
    }

    [Test]
    public void Compress_Html_KeepsMarkupAndPreBlocks()
    {
        var html = "<div>\n  <!-- c -->\n  <p>Ciao   mondo</p>\n  <pre>  a\n    b  </pre>\n</div>";
        var c = new CavemanStructuredCompressor().Compress(html, "html");

        Assert.That(c, Does.Contain("<p>Ciao mondo</p>"));
        Assert.That(c, Does.Contain("<pre>  a\n    b  </pre>"), "<pre> content is whitespace-significant");
        Assert.That(c, Does.Not.Contain("<!--"));
    }

    [Test]
    public void Compress_Markdown_CompactsTablesAndKeepsFences()
    {
        var md = "# Titolo   \n\n\n\n| a      | b     |\n|:-------|------:|\n| 1      | 2     |\n\n```json\n{ \"x\": 1 }\n```\n";
        var c = new CavemanStructuredCompressor().Compress(md, "md");

        Assert.That(c, Does.Contain("|a|b|"));
        Assert.That(c, Does.Contain("|:---|---:|"));
        Assert.That(c, Does.Contain("```json\n{\"x\":1}\n```"));
        Assert.That(c, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void Compress_MalformedJson_NeverThrowsAndNeverGrows()
    {
        var bad = "{ \"a\": [1, 2,, }";
        var c = new CavemanStructuredCompressor().Compress(bad, "json");
        Assert.That(c.Length, Is.LessThanOrEqualTo(bad.Length));
    }

    [Test]
    public void Detector_Xml_IsRecognisedAndRoutedToXmlCompression()
    {
        var xml = "<config>\n  <a>1</a>\n  <b>2</b>\n</config>";
        Assert.That(new CavemanContentDetector().Detect(xml).Type, Is.EqualTo(ContentType.Xml));
        var r = new CavemanContentRouter().Route("<config>\n    <a>1</a>\n    <!-- x -->\n    <b>2</b>\n</config>");
        Assert.That(r.DetectedType, Is.EqualTo(ContentType.Xml));
        Assert.That(r.Compressed, Does.Not.Contain("<!--"));
    }

    // ── Chat: structured content is compressed, not removed ────────────────

    [Test]
    public void RankAndSummarizeChat_StructuredContent_IsCompressedNotRemoved()
    {
        var chat = string.Join("\n\n",
            "## Risultato",
            "```json\n{\n  \"city\": \"Roma\",\n  \"temp\": 30\n}\n```",
            "<config>\n  <!-- c -->\n  <a>1</a>\n</config>",
            Discourse,
            "```csharp\n// comment\nvar x = Compute();\n\n\nConsole.WriteLine(x);\n```");

        var result = _rank.RankAndSummarizeChat(chat);

        Assert.That(result, Does.Contain("{\"city\":\"Roma\",\"temp\":30}"));
        Assert.That(result, Does.Contain("<config><a>1</a></config>"));
        Assert.That(result, Does.Contain("Console.WriteLine(x)"));
        Assert.That(result, Does.Contain("```json"));
        Assert.That(result.Length, Is.LessThan(chat.Length));
    }

    [Test]
    public void RankAndSummarizeChat_CompressStructuredFalse_KeepsLegacyStripping()
    {
        var chat = "Ecco:\n\n```json\n{ \"city\": \"Roma\" }\n```";
        var r = _rank.RankAndSummarizeChat(chat, new ChatSummarizeOptions { CompressStructured = false });
        Assert.That(r, Does.Not.Contain("city"));
    }

    // ── Tool calls are never removed (every provider shape) ────────────────

    public static IEnumerable<TestCaseData> ToolConversations()
    {
        yield return new TestCaseData("openai/deepseek/qwen/grok/glm/spark/mistral",
            "[{\"role\":\"user\",\"content\":\"meteo a Roma?\"}," +
            "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_9\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Roma\\\"}\"}}]}," +
            "{\"role\":\"tool\",\"tool_call_id\":\"call_9\",\"content\":\"{\\\"temp\\\":30}\"}," +
            "{\"role\":\"assistant\",\"content\":\"Roma ha 30 gradi.\"}]",
            "call_9");
        yield return new TestCaseData("claude",
            "[{\"role\":\"user\",\"content\":\"meteo?\"}," +
            "{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Controllo.\"},{\"type\":\"tool_use\",\"id\":\"toolu_77\",\"name\":\"get_weather\",\"input\":{\"city\":\"Roma\"}}]}," +
            "{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_77\",\"content\":\"30\"}]}," +
            "{\"role\":\"assistant\",\"content\":\"30 gradi.\"}]",
            "toolu_77");
        yield return new TestCaseData("bedrock-converse/nova",
            "[{\"role\":\"user\",\"content\":[{\"text\":\"meteo?\"}]}," +
            "{\"role\":\"assistant\",\"content\":[{\"toolUse\":{\"toolUseId\":\"tooluse_5\",\"name\":\"get_weather\",\"input\":{\"city\":\"Roma\"}}}]}," +
            "{\"role\":\"user\",\"content\":[{\"toolResult\":{\"toolUseId\":\"tooluse_5\",\"content\":[{\"text\":\"30\"}]}}]}," +
            "{\"role\":\"assistant\",\"content\":[{\"text\":\"30 gradi.\"}]}]",
            "tooluse_5");
        yield return new TestCaseData("gemini",
            "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"meteo?\"}]}," +
            "{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"get_weather\",\"args\":{\"city\":\"Roma\"}}}]}," +
            "{\"role\":\"user\",\"parts\":[{\"functionResponse\":{\"name\":\"get_weather\",\"response\":{\"temp\":30}}}]}," +
            "{\"role\":\"model\",\"parts\":[{\"text\":\"30 gradi.\"}]}]}",
            "functionCall");
        yield return new TestCaseData("cohere-command",
            "{\"chat_history\":[{\"role\":\"USER\",\"message\":\"meteo?\"}," +
            "{\"role\":\"CHATBOT\",\"message\":\"Controllo.\",\"tool_calls\":[{\"name\":\"get_weather\",\"parameters\":{\"city\":\"Roma\"}}]}," +
            "{\"role\":\"TOOL\",\"tool_results\":[{\"call\":{\"name\":\"get_weather\"},\"outputs\":[{\"temp\":30}]}]}]}",
            "get_weather");
        yield return new TestCaseData("glm-observation",
            "[{\"role\":\"user\",\"content\":\"meteo?\"}," +
            "{\"role\":\"assistant\",\"content\":\"get_weather\\n{\\\"city\\\":\\\"Roma\\\"}\"}," +
            "{\"role\":\"observation\",\"content\":\"{\\\"temp\\\":30}\"}]",
            "observation");
    }

    [TestCaseSource(nameof(ToolConversations))]
    public void Compaction_NeverRemovesToolMessages(string provider, string json, string mustSurvive)
    {
        var options = new ChatSummarizeOptions { ParseConversation = true, MaxTokens = 20 };
        var result = _rank.RankAndSummarizeChatDetailed(json, options);

        var messagesJson = result.Conversation.ToMessagesJson();
        Assert.That(messagesJson, Does.Contain(mustSurvive), provider);
        var toolMessagesIn = new CavemanConversationParser().Parse(json).Messages.Count(m => m.HasToolData);
        var toolMessagesOut = result.Conversation.Messages.Count(m => m.HasToolData);
        Assert.That(toolMessagesOut, Is.EqualTo(toolMessagesIn), provider);
        Assert.That(toolMessagesIn, Is.GreaterThan(0), provider);
    }

    [Test]
    public void ToMessagesJson_ToolMessage_IsEmittedByteForByteAsParsed()
    {
        var json = "[{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"f\",\"arguments\":\"{}\"}}]}]";
        var conv = new CavemanConversationParser().Parse(json);
        using var a = JsonDocument.Parse(json);
        using var b = JsonDocument.Parse(conv.ToMessagesJson());
        Assert.That(b.RootElement[0].GetRawText(), Is.EqualTo(a.RootElement[0].GetRawText()));
    }

    [TestCase("<tool_call>{\"name\":\"f\",\"arguments\":{}}</tool_call>")]                 // Qwen / Hermes
    [TestCase("<function_calls><invoke name=\"f\"></invoke></function_calls>")]            // Claude XML
    [TestCase("[TOOL_CALLS] [{\"name\":\"f\",\"arguments\":{}}]")]                         // Mistral
    [TestCase("<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>f")]           // DeepSeek
    [TestCase("<|python_tag|>f.call(x=1)")]                                                // Llama 3.1
    [TestCase("✿FUNCTION✿: f\n✿ARGS✿: {}")]                                              // Qwen-Agent
    public void TextTool_Markers_AreTreatedAsToolData(string content)
        => Assert.That(new CavemanMessage(CavemanRole.Assistant, content).HasToolData, Is.True);

    [Test]
    public void Compaction_TextToolMarker_MessageSurvivesUnderTightBudget()
    {
        var conv = "User: meteo a Roma?\n\nAssistant: <tool_call>{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Roma\"}}</tool_call>\n\nUser: grazie mille per l'aiuto di oggi " + Discourse;
        var r = _rank.RankAndSummarizeChat(conv, new ChatSummarizeOptions { ParseConversation = true, MaxTokens = 15 });
        Assert.That(r, Does.Contain("<tool_call>"));
        Assert.That(r, Does.Contain("get_weather"));
    }

    // ── Strict alternation / Converse ──────────────────────────────────────

    [Test]
    public void ToStrictAlternation_FixesSystemRolesEmptyAndLeadingAssistant()
    {
        var conv = new CavemanConversation();
        conv.Messages.Add(new CavemanMessage(CavemanRole.System, "Sei utile"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.Assistant, "Ciao, dimmi"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.User, "domanda uno"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.User, "   "));
        conv.Messages.Add(new CavemanMessage(CavemanRole.User, "domanda due"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.Assistant, "risposta"));

        var strict = conv.ToStrictAlternation(out var system);

        Assert.That(system, Is.EqualTo("Sei utile"));
        Assert.That(strict.Messages.Select(m => m.Role),
            Is.EqualTo(new[] { CavemanRole.User, CavemanRole.Assistant, CavemanRole.User, CavemanRole.Assistant }));
        Assert.That(strict.Messages[2].Content, Does.Contain("domanda uno").And.Contain("domanda due"));
        Assert.That(strict.Messages.All(m => !string.IsNullOrWhiteSpace(m.Content)), Is.True);
    }

    [Test]
    public void ToConverseMessagesJson_UsesTextBlocksAndSeparateSystem()
    {
        var conv = new CavemanConversation();
        conv.Messages.Add(new CavemanMessage(CavemanRole.System, "regole"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.User, "ciao"));
        conv.Messages.Add(new CavemanMessage(CavemanRole.Assistant, "salve"));

        var json = conv.ToConverseMessagesJson(out var system);
        using var doc = JsonDocument.Parse(json);

        Assert.That(system, Is.EqualTo("regole"));
        Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(2));
        Assert.That(doc.RootElement[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(doc.RootElement[0].GetProperty("content")[0].GetProperty("text").GetString(), Is.EqualTo("ciao"));
    }

    [Test]
    public void ToStrictAlternation_DoesNotMergeOrDropToolMessages()
    {
        var conv = new CavemanConversationParser().Parse(
            "[{\"role\":\"user\",\"content\":\"a\"},{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"f\",\"arguments\":\"{}\"}}]}," +
            "{\"role\":\"tool\",\"tool_call_id\":\"c1\",\"content\":\"r\"},{\"role\":\"tool\",\"tool_call_id\":\"c2\",\"content\":\"r2\"}]");
        var strict = conv.ToStrictAlternation(out _);
        Assert.That(strict.Messages.Count(m => m.HasToolData), Is.EqualTo(3));
    }

    [Test]
    public void ToConverseMessagesJson_OpenAiToolExchange_BecomesToolUseAndToolResult()
    {
        var conv = new CavemanConversationParser().Parse(
            "[{\"role\":\"user\",\"content\":\"meteo?\"}," +
            "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_9\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Roma\\\"}\"}}]}," +
            "{\"role\":\"tool\",\"tool_call_id\":\"call_9\",\"content\":\"30\"}," +
            "{\"role\":\"assistant\",\"content\":\"30 gradi\"}]");

        using var doc = JsonDocument.Parse(conv.ToConverseMessagesJson(out _));
        var turns = doc.RootElement.EnumerateArray().ToList();

        Assert.That(turns.Select(t => t.GetProperty("role").GetString()), Is.EqualTo(new[] { "user", "assistant", "user", "assistant" }));
        var use = turns[1].GetProperty("content")[0].GetProperty("toolUse");
        Assert.That(use.GetProperty("toolUseId").GetString(), Is.EqualTo("call_9"));
        Assert.That(use.GetProperty("name").GetString(), Is.EqualTo("get_weather"));
        Assert.That(use.GetProperty("input").GetProperty("city").GetString(), Is.EqualTo("Roma"));
        var result = turns[2].GetProperty("content")[0].GetProperty("toolResult");
        Assert.That(result.GetProperty("toolUseId").GetString(), Is.EqualTo("call_9"));
        Assert.That(result.GetProperty("content")[0].GetProperty("text").GetString(), Is.EqualTo("30"));
    }

    [Test]
    public void ToConverseMessagesJson_ParallelToolResults_AreMergedIntoOneUserTurn()
    {
        var conv = new CavemanConversationParser().Parse(
            "[{\"role\":\"user\",\"content\":\"due citta\"}," +
            "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[" +
            "{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"f\",\"arguments\":\"{}\"}}," +
            "{\"id\":\"b\",\"type\":\"function\",\"function\":{\"name\":\"f\",\"arguments\":\"{}\"}}]}," +
            "{\"role\":\"tool\",\"tool_call_id\":\"a\",\"content\":\"1\"}," +
            "{\"role\":\"tool\",\"tool_call_id\":\"b\",\"content\":\"2\"}]");

        using var doc = JsonDocument.Parse(conv.ToConverseMessagesJson(out _));
        var turns = doc.RootElement.EnumerateArray().ToList();

        Assert.That(turns.Count, Is.EqualTo(3));
        Assert.That(turns[1].GetProperty("content").GetArrayLength(), Is.EqualTo(2), "two toolUse blocks");
        Assert.That(turns[2].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(turns[2].GetProperty("content").GetArrayLength(), Is.EqualTo(2), "two toolResult blocks in ONE user turn");
    }

    [Test]
    public void ToConverseMessagesJson_AnthropicToolBlocks_AreMappedWithSameIds()
    {
        var conv = new CavemanConversationParser().Parse(
            "[{\"role\":\"user\",\"content\":\"meteo?\"}," +
            "{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Controllo.\"},{\"type\":\"tool_use\",\"id\":\"toolu_77\",\"name\":\"get_weather\",\"input\":{\"city\":\"Roma\"}}]}," +
            "{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_77\",\"content\":\"30\"}]}]");

        using var doc = JsonDocument.Parse(conv.ToConverseMessagesJson(out _));
        var turns = doc.RootElement.EnumerateArray().ToList();
        var assistant = turns[1].GetProperty("content");

        Assert.That(assistant[0].GetProperty("text").GetString(), Is.EqualTo("Controllo."));
        Assert.That(assistant[1].GetProperty("toolUse").GetProperty("toolUseId").GetString(), Is.EqualTo("toolu_77"));
        Assert.That(turns[2].GetProperty("content")[0].GetProperty("toolResult").GetProperty("toolUseId").GetString(), Is.EqualTo("toolu_77"));
    }

    [Test]
    public void ToMessagesJson_DoesNotEscapeBackticksQuotesOrAngleBrackets()
    {
        var conv = new CavemanConversation();
        conv.Messages.Add(new CavemanMessage(CavemanRole.User, "```json\n{\"a\":1}\n```\n<x/>"));
        var json = conv.ToMessagesJson();
        Assert.That(json, Does.Not.Contain("\\u0060").And.Not.Contain("\\u003C").And.Not.Contain("\\u0022"));
        Assert.That(JsonDocument.Parse(json).RootElement[0].GetProperty("content").GetString(), Is.EqualTo("```json\n{\"a\":1}\n```\n<x/>"));
    }

    // ── Models ─────────────────────────────────────────────────────────────

    [Test]
    public void ModelTokenizer_AllMajorModels_ReturnPositiveCounts()
    {
        var t = new ModelTokenizer();
        foreach (LlmModel m in Enum.GetValues<LlmModel>())
            Assert.That(t.CountTokens("Nel piccolo villaggio viveva un uomo di nome Elia", m), Is.GreaterThan(0), m.ToString());
        Assert.That(Enum.GetNames<LlmModel>(), Is.SupersetOf(new[]
            { "Gpt4", "Claude3", "Gemini", "Qwen", "DeepSeek", "Grok", "Glm", "Spark", "Mistral", "Nova", "Kimi", "Ernie", "Command" }));
    }
}
