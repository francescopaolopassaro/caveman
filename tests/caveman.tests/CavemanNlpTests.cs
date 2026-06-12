using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanNlpTests
{
    private const string ItalianText = "Nel piccolo villaggio di Valchiara, viveva un uomo di nome Elia.";

    [Test]
    public void TextSplitter_ParseText_ReturnsTokens()
    {
        var splitter = new CavemanTextSplitter();
        var tokens = splitter.ParseText(ItalianText);

        Assert.That(tokens, Is.Not.Empty);
        Assert.That(tokens.Any(t => t.Category == CavemanTokenCategory.Word), Is.True);
        Assert.That(tokens.Any(t => t.Category == CavemanTokenCategory.Punctuation), Is.True);
    }

    [Test]
    public void TextSplitter_CombineTokens_ReconstructsOriginal()
    {
        var splitter = new CavemanTextSplitter();
        var tokens = splitter.ParseText(ItalianText);
        var combined = splitter.CombineTokens(tokens);

        Assert.That(combined, Is.EqualTo(ItalianText));
    }

    [Test]
    public void TextSplitter_ExtractWords_ReturnsOnlyWords()
    {
        var splitter = new CavemanTextSplitter();
        var words = splitter.ExtractWords(ItalianText);

        Assert.That(words, Is.Not.Empty);
        Assert.That(words.All(w => w.All(char.IsLetter)), Is.True);
    }

    [Test]
    public void TextSplitter_EmptyInput_ReturnsEmpty()
    {
        var splitter = new CavemanTextSplitter();
        var tokens = splitter.ParseText(string.Empty);

        Assert.That(tokens, Is.Empty);
    }

    [Test]
    public void TextSplitter_MultilingualText_Works()
    {
        var splitter = new CavemanTextSplitter();
        var mixedText = "Hello 世界 123! Привет!";

        var tokens = splitter.ParseText(mixedText);

        Assert.That(tokens.Count(t => t.Category == CavemanTokenCategory.Word), Is.GreaterThanOrEqualTo(3));
        Assert.That(tokens.Count(t => t.Category == CavemanTokenCategory.Number), Is.EqualTo(1));
        Assert.That(tokens.Count(t => t.Category == CavemanTokenCategory.Punctuation), Is.EqualTo(2));
    }

    [Test]
    public void TextSplitter_CjkText_Works()
    {
        var splitter = new CavemanTextSplitter();
        var tokens = splitter.ParseText("你好世界");

        Assert.That(tokens, Is.Not.Empty);
        Assert.That(tokens.All(t => t.Category == CavemanTokenCategory.Word), Is.True);
    }

    [Test]
    public void SentenceDetector_SplitText_ReturnsSentences()
    {
        var detector = new CavemanSentenceDetector();
        var sentences = detector.SplitText(ItalianText);

        Assert.That(sentences, Is.Not.Empty);
    }

    [Test]
    public void SentenceDetector_MultipleSentences_DetectsAll()
    {
        var detector = new CavemanSentenceDetector();
        var text = "First sentence. Second sentence! Third sentence? And another one.";
        var sentences = detector.SplitText(text);

        Assert.That(sentences.Length, Is.EqualTo(4));
    }

    [Test]
    public void SentenceDetector_Abbreviation_DoesNotSplit()
    {
        var detector = new CavemanSentenceDetector();
        var text = "Dr. Smith arrived. He was late.";
        var sentences = detector.SplitText(text, "eng");

        Assert.That(sentences.Length, Is.EqualTo(2));
    }

    [Test]
    public void SentenceDetector_Newlines_AreHandled()
    {
        var detector = new CavemanSentenceDetector();
        var text = "Line one.\n\nLine two after blank line.";
        var sentences = detector.SplitText(text);

        Assert.That(sentences.Length, Is.EqualTo(2));
        Assert.That(sentences[0].Text, Does.Contain("Line one"));
        Assert.That(sentences[1].Text, Does.Contain("Line two"));
    }

    [Test]
    public void SentenceDetector_EmptyInput_ReturnsEmpty()
    {
        var detector = new CavemanSentenceDetector();
        var sentences = detector.SplitText(string.Empty);

        Assert.That(sentences, Is.Empty);
    }

    [Test]
    public void SentenceDetector_ExtractPhrases_ReturnsStringArray()
    {
        var detector = new CavemanSentenceDetector();
        var text = "First sentence. Second sentence.";
        var phrases = detector.ExtractPhrases(text);

        Assert.That(phrases.Length, Is.EqualTo(2));
        Assert.That(phrases[0], Does.Contain("First"));
        Assert.That(phrases[1], Does.Contain("Second"));
    }
}
