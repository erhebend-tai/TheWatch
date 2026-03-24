using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Azure;

/// <summary>
/// Provides extension methods to register Azure adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure adapters as their port interfaces.
    /// </summary>
    public static IServiceCollection AddAzureAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // Register Azure health provider
        services.AddSingleton<IInfrastructureHealthProvider, AzureInfrastructureHealthProvider>();

        // Register Azure port adapters
        services.AddSingleton<IAzurePort, AzurePortAdapter>();

        // Register Azure OpenAI Swarm adapter if endpoint is configured
        var aoaiEndpoint = config["AzureOpenAI:Endpoint"];
        var aoaiKey = config["AzureOpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(aoaiEndpoint) && !string.IsNullOrEmpty(aoaiKey))
        {
            services.AddSingleton<ISwarmPort>(sp =>
                new AzureOpenAISwarmAdapter(
                    aoaiEndpoint,
                    aoaiKey,
                    sp.GetRequiredService<ILogger<AzureOpenAISwarmAdapter>>()));
        }

        return services;
    }

    /// <summary>
    /// Registers the Azure OpenAI Swarm adapter standalone (for CLI usage without full DI).
    /// Example:
    ///   services.AddAzureOpenAISwarm("https://myinstance.openai.azure.com/", "key");
    /// </summary>
    public static IServiceCollection AddAzureOpenAISwarm(
        this IServiceCollection services, string endpoint, string apiKey)
    {
        services.AddSingleton<ISwarmPort>(sp =>
            new AzureOpenAISwarmAdapter(endpoint, apiKey,
                sp.GetRequiredService<ILogger<AzureOpenAISwarmAdapter>>()));
        return services;
    }
}
