// EmbeddingProvider — which AI service generates the vector embeddings.
// Each provider stays in its own house: Azure OpenAI → CosmosDB, Gemini → Firestore,
// Voyage AI → Qdrant. This keeps embeddings + storage co-located for latency and billing.
//
// Example: if (config.EmbeddingProvider == EmbeddingProvider.AzureOpenAI) UseCosmosVectors();

namespace TheWatch.Shared.Enums;

public enum EmbeddingProvider
{
    /// <summary>Azure OpenAI text-embedding-3-large (1536/3072 dims). Stored in CosmosDB.</summary>
    AzureOpenAI = 0,

    /// <summary>Google Gemini text-embedding-004 (768 dims). Stored in Firestore.</summary>
    Gemini = 1,

    /// <summary>Voyage AI voyage-3 (1024 dims). Anthropic's recommended partner. Stored in Qdrant.</summary>
    VoyageAI = 2,

    /// <summary>Mock embeddings for testing — random unit vectors.</summary>
    Mock = 99
}
