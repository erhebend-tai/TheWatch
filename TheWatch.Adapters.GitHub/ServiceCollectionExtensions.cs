using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.GitHub;

/// <summary>
/// Provides extension methods to register GitHub adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers GitHub adapters as their port interfaces.
    /// TODO: Add configuration from IConfiguration to enable/disable this provider.
    /// TODO: Register Octokit GitHubClient with proper authentication.
    /// </summary>
    public static IServiceCollection AddGitHubAdapters(this IServiceCollection services, IConfiguration config)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // TODO: Check configuration to determine if GitHub is enabled
        // var githubEnabled = config.GetValue<bool>("CloudProviders:GitHub:Enabled");
        // var githubToken = config.GetValue<string>("CloudProviders:GitHub:Token");
        // var repositoryOwner = config.GetValue<string>("CloudProviders:GitHub:RepositoryOwner");
        // var repositoryName = config.GetValue<string>("CloudProviders:GitHub:RepositoryName");

        // TODO: Register Octokit GitHubClient
        // services.AddSingleton(new GitHubClient(new ProductHeaderValue("TheWatch"))
        // {
        //     Credentials = new Credentials(githubToken)
        // });

        // Register GitHub health provider
        services.AddSingleton<IInfrastructureHealthProvider, GitHubInfrastructureHealthProvider>();

        // Register GitHub port adapters
        services.AddSingleton<IGitHubPort, GitHubPortAdapter>();

        return services;
    }
}
