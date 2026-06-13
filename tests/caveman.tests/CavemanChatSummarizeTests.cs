using System.Text;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanChatSummarizeTests
{
    // A long, multi-sentence Italian discourse (well above the word/sentence quota).
    private const string Discourse =
        "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. " +
        "A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre. " +
        "Fin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario e lo coltivava ogni giorno con grande pazienza. " +
        "Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti, conservandola in barattoli di vetro. " +
        "Non rubava le ombre per fare del male, ma per preservare i ricordi felici di tutti gli abitanti del paese. " +
        "Gli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone solitario.";

    private CavemanTextRank _textRank;

    [SetUp]
    public void Setup()
    {
        _textRank = new CavemanTextRank();
    }

    [Test]
    public void RankAndSummarizeChat_EmptyInput_ReturnsEmpty()
    {
        Assert.That(_textRank.RankAndSummarizeChat(string.Empty), Is.Empty);
        Assert.That(_textRank.RankAndSummarizeChat("   \n  \n"), Is.Empty);
    }

    [Test]
    public void RankAndSummarizeChat_MixedContent_PreservesKeywordsStripsNoiseSummarizesDiscourse()
    {
        var chat = string.Join("\n\n",
            "## I.5 - Stemma, gonfalone, sigillo",
            "```json\n{ \"role\": \"assistant\", \"score\": 0.91 }\n```",
            "<div class=\"answer\"><span>Risposta del servizio</span></div>",
            Discourse,
            "{ \"tool\": \"search\", \"hits\": 3 }");

        var result = _textRank.RankAndSummarizeChat(chat);

        // Keyword / service-result line is kept verbatim (it is not a discourse).
        Assert.That(result, Does.Contain("I.5 - Stemma, gonfalone, sigillo"));

        // JSON is stripped out, but HTML content is extracted and kept as plain text.
        Assert.That(result, Does.Not.Contain("\"role\""));
        Assert.That(result, Does.Not.Contain("\"tool\""));
        Assert.That(result, Does.Not.Contain("<div"));
        Assert.That(result, Does.Not.Contain("<span"));
        Assert.That(result, Does.Contain("Risposta del servizio"), "HTML inner text must be preserved");

        // The long discourse is summarized: present but shorter than the original paragraph.
        Assert.That(result, Does.Contain("Elia"));
        Assert.That(result.Length, Is.LessThan(chat.Length));
        Assert.That(result, Does.Not.Contain(Discourse), "the discourse should be compressed, not copied verbatim");
    }

    [Test]
    public void RankAndSummarizeChat_RealisticMarkdownDocument_RetainsAndSummarizesEmbeddedDiscourse()
    {
        // A realistic, "important" markdown document (front matter, headings, lists, links,
        // bold/italic, a table, a fenced code block, a blockquote and inline code) with a long
        // discourse buried in the middle. We want the prose retained+summarized and all the
        // markdown scaffolding stripped down to plain text.
        var markdown = string.Join("\n",
            "---",
            "title: Relazione storica",
            "author: Comune di Valchiara",
            "---",
            "",
            "# I.5 - Stemma, gonfalone, sigillo",
            "",
            "Riferimenti normativi: vedi [statuto comunale](https://example.org/statuto) e `art. 12`.",
            "",
            "- **Stemma**: scudo azzurro",
            "- *Gonfalone*: drappo bianco",
            "- Sigillo: tondo con la montagna",
            "",
            "## Descrizione storica",
            "",
            "> Nota dell'archivista: il testo seguente proviene dal registro del 1742.",
            "",
            Discourse,
            "",
            "## Allegati tecnici",
            "",
            "```csharp",
            "var x = new Stemma(); // questo non deve finire nel testo",
            "Console.WriteLine(x);",
            "```",
            "",
            "| Voce | Anno |",
            "| ---- | ---- |",
            "| Stemma | 1742 |",
            "",
            "![logo del comune](https://example.org/logo.png)");

        var result = _textRank.RankAndSummarizeChat(markdown);

        // The embedded discourse is retained (its subject survives) but compressed.
        Assert.That(result, Does.Contain("Elia"), "the embedded discourse must be retained");
        Assert.That(result, Does.Not.Contain(Discourse), "the discourse must be summarized, not copied verbatim");

        // The important keyword heading survives as plain text.
        Assert.That(result, Does.Contain("I.5 - Stemma, gonfalone, sigillo"));

        // Markdown scaffolding and code are stripped.
        Assert.That(result, Does.Not.Contain("```"));
        Assert.That(result, Does.Not.Contain("Console.WriteLine"));
        Assert.That(result, Does.Not.Contain("https://example.org"), "link/image URLs must be removed");
        Assert.That(result, Does.Not.Contain("title: Relazione"), "YAML front matter must be removed");
        Assert.That(result, Does.Not.Contain("**"));

        // Link label text is kept (markdown link converted to its text).
        Assert.That(result, Does.Contain("statuto comunale"));

        Assert.That(result.Length, Is.LessThan(markdown.Length));

        TestContext.WriteLine($"--- INPUT ({markdown.Length} ch) ---");
        TestContext.WriteLine(markdown);
        TestContext.WriteLine($"--- OUTPUT ({result.Length} ch) ---");
        TestContext.WriteLine(result);
    }

    [Test]
    public void RankAndSummarizeChat_ShortDiscourseBelowQuota_IsKeptVerbatim()
    {
        // Two sentences, well under the 50-word quota: treated as already-elaborated, kept as is.
        const string shortBlock = "Ciao, come posso aiutarti oggi? Dimmi pure cosa ti serve.";

        var result = _textRank.RankAndSummarizeChat(shortBlock);

        Assert.That(result, Is.EqualTo(shortBlock));
    }

    [Test]
    public void RankAndSummarizeChat_LargeDirtyChat_4000Lines()
    {
        var (chat, lineCount, keywordMarkers) = BuildDirtyChat(4000);

        Assert.That(lineCount, Is.GreaterThanOrEqualTo(4000), "generator must produce at least 4000 lines");

        var result = _textRank.RankAndSummarizeChat(chat);

        Assert.That(result, Is.Not.Empty);

        // Overall the conversation must shrink.
        Assert.That(result.Length, Is.LessThan(chat.Length));

        // JSON and raw tags must be gone...
        Assert.That(result, Does.Not.Contain("```"));
        Assert.That(result, Does.Not.Contain("\"role\""));
        Assert.That(result, Does.Not.Contain("\"latency_ms\""));
        Assert.That(result, Does.Not.Contain("<div"));
        Assert.That(result, Does.Not.Contain("<span"));
        Assert.That(result, Does.Not.Contain("<table"));

        // ...but the HTML inner text must be extracted and preserved as plain text.
        Assert.That(result, Does.Contain("cella"), "HTML table content must be preserved");

        // Every keyword / service-result marker must survive verbatim.
        foreach (var marker in keywordMarkers)
            Assert.That(result, Does.Contain(marker), $"keyword marker '{marker}' should be preserved");

        // Discourses must actually be compressed: no full discourse paragraph copied verbatim.
        Assert.That(result, Does.Not.Contain(Discourse));

        TestContext.WriteLine($"Input chars: {chat.Length} | lines: {lineCount}");
        TestContext.WriteLine($"Output chars: {result.Length} | reduction: {100.0 * (1 - (double)result.Length / chat.Length):F1}%");
        TestContext.WriteLine($"Keyword markers preserved: {keywordMarkers.Count}");
    }

    /// <summary>
    /// Builds a noisy chat transcript of at least <paramref name="targetLines"/> lines by
    /// cycling four block kinds: keyword/service results, JSON (fenced + inline), long
    /// discourses and HTML. Returns the text, its line count and the keyword markers emitted.
    /// </summary>
    private static (string Chat, int Lines, List<string> Markers) BuildDirtyChat(int targetLines)
    {
        var sb = new StringBuilder();
        var markers = new List<string>();
        int lines = 0;
        int turn = 0;

        void AppendBlock(string block)
        {
            sb.Append(block).Append("\n\n");
            lines += block.Count(c => c == '\n') + 1 + 1; // block lines + blank separator
        }

        while (lines < targetLines)
        {
            turn++;

            // 1. Keyword / service-result line (must be preserved verbatim).
            var marker = $"I.{turn} PROVA {turn} - Stemma, gonfalone, sigillo";
            markers.Add(marker);
            AppendBlock($"## {marker}");

            // 2. Fenced JSON block (must be stripped).
            AppendBlock(
                "```json\n" +
                "{\n" +
                $"  \"role\": \"assistant\",\n" +
                $"  \"turn\": {turn},\n" +
                $"  \"latency_ms\": {120 + turn},\n" +
                "  \"tokens\": [12, 44, 91]\n" +
                "}\n" +
                "```");

            // 3. Long discourse (must be summarized).
            AppendBlock(Discourse);

            // 4. HTML snippet (must be stripped).
            AppendBlock(
                $"<div class=\"answer\" id=\"a{turn}\">" +
                $"<span>Risultato {turn}</span>" +
                "<table><tr><td>cella</td></tr></table>" +
                "</div>");

            // 5. Inline JSON line (must be stripped).
            AppendBlock($"Esito: {{ \"status\": \"ok\", \"code\": {turn} }}");
        }

        return (sb.ToString(), lines, markers);
    }
}
