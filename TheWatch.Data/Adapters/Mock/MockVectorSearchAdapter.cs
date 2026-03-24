// MockVectorSearchAdapter — brute-force cosine similarity over an in-memory list.
// No external vector database needed. Good enough for dev/test with < 10K documents.
// Pairs with MockEmbeddingAdapter for a fully self-contained RAG test pipeline.
//
// Example: services.AddSingleton<IVectorSearchPort, MockVectorSearchAdapter>();

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockVectorSearchAdapter : IVectorSearchPort
{
    public VectorStoreProvider StoreProvider => VectorStoreProvider.Mock;

    private readonly ConcurrentDictionary<string, VectorDocument> _docs = new();
    private readonly IEmbeddingPort _embeddingPort;

    public MockVectorSearchAdapter(IEmbeddingPort embeddingPort)
    {
        _embeddingPort = embeddingPort;
    }

    public Task<StorageResult<bool>> UpsertAsync(VectorDocument document, CancellationToken ct = default)
    {
        document.Store = VectorStoreProvider.Mock;
        document.UpdatedAt = DateTime.UtcNow;
        _docs[document.Id] = document;
        return Task.FromResult(StorageResult<bool>.Ok(true));
    }

    public Task<StorageResult<int>> UpsertBatchAsync(IEnumerable<VectorDocument> documents, CancellationToken ct = default)
    {
        int count = 0;
        foreach (var doc in documents)
        {
            doc.Store = VectorStoreProvider.Mock;
            doc.UpdatedAt = DateTime.UtcNow;
            _docs[doc.Id] = doc;
            count++;
        }
        return Task.FromResult(StorageResult<int>.Ok(count));
    }

    public async Task<StorageResult<List<VectorSearchResult>>> SearchAsync(VectorSearchQuery query, CancellationToken ct = default)
    {
        // Get query vector — embed text if no pre-computed vector
        float[] queryVec;
        if (query.Vector is not null)
        {
            queryVec = query.Vector;
        }
        else if (!string.IsNullOrEmpty(query.Text))
        {
            var embedResult = await _embeddingPort.EmbedAsync(query.Text, ct);
            if (!embedResult.Success)
                return StorageResult<List<VectorSearchResult>>.Fail($"Failed to embed query: {embedResult.ErrorMessage}");
            queryVec = embedResult.Data!;
        }
        else
        {
            return StorageResult<List<VectorSearchResult>>.Fail("Query must have either Text or Vector");
        }

        // Filter by namespace and tags
        var candidates = _docs.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(query.Namespace))
            candidates = candidates.Where(d => d.Namespace == query.Namespace);
        if (query.TagFilters is not null && query.TagFilters.Count > 0)
            candidates = candidates.Where(d => query.TagFilters.All(t => d.Tags.Contains(t)));

        // Brute-force cosine similarity
        var scored = candidates
            .Where(d => d.Embedding.Length > 0)
            .Select(d => new { Doc = d, Score = CosineSimilarity(queryVec, d.Embedding) })
            .Where(x => x.Score >= query.MinScore)
            .OrderByDescending(x => x.Score)
            .Take(query.TopK)
            .Select(x => new VectorSearchResult
            {
                DocumentId = x.Doc.Id,
                Score = x.Score,
                Content = query.IncludeContent ? x.Doc.Content : null,
                ContentPreview = x.Doc.Content.Length > 200 ? x.Doc.Content[..200] + "..." : x.Doc.Content,
                Source = x.Doc.Source,
                Namespace = x.Doc.Namespace,
                ChunkIndex = x.Doc.ChunkIndex,
                Tags = x.Doc.Tags,
                Metadata = x.Doc.Metadata
            })
            .ToList();

        return StorageResult<List<VectorSearchResult>>.Ok(scored);
    }

    public Task<StorageResult<bool>> DeleteAsync(string documentId, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<bool>.Ok(_docs.TryRemove(documentId, out _)));

    public Task<StorageResult<int>> DeleteNamespaceAsync(string ns, CancellationToken ct = default)
    {
        var toRemove = _docs.Values.Where(d => d.Namespace == ns).Select(d => d.Id).ToList();
        foreach (var id in toRemove) _docs.TryRemove(id, out _);
        return Task.FromResult(StorageResult<int>.Ok(toRemove.Count));
    }

    public Task<long> CountAsync(string? ns = null, CancellationToken ct = default)
    {
        var count = ns is null ? _docs.Count : _docs.Values.Count(d => d.Namespace == ns);
        return Task.FromResult((long)count);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0 ? dot / denom : 0f;
    }
}
