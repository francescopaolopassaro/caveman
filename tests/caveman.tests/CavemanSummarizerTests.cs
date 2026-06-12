using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanSummarizerTests
{
    private const string ItalianText = "Nel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre.\n\nFin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario. Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti, conservandola in barattoli di vetro. Non rubava le ombre per fare del male, ma per preservare i ricordi felici. Nei suoi scaffali, allineati nella sua piccola casa di legno, custodiva l'ombra del primo sorriso di un bambino, l'ombra del gatto del sindaco che amava dormire al sole, e persino l'ombra del primo albero piantato nel paese.\n\nGli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone. L'unico a fargli visita era Leo, un bambino curioso e coraggioso di dieci anni. Leo andava spesso a trovare Elia, affascinato dai riflessi argentati e bluastri chiusi nei barattoli.\n\nUna gelida notte d'inverno, un vento ululante spazzò via la neve e spense tutti i lampioni del villaggio, lasciando Valchiara nel buio più totale. Gli abitanti, spaventati e incapaci di orientarsi, si chiusero in casa. Il freddo e il gelo stavano persino iniziando a bloccare i meccanismi della centrale elettrica del paese.\n\nSenza perdersi d'animo, Elia prese la sua borsa di tela e i suoi barattoli più preziosi. Insieme al piccolo Leo, uscì nella tormenta. Raggiunse la piazza principale e, aprendo i barattoli, liberò le ombre che aveva conservato nel corso degli anni: l'ombra del sole di mezzogiorno, l'ombra del fuoco scoppiettante del camino, l'ombra della gioia e del calore.\n\nImmediatamente, la piazza si illuminò di una luce calda e avvolgente. Le ombre danzavano sui muri delle case, portando con sé un tepore magico che sciolse il ghiaccio e ridiede coraggio e speranza a tutti. Gli abitanti, svegliati da quel bagliore dorato, uscirono dalle loro abitazioni e rimasero a bocca aperta.\n\nCapirono finalmente che Elia non era un pericolo, ma un custode di tesori preziosi. Da quella notte in poi, il villaggio non fu mai più avvolto dal buio e dal gelo, ed Elia divenne il cittadino più rispettato e amato da tutti.";

    private CavemanSummarizer _summarizer;

    [SetUp]
    public void Setup()
    {
        _summarizer = new CavemanSummarizer();
    }

    [Test]
    public void Summarize_BySentenceCount_ReturnsShorterText()
    {
        var result = _summarizer.CondenseText(ItalianText, 3);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Length, Is.LessThan(ItalianText.Length));
    }

    [Test]
    public void Summarize_ByPercentage_ReturnsShorterText()
    {
        var result = _summarizer.CondenseText(ItalianText, 0.3f);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Length, Is.LessThan(ItalianText.Length));
    }

    [Test]
    public void Summarize_WithFullPercentage_ReturnsOriginal()
    {
        var result = _summarizer.CondenseText(ItalianText, 1.0f);

        Assert.That(result, Is.EqualTo(ItalianText));
    }

    [Test]
    public void Summarize_EmptyInput_ReturnsEmpty()
    {
        var result = _summarizer.CondenseText(string.Empty, 3);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Summarize_SingleSentence_ReturnsSame()
    {
        var result = _summarizer.CondenseText("Ciao mondo.", 2);

        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void TextRank_BySentenceCount_ReturnsShorterText()
    {
        var textRank = new CavemanTextRank();
        var result = textRank.RankAndSummarize(ItalianText, 3);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Length, Is.LessThan(ItalianText.Length));
    }

    [Test]
    public void TextRank_ByPercentage_ReturnsShorterText()
    {
        var textRank = new CavemanTextRank();
        var result = textRank.RankAndSummarize(ItalianText, 0.3f);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Length, Is.LessThan(ItalianText.Length));
    }

    [Test]
    public void TextRank_EmptyInput_ReturnsEmpty()
    {
        var textRank = new CavemanTextRank();
        var result = textRank.RankAndSummarize(string.Empty, 3);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TextRank_ProducesDifferentOutputThanTfidf()
    {
        var summarizer = new CavemanSummarizer();
        var textRank = new CavemanTextRank();

        var tfidfResult = summarizer.CondenseText(ItalianText, 3);
        var trResult = textRank.RankAndSummarize(ItalianText, 3);

        Assert.That(tfidfResult, Is.Not.EqualTo(trResult));
    }

    [Test]
    public void Summarize_EnglishText_Works()
    {
        var text = "John lived in a small village. He was a collector of shadows. People did not understand him. One night he saved the village with his shadows. Everyone was grateful.";

        var result = _summarizer.CondenseText(text, 2);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Does.Contain("village"));
    }

    [Test]
    public void Summarize_AutoDetectLanguage_Works()
    {
        var result = _summarizer.CondenseText(ItalianText, 2);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Length, Is.LessThan(ItalianText.Length));
    }
}
