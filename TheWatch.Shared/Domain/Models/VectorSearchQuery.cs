// VectorSearchQuery — parameters for a similarity search against a vector store.
// Supports both text queries (auto-embedded) and pre-computed vector queries.
// Metadata filters scope the search to relevant namespaces and tags.
//
// Example:
//   new VectorSearchQuery
//   {
//       Text = "How does the evidence upload controller handle offline sync?",
//       Namespace = "codebase",
//       TopK = 10,
//       MinScore = 0.7f,
//       TagFilters = new() { "controller", "evidence" }
//   };

namespace TheWatch.Shared.Domain.Models;

public class VectorSearchQuery
{
    /// <summary>Natural language query text. Will be embedded by the configured provider.</summary>
    public string? Text { get; set; }

    /// <summary>Pre-computed query vector. If set, Text is ignored for embedding.</summary>
    public float[]? Vector { get; set; }

    /// <summary>Scope search to a specific namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Maximum number of results to return.</summary>
    public int TopK { get; set; } = 10;

    /// <summary>Minimum similarity score threshold (0.0-1.0). Results below this are filtered out.</summary>
    public float MinScore { get; set; } = 0.0f;

    /// <summary>Only return documents containing ALL of these tags.</summary>
    public List<string>? TagFilters { get; set; }

    /// <summary>Metadata key-value filters (provider-specific).</summary>
    public Dictionary<string, string>? MetadataFilters { get; set; }

    /// <summary>Include the full content in results (false = only ID + score + metadata).</summary>
    public bool IncludeContent { get; set; } = true;
}
