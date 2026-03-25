// =============================================================================
// MockContextRetrievalPort — in-memory mock for IContextRetrievalPort
// =============================================================================
// Provides seeded context chunks for development/testing. Supports both
// retrieval (keyword matching against a seed corpus) and ingestion (stores
// chunks in memory). Used by the swarm agent's RAG pipeline in mock mode.
//
// WAL: Uses [WAL-CONTEXTRET-MOCK] log prefix for all trace output.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

public class MockContextRetrievalPort : IContextRetrievalPort
{
    private readonly ILogger<MockContextRetrievalPort> _logger;
    private readonly List<ContextChunk> _corpus;
    private readonly List<ContextChunk> _ingested = [];

    public MockContextRetrievalPort(ILogger<MockContextRetrievalPort> logger)
    {
        _logger = logger;
        _corpus = BuildSeedCorpus();
    }

    public Task<StorageResult<List<ContextChunk>>> RetrieveContextAsync(
        string query, string[]? namespaces, int maxTokens, int topK,
        float minScore, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-CONTEXTRET-MOCK] RetrieveContextAsync: query={Query}, ns={Ns}, topK={TopK}",
            query, namespaces != null ? string.Join(",", namespaces) : "(all)", topK);

        var queryLower = query.ToLowerInvariant();
        var allChunks = _corpus.Concat(_ingested);

        // Filter by namespace if specified
        if (namespaces is { Length: > 0 })
            allChunks = allChunks.Where(c => namespaces.Contains(c.Namespace));

        // Score by keyword overlap (mock relevance — real adapters use cosine similarity)
        var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = allChunks
            .Select(c =>
            {
                var contentLower = c.Content.ToLowerInvariant();
                var hits = queryWords.Count(w => contentLower.Contains(w));
                var score = Math.Min(1.0f, hits / (float)Math.Max(queryWords.Length, 1));
                return (Chunk: c, Score: score);
            })
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        // Apply token budget
        var result = new List<ContextChunk>();
        var tokensUsed = 0;
        foreach (var (chunk, score) in scored)
        {
            if (tokensUsed + chunk.EstimatedTokens > maxTokens)
                break;

            result.Add(new ContextChunk
            {
                Content = chunk.Content,
                Source = chunk.Source,
                RelevanceScore = score,
                Provider = chunk.Provider,
                Store = chunk.Store,
                Namespace = chunk.Namespace,
                EstimatedTokens = chunk.EstimatedTokens
            });
            tokensUsed += chunk.EstimatedTokens;
        }

        _logger.LogDebug("[WAL-CONTEXTRET-MOCK] Returning {Count} chunks, {Tokens} tokens", result.Count, tokensUsed);
        return Task.FromResult(StorageResult<List<ContextChunk>>.Ok(result));
    }

    public Task<StorageResult<int>> IngestAsync(
        string content, string source, string ns, List<string>? tags,
        int chunkSize, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-CONTEXTRET-MOCK] IngestAsync: source={Source}, ns={Ns}, len={Len}",
            source, ns, content.Length);

        // Simple chunking by character count
        var chunks = 0;
        for (var i = 0; i < content.Length; i += chunkSize)
        {
            var end = Math.Min(i + chunkSize, content.Length);
            var chunkContent = content[i..end];

            _ingested.Add(new ContextChunk
            {
                Content = chunkContent,
                Source = source,
                Namespace = ns,
                Provider = EmbeddingProvider.Mock,
                Store = VectorStoreProvider.Mock,
                EstimatedTokens = chunkContent.Length / 4,
                RelevanceScore = 0f
            });
            chunks++;
        }

        return Task.FromResult(StorageResult<int>.Ok(chunks));
    }

    /// <summary>
    /// Build a seed corpus covering TheWatch's key domain areas so the RAG pipeline
    /// has meaningful data to return even without real vector stores.
    /// </summary>
    private static List<ContextChunk> BuildSeedCorpus() =>
    [
        // Swarm orchestration
        new()
        {
            Content = "TheWatch swarms use the OpenAI Swarm pattern on Azure OpenAI. Each agent has a system prompt, tools, and handoff functions. Handoff = a tool call like transfer_to_evidence_analyst() that switches the active agent. The loop runs until an agent produces text with no tool calls.",
            Source = "docs/architecture/swarm-pattern.md",
            Namespace = "architecture",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 60, RelevanceScore = 0.95f
        },
        new()
        {
            Content = "Available swarm presets: safety-report-pipeline (triage → threat → evidence → location → review → aggregate), neighborhood-watch (patrol coordination), compliance-audit (ISO/IEC checking), evidence-chain (chain of custody), emergency-dispatch (911 integration).",
            Source = "docs/swarm-presets.md",
            Namespace = "swarms",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.92f
        },
        new()
        {
            Content = "Swarm agent roles: Triage (classifies and routes), Specialist (domain work), Reviewer (validates output), Supervisor (monitors quality), Orchestrator (parallel coordination), Aggregator (collects results). Use low temperature (0.1-0.3) for safety-critical agents.",
            Source = "docs/swarm-roles.md",
            Namespace = "swarms",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 50, RelevanceScore = 0.90f
        },

        // Response coordination
        new()
        {
            Content = "Response coordination uses IResponseCoordinationPort for multi-responder dispatch. A ResponseRequest contains coordinates, urgency level, and required capabilities. The system finds nearby responders, dispatches them, tracks acknowledgments, and escalates to 911 if no response within the configured timeout.",
            Source = "TheWatch.Shared/Domain/Ports/IResponseCoordinationPort.cs",
            Namespace = "codebase",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.88f
        },

        // Evidence collection
        new()
        {
            Content = "Evidence is collected via IEvidencePort. Each EvidenceItem has a chain-of-custody hash, GPS coordinates, timestamp, and media type (photo/video/audio/document). Evidence is stored in Azure Blob Storage with immutable retention policies. The chain-of-custody is maintained via SHA-256 hash chains.",
            Source = "TheWatch.Shared/Domain/Ports/IEvidencePort.cs",
            Namespace = "codebase",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.85f
        },

        // Watch calls
        new()
        {
            Content = "Watch Calls provide live video with AI scene narration for de-escalation. WebRTC handles peer-to-peer video, SignalR relays signaling (SDP/ICE). Users must complete mandatory mock call training before live participation. Frame snapshots (1 fps) are sent to GPT-4o vision for neutral scene description.",
            Source = "docs/features/watch-calls.md",
            Namespace = "features",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.87f
        },
        new()
        {
            Content = "Scene narration guardrails: no race, ethnicity, age, gender, assumed intent, or subjective language. Narrations describe observable actions only. Example: 'A person is standing near a vehicle in a residential area' — never 'A suspicious person is loitering.' The ValidateNarrationAsync method checks for prohibited terms.",
            Source = "TheWatch.Shared/Domain/Ports/ISceneNarrationPort.cs",
            Namespace = "codebase",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.86f
        },

        // IoT integration
        new()
        {
            Content = "IoT alerts come from Alexa Skills, Google Home Actions, SmartThings, and custom webhooks via IIoTAlertPort and IIoTWebhookPort. Voice-activated panic alerts trigger immediate response coordination. Quick-tap detection (fall detection) uses accelerometer data to detect distress patterns.",
            Source = "docs/features/iot-integration.md",
            Namespace = "features",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 50, RelevanceScore = 0.82f
        },

        // RAG architecture
        new()
        {
            Content = "TheWatch uses multi-provider RAG: Azure OpenAI text-embedding-3-large → CosmosDB DiskANN, Gemini text-embedding-004 → Firestore KNN, VoyageAI voyage-3 → Qdrant HNSW. Ingest pushes to all providers; retrieval fans out across all stores and merges by relevance score.",
            Source = "docs/architecture/rag-strategy.md",
            Namespace = "architecture",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.90f
        },

        // Infrastructure
        new()
        {
            Content = "Provisioned Azure resources: SignalR Service (Free, signalr-thewatch), Redis Cache (Basic C0, redis-thewatch), SQL Server + WatchDb (Basic 5 DTU, sql-thewatch), Azure OpenAI (thewatchopenai) with gpt-4.1, gpt-4o, gpt-4o-mini, text-embedding-3-large deployments. Firebase project: thewatch-e470c.",
            Source = "deploy/README.md",
            Namespace = "infrastructure",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 60, RelevanceScore = 0.80f
        },

        // Hexagonal architecture
        new()
        {
            Content = "TheWatch follows hexagonal architecture. All domain contracts live in TheWatch.Shared/Domain/Ports/ as C# interfaces (41 ports). Adapters implement ports in TheWatch.Adapters.Mock (dev), TheWatch.Adapters.Azure (prod), TheWatch.Data (persistence). The AdapterRegistry in appsettings controls which adapter loads at startup.",
            Source = "docs/architecture/hexagonal.md",
            Namespace = "architecture",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.88f
        },

        // Standards and compliance
        new()
        {
            Content = "TheWatch targets ISO/IEC 27001 (information security), ISO 22320 (emergency management), and SOC 2 Type II compliance. Guard reports follow ISO 22320 Annex A format. Evidence handling follows Federal Rules of Evidence digital evidence standards. All PII is encrypted at rest with AES-256.",
            Source = "docs/compliance/standards.md",
            Namespace = "standards",
            Provider = EmbeddingProvider.Mock, Store = VectorStoreProvider.Mock,
            EstimatedTokens = 55, RelevanceScore = 0.78f
        }
    ];
}
