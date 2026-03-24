// DevWorkLog — structured log entry for Claude Code interactions within the Aspire app.
// Every Claude Code request/response is logged here for audit, replay, and dashboard display.
// Serilog writes these as structured JSON; the DevWork DataGrid consumes them.
//
// Example:
//   new DevWorkLog
//   {
//       SessionId = "sess-abc123",
//       Action = "ImplementFeature",
//       Prompt = "Implement the evidence upload controller",
//       Response = "Created EvidenceController.cs with 8 endpoints...",
//       FilesCreated = new() { "Controllers/EvidenceController.cs" },
//       DurationMs = 45000,
//       TokensUsed = 12500,
//       Status = "Success"
//   };

namespace TheWatch.Shared.Domain.Models;

public class DevWorkLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Claude Code session/conversation ID for grouping related requests.</summary>
    public string? SessionId { get; set; }

    /// <summary>What Claude Code was asked to do (e.g., ImplementFeature, ReviewCode, FixBug).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The prompt or instruction sent to Claude Code.</summary>
    public string? Prompt { get; set; }

    /// <summary>Structured summary of Claude Code's response.</summary>
    public string? Response { get; set; }

    /// <summary>Feature IDs affected by this work.</summary>
    public List<string> FeatureIds { get; set; } = new();

    /// <summary>Files created during this work session.</summary>
    public List<string> FilesCreated { get; set; } = new();

    /// <summary>Files modified during this work session.</summary>
    public List<string> FilesModified { get; set; } = new();

    /// <summary>How long the Claude Code operation took.</summary>
    public long DurationMs { get; set; }

    /// <summary>Approximate token count for cost tracking.</summary>
    public int TokensUsed { get; set; }

    /// <summary>Success, Error, Partial, Timeout.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Error details if status is Error.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Webhook that triggered this work (if any).</summary>
    public string? WebhookSource { get; set; }

    /// <summary>Correlation ID for tracing through the Aspire pipeline.</summary>
    public string? CorrelationId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
