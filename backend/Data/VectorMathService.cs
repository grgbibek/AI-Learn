using System.Numerics.Tensors;

namespace TaskFlow.Api.Data;

public class VectorMathService
{
    /// <summary>
    /// Computes Cosine Similarity between two float vectors using .NET 10 SIMD TensorPrimitives.
    /// Returns a value between 1.0 (identical meaning) and -1.0 (opposite meaning).
    /// </summary>
    public float CalculateCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException("Vectors must have identical dimensions.");
        }

        // Hardware-accelerated SIMD calculation in .NET 10 (System.Numerics.Tensors)
        return TensorPrimitives.CosineSimilarity(vectorA, vectorB);
    }
}
