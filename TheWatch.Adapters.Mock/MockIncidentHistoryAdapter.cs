// MockIncidentHistoryAdapter — in-memory mock adapter for IIncidentHistoryPort.
// Generates realistic seeded incident history with full timelines and HTML export.
//
// This is a PERMANENT first-class adapter. Every history screen works
// against this mock before live adapters (SQL read-model) exist.
//
// In production, this adapter is replaced by one that queries the SQL/Cosmos
// read-model built from event-sourced response coordination data.
//
// WAL: MockIncidentHistoryAdapter created with deterministic seeded data.

using System.Text;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockIncidentHistoryAdapter : IIncidentHistoryPort
{
    private readonly ILogger<MockIncidentHistoryAdapter> _logger;
    private readonly List<(string UserId, IncidentHistoryEntry Entry, IncidentTimeline Timeline)> _seeded;

    public MockIncidentHistoryAdapter(ILogger<MockIncidentHistoryAdapter> logger)
    {
        _logger = logger;
        _seeded = GenerateSeededData();
    }

    // ── Port Implementation ─────────────────────────────────────

    public Task<PaginatedResult<IncidentHistoryEntry>> GetHistoryAsync(
        string userId, int pageSize = 10, int pageNumber = 1,
        CancellationToken ct = default)
    {
        var userEntries = _seeded
            .Where(s => s.UserId == userId)
            .Select(s => s.Entry)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

        var page = userEntries
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new PaginatedResult<IncidentHistoryEntry>
        {
            Items = page,
            TotalCount = userEntries.Count,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        _logger.LogInformation(
            "[MockHistory] GetHistory for {UserId}: page {Page}/{Total}, {Count} items",
            userId, pageNumber, result.TotalPages, page.Count);

        return Task.FromResult(result);
    }

    public Task<IncidentTimeline?> GetTimelineAsync(
        string userId, string requestId, CancellationToken ct = default)
    {
        var match = _seeded.FirstOrDefault(s => s.UserId == userId && s.Entry.RequestId == requestId);
        if (match == default)
        {
            _logger.LogWarning("[MockHistory] Timeline not found: userId={UserId}, requestId={RequestId}",
                userId, requestId);
            return Task.FromResult<IncidentTimeline?>(null);
        }

        _logger.LogInformation("[MockHistory] GetTimeline for {RequestId}: {EventCount} events",
            requestId, match.Timeline.Events.Count);

        return Task.FromResult<IncidentTimeline?>(match.Timeline);
    }

    public Task<string?> ExportHtmlAsync(
        string userId, string requestId, CancellationToken ct = default)
    {
        var match = _seeded.FirstOrDefault(s => s.UserId == userId && s.Entry.RequestId == requestId);
        if (match == default)
            return Task.FromResult<string?>(null);

        var timeline = match.Timeline;
        var entry = match.Entry;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine($"  <title>Incident Report — {entry.RequestId}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    @media print {");
        sb.AppendLine("      body { font-size: 11pt; }");
        sb.AppendLine("      .no-print { display: none; }");
        sb.AppendLine("      @page { margin: 1in; }");
        sb.AppendLine("    }");
        sb.AppendLine("    body {");
        sb.AppendLine("      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;");
        sb.AppendLine("      max-width: 800px; margin: 0 auto; padding: 20px;");
        sb.AppendLine("      color: #1a1a1a; line-height: 1.6;");
        sb.AppendLine("    }");
        sb.AppendLine("    h1 { font-size: 1.5em; border-bottom: 2px solid #333; padding-bottom: 8px; }");
        sb.AppendLine("    h2 { font-size: 1.2em; color: #444; margin-top: 24px; }");
        sb.AppendLine("    .meta-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin: 16px 0; }");
        sb.AppendLine("    .meta-item { padding: 8px 12px; background: #f5f5f5; border-radius: 4px; }");
        sb.AppendLine("    .meta-label { font-weight: 600; font-size: 0.85em; color: #666; text-transform: uppercase; }");
        sb.AppendLine("    .meta-value { font-size: 1em; }");
        sb.AppendLine("    .timeline { border-left: 3px solid #2563eb; margin-left: 12px; padding-left: 20px; }");
        sb.AppendLine("    .event { margin-bottom: 16px; position: relative; }");
        sb.AppendLine("    .event::before {");
        sb.AppendLine("      content: ''; position: absolute; left: -26px; top: 6px;");
        sb.AppendLine("      width: 10px; height: 10px; background: #2563eb;");
        sb.AppendLine("      border-radius: 50%; border: 2px solid #fff;");
        sb.AppendLine("    }");
        sb.AppendLine("    .event-time { font-size: 0.85em; color: #666; }");
        sb.AppendLine("    .event-type { font-weight: 600; font-size: 0.8em; text-transform: uppercase;");
        sb.AppendLine("      color: #2563eb; letter-spacing: 0.5px; }");
        sb.AppendLine("    .event-detail { margin-top: 2px; }");
        sb.AppendLine("    .footer { margin-top: 32px; padding-top: 12px; border-top: 1px solid #ddd;");
        sb.AppendLine("      font-size: 0.8em; color: #888; }");
        sb.AppendLine("    .status-badge { display: inline-block; padding: 2px 8px; border-radius: 12px;");
        sb.AppendLine("      font-size: 0.85em; font-weight: 600; }");
        sb.AppendLine("    .status-resolved { background: #dcfce7; color: #166534; }");
        sb.AppendLine("    .status-cancelled { background: #fef3c7; color: #92400e; }");
        sb.AppendLine("    .status-escalated { background: #fee2e2; color: #991b1b; }");
        sb.AppendLine("    .btn-print { background: #2563eb; color: #fff; border: none; padding: 8px 16px;");
        sb.AppendLine("      border-radius: 4px; cursor: pointer; font-size: 0.9em; margin-bottom: 16px; }");
        sb.AppendLine("    .btn-print:hover { background: #1d4ed8; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Print button
        sb.AppendLine("  <button class=\"btn-print no-print\" onclick=\"window.print()\">Print / Save as PDF</button>");

        // Header
        sb.AppendLine($"  <h1>TheWatch Incident Report</h1>");

        // Summary metadata
        var statusClass = entry.Status switch
        {
            ResponseStatus.Resolved => "status-resolved",
            ResponseStatus.Cancelled => "status-cancelled",
            _ => "status-escalated"
        };

        sb.AppendLine("  <div class=\"meta-grid\">");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Incident ID</div><div class=\"meta-value\">{entry.RequestId}</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Status</div><div class=\"meta-value\"><span class=\"status-badge {statusClass}\">{entry.Status}</span></div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Scope</div><div class=\"meta-value\">{entry.Scope}</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Trigger</div><div class=\"meta-value\">{entry.TriggerSource ?? "Unknown"}</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Started</div><div class=\"meta-value\">{entry.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Ended</div><div class=\"meta-value\">{(entry.ResolvedAt.HasValue ? entry.ResolvedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "N/A")}</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Duration</div><div class=\"meta-value\">{(entry.DurationMinutes.HasValue ? $"{entry.DurationMinutes:F1} min" : "N/A")}</div></div>");
        sb.AppendLine($"    <div class=\"meta-item\"><div class=\"meta-label\">Responders</div><div class=\"meta-value\">{entry.ResponderCount}</div></div>");
        sb.AppendLine("  </div>");

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            sb.AppendLine($"  <p><strong>Description:</strong> {System.Net.WebUtility.HtmlEncode(entry.Description)}</p>");
        }

        // Timeline
        sb.AppendLine("  <h2>Event Timeline</h2>");
        sb.AppendLine("  <div class=\"timeline\">");

        foreach (var evt in timeline.Events.OrderBy(e => e.Timestamp))
        {
            sb.AppendLine("    <div class=\"event\">");
            sb.AppendLine($"      <div class=\"event-time\">{evt.Timestamp:HH:mm:ss} UTC</div>");
            sb.AppendLine($"      <div class=\"event-type\">{evt.EventType}</div>");
            if (!string.IsNullOrWhiteSpace(evt.ActorName))
                sb.AppendLine($"      <div><em>{System.Net.WebUtility.HtmlEncode(evt.ActorName)}</em></div>");
            sb.AppendLine($"      <div class=\"event-detail\">{System.Net.WebUtility.HtmlEncode(evt.Detail)}</div>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </div>");

        // Resolution
        if (!string.IsNullOrWhiteSpace(entry.Resolution))
        {
            sb.AppendLine($"  <h2>Resolution</h2>");
            sb.AppendLine($"  <p>{System.Net.WebUtility.HtmlEncode(entry.Resolution)}</p>");
        }

        // Footer
        sb.AppendLine("  <div class=\"footer\">");
        sb.AppendLine($"    <p>Generated by TheWatch on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine("    <p>This document is for personal records only. Responder identities have been anonymized.</p>");
        sb.AppendLine("    <p>For law enforcement or legal requests, contact support@thewatch.app with incident ID.</p>");
        sb.AppendLine("  </div>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        _logger.LogInformation("[MockHistory] Exported HTML for {RequestId}", requestId);
        return Task.FromResult<string?>(sb.ToString());
    }

    public Task<PersonalSafetyStats> GetStatsAsync(string userId, CancellationToken ct = default)
    {
        var userEntries = _seeded
            .Where(s => s.UserId == userId)
            .Select(s => s.Entry)
            .ToList();

        if (userEntries.Count == 0)
        {
            return Task.FromResult(new PersonalSafetyStats { UserId = userId });
        }

        var resolved = userEntries.Where(e => e.Status == ResponseStatus.Resolved).ToList();
        var cancelled = userEntries.Where(e => e.Status == ResponseStatus.Cancelled).ToList();
        var escalated = userEntries.Where(e => e.EscalationCount > 0).ToList();
        var durations = userEntries.Where(e => e.DurationMinutes.HasValue).Select(e => e.DurationMinutes!.Value).ToList();

        var scopeGroups = userEntries.GroupBy(e => e.Scope).OrderByDescending(g => g.Count()).ToList();
        var triggerGroups = userEntries
            .Where(e => !string.IsNullOrEmpty(e.TriggerSource))
            .GroupBy(e => e.TriggerSource!)
            .OrderByDescending(g => g.Count())
            .ToList();

        var stats = new PersonalSafetyStats
        {
            UserId = userId,
            TotalIncidents = userEntries.Count,
            ResolvedCount = resolved.Count,
            CancelledCount = cancelled.Count,
            EscalatedCount = escalated.Count,
            AverageResponseTimeMinutes = durations.Count > 0
                ? Math.Round(durations.Average() * 0.3, 1) : 0, // Approximate first-response time
            AverageDurationMinutes = durations.Count > 0
                ? Math.Round(durations.Average(), 1) : 0,
            AverageResponderCount = userEntries.Count > 0
                ? Math.Round(userEntries.Average(e => e.ResponderCount), 1) : 0,
            IncidentsResolvedWithoutEscalation = resolved.Count(e => e.EscalationCount == 0),
            MostCommonScope = scopeGroups.FirstOrDefault()?.Key,
            MostCommonTriggerSource = triggerGroups.FirstOrDefault()?.Key,
            FirstIncidentAt = userEntries.Min(e => e.CreatedAt),
            LastIncidentAt = userEntries.Max(e => e.CreatedAt)
        };

        _logger.LogInformation(
            "[MockHistory] Stats for {UserId}: {Total} incidents, {Resolved} resolved, avg {Duration:F1} min",
            userId, stats.TotalIncidents, stats.ResolvedCount, stats.AverageDurationMinutes);

        return Task.FromResult(stats);
    }

    // ── Seed Data Generation ─────────────────────────────────────

    private static List<(string UserId, IncidentHistoryEntry Entry, IncidentTimeline Timeline)> GenerateSeededData()
    {
        var data = new List<(string, IncidentHistoryEntry, IncidentTimeline)>();
        var rng = new Random(12345); // Deterministic for consistent mock data

        // Generate 15 incidents for a default user "u-demo"
        // and 8 incidents for "resp-001" (a volunteer who is also a user)
        var userConfigs = new[]
        {
            ("u-demo", 15),
            ("resp-001", 8),
            ("resp-002", 5),
        };

        foreach (var (userId, count) in userConfigs)
        {
            for (int i = 0; i < count; i++)
            {
                var requestId = $"req-{userId[..4]}-{(i + 1):D3}";
                var daysAgo = count - i + rng.Next(0, 7);
                var baseTime = DateTime.UtcNow.AddDays(-daysAgo).AddHours(rng.Next(6, 22));

                var scope = (ResponseScope)(rng.Next(0, 4)); // CheckIn through Evacuation
                var isResolved = rng.NextDouble() > 0.25; // 75% resolved, 25% cancelled
                var status = isResolved ? ResponseStatus.Resolved : ResponseStatus.Cancelled;
                var durationMinutes = Math.Round(1.5 + rng.NextDouble() * 15.0, 1);
                var resolvedAt = baseTime.AddMinutes(durationMinutes);
                var responderCount = scope switch
                {
                    ResponseScope.CheckIn => rng.Next(2, 6),
                    ResponseScope.Neighborhood => rng.Next(5, 12),
                    ResponseScope.Community => rng.Next(10, 30),
                    ResponseScope.Evacuation => rng.Next(20, 60),
                    _ => rng.Next(1, 5)
                };
                var escalationCount = rng.NextDouble() > 0.7 ? rng.Next(1, 3) : 0;

                var triggers = new[] { "PHRASE", "QUICK_TAP", "MANUAL_BUTTON", "FALL_DETECTION" };
                var triggerSource = triggers[rng.Next(triggers.Length)];
                var descriptions = new[]
                {
                    "Felt unsafe walking home late at night",
                    "Heard suspicious noises outside",
                    "Medical emergency — chest pain",
                    "Someone following me",
                    "Fell down — need help",
                    "Car accident — minor injuries",
                    "Smoke detector going off, possible fire",
                    null
                };
                var description = descriptions[rng.Next(descriptions.Length)];
                var resolutions = isResolved
                    ? new[] { "User confirmed safe", "Responder arrived, situation handled", "False alarm — user confirmed OK", "Emergency services on scene" }
                    : new[] { "User cancelled — false alarm", "User cancelled — feeling safe now", "Cancelled by user" };
                var resolution = resolutions[rng.Next(resolutions.Length)];

                var entry = new IncidentHistoryEntry
                {
                    RequestId = requestId,
                    Scope = scope,
                    Status = status,
                    TriggerSource = triggerSource,
                    Description = description,
                    Latitude = 30.2672 + (rng.NextDouble() - 0.5) * 0.02,
                    Longitude = -97.7431 + (rng.NextDouble() - 0.5) * 0.02,
                    CreatedAt = baseTime,
                    ResolvedAt = resolvedAt,
                    DurationMinutes = durationMinutes,
                    ResponderCount = responderCount,
                    EscalationCount = escalationCount,
                    Resolution = resolution
                };

                // Build timeline events
                var events = new List<TimelineEvent>();
                var t = baseTime;

                // 1. Trigger
                events.Add(new TimelineEvent
                {
                    EventType = TimelineEventType.Trigger,
                    Timestamp = t,
                    ActorName = "User",
                    Detail = $"SOS triggered via {triggerSource.ToLowerInvariant().Replace('_', ' ')}"
                });

                // 2. Dispatched
                t = t.AddSeconds(rng.Next(1, 5));
                var defaults = ResponseScopePresets.GetDefaults(scope);
                events.Add(new TimelineEvent
                {
                    EventType = TimelineEventType.Dispatched,
                    Timestamp = t,
                    ActorName = "System",
                    Detail = $"Dispatched {responderCount + rng.Next(2, 8)} responders within {defaults.RadiusMeters}m"
                });

                // 3. Acknowledgments
                for (int r = 0; r < Math.Min(responderCount, 5); r++)
                {
                    t = t.AddSeconds(rng.Next(10, 90));
                    events.Add(new TimelineEvent
                    {
                        EventType = TimelineEventType.Acknowledged,
                        Timestamp = t,
                        ActorName = $"Responder {r + 1}",
                        Detail = $"Acknowledged dispatch, ETA {rng.Next(2, 12)} minutes"
                    });
                }

                // 4. Optional escalation
                if (escalationCount > 0)
                {
                    t = t.AddSeconds(rng.Next(60, 180));
                    events.Add(new TimelineEvent
                    {
                        EventType = TimelineEventType.Escalated,
                        Timestamp = t,
                        ActorName = "System",
                        Detail = "Auto-escalated: insufficient responder acknowledgments within timeout"
                    });
                }

                // 5. Messages
                var messageCount = rng.Next(1, 5);
                for (int m = 0; m < messageCount; m++)
                {
                    t = t.AddSeconds(rng.Next(15, 60));
                    var msgs = new[]
                    {
                        "I can see the location, approaching now",
                        "On scene, everything looks OK",
                        "Traffic delay, ETA updated to 5 min",
                        "I'm here, making contact with user",
                        "Scene is clear, no visible threats"
                    };
                    events.Add(new TimelineEvent
                    {
                        EventType = TimelineEventType.Message,
                        Timestamp = t,
                        ActorName = $"Responder {rng.Next(1, Math.Max(2, responderCount))}",
                        Detail = msgs[rng.Next(msgs.Length)]
                    });
                }

                // 6. Optional arrival
                if (isResolved && rng.NextDouble() > 0.3)
                {
                    t = t.AddSeconds(rng.Next(30, 120));
                    events.Add(new TimelineEvent
                    {
                        EventType = TimelineEventType.ArrivedOnScene,
                        Timestamp = t,
                        ActorName = "Responder 1",
                        Detail = "Arrived on scene"
                    });
                }

                // 7. Resolution
                events.Add(new TimelineEvent
                {
                    EventType = isResolved ? TimelineEventType.Resolved : TimelineEventType.Cancelled,
                    Timestamp = resolvedAt,
                    ActorName = isResolved ? "System" : "User",
                    Detail = resolution
                });

                var timeline = new IncidentTimeline
                {
                    RequestId = requestId,
                    UserId = userId,
                    Scope = scope,
                    FinalStatus = status,
                    CreatedAt = baseTime,
                    ResolvedAt = resolvedAt,
                    DurationMinutes = durationMinutes,
                    TotalRespondersDispatched = responderCount + rng.Next(2, 8),
                    TotalRespondersAcknowledged = responderCount,
                    TotalEscalations = escalationCount,
                    Events = events
                };

                data.Add((userId, entry, timeline));
            }
        }

        return data;
    }
}
