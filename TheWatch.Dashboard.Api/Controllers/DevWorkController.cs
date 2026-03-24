// DevWorkController — REST endpoints for Claude Code DevWork logging and interaction.
// Provides structured logging of all Claude Code sessions, webhook receipts, and work results.
// The DevWork dashboard page queries these endpoints.
//
// Endpoints:
//   GET    /api/devwork/logs              — Recent DevWork logs
//   GET    /api/devwork/logs/{id}         — Single log entry
//   GET    /api/devwork/sessions/{sid}    — All logs for a session
//   GET    /api/devwork/features/{fid}    — All work done on a feature
//   POST   /api/devwork/logs              — Log a new work entry
//   POST   /api/devwork/webhook           — Receive webhook (GitHub, Firestore, custom)
//   PUT    /api/devwork/logs/{id}/status  — Update log status

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevWorkController : ControllerBase
{
    private readonly IDevWorkPort _devWorkPort;
    private readonly IFeatureTrackingPort _featurePort;
    private readonly ILogger<DevWorkController> _logger;

    public DevWorkController(IDevWorkPort devWorkPort, IFeatureTrackingPort featurePort, ILogger<DevWorkController> logger)
    {
        _devWorkPort = devWorkPort;
        _featurePort = featurePort;
        _logger = logger;
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetRecentLogs([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await _devWorkPort.GetRecentLogsAsync(limit, ct);
        return Ok(result.Data ?? new());
    }

    [HttpGet("logs/{id}")]
    public async Task<IActionResult> GetLogById(string id, CancellationToken ct)
    {
        var result = await _devWorkPort.GetByIdAsync(id, ct);
        return result.Success ? Ok(result.Data) : NotFound(new { error = result.ErrorMessage });
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetBySession(string sessionId, CancellationToken ct)
    {
        var result = await _devWorkPort.GetBySessionIdAsync(sessionId, ct);
        return Ok(result.Data ?? new());
    }

    [HttpGet("features/{featureId}")]
    public async Task<IActionResult> GetByFeature(string featureId, CancellationToken ct)
    {
        var result = await _devWorkPort.GetByFeatureIdAsync(featureId, ct);
        return Ok(result.Data ?? new());
    }

    [HttpPost("logs")]
    public async Task<IActionResult> LogWork([FromBody] DevWorkLog log, CancellationToken ct)
    {
        log.CorrelationId ??= HttpContext.TraceIdentifier;
        var result = await _devWorkPort.LogWorkAsync(log, ct);

        _logger.LogInformation(
            "DevWork logged: {Action} | Session={SessionId} | Features=[{Features}] | Status={Status}",
            log.Action, log.SessionId, string.Join(",", log.FeatureIds), log.Status);

        return result.Success ? CreatedAtAction(nameof(GetLogById), new { id = result.Data!.Id }, result.Data) : StatusCode(500, new { error = result.ErrorMessage });
    }

    /// <summary>
    /// Webhook endpoint for external systems (GitHub, Firestore triggers, Google Cloud Functions).
    /// Logs the webhook receipt and optionally correlates to feature IDs.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook(
        [FromHeader(Name = "X-Webhook-Source")] string? source,
        [FromHeader(Name = "X-Webhook-Event")] string? eventType,
        [FromBody] object payload,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);

        _logger.LogInformation(
            "Webhook received: Source={Source}, Event={Event}, CorrelationId={CorrelationId}, PayloadSize={Size}",
            source ?? "unknown", eventType ?? "unknown", correlationId, payloadJson.Length);

        var log = new DevWorkLog
        {
            Action = $"Webhook:{eventType ?? "unknown"}",
            Prompt = $"Webhook from {source}: {eventType}",
            Response = payloadJson.Length > 2000 ? payloadJson[..2000] + "..." : payloadJson,
            WebhookSource = source ?? "unknown",
            CorrelationId = correlationId,
            Status = "Received",
            Timestamp = DateTime.UtcNow
        };

        var result = await _devWorkPort.LogWorkAsync(log, ct);

        // In production: publish DevWorkMessage to RabbitMQ for async processing
        // await _rabbitPublisher.PublishAsync("devwork-webhook", new DevWorkMessage(...));

        return Ok(new
        {
            received = true,
            logId = result.Data?.Id,
            correlationId,
            source,
            eventType
        });
    }

    [HttpPut("logs/{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] DevWorkStatusUpdate update, CancellationToken ct)
    {
        var result = await _devWorkPort.UpdateStatusAsync(id, update.Status, update.ErrorMessage, ct);
        return result.Success ? Ok(new { id, update.Status }) : NotFound(new { error = result.ErrorMessage });
    }
}

public record DevWorkStatusUpdate(string Status, string? ErrorMessage = null);
