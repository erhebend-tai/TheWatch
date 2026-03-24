using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Oracle;

/// <summary>
/// Provides extension methods to register Oracle Cloud adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Oracle Cloud adapters as their port interfaces.
    /// TODO: Add configuration from IConfiguration to enable/disable this provider.
    /// </summary>
    public static IServiceCollection AddOracleAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // TODO: Check configuration to determine if Oracle Cloud is enabled
        // var oracleEnabled = config.GetValue<bool>("CloudProviders:Oracle:Enabled");

        // Register Oracle Cloud health provider
        services.AddSingleton<IInfrastructureHealthProvider, OracleInfrastructureHealthProvider>();

        // Register Oracle Cloud port adapters
        services.AddSingleton<IOraclePort, OraclePortAdapter>();

        return services;
    }
}
