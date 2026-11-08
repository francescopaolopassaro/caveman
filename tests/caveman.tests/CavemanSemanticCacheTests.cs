using caveman.core.entities;
using caveman.core.services;

namespace caveman.tests;

[TestFixture]
public class CavemanSemanticCacheTests
{
    private static CavemanSemanticCache NewCache(Action<CavemanSemanticCacheOptions>? configure = null)
    {
        var o = new CavemanSemanticCacheOptions();
        configure?.Invoke(o);
        return new CavemanSemanticCache(o);
    }

    [TestCase("ita", "Come posso creare un nuovo file di configurazione per il database?", "Come creare un nuovo file configurazione database?")]
    [TestCase("eng", "How do I create a new configuration file for the database?", "How to create new database configuration file?")]
    [TestCase("fra", "Comment créer un nouveau fichier de configuration pour la base de données ?", "Comment créer nouveau fichier configuration base de données")]
    [TestCase("deu", "Wie erstelle ich eine neue Konfigurationsdatei für die Datenbank?", "Neue Konfigurationsdatei für Datenbank erstellen")]
    [TestCase("spa", "¿Cómo puedo crear un nuevo archivo de configuración para la base de datos?", "Crear nuevo archivo configuración base de datos")]
    [TestCase("por", "Como posso criar um novo arquivo de configuração para o banco de dados?", "Criar novo arquivo configuração banco dados")]
    [TestCase("nld", "Hoe kan ik een nieuw configuratiebestand voor de database maken?", "Nieuw configuratiebestand database maken")]
    [TestCase("rus", "Как создать новый файл конфигурации для базы данных?", "Создать новый файл конфигурации базы данных")]
    public void Get_ParaphraseInSupportedLanguage_IsHit(string iso3, string original, string paraphrase)
    {
        var cache = NewCache(o => o.Language = iso3);
        cache.Set(original, "RISPOSTA");

        Assert.That(cache.TryGet(paraphrase, out var hit), Is.True, $"{iso3}: nessun hit");
        Assert.That(hit.Response, Is.EqualTo("RISPOSTA"));
        Assert.That(hit.Similarity, Is.GreaterThanOrEqualTo(0.65));
        Assert.That(hit.Language, Is.EqualTo(iso3));
    }

    [TestCase("Come posso creare un nuovo file di configurazione per il database?", "Come creare un nuovo file configurazione database?")]
    [TestCase("How do I create a new configuration file for the database?", "How to create new database configuration file?")]
    [TestCase("Wie erstelle ich eine neue Konfigurationsdatei für die Datenbank?", "Neue Konfigurationsdatei für Datenbank erstellen")]
    public void Get_AutoDetectLanguage_IsHit(string original, string paraphrase)
    {
        var cache = NewCache();
        cache.Set(original, "R");
        Assert.That(cache.Get(paraphrase), Is.Not.Null);
    }

    [Test]
    public void Get_ChineseParaphrase_IsHit()
    {
        var cache = NewCache(o => o.Language = "zho");
        cache.Set("如何为数据库创建新的配置文件", "R");
        Assert.That(cache.Get("怎么为数据库创建新的配置文件"), Is.Not.Null);
    }

    [Test]
    public void Get_JapaneseSameQueryDifferentPunctuation_IsHit()
    {
        var cache = NewCache(o => o.Language = "jpn");
        cache.Set("データベースの設定ファイルを作成する方法は？", "R");
        Assert.That(cache.Get("データベースの設定ファイルを作成する方法"), Is.Not.Null);
    }

    [Test]
    public void Get_UnrelatedQuery_IsMiss()
    {
        var cache = NewCache(o => o.Language = "ita");
        cache.Set("Come posso creare un nuovo file di configurazione per il database?", "R");
        Assert.That(cache.Get("Qual è la capitale della Francia e quanti abitanti ha?"), Is.Null);
        Assert.That(cache.Stats.Misses, Is.EqualTo(1));
    }

    [Test]
    public void Get_DifferentFileName_IsMiss()
    {
        var cache = NewCache(o => o.Language = "ita");
        cache.Set("Elimina il file Program.cs dal progetto", "A");
        Assert.That(cache.Get("Elimina il file Startup.cs dal progetto"), Is.Null);
        Assert.That(cache.Get("elimina file Program.cs dal progetto"), Is.Not.Null);
    }

    [Test]
    public void Get_DifferentNumber_IsMiss()
    {
        var cache = NewCache(o => o.Language = "eng");
        cache.Set("Set the retry count to 3 in the settings", "A");
        Assert.That(cache.Get("Set the retry count to 5 in the settings"), Is.Null);
    }

    [Test]
    public void Get_QueryWithJson_TokenizesContentInsteadOfDiscarding()
    {
        var cache = NewCache(o => o.Language = "eng");
        cache.Set("Validate this payload {\"name\": \"alice\", \"role\": \"admin\"}", "A");
        Assert.That(cache.Get("validate payload {\"name\": \"alice\", \"role\": \"admin\"}"), Is.Not.Null);
        Assert.That(cache.Get("validate payload {\"name\": \"bob\", \"role\": \"admin\"}"), Is.Null);
    }

    [Test]
    public void Get_OtherLanguage_IsNotCrossMatched()
    {
        var cache = NewCache();
        cache.Set("How do I create a new configuration file for the database?", "A");
        Assert.That(cache.Get("Come posso creare un nuovo file di configurazione per il database?"), Is.Null);
    }

    [Test]
    public void Get_ThresholdIsConfigurable()
    {
        var strict = NewCache(o => { o.Language = "eng"; o.SimilarityThreshold = 0.99; });
        strict.Set("create configuration file database", "A");
        Assert.That(strict.Get("create configuration file for the database server"), Is.Null);

        var loose = NewCache(o => { o.Language = "eng"; o.SimilarityThreshold = 0.4; });
        loose.Set("create configuration file database", "A");
        Assert.That(loose.Get("create configuration file for the database server"), Is.Not.Null);
    }

    [Test]
    public void Set_BeyondMaxEntries_EvictsLeastRecentlyUsed()
    {
        var cache = NewCache(o => { o.Language = "eng"; o.MaxEntries = 2; });
        cache.Set("alpha beta gamma", "1");
        cache.Set("delta epsilon zeta", "2");
        Assert.That(cache.Get("alpha beta gamma"), Is.Not.Null); // refresh entry 1
        cache.Set("theta kappa lambda", "3");

        Assert.That(cache.Count, Is.EqualTo(2));
        Assert.That(cache.Get("delta epsilon zeta"), Is.Null);
        Assert.That(cache.Get("alpha beta gamma"), Is.Not.Null);
    }

    [Test]
    public void Get_AfterTtl_IsMiss()
    {
        var clock = new ManualClock();
        var cache = NewCache(o => { o.Language = "eng"; o.Ttl = TimeSpan.FromMinutes(5); o.TimeProvider = clock; });
        cache.Set("alpha beta gamma", "1");
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.That(cache.Get("alpha beta gamma"), Is.Not.Null);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(cache.Get("alpha beta gamma"), Is.Null);
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Stats_CountsHitsMissesAndSavedChars()
    {
        var cache = NewCache(o => o.Language = "eng");
        cache.Set("alpha beta gamma", "twelve chars");
        cache.Get("alpha beta gamma");
        cache.Get("totally different words here");
        var s = cache.Stats;
        Assert.That(s.Hits, Is.EqualTo(1));
        Assert.That(s.Misses, Is.EqualTo(1));
        Assert.That(s.CharsSaved, Is.EqualTo(12));
        Assert.That(s.HitRate, Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void Set_SameNormalizedQuery_OverwritesInsteadOfDuplicating()
    {
        var cache = NewCache(o => o.Language = "eng");
        cache.Set("alpha beta gamma", "old");
        cache.Set("Gamma, beta: alpha!", "new");
        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.Get("alpha beta gamma")!.Response, Is.EqualTo("new"));
    }

    [Test]
    public void Get_EmptyOrStopwordOnlyQuery_IsMissAndNeverThrows()
    {
        var cache = NewCache(o => o.Language = "eng");
        cache.Set("alpha beta gamma", "1");
        Assert.That(cache.Get(""), Is.Null);
        Assert.That(cache.Get("   "), Is.Null);
        Assert.That(cache.Get("the of and"), Is.Null);
    }

    [Test]
    public void Cache_ConcurrentUse_IsThreadSafe()
    {
        var cache = NewCache(o => { o.Language = "eng"; o.MaxEntries = 50; });
        Parallel.For(0, 500, i =>
        {
            cache.Set($"query number{i} about topic{i % 7}", "r" + i);
            cache.Get($"query number{i} about topic{i % 7}");
        });
        Assert.That(cache.Count, Is.LessThanOrEqualTo(50));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
