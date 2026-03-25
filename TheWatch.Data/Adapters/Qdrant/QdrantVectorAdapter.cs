// =============================================================================
// QdrantVectorAdapter — IVectorSearchPort implementation using Qdrant HNSW.
// =============================================================================
// Paired with VoyageAI/Claude embeddings. Uses Qdrant.Client 1.13.0 NuGet
// already referenced in TheWatch.Data.csproj.
//
// Collection: configurable via AIProviders:Qdrant:CollectionName (default: "thewatch-vectors")
// Namespace mapping: Qdrant payload field "namespace" for filtered searches.
// Tags mapping: Qdrant payload field "tags" (repeated keyword).
//
// WAL: [WAL-VECTOR-QDRANT] prefix for all log messages.
// =============================================================================

using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Qdrant;

public class QdrantVectorAdapter : IVectorSearchPort
{
    private readonly QdrantClient _client;
    private readonly IEmbeddingPort _embeddingPort;
    private readonly ILogger<QdrantVectorAdapter> _logger;
    private readonly string _collectionName;

    public VectorStoreProvider StoreProvider => VectorStoreProvider.Qdrant;

    public QdrantVectorAdapter(
        QdrantClient client,
        IEmbeddingPort embeddingPort,
        string collectionName,
        ILogger<QdrantVectorAdapter> logger)
    {
        _client = client;
        _embeddingPort = embeddingPort;
        _collectionName = collectionName;
        _logger = logger;
        _logger.LogInformation("[WAL-VECTOR-QDRANT] Initialized with collection={Collection}", collectionName);
    }

    public async Task<StorageResult<bool>> UpsertAsync(VectorDocument document, CancellationToken ct = default)
    {
        try
        {
            document.Store = VectorStoreProvider.Qdrant;
            document.UpdatedAt = DateTime.UtcNow;

            var point = ToPointStruct(document);
            await _client.UpsertAsync(_collectionName, new[] { point }, cancellationToken: ct);

            _logger.LogDebug("[WAL-VECTOR-QDRANT] Upserted doc {Id} to {Collection}",
                document.Id, _collectionName);

            return StorageResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Failed to upsert doc {Id}", document.Id);
            return StorageResult<bool>.Fail($"Qdrant upsert failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<int>> UpsertBatchAsync(IEnumerable<VectorDocument> documents, CancellationToken ct = default)
    {
        try
        {
            var docs = documents.ToList();
            foreach (var doc in docs)
            {
                doc.Store = VectorStoreProvider.Qdrant;
                doc.UpdatedAt = DateTime.UtcNow;
            }

            // Qdrant batch upsert in chunks of 100
            var points = docs.Select(ToPointStruct).ToList();
            const int batchSize = 100;

            for (int i = 0; i < points.Count; i += batchSize)
            {
                var batch = points.Skip(i).Take(batchSize).ToList();
                await _client.UpsertAsync(_collectionName, batch, cancellationToken: ct);
            }

            _logger.LogDebug("[WAL-VECTOR-QDRANT] Batch upserted {Count} docs to {Collection}",
                docs.Count, _collectionName);

            return StorageResult<int>.Ok(docs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Failed batch upsert");
            return StorageResult<int>.Fail($"Qdrant batch upsert failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<List<VectorSearchResult>>> SearchAsync(VectorSearchQuery query, CancellationToken ct = default)
    {
        try
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

            // Build Qdrant filter for namespace + tags
            Filter? filter = BuildFilter(query);

            var searchResults = await _client.SearchAsync(
                _collectionName,
                queryVec,
                limit: (ulong)query.TopK,
                scoreThreshold: query.MinScore,
                filter: filter,
                payloadSelector: true,
                cancellationToken: ct);

            var results = searchResults.Select(r => new VectorSearchResult
            {
                DocumentId = r.Id.Uuid ?? r.Id.Num.ToString(),
                Score = r.Score,
                Content = query.IncludeContent ? GetPayloadString(r.Payload, "content") : null,
                ContentPreview = TruncatePreview(GetPayloadString(r.Payload, "content")),
                Source = GetPayloadString(r.Payload, "source"),
                Namespace = GetPayloadString(r.Payload, "namespace"),
                ChunkIndex = GetPayloadInt(r.Payload, "chunk_index"),
                Tags = GetPayloadStringList(r.Payload, "tags"),
                Metadata = GetPayloadMetadata(r.Payload)
            }).ToList();

            _logger.LogDebug("[WAL-VECTOR-QDRANT] Search returned {Count} results from {Collection}",
                results.Count, _collectionName);

            return StorageResult<List<VectorSearchResult>>.Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Search failed");
            return StorageResult<List<VectorSearchResult>>.Fail($"Qdrant search failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<bool>> DeleteAsync(string documentId, CancellationToken ct = default)
    {
        try
        {
            // Use filter-based delete with the stored document ID
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "document_id",
                    Match = new Match { Keyword = documentId }
                }
            });

            await _client.DeleteAsync(_collectionName, filter, cancellationToken: ct);

            _logger.LogDebug("[WAL-VECTOR-QDRANT] Deleted doc {Id} from {Collection}",
                documentId, _collectionName);

            return StorageResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Failed to delete doc {Id}", documentId);
            return StorageResult<bool>.Fail($"Qdrant delete failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<int>> DeleteNamespaceAsync(string ns, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "namespace",
                    Match = new Match { Keyword = ns }
                }
            });

            await _client.DeleteAsync(_collectionName, filter, cancellationToken: ct);

            _logger.LogDebug("[WAL-VECTOR-QDRANT] Deleted namespace {Ns} from {Collection}",
                ns, _collectionName);

            // Qdrant doesn't return count on delete, estimate with 0
            return StorageResult<int>.Ok(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Failed to delete namespace {Ns}", ns);
            return StorageResult<int>.Fail($"Qdrant namespace delete failed: {ex.Message}");
        }
    }

    public async Task<long> CountAsync(string? ns = null, CancellationToken ct = default)
    {
        try
        {
            if (ns is null)
            {
                var info = await _client.GetCollectionInfoAsync(_collectionName, ct);
                return (long)info.PointsCount;
            }

            // Count with namespace filter
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "namespace",
                    Match = new Match { Keyword = ns }
                }
            });

            var count = await _client.CountAsync(_collectionName, filter, cancellationToken: ct);
            return (long)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-VECTOR-QDRANT] Count failed for ns={Ns}", ns);
            return 0;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static PointStruct ToPointStruct(VectorDocument doc)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = doc.Id },
            Vectors = doc.Embedding
        };

        point.Payload["document_id"] = doc.Id;
        point.Payload["content"] = doc.Content;
        point.Payload["source"] = doc.Source ?? string.Empty;
        point.Payload["namespace"] = doc.Namespace;
        point.Payload["chunk_index"] = doc.ChunkIndex;
        point.Payload["total_chunks"] = doc.TotalChunks;
        point.Payload["estimated_tokens"] = doc.EstimatedTokens;
        point.Payload["provider"] = doc.Provider.ToString();
        point.Payload["created_at"] = doc.CreatedAt.ToString("O");

        // Tags as repeated string value
        var tagList = new ListValue();
        foreach (var tag in doc.Tags)
            tagList.Values.Add(new Value { StringValue = tag });
        point.Payload["tags"] = new Value { ListValue = tagList };

        // Metadata as nested struct
        var metaStruct = new Struct();
        foreach (var kvp in doc.Metadata)
            metaStruct.Fields[kvp.Key] = new Value { StringValue = kvp.Value };
        point.Payload["metadata"] = new Value { StructValue = metaStruct };

        return point;
    }

    private static Filter? BuildFilter(VectorSearchQuery query)
    {
        var conditions = new List<Condition>();

        if (!string.IsNullOrEmpty(query.Namespace))
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "namespace",
                    Match = new Match { Keyword = query.Namespace }
                }
            });
        }

        if (query.TagFilters is not null)
        {
            foreach (var tag in query.TagFilters)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "tags",
                        Match = new Match { Keyword = tag }
                    }
                });
            }
        }

        if (conditions.Count == 0) return null;

        var filter = new Filter();
        foreach (var c in conditions) filter.Must.Add(c);
        return filter;
    }

    private static string? GetPayloadString(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? v.StringValue : null;

    private static int GetPayloadInt(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;

    private static List<string> GetPayloadStringList(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v.ListValue is null)
            return new();
        return v.ListValue.Values.Select(x => x.StringValue).ToList();
    }

    private static Dictionary<string, string> GetPayloadMetadata(IDictionary<string, Value> payload)
    {
        if (!payload.TryGetValue("metadata", out var v) || v.StructValue is null)
            return new();
        return v.StructValue.Fields.ToDictionary(f => f.Key, f => f.Value.StringValue);
    }

    private static string? TruncatePreview(string? content) =>
        content is null ? null
        : content.Length > 200 ? content[..200] + "..."
        : content;
}
