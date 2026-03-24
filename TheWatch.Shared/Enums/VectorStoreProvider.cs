// VectorStoreProvider — which vector database stores and searches embeddings.
// Each store is paired with its provider's embedding model:
//   CosmosDB  ← Azure OpenAI embeddings
//   Firestore ← Gemini embeddings
//   Qdrant    ← Voyage AI embeddings (for Claude context)
//
// Example: if (config.VectorStore == VectorStoreProvider.Qdrant) SearchQdrant(query);

namespace TheWatch.Shared.Enums;

public enum VectorStoreProvider
{
    /// <summary>Azure CosmosDB with vector indexing (DiskANN). Native to Azure ecosystem.</summary>
    CosmosDB = 0,

    /// <summary>Google Firestore with KNN vector search. Native to Google Cloud.</summary>
    Firestore = 1,

    /// <summary>Qdrant — dedicated open-source vector database. Paired with Claude/Voyage AI.</summary>
    Qdrant = 2,

    /// <summary>Mock vector store for testing — brute-force cosine similarity over in-memory list.</summary>
    Mock = 99
}
