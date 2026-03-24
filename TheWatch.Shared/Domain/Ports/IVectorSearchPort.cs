// IVectorSearchPort — domain port for vector storage and similarity search.
// NO database SDK imports allowed in this file. Adapters implement this per-store:
//   - CosmosDBVectorAdapter    → Azure CosmosDB DiskANN vector index
//   - FirestoreVectorAdapter   → Google Firestore KNN vector search
//   - QdrantVectorAdapter      → Qdrant HNSW vector search
//   - MockVectorSearchAdapter  → brute-force cosine similarity in memory
//
// Example:
//   await vectorPort.UpsertAsync(document, ct);
//   var results = await vectorPort.SearchAsync(new VectorSearchQuery { Text = "offline sync" }, ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IVectorSearchPort
{
    /// <summary>Which vector store backend this adapter uses.</summary>
    VectorStoreProvider StoreProvider { get; }

    /// <summary>Insert or update a vector document. Replaces existing document with same ID.</summary>
    Task<StorageResult<bool>> UpsertAsync(VectorDocument document, CancellationToken ct = default);

    /// <summary>Insert or update multiple documents in batch.</summary>
    Task<StorageResult<int>> UpsertBatchAsync(IEnumerable<VectorDocument> documents, CancellationToken ct = default);

    /// <summary>
    /// Search for similar documents. If query.Vector is null and query.Text is set,
    /// the implementation must use the paired IEmbeddingPort to embed the query first.
    /// </summary>
    Task<StorageResult<List<VectorSearchResult>>> SearchAsync(VectorSearchQuery query, CancellationToken ct = default);

    /// <summary>Delete a document by ID.</summary>
    Task<StorageResult<bool>> DeleteAsync(string documentId, CancellationToken ct = default);

    /// <summary>Delete all documents in a namespace.</summary>
    Task<StorageResult<int>> DeleteNamespaceAsync(string ns, CancellationToken ct = default);

    /// <summary>Get the total document count, optionally filtered by namespace.</summary>
    Task<long> CountAsync(string? ns = null, CancellationToken ct = default);
}
