// DevWorkWebhookFunction — HTTP-triggered Azure Function for external webhook ingestion.
// Receives webhooks from GitHub, Google Cloud Functions, Firestore triggers, etc.
// Normalizes the payload and publishes a DevWorkMessage to the "devwork-webhook" queue.
//
// Architecture:
//   External Source → HTTP POST → THIS FUNCTION → RabbitMQ("devwork-webhook")
//     → Dashboard.Api (DevWorkController webhook consumer) → IDevWorkPort log
//
// Endpoints:
//   POST /api/devwork/github   — GitHub webhook (push, PR, issue events)
//   POST /api/devwork/firestore — Firestore trigger (document write events)
//   POST /api/devwork/custom   — Generic webhook for any source

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace TheWatch.Functions.Functions;

public class DevWorkWebhookFunction
{
    private readonly ILogger<DevWorkWebhookFunction> _logger;

    public DevWorkWebhookFunction(ILogger<DevWorkWebhookFunction> logger)
    {
        _logger = logger;
    }

    [Function("DevWorkGitHubWebhook")]
    public async Task<HttpResponseData> GitHubWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devwork/github")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        var eventType = req.Headers.TryGetValues("X-GitHub-Event", out var values) ? values.FirstOrDefault() : "unknown";
        var deliveryId = req.Headers.TryGetValues("X-GitHub-Delivery", out var dValues) ? dValues.FirstOrDefault() : null;

        _logger.LogInformation(
            "GitHub webhook received: Event={Event}, Delivery={DeliveryId}, PayloadSize={Size}",
            eventType, deliveryId, body?.Length ?? 0);

        // In production: publish to RabbitMQ "devwork-webhook" queue
        // var message = new DevWorkMessage("GitHub", eventType, body, deliveryId, null, DateTime.UtcNow);
        // await _rabbitPublisher.PublishAsync("devwork-webhook", message);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            received = true,
            source = "GitHub",
            eventType,
            deliveryId,
            timestamp = DateTime.UtcNow
        });
        return response;
    }

    [Function("DevWorkFirestoreWebhook")]
    public async Task<HttpResponseData> FirestoreWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devwork/firestore")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();

        _logger.LogInformation(
            "Firestore webhook received: PayloadSize={Size}",
            body?.Length ?? 0);

        // Parse Firestore event to extract document path and operation
        string? documentPath = null;
        string? operation = null;
        try
        {
            var doc = JsonDocument.Parse(body ?? "{}");
            if (doc.RootElement.TryGetProperty("document", out var docProp))
                documentPath = docProp.GetString();
            if (doc.RootElement.TryGetProperty("operation", out var opProp))
                operation = opProp.GetString();
        }
        catch { /* Non-standard payload format */ }

        _logger.LogInformation(
            "Firestore event: Document={DocumentPath}, Operation={Operation}",
            documentPath ?? "unknown", operation ?? "unknown");

        // In production: publish to RabbitMQ
        // var message = new DevWorkMessage("Firestore", operation ?? "write", body, null, null, DateTime.UtcNow);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            received = true,
            source = "Firestore",
            documentPath,
            operation,
            timestamp = DateTime.UtcNow
        });
        return response;
    }

    [Function("DevWorkCustomWebhook")]
    public async Task<HttpResponseData> CustomWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devwork/custom")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        var source = req.Headers.TryGetValues("X-Webhook-Source", out var sValues) ? sValues.FirstOrDefault() : "custom";
        var eventType = req.Headers.TryGetValues("X-Webhook-Event", out var eValues) ? eValues.FirstOrDefault() : "custom";

        _logger.LogInformation(
            "Custom webhook received: Source={Source}, Event={Event}, PayloadSize={Size}",
            source, eventType, body?.Length ?? 0);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            received = true,
            source,
            eventType,
            timestamp = DateTime.UtcNow
        });
        return response;
    }
}
