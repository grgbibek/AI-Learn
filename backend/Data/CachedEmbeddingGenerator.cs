using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;

namespace TaskFlow.Api.Data;

public sealed class CachedEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    IMemoryCache cache,
    ILogger<CachedEmbeddingGenerator> logger) : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(2),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
    };

    public EmbeddingGeneratorMetadata Metadata => new("CachedEmbeddingGenerator");

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options is not null)
        {
            return await inner.GenerateAsync(values, options, cancellationToken);
        }

        var inputs = values.ToList();
        var results = new Embedding<float>?[inputs.Count];
        var misses = new List<(int Index, string Text, string CacheKey)>();

        for (var index = 0; index < inputs.Count; index++)
        {
            var cacheKey = BuildCacheKey(inputs[index]);
            if (cache.TryGetValue<Embedding<float>>(cacheKey, out var cachedEmbedding))
            {
                results[index] = cachedEmbedding;
                continue;
            }

            misses.Add((index, inputs[index], cacheKey));
        }

        if (misses.Count > 0)
        {
            var generated = await inner.GenerateAsync(misses.Select(miss => miss.Text), cancellationToken: cancellationToken);
            for (var index = 0; index < misses.Count; index++)
            {
                var embedding = generated[index];
                var miss = misses[index];
                results[miss.Index] = embedding;
                cache.Set(miss.CacheKey, embedding, CacheOptions);
            }
        }

        logger.LogDebug(
            "Embedding cache served {HitCount} hit(s) and {MissCount} miss(es).",
            inputs.Count - misses.Count,
            misses.Count);

        return new GeneratedEmbeddings<Embedding<float>>(results.Select(result => result!).ToList());
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        inner.Dispose();
    }

    private static string BuildCacheKey(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{AppCacheKeys.EmbeddingPrefix}{Convert.ToHexString(hash)}";
    }
}