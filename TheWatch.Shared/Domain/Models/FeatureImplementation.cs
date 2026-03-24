// FeatureImplementation — tracks a feature's implementation status in the MudBlazor DataGrid.
// Populated from Firestore (Google Cloud Functions logs) and Aspire application logs.
// Serilog structured logs enriched with FeatureId are correlated to these records.
//
// Example:
//   new FeatureImplementation
//   {
//       Id = "feat-evidence-upload",
//       Name = "Evidence Upload Controller",
//       Category = FeatureCategory.EvidenceSystem,
//       Status = FeatureStatus.Completed,
//       Project = "TheWatch.Dashboard.Api",
//       FilePaths = new() { "Controllers/EvidenceController.cs" },
//       AssignedTo = "Claude Code",
//       CompletedAt = DateTime.UtcNow
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class FeatureImplementation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public FeatureCategory Category { get; set; }
    public FeatureStatus Status { get; set; } = FeatureStatus.Planned;

    /// <summary>Which project/solution this feature lives in.</summary>
    public string? Project { get; set; }

    /// <summary>File paths affected by this feature (relative to solution root).</summary>
    public List<string> FilePaths { get; set; } = new();

    /// <summary>Who/what is implementing (e.g., "Claude Code", "Developer", "CI/CD").</summary>
    public string? AssignedTo { get; set; }

    /// <summary>Priority 1 = highest. Used for DataGrid sort.</summary>
    public int Priority { get; set; } = 5;

    /// <summary>0-100 completion percentage for partial progress.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Firestore document ID for bi-directional sync.</summary>
    public string? FirestoreDocId { get; set; }

    /// <summary>Source of the last update (Firestore, Aspire, ClaudeCode, Manual).</summary>
    public string? LastUpdateSource { get; set; }

    public string? BlockedReason { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
