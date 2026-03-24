using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register mock adapters for development.
// In production, swap to live adapters via AdapterRegistry config.
builder.Services.AddMockResponseCoordination();

builder.Build().Run();

// ─────────────────────────────────────────────────────────────
// Extension methods for DI registration
// ─────────────────────────────────────────────────────────────

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register mock implementations of response coordination ports.
    /// In production, replace with live RabbitMQ/Hangfire implementations.
    /// </summary>
    public static IServiceCollection AddMockResponseCoordination(this IServiceCollection services)
    {
        // These will be registered by the mock adapter project.
        // For now, function implementations use the ports directly.
        return services;
    }
}
