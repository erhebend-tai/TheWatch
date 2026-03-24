// IEmbeddingPort — domain port for generating vector embeddings from text.
// NO AI SDK imports allowed in this file. Adapters implement this per-provider:
//   - AzureOpenAIEmbeddingAdapter  → text-embedding-3-large (1536/3072 dims)
//   - GeminiEmbeddingAdapter       → text-embedding-004 (768 dims)
//   - VoyageAIEmbeddingAdapter     → voyage-3 (1024 dims)
//   - MockEmbeddingAdapter         → random unit vectors for testing
//
// Each provider's embeddings are stored in that provider's native vector store:
//   Azure OpenAI → CosmosDB | Gemini → Firestore | Voyage AI → Qdrant
//
// Example:
//   var result = await embeddingPort.EmbedAsync("How does offline evidence sync work?", ct);
//   // result.Embedding = float[1024] (if VoyageAI)
//   var batch = await embeddingPort.EmbedBatchAsync(new[] { "chunk1", "chunk2" }, ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IEmbeddingPort
{
    /// <summary>Which provider this adapter uses.</summary>
    EmbeddingProvider Provider { get; }

    /// <summary>Dimensionality of the embeddings produced (e.g., 1536, 768, 1024).</summary>
    int Dimensions { get; }

    /// <summary>Generate an embedding for a single text input.</summary>
    Task<StorageResult<float[]>> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Generate embeddings for multiple texts in one API call (batch efficiency).</summary>
    Task<StorageResult<float[][]>> EmbedBatchAsync(string[] texts, CancellationToken ct = default);

    /// <summary>
    /// Embed text and wrap it in a VectorDocument ready for storage.
    /// Sets Provider, Embedding, and estimates token count.
    /// </summary>
    Task<StorageResult<VectorDocument>> EmbedDocumentAsync(string content, string source, string ns, List<string>? tags = null, CancellationToken ct = default);
}
