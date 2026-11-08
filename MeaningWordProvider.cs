using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;

namespace caveman.core
{
    public static class MeaningWordProvider
    {
        private static readonly Assembly Assembly = typeof(MeaningWordProvider).Assembly;
        private static readonly ConcurrentDictionary<string, MeaningWords> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static MeaningWords Get(string iso3)
        {
            if (string.IsNullOrWhiteSpace(iso3))
            {
                return MeaningWords.Empty;
            }

            return Cache.GetOrAdd(iso3.Trim().ToLowerInvariant(), Load);
        }

        public static bool IsNegation(string word, string iso3) =>
            !string.IsNullOrEmpty(word) && Get(iso3).IsNegation(word);

        public static bool IsQueryWord(string word, string iso3) =>
            !string.IsNullOrEmpty(word) && Get(iso3).QueryWords.Contains(word.ToLowerInvariant());

        private static MeaningWords Load(string iso3)
        {
            using var raw = Assembly.GetManifestResourceStream($"caveman.core.worddata.{iso3}.keep.yaml.br");

            if (raw is null)
            {
                return MeaningWords.Empty;
            }

            using var brotli = new BrotliStream(raw, CompressionMode.Decompress);
            using var reader = new StreamReader(brotli);

            var negations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queryWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string>? current = null;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (!char.IsWhiteSpace(line[0]))
                {
                    current = line.TrimEnd().TrimEnd(':') switch
                    {
                        "negations" => negations,
                        "query_words" => queryWords,
                        _ => null
                    };
                    continue;
                }

                var text = line.Trim();

                if (current is null || text.Length < 3 || text[0] != '-')
                {
                    continue;
                }

                var word = text[1..].Trim().Trim('"').Replace("\\\"", "\"");

                if (word.Length > 0)
                {
                    current.Add(word);
                }
            }

            return new MeaningWords(negations, queryWords, iso3.Equals("zho", StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class MeaningWords
    {
        public static readonly MeaningWords Empty = new(new HashSet<string>(), new HashSet<string>(), false);

        private readonly bool negationPrefixes;

        public MeaningWords(HashSet<string> negations, HashSet<string> queryWords, bool negationPrefixes)
        {
            Negations = negations;
            QueryWords = queryWords;
            this.negationPrefixes = negationPrefixes;
        }

        public HashSet<string> Negations { get; }

        public HashSet<string> QueryWords { get; }

        public bool IsNegation(string word)
        {
            var lower = word.ToLowerInvariant();

            if (Negations.Contains(lower))
            {
                return true;
            }

            return negationPrefixes && Negations.Any(prefix => lower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsMeaningBearing(string word) =>
            IsNegation(word) || QueryWords.Contains(word.ToLowerInvariant());
    }
}
