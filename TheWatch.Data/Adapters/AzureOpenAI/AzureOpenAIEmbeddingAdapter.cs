// =============================================================================
// AzureOpenAIEmbeddingAdapter — IEmbeddingPort implementation using Azure OpenAI
// text-embedding-3-large (3072 dims). Paired with CosmosDB DiskANN vector store.
// =============================================================================
// Uses the Azure.AI.OpenAI SDK (2.1.0) already referenced in TheWatch.Data.csproj.
// Configuration flows from AIProviders:AzureOpenAI section in appsettings.json.
//
// WAL: [WAL-EMBEDDING-AOAI] prefix for all log messages.
// =============================================================================

using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.AzureOpenAI;

public class AzureOpenAIEmbeddingAdapter : IEmbeddingPort
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<AzureOpenAIEmbeddingAdapter> _logger;
    private readonly int _dimensions;

    public EmbeddingProvider Provider => EmbeddingProvider.AzureOpenAI;
    public int Dimensions => _dimensions;

    public AzureOpenAIEmbeddingAdapter(
        AzureOpenAIClient aoaiClient,
        string deploymentName,
        int dimensions,
        ILogger<AzureOpenAIEmbeddingAdapter> logger)
    {
        _client = aoaiClient.GetEmbeddingClient(deploymentName);
        _dimensions = dimensions;
        _logger = logger;
        _logger.LogInformation("[WAL-EMBEDDING-AOAI] Initialized with deployment={Deployment}, dims={Dims}",
            deploymentName, dimensions);
    }

    public async Task<StorageResult<float[]>> EmbedAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var options = new EmbeddingGenerationOptions { Dimensions = _dimensions };
            var response = await _client.GenerateEmbeddingAsync(text, options, ct);
            var vector = response.Value.ToFloats().ToArray();

            _logger.LogDebug("[WAL-EMBEDDING-AOAI] Embedded {Len} chars → {Dims}d vector",
                text.Length, vector.Length);

            return StorageResult<float[]>.Ok(vector);
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "[WAL-EMBEDDING-AOAI] API error embedding text ({Len} chars)", text.Length);
            return StorageResult<float[]>.Fail($"Azure OpenAI embedding failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<float[][]>> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
    {
        try
        {
            var options = new EmbeddingGenerationOptions { Dimensions = _dimensions };
            var response = await _client.GenerateEmbeddingsAsync(texts, options, ct);

            var results = new float[response.Value.Count][];
            for (int i = 0; i < response.Value.Count; i++)
                results[i] = response.Value[i].ToFloats().ToArray();

            _logger.LogDebug("[WAL-EMBEDDING-AOAI] Batch embedded {Count} texts → {Dims}d vectors",
                texts.Length, _dimensions);

            return StorageResult<float[][]>.Ok(results);
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "[WAL-EMBEDDING-AOAI] API error batch-embedding {Count} texts", texts.Length);
            return StorageResult<float[][]>.Fail($"Azure OpenAI batch embedding failed: {ex.Message}");
        }
    }

    public async Task<StorageResult<VectorDocument>> EmbedDocumentAsync(
        string content, string source, string ns, List<string>? tags = null, CancellationToken ct = default)
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
            Store = VectorStoreProvider.CosmosDB, // Azure OpenAI pairs with CosmosDB
            EstimatedTokens = content.Length / 4
        };

        return StorageResult<VectorDocument>.Ok(doc);
    }
}
