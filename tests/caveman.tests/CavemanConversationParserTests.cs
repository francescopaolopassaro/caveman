using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanConversationParserTests
{
    private CavemanConversationParser _parser;

    [SetUp]
    public void Setup() => _parser = new CavemanConversationParser();

    [Test]
    public void Parse_OpenAiJsonArray_ReadsRolesAndContent()
    {
        var json = """
        [
          { "role": "system", "content": "Sei un assistente." },
          { "role": "user", "content": "Ciao" },
          { "role": "assistant", "content": "Salve, come posso aiutarti?" }
        ]
        """;

        var conv = _parser.Parse(json);

        Assert.That(conv.Format, Is.EqualTo("openai-json"));
        Assert.That(conv.Messages, Has.Count.EqualTo(3));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.System));
        Assert.That(conv.Messages[1].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[2].Role, Is.EqualTo(CavemanRole.Assistant));
        Assert.That(conv.Messages[1].Content, Is.EqualTo("Ciao"));
        Assert.That(conv.IsStructured, Is.True);
    }

    [Test]
    public void Parse_MessagesWrapper_AndAnthropicContentBlocks()
    {
        var json = """
        {
          "messages": [
            { "role": "user", "content": [ { "type": "text", "text": "prima parte" }, { "type": "text", "text": "seconda parte" } ] },
            { "role": "assistant", "content": "ok" }
          ]
        }
        """;

        var conv = _parser.Parse(json);

        Assert.That(conv.Messages, Has.Count.EqualTo(2));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[0].Content, Does.Contain("prima parte"));
        Assert.That(conv.Messages[0].Content, Does.Contain("seconda parte"));
    }

    [Test]
    public void Parse_HumanAndAiRoles_MapToUserAndAssistant()
    {
        var json = """[ { "role": "human", "content": "x" }, { "role": "ai", "content": "y" } ]""";

        var conv = _parser.Parse(json);

        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[1].Role, Is.EqualTo(CavemanRole.Assistant));
    }

    [Test]
    public void Parse_ChatML_Format()
    {
        var chatml = "<|im_start|>system\nSei utile.<|im_end|>\n<|im_start|>user\nCiao<|im_end|>\n<|im_start|>assistant\nSalve<|im_end|>";

        var conv = _parser.Parse(chatml);

        Assert.That(conv.Format, Is.EqualTo("chatml"));
        Assert.That(conv.Messages, Has.Count.EqualTo(3));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.System));
        Assert.That(conv.Messages[2].Content, Is.EqualTo("Salve"));
    }

    [Test]
    public void Parse_GemmaTurns_Format()
    {
        var gemma = "<start_of_turn>user\nCiao<end_of_turn>\n<start_of_turn>model\nSalve a te<end_of_turn>";

        var conv = _parser.Parse(gemma);

        Assert.That(conv.Format, Is.EqualTo("gemma"));
        Assert.That(conv.Messages, Has.Count.EqualTo(2));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[1].Role, Is.EqualTo(CavemanRole.Assistant));
    }

    [Test]
    public void Parse_LlamaInst_WithSystem()
    {
        var llama = "<s>[INST] <<SYS>>\nSei un assistente.\n<</SYS>>\n\nQual e la capitale? [/INST] Roma. </s>";

        var conv = _parser.Parse(llama);

        Assert.That(conv.Format, Is.EqualTo("llama-inst"));
        Assert.That(conv.Messages.Any(m => m.Role == CavemanRole.System), Is.True);
        Assert.That(conv.Messages.Any(m => m.Role == CavemanRole.User && m.Content.Contains("capitale")), Is.True);
        Assert.That(conv.Messages.Any(m => m.Role == CavemanRole.Assistant && m.Content.Contains("Roma")), Is.True);
    }

    [Test]
    public void Parse_PlainTranscript_WithLabels()
    {
        var transcript = "User: Ciao, ho una domanda.\nAssistant: Certo, dimmi pure.\nUser: Grazie.";

        var conv = _parser.Parse(transcript);

        Assert.That(conv.Format, Is.EqualTo("transcript"));
        Assert.That(conv.Messages, Has.Count.EqualTo(3));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[1].Role, Is.EqualTo(CavemanRole.Assistant));
    }

    [Test]
    public void Parse_ItalianTranscriptLabels()
    {
        var transcript = "Utente: Buongiorno.\nAssistente: Buongiorno a lei.";

        var conv = _parser.Parse(transcript);

        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.User));
        Assert.That(conv.Messages[1].Role, Is.EqualTo(CavemanRole.Assistant));
    }

    [Test]
    public void Parse_PlainProse_FallsBackToSingleUnknownMessage()
    {
        var prose = "Questo e un semplice paragrafo senza alcuna struttura di ruoli o turni.";

        var conv = _parser.Parse(prose);

        Assert.That(conv.Format, Is.EqualTo("plain"));
        Assert.That(conv.Messages, Has.Count.EqualTo(1));
        Assert.That(conv.Messages[0].Role, Is.EqualTo(CavemanRole.Unknown));
        Assert.That(conv.IsStructured, Is.False);
    }

    [Test]
    public void Parse_Empty_ReturnsEmptyPlain()
    {
        var conv = _parser.Parse("   ");
        Assert.That(conv.Messages, Is.Empty);
        Assert.That(conv.Format, Is.EqualTo("plain"));
    }
}
