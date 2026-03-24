using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Cloudflare;

/// <summary>
/// Provides extension methods to register Cloudflare adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Cloudflare adapters as their port interfaces.
    /// Cloudflare communicates via REST API (no SDK required).
    /// TODO: Add configuration from IConfiguration to enable/disable this provider.
    /// </summary>
    public static IServiceCollection AddCloudflareAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // TODO: Check configuration to determine if Cloudflare is enabled
        // var cloudflareEnabled = config.GetValue<bool>("CloudProviders:Cloudflare:Enabled");
        // var cloudflareApiKey = config.GetValue<string>("CloudProviders:Cloudflare:ApiKey");

        // Register Cloudflare health provider
        services.AddSingleton<IInfrastructureHealthProvider, CloudflareInfrastructureHealthProvider>();

        // Register Cloudflare port adapters
        services.AddSingleton<ICloudflarePort, CloudflarePortAdapter>();

        return services;
    }
}
