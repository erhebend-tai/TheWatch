// VectorSearchResult — a single result from a vector similarity search.
// Contains the matched document, its similarity score, and optional content.
//
// Example:
//   foreach (var result in searchResults)
//       Console.WriteLine($"[{result.Score:F3}] {result.Source}: {result.ContentPreview}");

namespace TheWatch.Shared.Domain.Models;

public class VectorSearchResult
{
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Cosine similarity score (0.0 = unrelated, 1.0 = identical).</summary>
    public float Score { get; set; }

    /// <summary>Full content of the matched document (if IncludeContent was true).</summary>
    public string? Content { get; set; }

    /// <summary>First 200 chars of content for quick preview.</summary>
    public string? ContentPreview { get; set; }

    /// <summary>Source path/identifier of the matched document.</summary>
    public string? Source { get; set; }

    public string? Namespace { get; set; }
    public int ChunkIndex { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
