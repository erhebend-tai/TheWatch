// ContextController — REST endpoints for RAG context retrieval and document ingestion.
// Bridges the three AI provider stacks (Azure OpenAI/CosmosDB, Gemini/Firestore,
// VoyageAI/Qdrant) into a unified API for context-augmented LLM prompts.
//
// Endpoints:
//   POST /api/context/search      — Retrieve relevant context chunks for a query
//   POST /api/context/ingest      — Ingest a document into vector stores
//   GET  /api/context/stats       — Document counts per namespace and store
//   DELETE /api/context/{ns}      — Delete all documents in a namespace
//
// WAL: All operations logged with timing, chunk counts, and store breakdown.

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContextController : ControllerBase
{
    private readonly IContextRetrievalPort _contextPort;
    private readonly IEnumerable<IVectorSearchPort> _vectorPorts;
    private readonly ILogger<ContextController> _logger;

    public ContextController(
        IContextRetrievalPort contextPort,
        IEnumerable<IVectorSearchPort> vectorPorts,
        ILogger<ContextController> logger)
    {
        _contextPort = contextPort;
        _vectorPorts = vectorPorts;
        _logger = logger;
    }

    /// <summary>
    /// Retrieve relevant context chunks for an LLM prompt.
    /// Searches across all configured vector stores (CosmosDB, Firestore, Qdrant).
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] ContextSearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required" });

        var result = await _contextPort.RetrieveContextAsync(
            request.Query,
            request.Namespaces,
            request.MaxTokens,
            request.TopK,
            request.MinScore,
            ct);

        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        return Ok(new
        {
            Query = request.Query,
            ChunkCount = result.Data!.Count,
            TotalEstimatedTokens = result.Data.Sum(c => c.EstimatedTokens),
            Chunks = result.Data
        });
    }

    /// <summary>
    /// Ingest a document into the configured vector stores.
    /// Chunks, embeds, and upserts across all provider stacks.
    /// </summary>
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] ContextIngestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Content is required" });

        var result = await _contextPort.IngestAsync(
            request.Content,
            request.Source ?? "manual",
            request.Namespace ?? "default",
            request.Tags,
            request.ChunkSize,
            ct);

        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        _logger.LogInformation("Ingested {Count} vectors for {Source}", result.Data, request.Source);

        return Ok(new
        {
            VectorsCreated = result.Data,
            request.Source,
            request.Namespace
        });
    }

    /// <summary>
    /// Get document counts per namespace across all vector stores.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = new List<object>();
        foreach (var port in _vectorPorts)
        {
            var total = await port.CountAsync(ct: ct);
            stats.Add(new
            {
                Store = port.StoreProvider.ToString(),
                TotalDocuments = total
            });
        }
        return Ok(stats);
    }

    /// <summary>Delete all documents in a namespace across all stores.</summary>
    [HttpDelete("{ns}")]
    public async Task<IActionResult> DeleteNamespace(string ns, CancellationToken ct)
    {
        int total = 0;
        foreach (var port in _vectorPorts)
        {
            var result = await port.DeleteNamespaceAsync(ns, ct);
            if (result.Success) total += result.Data;
        }
        return Ok(new { Namespace = ns, DeletedDocuments = total });
    }
}

public record ContextSearchRequest(
    string Query,
    string[]? Namespaces = null,
    int MaxTokens = 8000,
    int TopK = 10,
    float MinScore = 0.5f
);

public record ContextIngestRequest(
    string Content,
    string? Source = null,
    string? Namespace = null,
    List<string>? Tags = null,
    int ChunkSize = 1500
);
