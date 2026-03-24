using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.AWS;

/// <summary>
/// Provides extension methods to register AWS adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers AWS adapters as their port interfaces.
    /// TODO: Add configuration from IConfiguration to enable/disable this provider.
    /// </summary>
    public static IServiceCollection AddAwsAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // TODO: Check configuration to determine if AWS is enabled
        // var awsEnabled = config.GetValue<bool>("CloudProviders:AWS:Enabled");

        // Register AWS health provider
        services.AddSingleton<IInfrastructureHealthProvider, AwsInfrastructureHealthProvider>();

        // Register AWS port adapters
        services.AddSingleton<IAwsPort, AwsPortAdapter>();

        return services;
    }
}
