// WebhookReceiverFunction — HTTP-triggered functions for receiving external webhooks.
// Handles: mobile device status updates, responder acknowledgments via deep link,
// and 3rd-party integration callbacks (future: Twilio, SendGrid, etc.)

using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Functions.Functions;

public class WebhookReceiverFunction
{
    private readonly ILogger<WebhookReceiverFunction> _logger;
    private readonly ISmsPort _smsPort;
    private readonly IResponseTrackingPort _trackingPort;
    private readonly INotificationTrackingPort _notifTrackingPort;

    public WebhookReceiverFunction(
        ILogger<WebhookReceiverFunction> logger,
        ISmsPort smsPort,
        IResponseTrackingPort trackingPort,
        INotificationTrackingPort notifTrackingPort)
    {
        _logger = logger;
        _smsPort = smsPort;
        _trackingPort = trackingPort;
        _notifTrackingPort = notifTrackingPort;
    }

    /// <summary>
    /// Receives SOS trigger events from mobile devices.
    /// The mobile app POSTs here when an SOS is triggered (phrase, tap, or manual).
    /// This function creates the ResponseRequest and publishes to the dispatch queue.
    /// </summary>
    [Function("SOSTrigger")]
    public async Task<HttpResponseData> HandleSOSTrigger(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/sos")] HttpRequestData req)
    {
        _logger.LogInformation("SOS trigger webhook received");

        var body = await req.ReadAsStringAsync();
        var trigger = JsonSerializer.Deserialize<SOSTriggerPayload>(body ?? "{}");

        if (trigger is null || string.IsNullOrEmpty(trigger.UserId))
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid SOS trigger payload");
            return badResponse;
        }

        _logger.LogWarning(
            "SOS TRIGGERED: UserId={UserId}, Scope={Scope}, Source={Source}, Lat={Lat}, Lng={Lng}",
            trigger.UserId, trigger.Scope, trigger.TriggerSource, trigger.Latitude, trigger.Longitude);

        // Build ResponseRequest from trigger
        var defaults = ResponseScopePresets.GetDefaults(trigger.Scope);
        var requestId = Guid.NewGuid().ToString("N")[..12];

        // In production: publish to "response-dispatch" RabbitMQ queue
        // await _rabbitMqPublisher.PublishAsync("response-dispatch", new ResponseDispatchMessage(...));

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            requestId,
            status = "dispatching",
            scope = trigger.Scope.ToString(),
            radius = defaults.RadiusMeters,
            desiredResponders = defaults.DesiredResponders,
            escalationPolicy = defaults.Escalation.ToString()
        });

        return response;
    }

    /// <summary>
    /// Receives responder acknowledgments via deep link / push notification action.
    /// When a responder taps "I'm on my way" in their notification, it hits this endpoint.
    /// </summary>
    [Function("ResponderAck")]
    public async Task<HttpResponseData> HandleResponderAck(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/ack")] HttpRequestData req)
    {
        _logger.LogInformation("Responder acknowledgment webhook received");

        var body = await req.ReadAsStringAsync();
        var ack = JsonSerializer.Deserialize<ResponderAckPayload>(body ?? "{}");

        if (ack is null || string.IsNullOrEmpty(ack.RequestId))
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid acknowledgment payload");
            return badResponse;
        }

        _logger.LogInformation(
            "Responder ACK: RequestId={RequestId}, ResponderId={ResponderId}, Status={Status}",
            ack.RequestId, ack.ResponderId, ack.Status);

        // In production: update IResponseTrackingPort, check if enough responders
        // to cancel scheduled escalation

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { accepted = true });
        return response;
    }

    /// <summary>
    /// Receives inbound SMS replies from the SMS gateway (Twilio, Azure Communication Services).
    /// When a user replies "Y" or "N" to an SMS notification, the gateway POSTs here.
    ///
    /// Twilio payload: From, Body, To, MessageSid, AccountSid
    /// Azure Comm Services payload: from, message.text, to, messageId
    ///
    /// This function:
    ///   1. Parses the reply via ISmsPort.ProcessInboundSmsAsync()
    ///   2. Converts to a NotificationResponse (Accept/Decline/NeedHelp/etc.)
    ///   3. If Accept: creates a ResponderAcknowledgment via IResponseTrackingPort
    ///   4. Tracks the response via INotificationTrackingPort
    /// </summary>
    [Function("SmsInbound")]
    public async Task<HttpResponseData> HandleInboundSms(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/sms/inbound")] HttpRequestData req)
    {
        _logger.LogInformation("Inbound SMS webhook received");

        var body = await req.ReadAsStringAsync();

        // Support both Twilio and Azure Communication Services payload formats
        string? fromNumber = null;
        string? smsBody = null;
        string? toNumber = null;

        // Try Twilio format first (form-urlencoded)
        if (req.Headers.TryGetValues("Content-Type", out var contentTypes) &&
            contentTypes.Any(ct => ct.Contains("application/x-www-form-urlencoded")))
        {
            // Parse Twilio's form-urlencoded body without System.Web dependency
            var pairs = (body ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => Uri.UnescapeDataString(p[0]),
                    p => Uri.UnescapeDataString(p[1]),
                    StringComparer.OrdinalIgnoreCase
                );
            fromNumber = pairs.GetValueOrDefault("From");
            smsBody = pairs.GetValueOrDefault("Body");
            toNumber = pairs.GetValueOrDefault("To");
        }
        else
        {
            // Try JSON format (Azure Comm Services or generic)
            var json = JsonSerializer.Deserialize<JsonElement>(body ?? "{}");
            fromNumber = json.TryGetProperty("from", out var f) ? f.GetString()
                       : json.TryGetProperty("From", out var fAlt) ? fAlt.GetString() : null;
            smsBody = json.TryGetProperty("message", out var m) && m.TryGetProperty("text", out var mt) ? mt.GetString()
                    : json.TryGetProperty("Body", out var b) ? b.GetString() : null;
            toNumber = json.TryGetProperty("to", out var t) ? t.GetString()
                     : json.TryGetProperty("To", out var tAlt) ? tAlt.GetString() : null;
        }

        if (string.IsNullOrEmpty(fromNumber) || string.IsNullOrEmpty(smsBody))
        {
            _logger.LogWarning("Invalid inbound SMS: missing From or Body");
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Missing From or Body");
            return badResponse;
        }

        _logger.LogInformation(
            "SMS INBOUND: From={From}, Body=\"{Body}\"",
            fromNumber, smsBody.Length > 50 ? smsBody[..50] + "..." : smsBody);

        // Parse the reply into a structured response
        var notifResponse = await _smsPort.ProcessInboundSmsAsync(fromNumber, smsBody, toNumber);

        if (notifResponse is null)
        {
            _logger.LogWarning("Could not parse SMS reply from {From}: \"{Body}\"", fromNumber, smsBody);

            // Send a help message back
            await _smsPort.SendSmsAsync(fromNumber,
                "TheWatch: Reply Y to accept, N to decline, HELP if you need assistance, or OK if you're safe.");

            var unknownResponse = req.CreateResponse(HttpStatusCode.OK);
            await unknownResponse.WriteAsJsonAsync(new { parsed = false, helpSent = true });
            return unknownResponse;
        }

        // Track the notification response
        await _notifTrackingPort.RecordResponseAsync(notifResponse);

        // If Accept → create a responder acknowledgment
        if (notifResponse.Action == NotificationResponseAction.Accept)
        {
            var ack = new ResponderAcknowledgment(
                AckId: Guid.NewGuid().ToString("N")[..12],
                RequestId: notifResponse.RequestId,
                ResponderId: notifResponse.ResponderId,
                ResponderName: notifResponse.ResponderId, // Will be resolved by tracking port
                ResponderRole: null,
                ResponderLatitude: notifResponse.ResponderLatitude ?? 0,
                ResponderLongitude: notifResponse.ResponderLongitude ?? 0,
                DistanceMeters: 0, // Unknown from SMS — no location data
                EstimatedArrival: null,
                Status: AckStatus.Acknowledged,
                AcknowledgedAt: DateTime.UtcNow
            );

            await _trackingPort.AcknowledgeAsync(ack);

            _logger.LogInformation(
                "SMS Accept converted to acknowledgment: {ResponderId} → {RequestId}",
                notifResponse.ResponderId, notifResponse.RequestId);

            // Send confirmation
            await _smsPort.SendSmsAsync(fromNumber,
                "TheWatch: You've accepted the request. Open the app for details and to share your location.");
        }
        else if (notifResponse.Action == NotificationResponseAction.NeedHelp)
        {
            _logger.LogWarning(
                "SMS NEED HELP from {From} for request {RequestId} — triggering secondary response",
                fromNumber, notifResponse.RequestId);

            // TODO: Trigger secondary SOS response for this person
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            parsed = true,
            action = notifResponse.Action.ToString(),
            requestId = notifResponse.RequestId,
            responderId = notifResponse.ResponderId
        });
        return response;
    }

    /// <summary>
    /// Receives delivery status callbacks from the SMS gateway.
    /// Twilio sends: MessageSid, MessageStatus (queued, sent, delivered, failed, etc.)
    /// </summary>
    [Function("SmsStatus")]
    public async Task<HttpResponseData> HandleSmsStatus(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/sms/status")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        _logger.LogDebug("SMS status callback: {Body}", body);

        // Parse status update and record delivery result
        // In production: update NotificationDeliveryStatus in tracking port

        var response = req.CreateResponse(HttpStatusCode.OK);
        return response;
    }

    /// <summary>
    /// Health check endpoint for the Functions app.
    /// </summary>
    [Function("HealthCheck")]
    public HttpResponseData HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("TheWatch.Functions healthy");
        return response;
    }
}

// ─────────────────────────────────────────────────────────────
// Webhook Payloads
// ─────────────────────────────────────────────────────────────

public record SOSTriggerPayload(
    string UserId,
    string? DeviceId,
    ResponseScope Scope,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    string? TriggerSource,  // "PHRASE", "QUICK_TAP", "MANUAL_BUTTON"
    float? TriggerConfidence,
    string? Description
);

public record ResponderAckPayload(
    string RequestId,
    string ResponderId,
    AckStatus Status,
    double? ResponderLatitude,
    double? ResponderLongitude,
    int? EstimatedArrivalMinutes
);
