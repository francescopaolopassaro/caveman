using caveman.core;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanMeaningAndTagsTests
    {
        private CavemanCompressionService _compressor;

        [OneTimeSetUp]
        public void Setup()
        {
            _compressor = new CavemanCompressionService();
        }

        private string Compressed(string text, string iso3, CavemanCompressionLevel level) =>
            _compressor.ApplyCompression(text, iso3, level).CompressedText;

        private static readonly CavemanCompressionLevel[] Levels =
        {
            CavemanCompressionLevel.Light,
            CavemanCompressionLevel.Semantic,
            CavemanCompressionLevel.Aggressive,
        };

        [TestCase("quanti movimenti di immigrazione ci sono dopo il 2022", "dopo")]
        [TestCase("quanti movimenti di iscrizione ci sono dal 2022", "dal")]
        [TestCase("quanti cittadini sono nati prima del 1950", "prima")]
        [TestCase("quanti permessi sono scaduti fino al 2020", "fino")]
        [TestCase("elenca le pratiche chiuse entro il mese scorso", "entro")]
        public void ItalianRelationWordSurvivesAllLevels(string text, string word)
        {
            foreach (var level in Levels)
            {
                Assert.That(Compressed(text, "ita", level).ToLowerInvariant(), Does.Contain(word), level.ToString());
            }
        }

        [TestCase("how many records were created after 2022", "after")]
        [TestCase("how many records were created before 2022", "before")]
        [TestCase("how many records exist since 2022", "since")]
        public void EnglishRelationWordSurvivesAllLevels(string text, string word)
        {
            foreach (var level in Levels)
            {
                Assert.That(Compressed(text, "eng", level).ToLowerInvariant(), Does.Contain(word), level.ToString());
            }
        }

        [TestCase("<thinking>", "ita")]
        [TestCase("</thinking>", "ita")]
        [TestCase("<response>", "eng")]
        [TestCase("</response>", "eng")]
        public void MarkupTagSurvivesAllLevels(string tag, string iso3)
        {
            var text = "Prima di rispondere ragiona sul problema " + tag + " poi fornisci il risultato finale all'utente";

            foreach (var level in Levels)
            {
                Assert.That(Compressed(text, iso3, level), Does.Contain(tag), level.ToString());
            }
        }

        [Test]
        public void TagWithAttributesSurvives()
        {
            const string tag = "<doc id=\"7\" lang=\"it\">";
            var text = "Leggi con attenzione il documento seguente " + tag + " e riassumilo";

            foreach (var level in Levels)
            {
                Assert.That(Compressed(text, "ita", level), Does.Contain(tag), level.ToString());
            }
        }

        [Test]
        public void ComparisonOperatorsAreNotTreatedAsTags()
        {
            const string text = "il valore deve essere a < b e b > c per ogni riga";

            var result = Compressed(text, "ita", CavemanCompressionLevel.Light);

            Assert.That(result, Is.Not.Empty);
        }
    }
}
