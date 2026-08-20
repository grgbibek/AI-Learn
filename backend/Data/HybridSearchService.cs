namespace TaskFlow.Api.Data;

/// <summary>
/// Keyword-side of hybrid search (BM25) plus Reciprocal Rank Fusion for merging
/// the keyword ranking with a separately-computed vector similarity ranking.
/// </summary>
public class HybridSearchService
{
    private static readonly char[] TokenDelimiters =
        [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-'];

    public static List<string> Tokenize(string text) =>
        text.ToLowerInvariant().Split(TokenDelimiters, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Precomputed inverted index over a corpus: term -> (document position -> term frequency).
    /// Build once per corpus version and reuse across questions, instead of re-tokenizing every
    /// document and re-scanning the whole corpus for document-frequency counts on every single ask.
    /// </summary>
    public sealed record Bm25Index(
        int DocumentCount,
        double AverageDocumentLength,
        IReadOnlyList<int> DocumentLengths,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> Postings);

    public Bm25Index BuildIndex(IReadOnlyList<string> corpus)
    {
        var docTokens = corpus.Select(Tokenize).ToList();
        var docLengths = docTokens.Select(t => t.Count).ToList();
        var avgDocLength = docLengths.Count > 0 ? docLengths.Average() : 0;

        var postings = new Dictionary<string, Dictionary<int, int>>();
        for (var i = 0; i < docTokens.Count; i++)
        {
            foreach (var group in docTokens[i].GroupBy(t => t))
            {
                if (!postings.TryGetValue(group.Key, out var docFrequencies))
                {
                    docFrequencies = [];
                    postings[group.Key] = docFrequencies;
                }
                docFrequencies[i] = group.Count();
            }
        }

        return new Bm25Index(
            docTokens.Count,
            avgDocLength,
            docLengths,
            postings.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, int>)kv.Value));
    }

    /// <summary>
    /// Scores a query against a prebuilt index by walking only the postings for the query's terms
    /// (documents that don't contain any query term never get visited), instead of the naive
    /// approach of scanning every document in the corpus for every query term.
    /// </summary>
    public List<double> ScoreBm25(string query, Bm25Index index, double k1 = 1.5, double b = 0.75)
    {
        var scores = new double[index.DocumentCount];
        var n = index.DocumentCount;

        foreach (var term in Tokenize(query).Distinct())
        {
            if (!index.Postings.TryGetValue(term, out var postingsForTerm))
            {
                continue;
            }

            var df = postingsForTerm.Count;
            var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));

            foreach (var (docIndex, tf) in postingsForTerm)
            {
                var lengthNorm = index.AverageDocumentLength == 0 ? 1 : index.DocumentLengths[docIndex] / index.AverageDocumentLength;
                var denom = tf + k1 * (1 - b + b * lengthNorm);
                scores[docIndex] += denom == 0 ? 0 : idf * (tf * (k1 + 1)) / denom;
            }
        }

        return scores.ToList();
    }

    /// <summary>
    /// Classic Okapi BM25: term-frequency saturation (k1) + document-length normalization (b).
    /// Returns a raw relevance score per document, higher = more keyword-relevant. Scores are only
    /// meaningful relative to each other within this same corpus/query pair.
    /// </summary>
    public List<double> ScoreBm25(string query, IReadOnlyList<string> corpus, double k1 = 1.5, double b = 0.75)
    {
        var queryTerms = Tokenize(query).Distinct().ToList();
        var docTokens = corpus.Select(Tokenize).ToList();
        var docLengths = docTokens.Select(t => t.Count).ToList();
        var avgDocLength = docLengths.Count > 0 ? docLengths.Average() : 0;
        var n = corpus.Count;

        var docFrequency = queryTerms.ToDictionary(
            term => term,
            term => docTokens.Count(tokens => tokens.Contains(term)));

        var scores = new List<double>(n);
        for (var i = 0; i < n; i++)
        {
            var termFrequencies = docTokens[i].GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            double score = 0;
            foreach (var term in queryTerms)
            {
                var df = docFrequency[term];
                if (df == 0) continue;

                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                var tf = termFrequencies.GetValueOrDefault(term, 0);
                var lengthNorm = avgDocLength == 0 ? 1 : docLengths[i] / avgDocLength;
                var denom = tf + k1 * (1 - b + b * lengthNorm);

                score += denom == 0 ? 0 : idf * (tf * (k1 + 1)) / denom;
            }
            scores.Add(score);
        }
        return scores;
    }

    /// <summary>
    /// Merges independently-ranked result lists by rank position rather than raw score, since a
    /// cosine similarity score and a BM25 score are on incomparable scales. Standard formula: 1/(k+rank).
    /// </summary>
    public static Dictionary<int, double> ReciprocalRankFusion(int k = 60, params IEnumerable<int>[] rankedIndexLists)
    {
        var fused = new Dictionary<int, double>();
        foreach (var rankedList in rankedIndexLists)
        {
            var rank = 1;
            foreach (var index in rankedList)
            {
                fused[index] = fused.GetValueOrDefault(index) + 1.0 / (k + rank);
                rank++;
            }
        }
        return fused;
    }
}
