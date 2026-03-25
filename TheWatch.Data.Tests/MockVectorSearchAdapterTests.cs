// MockVectorSearchAdapterTests — standalone xUnit tests for MockVectorSearchAdapter.
// Uses MockEmbeddingAdapter to generate deterministic embeddings for repeatable tests.
//
// WAL: Vector search powers the Standards Inferencing System and keyword cluster caching.
// Cosine similarity must be deterministic (same query text → same results every time).
// The mock adapter uses brute-force search; real adapters (Qdrant, Pinecone) use ANN
// but must produce results within an acceptable recall threshold.
//
// Example (RAG pipeline flow):
//   1. Document ingested → EmbedDocumentAsync → UpsertAsync
//   2. User query → SearchAsync (auto-embeds query text) → ranked results
//   3. Top-K results fed to LLM as context

using TheWatch.Data.Adapters.Mock;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Tests;

public class MockVectorSearchAdapterTests
{
    private static (MockVectorSearchAdapter search, MockEmbeddingAdapter embedding) CreateAdapters()
    {
        var embedding = new MockEmbeddingAdapter();
        var search = new MockVectorSearchAdapter(embedding);
        return (search, embedding);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResults()
    {
        var (search, embedding) = CreateAdapters();

        // Index a document with a known embedding
        var embedResult = await embedding.EmbedAsync("fire safety protocol");
        var doc = new VectorDocument
        {
            Id = "doc-1",
            Content = "fire safety protocol",
            Embedding = embedResult.Data!,
            Namespace = "standards",
            Source = "safety-manual.pdf",
            Tags = new List<string> { "safety", "fire" }
        };
        await search.UpsertAsync(doc);

        // Search with the same text (should get high similarity)
        var results = await search.SearchAsync(new VectorSearchQuery
        {
            Text = "fire safety protocol",
            Namespace = "standards",
            TopK = 5,
            MinScore = 0.5f
        });

        Assert.True(results.Success);
        Assert.True(results.Data!.Count > 0, "Must find at least one result");
        Assert.Equal("doc-1", results.Data[0].DocumentId);
        Assert.True(results.Data[0].Score > 0.9f, "Same text should have very high similarity");
    }

    [Fact]
    public async Task UpsertAsync_StoresDocument()
    {
        var (search, embedding) = CreateAdapters();

        var embedResult = await embedding.EmbedAsync("emergency evacuation plan");
        var doc = new VectorDocument
        {
            Id = "doc-evac",
            Content = "emergency evacuation plan",
            Embedding = embedResult.Data!,
            Namespace = "plans"
        };

        var result = await search.UpsertAsync(doc);

        Assert.True(result.Success);

        var count = await search.CountAsync("plans");
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocument()
    {
        var (search, embedding) = CreateAdapters();

        var embedResult = await embedding.EmbedAsync("temporary document");
        var doc = new VectorDocument
        {
            Id = "doc-temp",
            Content = "temporary document",
            Embedding = embedResult.Data!,
            Namespace = "temp"
        };
        await search.UpsertAsync(doc);

        var deleteResult = await search.DeleteAsync("doc-temp");

        Assert.True(deleteResult.Success);

        var count = await search.CountAsync("temp");
        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task SearchAsync_RespectsNamespaceFilter()
    {
        var (search, embedding) = CreateAdapters();

        var embed1 = await embedding.EmbedAsync("alpha content");
        var embed2 = await embedding.EmbedAsync("beta content");

        await search.UpsertAsync(new VectorDocument { Id = "d1", Content = "alpha content", Embedding = embed1.Data!, Namespace = "ns-a" });
        await search.UpsertAsync(new VectorDocument { Id = "d2", Content = "beta content", Embedding = embed2.Data!, Namespace = "ns-b" });

        var results = await search.SearchAsync(new VectorSearchQuery
        {
            Text = "alpha content",
            Namespace = "ns-a",
            TopK = 10,
            MinScore = 0.0f
        });

        Assert.True(results.Success);
        Assert.All(results.Data!, r => Assert.Equal("ns-a", r.Namespace));
    }

    [Fact]
    public async Task UpsertBatchAsync_StoresMultipleDocuments()
    {
        var (search, embedding) = CreateAdapters();

        var docs = new List<VectorDocument>();
        for (int i = 0; i < 5; i++)
        {
            var emb = await embedding.EmbedAsync($"batch doc {i}");
            docs.Add(new VectorDocument { Id = $"batch-{i}", Content = $"batch doc {i}", Embedding = emb.Data!, Namespace = "batch" });
        }

        var result = await search.UpsertBatchAsync(docs);

        Assert.True(result.Success);
        Assert.Equal(5, result.Data);

        var count = await search.CountAsync("batch");
        Assert.Equal(5L, count);
    }

    [Fact]
    public async Task DeleteNamespaceAsync_RemovesAllInNamespace()
    {
        var (search, embedding) = CreateAdapters();

        for (int i = 0; i < 3; i++)
        {
            var emb = await embedding.EmbedAsync($"ns doc {i}");
            await search.UpsertAsync(new VectorDocument { Id = $"ns-{i}", Content = $"ns doc {i}", Embedding = emb.Data!, Namespace = "disposable" });
        }

        var result = await search.DeleteNamespaceAsync("disposable");

        Assert.True(result.Success);
        Assert.Equal(3, result.Data);

        var count = await search.CountAsync("disposable");
        Assert.Equal(0L, count);
    }
}
