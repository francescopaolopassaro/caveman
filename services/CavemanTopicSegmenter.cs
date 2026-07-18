// -----------------------------------------------------------------------------
// <copyright file="CavemanTopicSegmenter.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>TextTiling-style topic segmentation: splits a document into topically-coherent blocks by tracking vocabulary shift between sentence windows.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core.services;

/// <summary>
/// Splits a document into topically-coherent segments using a TextTiling-style algorithm
/// (Hearst, 1997): group sentences into fixed-size blocks, score the vocabulary similarity
/// between every pair of adjacent blocks, and cut at the "valleys" where similarity drops
/// sharply relative to the similarity peaks on both sides. Pure term-frequency cosine
/// similarity — no external dependency, no model.
/// <para>
/// Segmentation on its own doesn't compress anything; it exists so other components can
/// treat a long, multi-topic document as what it actually is instead of one undifferentiated
/// bag of sentences — e.g. a summarizer can allocate its sentence budget per topic instead of
/// letting one statistically dense topic dominate the whole summary.
/// </para>
/// </summary>
public sealed class CavemanTopicSegmenter
{
    /// <summary>Sentences grouped into one comparison block (default 3). Larger = coarser, more stable boundaries.</summary>
    public int SentencesPerBlock { get; init; } = 3;

    /// <summary>Boundary depth-score threshold as (mean − factor × stddev) of all depth scores (default 0.5 = liberal, matching the original TextTiling paper).</summary>
    public double DepthThresholdFactor { get; init; } = 0.5;

    private readonly CavemanSentenceDetector _sentenceDetector;
    private readonly FunctionWordProvider? _wordProvider;

    public CavemanTopicSegmenter(FunctionWordProvider? wordProvider = null)
    {
        _wordProvider = wordProvider;
        _sentenceDetector = new CavemanSentenceDetector(wordProvider);
    }

    /// <summary>
    /// Segments <paramref name="text"/> into topic blocks. A document too short to form at
    /// least 3 comparison blocks is returned as a single segment spanning the whole text.
    /// </summary>
    public List<TopicSegment> Segment(string text, string? iso3 = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<TopicSegment>();

        var sentences = _sentenceDetector.SplitText(text, iso3);
        if (sentences.Length == 0)
            return new List<TopicSegment>();

        var blocks = GroupIntoBlocks(sentences, SentencesPerBlock);
        if (blocks.Count < 3)
            return new List<TopicSegment> { WholeDocument(sentences) };

        var functionWords = !string.IsNullOrEmpty(iso3) && _wordProvider != null
            ? _wordProvider.GetFunctionWords(iso3)
            : null;

        var vectors = blocks.Select(b => BuildTermVector(b, functionWords)).ToList();

        var similarities = new double[blocks.Count - 1];
        for (int i = 0; i < similarities.Length; i++)
            similarities[i] = CosineSimilarity(vectors[i], vectors[i + 1]);

        var depths = ComputeDepthScores(similarities);
        if (depths.Length == 0)
            return new List<TopicSegment> { WholeDocument(sentences) };

        double mean = depths.Average();
        double stddev = Math.Sqrt(depths.Select(d => (d - mean) * (d - mean)).Average());
        double threshold = mean - DepthThresholdFactor * stddev;

        // A boundary sits between block[i] and block[i+1] when depths[i] clears the
        // threshold — translate that block-gap index into the sentence index where the
        // next segment starts.
        var boundarySentenceIndexes = new List<int>();
        for (int i = 0; i < depths.Length; i++)
        {
            if (depths[i] <= threshold) continue;
            int sentenceIdx = blocks.Take(i + 1).Sum(b => b.Count);
            if (sentenceIdx > 0 && sentenceIdx < sentences.Length)
                boundarySentenceIndexes.Add(sentenceIdx);
        }

        return BuildSegments(sentences, boundarySentenceIndexes);
    }

    private static List<List<CavemanSentence>> GroupIntoBlocks(CavemanSentence[] sentences, int perBlock)
    {
        var blocks = new List<List<CavemanSentence>>();
        for (int i = 0; i < sentences.Length; i += perBlock)
            blocks.Add(sentences.Skip(i).Take(perBlock).ToList());
        return blocks;
    }

    private static Dictionary<string, int> BuildTermVector(List<CavemanSentence> block, HashSet<string>? functionWords)
    {
        var vector = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in block)
            foreach (var token in sentence.Tokens)
            {
                if (token.Category != CavemanTokenCategory.Word) continue;
                var w = token.Value.ToLowerInvariant();
                if (functionWords != null && functionWords.Contains(w)) continue;
                vector[w] = vector.GetValueOrDefault(w) + 1;
            }
        return vector;
    }

    private static double CosineSimilarity(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        double dot = 0, normA = 0, normB = 0;
        foreach (var (term, weight) in a)
        {
            normA += (double)weight * weight;
            if (b.TryGetValue(term, out var bw)) dot += (double)weight * bw;
        }
        foreach (var weight in b.Values) normB += (double)weight * weight;

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    // Depth score at gap i: how far similarity dips below the nearest similarity peak on
    // each side. A deep, isolated valley is a strong topic-boundary signal; a shallow dip
    // (or a valley that's part of a long, gradual decline) is not.
    private static double[] ComputeDepthScores(double[] similarities)
    {
        var depths = new double[similarities.Length];
        for (int i = 0; i < similarities.Length; i++)
        {
            double leftPeak = similarities[i];
            for (int j = i - 1; j >= 0 && similarities[j] >= leftPeak; j--)
                leftPeak = similarities[j];

            double rightPeak = similarities[i];
            for (int j = i + 1; j < similarities.Length && similarities[j] >= rightPeak; j++)
                rightPeak = similarities[j];

            depths[i] = (leftPeak - similarities[i]) + (rightPeak - similarities[i]);
        }
        return depths;
    }

    private static List<TopicSegment> BuildSegments(CavemanSentence[] sentences, List<int> boundaries)
    {
        var segments = new List<TopicSegment>();
        int start = 0;
        foreach (var boundary in boundaries.Distinct().OrderBy(b => b))
        {
            segments.Add(MakeSegment(sentences, start, boundary));
            start = boundary;
        }
        segments.Add(MakeSegment(sentences, start, sentences.Length));
        return segments;
    }

    private static TopicSegment MakeSegment(CavemanSentence[] sentences, int start, int end) => new(
        StartSentence: start,
        EndSentence: end,
        Text: string.Join(" ", sentences.Skip(start).Take(end - start).Select(s => s.Text.Trim())),
        SentenceCount: end - start);

    private static TopicSegment WholeDocument(CavemanSentence[] sentences) =>
        MakeSegment(sentences, 0, sentences.Length);
}

/// <summary>A contiguous, topically-coherent run of sentences within a document.</summary>
public readonly record struct TopicSegment(int StartSentence, int EndSentence, string Text, int SentenceCount);
