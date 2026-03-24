using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;

namespace TheWatch.Dashboard.Api.Controllers;

/// <summary>
/// Receives structured log entries from Android and iOS devices.
///
/// Mobile devices sync their local logs (Room/SwiftData) to Firestore,
/// but can also POST directly to this endpoint when the Dashboard API
/// is reachable. This enables real-time log viewing in the MAUI dashboard
/// without Firestore latency.
///
/// Ingest flow:
///   Mobile device → POST /api/mobilelog/ingest → broadcast via SignalR
///   Mobile device → POST /api/mobilelog/batch  → bulk ingest + broadcast
///
/// The MAUI dashboard subscribes to "MobileLogReceived" SignalR events
/// to display logs in real time.
/// </summary>
[ApiController]
[Route("api/mobilelog")]
public class MobileLogController : ControllerBase
{
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<MobileLogController> _logger;

    // In-memory ring buffer for recent logs (dev mode).
    // In production, these would be written to a database.
    private static readonly LinkedList<MobileLogEntry> _recentLogs = new();
    private static readonly object _lock = new();
    private const int MaxBufferSize = 5000;

    public MobileLogController(IHubContext<DashboardHub> hub, ILogger<MobileLogController> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a single log entry from a mobile device.
    /// </summary>
    [HttpPost("ingest")]
    public async Task<ActionResult> Ingest([FromBody] MobileLogEntry entry)
    {
        BufferEntry(entry);

        // Broadcast to MAUI dashboard viewers
        await _hub.Clients.All.SendAsync("MobileLogReceived", entry);

        // Also log to server Serilog for correlation
        LogToSerilog(entry);

        return Accepted();
    }

    /// <summary>
    /// Ingest a batch of log entries (used by periodic sync).
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<BatchIngestResult>> IngestBatch([FromBody] List<MobileLogEntry> entries)
    {
        if (entries == null || entries.Count == 0)
            return BadRequest(new { error = "Empty batch" });

        var accepted = 0;
        foreach (var entry in entries)
        {
            BufferEntry(entry);
            LogToSerilog(entry);
            accepted++;
        }

        // Broadcast summary to dashboard
        await _hub.Clients.All.SendAsync("MobileLogBatchReceived", new
        {
            Count = accepted,
            Devices = entries.Select(e => e.DeviceId).Distinct().Count(),
            Platforms = entries.Select(e => e.Platform).Distinct().ToList(),
            TimeRange = new
            {
                From = entries.Min(e => e.TimestampMs),
                To = entries.Max(e => e.TimestampMs)
            }
        });

        _logger.LogInformation(
            "Batch ingest: {Count} entries from {Devices} device(s)",
            accepted, entries.Select(e => e.DeviceId).Distinct().Count());

        return Ok(new BatchIngestResult(accepted, entries.Count));
    }

    /// <summary>
    /// Query recent mobile log entries.
    /// </summary>
    [HttpGet("recent")]
    public ActionResult<List<MobileLogEntry>> GetRecent(
        [FromQuery] int limit = 100,
        [FromQuery] int? minLevel = null,
        [FromQuery] string? sourceContext = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? deviceId = null,
        [FromQuery] string? correlationId = null)
    {
        lock (_lock)
        {
            var query = _recentLogs.AsEnumerable();

            if (minLevel.HasValue)
                query = query.Where(e => e.Level >= minLevel.Value);
            if (sourceContext != null)
                query = query.Where(e => e.SourceContext == sourceContext);
            if (platform != null)
                query = query.Where(e => string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase));
            if (deviceId != null)
                query = query.Where(e => e.DeviceId == deviceId);
            if (correlationId != null)
                query = query.Where(e => e.CorrelationId == correlationId);

            return Ok(query.Take(limit).ToList());
        }
    }

    /// <summary>
    /// Get distinct source contexts for filtering UI.
    /// </summary>
    [HttpGet("sources")]
    public ActionResult<List<string>> GetSources()
    {
        lock (_lock)
        {
            return Ok(_recentLogs
                .Select(e => e.SourceContext)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList());
        }
    }

    /// <summary>
    /// Get connected device summary.
    /// </summary>
    [HttpGet("devices")]
    public ActionResult<List<DeviceSummary>> GetDevices()
    {
        lock (_lock)
        {
            var devices = _recentLogs
                .Where(e => !string.IsNullOrEmpty(e.DeviceId))
                .GroupBy(e => e.DeviceId)
                .Select(g => new DeviceSummary(
                    DeviceId: g.Key!,
                    Platform: g.First().Platform ?? "Unknown",
                    EntryCount: g.Count(),
                    LastSeen: g.Max(e => e.TimestampMs),
                    ErrorCount: g.Count(e => e.Level >= 4) // Error + Fatal
                ))
                .OrderByDescending(d => d.LastSeen)
                .ToList();

            return Ok(devices);
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static void BufferEntry(MobileLogEntry entry)
    {
        lock (_lock)
        {
            _recentLogs.AddFirst(entry);
            while (_recentLogs.Count > MaxBufferSize)
                _recentLogs.RemoveLast();
        }
    }

    private void LogToSerilog(MobileLogEntry entry)
    {
        var msg = $"[{entry.Platform}/{entry.DeviceId}] [{entry.SourceContext}] {entry.RenderedMessage}";
        switch (entry.Level)
        {
            case 0: case 1: _logger.LogDebug(msg); break;
            case 2: _logger.LogInformation(msg); break;
            case 3: _logger.LogWarning(msg); break;
            case 4: _logger.LogError("{MobileLog} {Exception}", msg, entry.Exception ?? ""); break;
            case 5: _logger.LogCritical("{MobileLog} {Exception}", msg, entry.Exception ?? ""); break;
        }
    }
}

// ── DTOs ─────────────────────────────────────────────────────

/// <summary>
/// Structured log entry received from Android/iOS.
/// Matches the LogEntry model on both platforms.
/// </summary>
public record MobileLogEntry(
    string Id,
    long TimestampMs,
    int Level, // 0=Verbose, 1=Debug, 2=Info, 3=Warning, 4=Error, 5=Fatal
    string SourceContext,
    string MessageTemplate,
    string RenderedMessage,
    Dictionary<string, string>? Properties = null,
    string? Exception = null,
    string? CorrelationId = null,
    string? UserId = null,
    string? DeviceId = null,
    string? Platform = null // "Android" or "iOS"
);

public record BatchIngestResult(int Accepted, int Total);

public record DeviceSummary(
    string DeviceId,
    string Platform,
    int EntryCount,
    long LastSeen,
    int ErrorCount
);
