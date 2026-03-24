// ContextRetrievalService — IContextRetrievalPort implementation that orchestrates
// embedding + vector search across all configured provider stacks.
//
// In Mock mode: single MockEmbedding + MockVectorSearch pair.
// In Live mode: fans out to Azure OpenAI/CosmosDB, Gemini/Firestore, VoyageAI/Qdrant
// in parallel, merges results by score, deduplicates by source, and trims to token budget.
//
// Chunking strategy: splits content on paragraph boundaries (double newline),
// then subdivides by sentence if a chunk exceeds chunkSize.
//
// WAL: Every ingest and retrieval is logged with document counts and timing.

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

public class ContextRetrievalService : IContextRetrievalPort
{
    private readonly IEnumerable<IVectorSearchPort> _vectorPorts;
    private readonly IEnumerable<IEmbeddingPort> _embeddingPorts;
    private readonly ILogger<ContextRetrievalService> _logger;

    public ContextRetrievalService(
        IEnumerable<IVectorSearchPort> vectorPorts,
        IEnumerable<IEmbeddingPort> embeddingPorts,
        ILogger<ContextRetrievalService> logger)
    {
        _vectorPorts = vectorPorts;
        _embeddingPorts = embeddingPorts;
        _logger = logger;
    }

    public async Task<StorageResult<List<ContextChunk>>> RetrieveContextAsync(
        string query, string[]? namespaces = null, int maxTokens = 8000,
        int topK = 10, float minScore = 0.5f, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Context retrieval: Query=\"{Query}\", Namespaces=[{NS}], MaxTokens={MaxTokens}, TopK={TopK}",
            query.Length > 100 ? query[..100] + "..." : query,
            namespaces is not null ? string.Join(",", namespaces) : "all",
            maxTokens, topK);

        var allResults = new List<(VectorSearchResult Result, VectorStoreProvider Store)>();

        // Fan out across all configured vector stores
        var searchTasks = _vectorPorts.Select(async port =>
        {
            try
            {
                var searchQuery = new VectorSearchQuery
                {
                    Text = query,
                    TopK = topK,
                    MinScore = minScore,
                    IncludeContent = true
                };

                // Search each namespace, or search without namespace filter
                if (namespaces is not null)
                {
                    foreach (var ns in namespaces)
                    {
                        searchQuery.Namespace = ns;
                        var result = await port.SearchAsync(searchQuery, ct);
                        if (result.Success && result.Data is not null)
                            return result.Data.Select(r => (r, port.StoreProvider)).ToList();
                    }
                    return new List<(VectorSearchResult, VectorStoreProvider)>();
                }
                else
                {
                    var result = await port.SearchAsync(searchQuery, ct);
                    return result.Success && result.Data is not null
                        ? result.Data.Select(r => (r, port.StoreProvider)).ToList()
                        : new List<(VectorSearchResult, VectorStoreProvider)>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vector search failed for {Store}", port.StoreProvider);
                return new List<(VectorSearchResult, VectorStoreProvider)>();
            }
        });

        var results = await Task.WhenAll(searchTasks);
        foreach (var batch in results)
            allResults.AddRange(batch);

        // Deduplicate by source (keep highest score per source)
        var deduped = allResults
            .GroupBy(r => r.Result.Source ?? r.Result.DocumentId)
            .Select(g => g.OrderByDescending(r => r.Result.Score).First())
            .OrderByDescending(r => r.Result.Score)
            .ToList();

        // Convert to ContextChunks and trim to token budget
        var chunks = new List<ContextChunk>();
        int tokenBudget = maxTokens;

        foreach (var (result, store) in deduped)
        {
            if (tokenBudget <= 0) break;
            var content = result.Content ?? result.ContentPreview ?? "";
            var estimatedTokens = content.Length / 4;

            if (estimatedTokens > tokenBudget)
            {
                // Truncate to fit remaining budget
                var maxChars = tokenBudget * 4;
                content = content[..Math.Min(content.Length, maxChars)];
                estimatedTokens = tokenBudget;
            }

            // Determine which embedding provider was used based on the store
            var provider = store switch
            {
                VectorStoreProvider.CosmosDB => EmbeddingProvider.AzureOpenAI,
                VectorStoreProvider.Firestore => EmbeddingProvider.Gemini,
                VectorStoreProvider.Qdrant => EmbeddingProvider.VoyageAI,
                _ => EmbeddingProvider.Mock
            };

            chunks.Add(new ContextChunk
            {
                Content = content,
                Source = result.Source ?? "unknown",
                RelevanceScore = result.Score,
                Provider = provider,
                Store = store,
                Namespace = result.Namespace ?? "default",
                EstimatedTokens = estimatedTokens
            });

            tokenBudget -= estimatedTokens;
        }

        _logger.LogInformation(
            "Context retrieved: {ChunkCount} chunks, {TotalTokens} est. tokens, from {StoreCount} stores",
            chunks.Count, chunks.Sum(c => c.EstimatedTokens), _vectorPorts.Count());

        return StorageResult<List<ContextChunk>>.Ok(chunks);
    }

    public async Task<StorageResult<int>> IngestAsync(
        string content, string source, string ns, List<string>? tags = null,
        int chunkSize = 1500, CancellationToken ct = default)
    {
        _logger.LogInformation("Ingesting: Source={Source}, Namespace={NS}, Length={Length}",
            source, ns, content.Length);

        // Chunk the content
        var textChunks = ChunkText(content, chunkSize);
        int totalUpserted = 0;

        // Embed and upsert into each configured store via its paired embedding port
        foreach (var embeddingPort in _embeddingPorts)
        {
            var pairedStore = GetPairedStore(embeddingPort.Provider);
            var vectorPort = _vectorPorts.FirstOrDefault(v => v.StoreProvider == pairedStore);
            if (vectorPort is null) continue;

            var documents = new List<VectorDocument>();
            for (int i = 0; i < textChunks.Length; i++)
            {
                var embedResult = await embeddingPort.EmbedDocumentAsync(
                    textChunks[i], source, ns, tags, ct);

                if (embedResult.Success && embedResult.Data is not null)
                {
                    embedResult.Data.ChunkIndex = i;
                    embedResult.Data.TotalChunks = textChunks.Length;
                    embedResult.Data.Store = pairedStore;
                    documents.Add(embedResult.Data);
                }
            }

            if (documents.Count > 0)
            {
                var upsertResult = await vectorPort.UpsertBatchAsync(documents, ct);
                if (upsertResult.Success)
                    totalUpserted += upsertResult.Data;
            }
        }

        _logger.LogInformation("Ingested: {Total} vectors across {StoreCount} stores for {Source}",
            totalUpserted, _vectorPorts.Count(), source);

        return StorageResult<int>.Ok(totalUpserted);
    }

    private static VectorStoreProvider GetPairedStore(EmbeddingProvider provider) => provider switch
    {
        EmbeddingProvider.AzureOpenAI => VectorStoreProvider.CosmosDB,
        EmbeddingProvider.Gemini => VectorStoreProvider.Firestore,
        EmbeddingProvider.VoyageAI => VectorStoreProvider.Qdrant,
        _ => VectorStoreProvider.Mock
    };

    private static string[] ChunkText(string text, int maxChunkSize)
    {
        if (text.Length <= maxChunkSize)
            return new[] { text };

        var chunks = new List<string>();
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var para in paragraphs)
        {
            if (current.Length + para.Length + 2 > maxChunkSize)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.Trim());
                    current = "";
                }

                // If a single paragraph exceeds chunk size, split by sentence
                if (para.Length > maxChunkSize)
                {
                    var sentences = para.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var sentence in sentences)
                    {
                        if (current.Length + sentence.Length + 2 > maxChunkSize && current.Length > 0)
                        {
                            chunks.Add(current.Trim());
                            current = "";
                        }
                        current += sentence + ". ";
                    }
                }
                else
                {
                    current = para;
                }
            }
            else
            {
                current += (current.Length > 0 ? "\n\n" : "") + para;
            }
        }

        if (current.Trim().Length > 0)
            chunks.Add(current.Trim());

        return chunks.ToArray();
    }
}
