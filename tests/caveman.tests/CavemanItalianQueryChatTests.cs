using caveman.core;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanItalianQueryChatTests
    {
        private CavemanCompressionService _compressor;

        private static readonly CavemanCompressionLevel[] Levels =
        {
            CavemanCompressionLevel.Light,
            CavemanCompressionLevel.Semantic,
            CavemanCompressionLevel.Aggressive,
        };

        [OneTimeSetUp]
        public void Setup()
        {
            _compressor = new CavemanCompressionService();
        }

        private static IEnumerable<TestCaseData> Queries()
        {
            yield return Case("quanti cittadini sono nati dopo il 2022", "quanti", "dopo", "2022");
            yield return Case("quanti movimenti di immigrazione ci sono dal 2022 in poi", "quanti", "dal", "2022");
            yield return Case("elenca i protocolli del 2026 con importo maggiore di 500 euro", "elenca", "maggiore", "500", "euro");
            yield return Case("mostrami le pratiche non ancora chiuse entro il 31/12/2025", "non", "entro", "31/12/2025");
            yield return Case("somma degli importi versati tra il 01/01/2024 e il 30/06/2024", "somma", "tra", "01/01/2024", "30/06/2024");
            yield return Case("media dei consumi superiore a 120 mq", "media", "superiore", "120");
            yield return Case("chi non ha pagato la TARI almeno una volta", "non", "almeno");
            yield return Case("quante persone hanno più di 65 anni", "quante", "più", "65");
            yield return Case("cittadini con età minore o uguale a 18 anni", "minore", "uguale", "18");
            yield return Case("immobili senza rendita catastale", "senza");
            yield return Case("nessun cittadino deve risultare senza codice fiscale", "nessun", "senza");
            yield return Case("percentuale di residenti stranieri sul totale escluso chi è deceduto", "percentuale", "escluso", "totale");
            yield return Case("ordina per data decrescente e mostra i primi 10", "ordina", "decrescente", "primi", "10");
            yield return Case("scarica il report da https://example.org/report?id=5 solo per il 2025", "https://example.org/report?id=5", "solo", "2025");
            yield return Case("invia il risultato a mario.rossi@example.it solo se maggiore di 3,5%", "mario.rossi@example.it", "solo", "maggiore", "3,5%");
            yield return Case("quanti sono i nati prima del 1950 e ancora viventi", "quanti", "prima", "1950");
            yield return Case("totale versamenti fino al 15/03/2026 compreso", "totale", "fino", "15/03/2026", "compreso");
            yield return Case("conta i documenti protocollati nel mese di novembre 2026", "conta", "novembre", "2026");
            yield return Case("quali pratiche scadono tra 30 giorni", "quali", "tra", "30", "giorni");
            yield return Case("mostra gli importi inferiori a 1.250,00 euro", "inferiori", "euro");
        }

        private static TestCaseData Case(string text, params string[] mustSurvive) =>
            new TestCaseData(text, mustSurvive).SetName(text);

        [TestCaseSource(nameof(Queries))]
        public void QueryKeepsItsMeaningAtEveryLevel(string text, string[] mustSurvive)
        {
            foreach (var level in Levels)
            {
                var compressed = _compressor.ApplyCompression(text, "ita", level).CompressedText.ToLowerInvariant();

                foreach (var word in mustSurvive)
                {
                    Assert.That(compressed, Does.Contain(word.ToLowerInvariant()), $"{level}: '{word}' mancante in '{compressed}'");
                }
            }
        }

        [TestCase("how many citizens were born after 2022", "after", "2022")]
        [TestCase("list invoices greater than 500 euro before 2025", "greater", "500", "before")]
        [TestCase("sum of payments between 2024 and 2025 excluding refunds", "between", "excluding")]
        [TestCase("which records are not closed until 2026", "not", "until", "2026")]
        public void EnglishQueryKeepsItsMeaningAtEveryLevel(string text, params string[] mustSurvive)
        {
            foreach (var level in Levels)
            {
                var compressed = _compressor.ApplyCompression(text, "eng", level).CompressedText.ToLowerInvariant();

                foreach (var word in mustSurvive)
                {
                    Assert.That(compressed, Does.Contain(word), $"{level}: '{word}' mancante in '{compressed}'");
                }
            }
        }

        [Test]
        public void WordNextToNumberIsKept()
        {
            const string text = "mostra tutti quelli con almeno 3 figli e al massimo 2 auto";

            foreach (var level in Levels)
            {
                var compressed = _compressor.ApplyCompression(text, "ita", level).CompressedText.ToLowerInvariant();

                Assert.That(compressed, Does.Contain("almeno"), level.ToString());
                Assert.That(compressed, Does.Contain("3"), level.ToString());
                Assert.That(compressed, Does.Contain("massimo"), level.ToString());
                Assert.That(compressed, Does.Contain("2"), level.ToString());
            }
        }

        [Test]
        public void EveryShippedLanguageHasWellFormedKeepData()
        {
            var provider = new FunctionWordProvider();

            foreach (var iso3 in provider.GetAllSupportedIso3())
            {
                var words = MeaningWordProvider.Get(iso3);

                Assert.That(words.QueryWords.Count, Is.GreaterThan(0), iso3);
            }

            Assert.That(MeaningWordProvider.Get("ita").QueryWords.Count, Is.GreaterThanOrEqualTo(1000));
            Assert.That(MeaningWordProvider.Get("eng").QueryWords.Count, Is.GreaterThanOrEqualTo(1000));
            Assert.That(MeaningWordProvider.Get("ita").Negations, Does.Contain("non"));
            Assert.That(MeaningWordProvider.Get("eng").Negations, Does.Contain("not"));
        }
    }
}
