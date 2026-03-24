using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Google;

/// <summary>
/// Provides extension methods to register Google Cloud adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Google Cloud adapters as their port interfaces.
    /// TODO: Add configuration from IConfiguration to enable/disable this provider.
    /// </summary>
    public static IServiceCollection AddGoogleAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // TODO: Check configuration to determine if Google Cloud is enabled
        // var googleEnabled = config.GetValue<bool>("CloudProviders:Google:Enabled");

        // Register Google Cloud health provider
        services.AddSingleton<IInfrastructureHealthProvider, GoogleInfrastructureHealthProvider>();

        // Register Google Cloud port adapters
        services.AddSingleton<IFirestorePort, FirestorePortAdapter>();

        return services;
    }
}
