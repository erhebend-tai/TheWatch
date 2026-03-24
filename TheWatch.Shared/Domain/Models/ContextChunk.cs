// ContextChunk — a piece of retrieved context assembled for an LLM prompt.
// The IContextRetrievalPort aggregates VectorSearchResults from one or more
// provider stacks into ContextChunks, ready to be injected into a Claude/GPT/Gemini prompt.
//
// Example:
//   var chunks = await contextPort.RetrieveContextAsync("How does offline sync work?", ct);
//   var prompt = $"Given this context:\n{string.Join("\n---\n", chunks.Select(c => c.Content))}\n\nAnswer: ...";

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class ContextChunk
{
    /// <summary>The text content to inject into the LLM prompt.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Where this chunk came from (file path, document ID, URL).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Relevance score from vector search (0.0–1.0).</summary>
    public float RelevanceScore { get; set; }

    /// <summary>Which embedding provider found this chunk.</summary>
    public EmbeddingProvider Provider { get; set; }

    /// <summary>Which vector store it was retrieved from.</summary>
    public VectorStoreProvider Store { get; set; }

    /// <summary>Namespace the chunk belongs to (codebase, incidents, standards, etc.).</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Token count estimate for context window budget management.</summary>
    public int EstimatedTokens { get; set; }
}
