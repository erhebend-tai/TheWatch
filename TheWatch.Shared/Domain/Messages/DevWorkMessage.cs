// DevWorkMessage — RabbitMQ message for Claude Code webhook-triggered work.
// Published by DevWorkWebhookFunction when a webhook fires.
// Consumed by the Dashboard.Api to log and optionally trigger Claude Code.
//
// Example:
//   new DevWorkMessage(
//       WebhookSource: "GitHub",
//       EventType: "push",
//       Payload: "{ \"ref\": \"refs/heads/main\", ... }",
//       CorrelationId: "corr-abc123",
//       FeatureIds: new[] { "feat-evidence-upload" }
//   );

namespace TheWatch.Shared.Domain.Messages;

public record DevWorkMessage(
    string WebhookSource,
    string EventType,
    string? Payload,
    string? CorrelationId,
    string[]? FeatureIds,
    DateTime Timestamp
);
