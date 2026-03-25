// =============================================================================
// TheWatch.Functions — Azure Functions worker entry point.
// =============================================================================
// Hosts RabbitMQ-triggered functions for async dispatch fan-out, escalation
// checks, evidence processing, and webhook receivers.
//
// Dependencies:
//   ISpatialIndex          — finds nearby responders (geospatial H3/geohash)
//   INotificationSendPort  — sends push notifications (FCM/APNs)
//   IParticipationPort     — checks responder opt-in/availability
//
// In Development: all ports use Mock adapters (in-memory, deterministic).
// In Production: swap via AdapterRegistry config in appsettings.Production.json.
//
// WAL: Functions are stateless. All state lives in the ports (databases, queues).
// =============================================================================

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheWatch.Adapters.Mock;
using TheWatch.Shared.Domain.Ports;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register mock adapters for development.
// In production, swap to live adapters via AdapterRegistry config.
builder.Services.AddFunctionsMockAdapters();

builder.Build().Run();

// ─────────────────────────────────────────────────────────────
// Extension methods for DI registration
// ─────────────────────────────────────────────────────────────

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register mock implementations of all ports consumed by Azure Functions.
    /// In production, replace with live adapters (PostGIS spatial, FCM/APNs notifications, etc.).
    ///
    /// Ports registered:
    ///   ISpatialIndex            — geospatial responder lookup
    ///   INotificationSendPort    — push notification delivery (FCM/APNs)
    ///   IParticipationPort       — responder opt-in/availability checks
    ///   IResponseRequestPort     — response request CRUD
    ///   IResponseTrackingPort    — responder acknowledgment tracking
    ///   IEscalationPort          — escalation scheduling/execution
    ///   INotificationTrackingPort — delivery tracking
    /// </summary>
    public static IServiceCollection AddFunctionsMockAdapters(this IServiceCollection services)
    {
        // Spatial index — used by ResponseDispatchFunction to find nearby responders
        services.AddSingleton<ISpatialIndex, MockSpatialIndex>();

        // Push notifications — used by ResponseDispatchFunction to send alerts
        services.AddSingleton<INotificationSendPort, MockNotificationSendAdapter>();

        // Notification tracking — records delivery status
        services.AddSingleton<INotificationTrackingPort, MockNotificationTrackingAdapter>();

        // Participation — responder opt-in checks (used by dispatch filter logic)
        services.AddSingleton<IParticipationPort, MockParticipationAdapter>();

        // Response coordination sub-ports (used by escalation and tracking functions)
        services.AddSingleton<IResponseRequestPort, MockResponseRequestAdapter>();
        services.AddSingleton<IResponseTrackingPort, MockResponseTrackingAdapter>();
        services.AddSingleton<IEscalationPort, MockEscalationAdapter>();
        services.AddSingleton<IResponseDispatchPort, MockResponseDispatchAdapter>();

        return services;
    }
}
