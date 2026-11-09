using System.Text.Json;
using caveman.core;
using caveman.core.entities;
using caveman.core.services;
using NUnit.Framework;

namespace caveman.tests;

[TestFixture]
public class CavemanDocumentCompressorTests
{
    private const string Destructive = "## Rules\n\nNever run `rm -rf /` or git push --force on the shared branch without asking the owner first.\n";

    [Test]
    public void GuardOnByDefault_DocumentWithDestructiveCommandIsReturnedUntouched()
    {
        var r = new CavemanDocumentCompressor().Compress(Destructive);
        Assert.That(r.SkippedBySafetyGuard, Is.True);
        Assert.That(r.Text, Is.EqualTo(Destructive));
    }

    [Test]
    public void GuardDisabled_SameDocumentIsCompressed()
    {
        var r = new CavemanDocumentCompressor().Compress(Destructive, new CavemanDocumentOptions { CheckDestructiveCommands = false });
        Assert.That(r.SkippedBySafetyGuard, Is.False);
        Assert.That(r.Text, Does.Not.Contain("##"));
        Assert.That(r.Text, Does.Contain("rm -rf"));
    }

    [Test]
    public void MarkdownDecorationIsStripped_ContentKept()
    {
        const string doc = "# Title\n\n> quoted note\n\n---\n\n**Bold** and *italic* with [site](https://example.com) and `Foo.Bar`.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";
        var r = new CavemanDocumentCompressor().Compress(doc, new CavemanDocumentOptions { CompressProse = false });
        Assert.That(r.Text, Does.Not.Contain("# Title"));
        Assert.That(r.Text, Does.Not.Contain("**"));
        Assert.That(r.Text, Does.Not.Contain("|---|"));
        Assert.That(r.Text, Does.Not.Contain("\n---"));
        Assert.That(r.Text, Does.Contain("Bold and italic with site (https://example.com) and `Foo.Bar`."));
        Assert.That(r.Text, Does.Contain("| 1 | 2 |"));
    }

    [Test]
    public void FencedJson_IsMinified_AndStillParsesToTheSameValue()
    {
        const string json = "{\n  \"a\": 1,\n  \"b\": [1, 2, 3],\n  \"c\": { \"d\": \"x y\" }\n}";
        string doc = "Config:\n\n```json\n" + json + "\n```\n";
        var r = new CavemanDocumentCompressor().Compress(doc, new CavemanDocumentOptions { CompressProse = false });
        int s = r.Text.IndexOf("```json\n", StringComparison.Ordinal) + 8;
        int e = r.Text.IndexOf("\n```", s, StringComparison.Ordinal);
        string body = r.Text[s..e];
        Assert.That(body.Length, Is.LessThan(json.Length));
        Assert.That(JsonElement.DeepEquals(JsonDocument.Parse(body).RootElement, JsonDocument.Parse(json).RootElement), Is.True);
        Assert.That(r.FencedBlocksMinified, Is.EqualTo(1));
    }

    [Test]
    public void Maximum_PreservesOperatorsAndIdentifiers()
    {
        const string doc = "- The value must be >0 and the status != failed when RF-M1-01 passes in src/app/Main.cs, so that the whole thing is considered valid by the reviewer.\n";
        var r = new CavemanDocumentCompressor().Compress(doc, CavemanDocumentOptions.Maximum);
        Assert.That(r.Text, Does.Contain(">0"));
        Assert.That(r.Text, Does.Contain("!="));
        Assert.That(r.Text, Does.Contain("RF-M1-01"));
        Assert.That(r.Text, Does.Contain("src/app/Main.cs"));
    }

    [Test]
    public void OutputIsNeverLongerThanInput_AndCrlfIsKept()
    {
        const string doc = "short\r\ntext\r\n";
        var r = new CavemanDocumentCompressor().Compress(doc);
        Assert.That(r.Text.Length, Is.LessThanOrEqualTo(doc.Length));
        Assert.That(r.SavedChars, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task ExistingLevels_AreUnchangedWhenCodeProtectionIsOff()
    {
        const string text = "The quick brown fox jumps over the lazy dog because it was very tired indeed.";
        var a = await new CavemanCompressionService().CompressAsync(text, CavemanCompressionLevel.Light);
        var b = await new CavemanCompressionService().WithCodeProtection(false).CompressAsync(text, CavemanCompressionLevel.Light);
        Assert.That(b.CompressedText, Is.EqualTo(a.CompressedText));
    }
}
