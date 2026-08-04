using Microsoft.Extensions.AI;

namespace TaskFlow.Api.Data;

/// <summary>
/// A mock IEmbeddingGenerator for local development that maps semantic keywords 
/// to high-dimensional embedding vectors float[128].
/// </summary>
public class DevMockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("DevMockEmbeddingGenerator");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, 
        EmbeddingGenerationOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        var resultList = new List<Embedding<float>>();

        foreach (var text in values)
        {
            var vector = GenerateDeterministicVector(text, dimensions: 128);
            resultList.Add(new Embedding<float>(vector));
        }

        var generated = new GeneratedEmbeddings<Embedding<float>>(resultList);
        return Task.FromResult(generated);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    /// <summary>
    /// Generates a deterministic vector float[] based on semantic word clusters.
    /// Texts sharing domain concepts (e.g., "Angular", "Signals", "State") produce close vector coordinates.
    /// </summary>
    private static float[] GenerateDeterministicVector(string text, int dimensions)
    {
        var vector = new float[dimensions];
        var textLower = text.ToLowerInvariant();

        // Seed base vector weights based on semantic topic features
        float frontendWeight = (textLower.Contains("angular") || textLower.Contains("signal") || textLower.Contains("rxjs") || textLower.Contains("ui") || textLower.Contains("component") || textLower.Contains("state")) ? 0.85f : 0.05f;
        float backendWeight = (textLower.Contains(".net") || textLower.Contains("c#") || textLower.Contains("api") || textLower.Contains("ef core") || textLower.Contains("database") || textLower.Contains("minimal")) ? 0.85f : 0.05f;
        float devopsWeight = (textLower.Contains("docker") || textLower.Contains("git") || textLower.Contains("ci/cd") || textLower.Contains("tracing") || textLower.Contains("telemetry")) ? 0.85f : 0.05f;

        var hash = text.GetHashCode();
        var rnd = new Random(hash);

        for (int i = 0; i < dimensions; i++)
        {
            float noise = ((float)rnd.NextDouble() - 0.5f) * 0.1f;
            if (i < 40) vector[i] = frontendWeight + noise;
            else if (i < 80) vector[i] = backendWeight + noise;
            else vector[i] = devopsWeight + noise;
        }

        // Normalize vector to unit length (length = 1.0)
        float sumOfSquares = 0f;
        for (int i = 0; i < dimensions; i++) sumOfSquares += vector[i] * vector[i];
        float length = MathF.Sqrt(sumOfSquares);
        for (int i = 0; i < dimensions; i++) vector[i] /= length;

        return vector;
    }
}
