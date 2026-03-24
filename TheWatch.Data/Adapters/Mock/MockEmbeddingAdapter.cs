// MockEmbeddingAdapter — generates deterministic pseudo-random embeddings for testing.
// Produces consistent vectors for the same input text (hash-seeded RNG) so that
// vector search tests are repeatable. Dimensions = 128 (small for fast tests).
//
// Example: services.AddSingleton<IEmbeddingPort, MockEmbeddingAdapter>();

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockEmbeddingAdapter : IEmbeddingPort
{
    public EmbeddingProvider Provider => EmbeddingProvider.Mock;
    public int Dimensions => 128;

    public Task<StorageResult<float[]>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var embedding = GenerateDeterministicEmbedding(text, Dimensions);
        return Task.FromResult(StorageResult<float[]>.Ok(embedding));
    }

    public Task<StorageResult<float[][]>> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
    {
        var embeddings = texts.Select(t => GenerateDeterministicEmbedding(t, Dimensions)).ToArray();
        return Task.FromResult(StorageResult<float[][]>.Ok(embeddings));
    }

    public async Task<StorageResult<VectorDocument>> EmbedDocumentAsync(string content, string source, string ns, List<string>? tags = null, CancellationToken ct = default)
    {
        var embedResult = await EmbedAsync(content, ct);
        if (!embedResult.Success)
            return StorageResult<VectorDocument>.Fail(embedResult.ErrorMessage ?? "Embedding failed");

        var doc = new VectorDocument
        {
            Content = content,
            Embedding = embedResult.Data!,
            Source = source,
            Namespace = ns,
            Tags = tags ?? new(),
            Provider = Provider,
            Store = VectorStoreProvider.Mock,
            EstimatedTokens = content.Length / 4
        };

        return StorageResult<VectorDocument>.Ok(doc);
    }

    /// <summary>
    /// Generate a unit vector deterministically from the text's hash.
    /// Same text always produces the same embedding for test repeatability.
    /// </summary>
    private static float[] GenerateDeterministicEmbedding(string text, int dims)
    {
        var seed = text.GetHashCode();
        var rng = new Random(seed);
        var vec = new float[dims];
        float norm = 0;

        for (int i = 0; i < dims; i++)
        {
            vec[i] = (float)(rng.NextDouble() * 2 - 1); // [-1, 1]
            norm += vec[i] * vec[i];
        }

        // Normalize to unit vector
        norm = MathF.Sqrt(norm);
        if (norm > 0)
            for (int i = 0; i < dims; i++)
                vec[i] /= norm;

        return vec;
    }
}
