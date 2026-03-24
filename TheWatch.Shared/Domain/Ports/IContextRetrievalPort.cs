// IContextRetrievalPort — high-level RAG interface that orchestrates embedding + vector search.
// This is the port that LLM-facing services call to get relevant context for a prompt.
// It fans out across all three provider stacks (Azure/Google/Qdrant), merges results,
// deduplicates, and returns ranked ContextChunks within a token budget.
//
// Architecture:
//   User query → IContextRetrievalPort.RetrieveContextAsync
//     → Fan-out to each configured IVectorSearchPort (CosmosDB, Firestore, Qdrant)
//     → Each port uses its paired IEmbeddingPort to embed the query
//     → Merge results, deduplicate by source, rank by score
//     → Trim to token budget → return ContextChunk[]
//
// Example:
//   var chunks = await contextPort.RetrieveContextAsync("How does the SOS pipeline work?",
//       namespaces: new[] { "codebase", "standards" }, maxTokens: 8000, ct);
//   // chunks = [ { Content: "ResponseController.cs ...", Score: 0.92 }, ... ]

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IContextRetrievalPort
{
    /// <summary>
    /// Retrieve relevant context chunks for an LLM prompt.
    /// Searches across all configured vector stores, merges and ranks results.
    /// </summary>
    /// <param name="query">Natural language query or question.</param>
    /// <param name="namespaces">Namespaces to search (null = all). E.g., "codebase", "incidents", "standards".</param>
    /// <param name="maxTokens">Maximum total tokens across all returned chunks (budget management).</param>
    /// <param name="topK">Maximum number of chunks to return per provider.</param>
    /// <param name="minScore">Minimum relevance score (0.0–1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StorageResult<List<ContextChunk>>> RetrieveContextAsync(
        string query,
        string[]? namespaces = null,
        int maxTokens = 8000,
        int topK = 10,
        float minScore = 0.5f,
        CancellationToken ct = default);

    /// <summary>
    /// Ingest a document into the appropriate vector store(s).
    /// Chunks the content, embeds each chunk, and upserts into the configured stores.
    /// </summary>
    /// <param name="content">Full text content to ingest.</param>
    /// <param name="source">Source identifier (file path, URL, document ID).</param>
    /// <param name="ns">Namespace to file this under.</param>
    /// <param name="tags">Optional tags for metadata filtering.</param>
    /// <param name="chunkSize">Max characters per chunk (default 1500 ≈ 375 tokens).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StorageResult<int>> IngestAsync(
        string content,
        string source,
        string ns,
        List<string>? tags = null,
        int chunkSize = 1500,
        CancellationToken ct = default);
}
