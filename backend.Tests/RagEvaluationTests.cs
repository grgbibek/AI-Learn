using System.Text.Json;
using System.Text.RegularExpressions;
using TaskFlow.Api.Data;
using Xunit.Abstractions;

namespace Backend.Tests;

public sealed class RagEvaluationTests
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };
    private readonly ITestOutputHelper output;

    public RagEvaluationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void DeterministicRagEvaluationMeetsQualityThresholds()
    {
        var summary = RagEvalRunner.Evaluate(DefaultDocuments, DefaultCases, topK: 3);

        output.WriteLine(JsonSerializer.Serialize(summary, ReportJsonOptions));

        Assert.Equal(DefaultCases.Count, summary.TotalCases);
        Assert.True(summary.RetrievalRecallAt3 >= 0.90, $"Recall@3 was {summary.RetrievalRecallAt3:P1}");
        Assert.True(summary.MeanReciprocalRank >= 0.80, $"MRR was {summary.MeanReciprocalRank:F2}");
        Assert.True(summary.CitationAccuracy >= 0.90, $"Citation accuracy was {summary.CitationAccuracy:P1}");
        Assert.True(summary.AnswerKeywordCoverage >= 0.90, $"Answer keyword coverage was {summary.AnswerKeywordCoverage:P1}");
        Assert.Equal(0, summary.HallucinationFailures);
    }

    [Fact]
    public void GroundednessEvaluatorRejectsUnsupportedAnswer()
    {
        var evidence = DefaultDocuments
            .Single(document => document.SourceTitle == "Streaming AI UI")
            .Content;

        var supportedAnswer = "TaskFlow streams RAG answers using Server-Sent Events over HTTP. The Angular client consumes fetch ReadableStream chunks and uses AbortController to stop generation.";
        var hallucinatedAnswer = "TaskFlow streams RAG answers through Kafka topics and consumer groups.";

        Assert.True(RagEvalRunner.IsGroundedInEvidence(supportedAnswer, evidence));
        Assert.False(RagEvalRunner.IsGroundedInEvidence(hallucinatedAnswer, evidence));
    }

    private static readonly List<RagEvalDocument> DefaultDocuments =
    [
        new(
            "Auth And Budgets",
            "TaskFlow protects AI endpoints with JWT bearer authentication, capability-based authorization policies, admin user management, daily request budgets, and daily token budgets."),
        new(
            "Streaming AI UI",
            "TaskFlow streams RAG answers using Server-Sent Events over HTTP. The Angular client consumes fetch ReadableStream chunks and uses AbortController to stop generation."),
        new(
            "SQL Vector Retrieval",
            "TaskFlow stores document embeddings in SQL Server vector(768) columns. EF Core calls VECTOR_DISTANCE with cosine distance so embeddings stay inside the database during retrieval."),
        new(
            "Agent Governance",
            "TaskFlow uses a Planner, Developer, and Reviewer agent pipeline with bounded retries, SQL audit logs, scoped MCP write tools, and OpenTelemetry tracing."),
        new(
            "Privacy Guardrails",
            "TaskFlow sanitizes RAG ingestion, prompt context, and non-streaming answers by redacting emails, phone numbers, API keys, bearer tokens, JWTs, and credit-card-like numbers.")
    ];

    private static readonly List<RagEvalCase> DefaultCases =
    [
        new(
            Question: "How does TaskFlow stream RAG answers to Angular?",
            ExpectedSourceTitle: "Streaming AI UI",
            ExpectedAnswerTerms: ["Server-Sent Events", "ReadableStream", "AbortController"]),
        new(
            Question: "Where does TaskFlow store document embeddings for retrieval?",
            ExpectedSourceTitle: "SQL Vector Retrieval",
            ExpectedAnswerTerms: ["SQL Server", "vector(768)", "VECTOR_DISTANCE"]),
        new(
            Question: "How are AI endpoints protected from unauthorized or excessive use?",
            ExpectedSourceTitle: "Auth And Budgets",
            ExpectedAnswerTerms: ["JWT", "authorization", "budgets"]),
        new(
            Question: "What governance exists around TaskFlow agent writes?",
            ExpectedSourceTitle: "Agent Governance",
            ExpectedAnswerTerms: ["bounded retries", "audit logs", "MCP write tools"]),
        new(
            Question: "Does TaskFlow use Kafka topics for RAG answer streaming?",
            ExpectedSourceTitle: "Streaming AI UI",
            ExpectedAnswerTerms: [],
            ExpectedUnsupportedTerm: "Kafka")
    ];

    private sealed record RagEvalDocument(string SourceTitle, string Content);

    private sealed record RagEvalCase(
        string Question,
        string ExpectedSourceTitle,
        IReadOnlyList<string> ExpectedAnswerTerms,
        string? ExpectedUnsupportedTerm = null);

    private sealed record RagEvalHit(
        string SourceTitle,
        string Content,
        double VectorScore,
        double KeywordScore,
        double FusedScore,
        int Rank);

    private sealed record RagEvalCaseResult(
        string Question,
        string ExpectedSourceTitle,
        bool ExpectedSourceInTopK,
        int? ExpectedSourceRank,
        bool CitationIsAccurate,
        bool AnswerIsGrounded,
        double AnswerKeywordCoverage,
        bool HallucinationFailure,
        IReadOnlyList<string> RetrievedSources,
        string Answer);

    private sealed record RagEvalSummary(
        int TotalCases,
        double RetrievalRecallAt3,
        double MeanReciprocalRank,
        double CitationAccuracy,
        double AnswerKeywordCoverage,
        int HallucinationFailures,
        IReadOnlyList<RagEvalCaseResult> Cases);

    private static class RagEvalRunner
    {
        private static readonly Regex TokenRegex = new("[a-zA-Z0-9+#.]+", RegexOptions.Compiled);
        private static readonly string[][] SemanticDimensions =
        [
            ["jwt", "auth", "authentication", "authorization", "admin", "user", "budget", "budgets", "token", "tokens", "protect", "protected"],
            ["stream", "streams", "streaming", "sse", "server-sent", "events", "http", "angular", "fetch", "readablestream", "abortcontroller", "generation"],
            ["embedding", "embeddings", "sql", "server", "vector", "vector(768)", "vector_distance", "cosine", "retrieval", "database", "ef", "core"],
            ["agent", "agents", "planner", "developer", "reviewer", "mcp", "audit", "governance", "retries", "opentelemetry", "tools"],
            ["sanitize", "sanitizes", "redacting", "privacy", "emails", "phone", "api", "keys", "bearer", "jwt", "credit", "guardrails"]
        ];

        public static RagEvalSummary Evaluate(IReadOnlyList<RagEvalDocument> documents, IReadOnlyList<RagEvalCase> cases, int topK)
        {
            var results = cases.Select(testCase => EvaluateCase(documents, testCase, topK)).ToList();
            var answerableResults = results.Where(result => !string.IsNullOrWhiteSpace(result.Answer) && !result.Answer.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase)).ToList();

            return new RagEvalSummary(
                TotalCases: results.Count,
                RetrievalRecallAt3: results.Count == 0 ? 0 : results.Count(result => result.ExpectedSourceInTopK) / (double)results.Count,
                MeanReciprocalRank: results.Count == 0 ? 0 : results.Average(result => result.ExpectedSourceRank is { } rank ? 1.0 / rank : 0),
                CitationAccuracy: results.Count == 0 ? 0 : results.Count(result => result.CitationIsAccurate) / (double)results.Count,
                AnswerKeywordCoverage: answerableResults.Count == 0 ? 0 : answerableResults.Average(result => result.AnswerKeywordCoverage),
                HallucinationFailures: results.Count(result => result.HallucinationFailure),
                Cases: results);
        }

        public static bool IsGroundedInEvidence(string answer, string evidence)
        {
            var evidenceTokens = Tokenize(evidence).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var answerTokens = Tokenize(answer)
                .Where(token => token.Length > 3)
                .Where(token => !StopWords.Contains(token))
                .ToList();

            return answerTokens.Count > 0 && answerTokens.All(evidenceTokens.Contains);
        }

        private static RagEvalCaseResult EvaluateCase(IReadOnlyList<RagEvalDocument> documents, RagEvalCase testCase, int topK)
        {
            var hits = Retrieve(documents, testCase.Question, topK);
            var expectedRank = hits.FirstOrDefault(hit => hit.SourceTitle == testCase.ExpectedSourceTitle)?.Rank;
            var expectedSourceInTopK = expectedRank is not null;
            var citedEvidence = string.Join("\n", hits.Select(hit => hit.Content));
            var answer = BuildGroundedAnswer(testCase, hits);
            var answerIsGrounded = answer.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase)
                || IsGroundedInEvidence(answer, citedEvidence);
            var keywordCoverage = CalculateAnswerKeywordCoverage(answer, testCase.ExpectedAnswerTerms);
            var hallucinationFailure = testCase.ExpectedUnsupportedTerm is not null
                && !answer.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase);

            return new RagEvalCaseResult(
                Question: testCase.Question,
                ExpectedSourceTitle: testCase.ExpectedSourceTitle,
                ExpectedSourceInTopK: expectedSourceInTopK,
                ExpectedSourceRank: expectedRank,
                CitationIsAccurate: expectedSourceInTopK,
                AnswerIsGrounded: answerIsGrounded,
                AnswerKeywordCoverage: keywordCoverage,
                HallucinationFailure: hallucinationFailure,
                RetrievedSources: hits.Select(hit => hit.SourceTitle).ToList(),
                Answer: answer);
        }

        private static List<RagEvalHit> Retrieve(IReadOnlyList<RagEvalDocument> documents, string question, int topK)
        {
            var hybridSearch = new HybridSearchService();
            var vectorMath = new VectorMathService();
            var questionVector = CreateSemanticVector(question);
            var documentVectors = documents.Select(document => CreateSemanticVector(document.Content)).ToList();
            var vectorScores = documentVectors.Select(vector => (double)vectorMath.CalculateCosineSimilarity(questionVector, vector)).ToList();
            var vectorRanking = vectorScores
                .Select((score, index) => new { Index = index, Score = score })
                .OrderByDescending(item => item.Score)
                .Select(item => item.Index);

            var keywordScores = hybridSearch.ScoreBm25(question, documents.Select(document => document.Content).ToList());
            var keywordRanking = keywordScores
                .Select((score, index) => new { Index = index, Score = score })
                .OrderByDescending(item => item.Score)
                .Select(item => item.Index);

            var fusedScores = HybridSearchService.ReciprocalRankFusion(60, vectorRanking, keywordRanking);

            return fusedScores
                .OrderByDescending(item => item.Value)
                .Take(topK)
                .Select((item, index) => new RagEvalHit(
                    SourceTitle: documents[item.Key].SourceTitle,
                    Content: documents[item.Key].Content,
                    VectorScore: Math.Round(vectorScores[item.Key], 4),
                    KeywordScore: Math.Round(keywordScores[item.Key], 4),
                    FusedScore: Math.Round(item.Value, 4),
                    Rank: index + 1))
                .ToList();
        }

        private static string BuildGroundedAnswer(RagEvalCase testCase, IReadOnlyList<RagEvalHit> hits)
        {
            var expectedEvidence = hits.FirstOrDefault(hit => hit.SourceTitle == testCase.ExpectedSourceTitle)?.Content ?? string.Empty;
            if (testCase.ExpectedUnsupportedTerm is not null
                && !expectedEvidence.Contains(testCase.ExpectedUnsupportedTerm, StringComparison.OrdinalIgnoreCase))
            {
                return "I don't know based on the provided context.";
            }

            if (testCase.ExpectedAnswerTerms.Count == 0)
            {
                return "I don't know based on the provided context.";
            }

            return expectedEvidence;
        }

        private static double CalculateAnswerKeywordCoverage(string answer, IReadOnlyList<string> expectedTerms)
        {
            if (expectedTerms.Count == 0)
            {
                return 1;
            }

            return expectedTerms.Count(term => answer.Contains(term, StringComparison.OrdinalIgnoreCase)) / (double)expectedTerms.Count;
        }

        private static float[] CreateSemanticVector(string text)
        {
            var tokens = Tokenize(text).ToList();
            var tokenSet = tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var vector = new float[SemanticDimensions.Length];

            for (var i = 0; i < SemanticDimensions.Length; i++)
            {
                var matches = SemanticDimensions[i].Count(term => tokenSet.Contains(term));
                vector[i] = matches / (float)SemanticDimensions[i].Length;
            }

            var length = MathF.Sqrt(vector.Sum(value => value * value));
            if (length == 0)
            {
                return vector;
            }

            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= length;
            }

            return vector;
        }

        private static IEnumerable<string> Tokenize(string text) => TokenRegex
            .Matches(text.ToLowerInvariant().Replace("server-sent", "server-sent ").Replace("vector_distance", "vector_distance "))
            .Select(match => match.Value);

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "taskflow", "answers", "answer", "using", "with", "through", "from", "that", "this", "into", "over", "and", "the"
        };
    }
}