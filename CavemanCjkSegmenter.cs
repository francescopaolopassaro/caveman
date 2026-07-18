// -----------------------------------------------------------------------------
// <copyright file="CavemanCjkSegmenter.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Dictionary-based Chinese word segmentation, ported from Synthelion's
// Python cjk_segmenter module (part of the 1.4.2 quality pass).</summary>
// -----------------------------------------------------------------------------
namespace caveman.core
{
    /// <summary>
    /// Chinese has no spaces between words, so the shared word-splitting regex used
    /// everywhere else in Caveman matches an entire run of Han characters as a single
    /// "token" — a whole sentence collapses into one unsplittable blob, so no function
    /// word ever matches and compression/detection both silently no-op for Chinese
    /// beyond punctuation stripping.
    /// <para>
    /// This class segments a run of Han characters into real words using the same core
    /// algorithm as jieba's dictionary mode (DAG construction + dynamic-programming
    /// shortest-cost path, https://github.com/fxsjy/jieba) — re-implemented here rather
    /// than taken as a dependency, per Caveman's "zero ML model" positioning: jieba's
    /// dictionary path is not ML at all (a weighted directed-acyclic-graph search over a
    /// static word list), only its optional HMM-based unknown-word tagger is, and this
    /// class deliberately skips that part, falling back to single-character tokens for
    /// out-of-vocabulary runs instead.
    /// </para>
    /// <para>
    /// The dictionary is built from Caveman's own zho worddata (curated function words +
    /// UD-derived lemma surface forms + proper nouns) rather than a bundled frequency
    /// corpus, so segmentation quality tracks whatever vocabulary Caveman already ships —
    /// smaller than jieba's ~350k-word dictionary, but zero extra weight and no
    /// additional license to track.
    /// </para>
    /// </summary>
    public static class CavemanCjkSegmenter
    {
        private const int MaxWordLen = 8;

        private static HashSet<string>? _dictionary;
        private static int _maxLen = 1;
        private static readonly object Lock = new();

        private static HashSet<string> BuildDictionary(FunctionWordProvider provider)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in provider.GetFunctionWords("zho"))
                if (!string.IsNullOrEmpty(w)) words.Add(w);

            var lemmaMap = provider.GetLemmaMap("zho");
            foreach (var kv in lemmaMap)
            {
                if (!string.IsNullOrEmpty(kv.Key)) words.Add(kv.Key);
                if (!string.IsNullOrEmpty(kv.Value)) words.Add(kv.Value);
            }

            var data = provider.LoadWordData("zho");
            if (data?.proper_nouns != null)
                foreach (var w in data.proper_nouns)
                    if (!string.IsNullOrEmpty(w)) words.Add(w);

            return words;
        }

        private static HashSet<string> GetDictionary(FunctionWordProvider provider)
        {
            if (_dictionary != null) return _dictionary;
            lock (Lock)
            {
                if (_dictionary != null) return _dictionary;
                _dictionary = BuildDictionary(provider);
                _maxLen = _dictionary.Count == 0 ? 1 : Math.Min(MaxWordLen, _dictionary.Max(w => w.Length));
            }
            return _dictionary;
        }

        /// <summary>
        /// True for CJK Unified Ideographs (the common Han block Caveman's zho worddata is
        /// built from). Deliberately narrow — punctuation, digits, and Latin text mixed
        /// into Chinese input are handled by the existing tokenizer regex, not this class.
        /// </summary>
        public static bool IsHan(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
        }

        /// <summary>
        /// Segments a contiguous run of Han characters into words.
        /// <para>
        /// DAG + dynamic programming, longest-match-biased: for each start position, every
        /// dictionary word starting there is an edge to the position right after it; the
        /// best path end-to-start maximises the sum of each edge's weight (word length
        /// squared, since no real corpus frequency data is available here — this favours
        /// fewer, longer matched words over many single-character fallbacks, the same bias
        /// jieba's frequency-based scoring achieves via real corpus statistics). A position
        /// with no dictionary word starting there falls back to a single-character token,
        /// so segmentation always terminates and never drops content.
        /// </para>
        /// </summary>
        public static List<string> SegmentHanRun(string text, FunctionWordProvider provider)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            var dictionary = GetDictionary(provider);
            int n = text.Length;
            int maxLen = _maxLen;

            var dag = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                dag[i] = new List<int>();
                int limit = Math.Min(n, i + maxLen);
                for (int j = i + 1; j <= limit; j++)
                {
                    if (dictionary.Contains(text.Substring(i, j - i)))
                        dag[i].Add(j);
                }
            }

            var bestCost = new double[n + 1];
            var choice = new int[n];
            for (int i = 0; i < n; i++) choice[i] = i + 1;

            for (int i = n - 1; i >= 0; i--)
            {
                double best = -1;
                int bestJ = i + 1;
                var candidates = dag[i].Count > 0 ? dag[i] : new List<int> { i + 1 };
                foreach (var j in candidates)
                {
                    double score = (double)(j - i) * (j - i) + bestCost[j];
                    if (score > best)
                    {
                        best = score;
                        bestJ = j;
                    }
                }
                bestCost[i] = best;
                choice[i] = bestJ;
            }

            int pos = 0;
            while (pos < n)
            {
                int j = choice[pos];
                result.Add(text.Substring(pos, j - pos));
                pos = j;
            }
            return result;
        }

        /// <summary>Test/dev helper: forces the dictionary to rebuild on next use (e.g.
        /// after worddata files change on disk).</summary>
        public static void ResetCache()
        {
            lock (Lock)
            {
                _dictionary = null;
                _maxLen = 1;
            }
        }
    }
}
