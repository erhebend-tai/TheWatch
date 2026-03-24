using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Configuration;
using TheWatch.Adapters.Mock;

namespace TheWatch.Dashboard.Api.Configuration;

/// <summary>
/// Master adapter wiring for the Dashboard API.
/// Reads AdapterRegistry from configuration and registers the correct
/// adapter implementations. This is the ONLY place that knows about
/// specific adapter projects.
///
/// To add a new cloud provider:
///   1. Create TheWatch.Adapters.NewProvider/ project
///   2. Add ProjectReference to Dashboard.Api.csproj
///   3. Add case here for "Live" → call AddNewProviderAdapters()
///   4. Add config entry to AdapterRegistry
///
/// That's it. No other files change.
/// </summary>
public static class AdapterWiring
{
    public static IServiceCollection AddTheWatchAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var registry = new AdapterRegistry();
        configuration.GetSection(AdapterRegistry.SectionName).Bind(registry);
        services.AddSingleton(registry);

        // ──────────────────────────────────────────────
        // Mock adapters are ALWAYS registered.
        // They serve as fallback for any provider set to "Mock".
        // ──────────────────────────────────────────────
        services.AddMockAdapters();

        // ──────────────────────────────────────────────
        // Native adapters override mocks when configured.
        // They utilize local hardware, SQLite, and platform APIs.
        // Uncomment each block when the native adapter project is ready.
        // ──────────────────────────────────────────────

        // if (registry.IsNative("Azure")) 
        // {
        //     // using TheWatch.Adapters.Native.Storage;
        //     // services.AddNativeStorageAdapters(configuration);
        // }

        // ──────────────────────────────────────────────
        // Live adapters override mocks and native when configured.
        // Uncomment each block when the adapter project is ready.
        // ──────────────────────────────────────────────

        // if (registry.IsLive("GitHub"))
        // {
        //     // using TheWatch.Adapters.GitHub;
        //     // services.AddGitHubAdapters(configuration);
        // }

        // if (registry.IsLive("Azure"))
        // {
        //     // using TheWatch.Adapters.Azure;
        //     // services.AddAzureAdapters(configuration);
        // }

        // if (registry.IsLive("AWS"))
        // {
        //     // using TheWatch.Adapters.AWS;
        //     // services.AddAwsAdapters(configuration);
        // }

        // if (registry.IsLive("Google"))
        // {
        //     // using TheWatch.Adapters.Google;
        //     // services.AddGoogleAdapters(configuration);
        // }

        // if (registry.IsLive("Oracle"))
        // {
        //     // using TheWatch.Adapters.Oracle;
        //     // services.AddOracleAdapters(configuration);
        // }

        // if (registry.IsLive("Cloudflare"))
        // {
        //     // using TheWatch.Adapters.Cloudflare;
        //     // services.AddCloudflareAdapters(configuration);
        // }

        // ──────────────────────────────────────────────
        // IoT Platform adapters override mocks when configured.
        // Each platform has its own adapter for webhook validation
        // and OAuth2 token management.
        // ──────────────────────────────────────────────

        // if (registry.IsLive("Alexa"))
        // {
        //     // using TheWatch.Adapters.Alexa;
        //     // services.AddAlexaAdapters(configuration);
        //     // Validates Alexa request signing cert chain
        //     // Handles Alexa Skills Kit IntentRequest/LaunchRequest/SessionEnded
        //     // Supports Smart Home API for device discovery
        // }

        // if (registry.IsLive("GoogleHome"))
        // {
        //     // using TheWatch.Adapters.GoogleHome;
        //     // services.AddGoogleHomeAdapters(configuration);
        //     // Validates Google JWT with public key set
        //     // Handles Actions on Google fulfillment webhooks
        //     // Supports Home Graph for device sync
        // }

        // if (registry.IsLive("SmartThings"))
        // {
        //     // using TheWatch.Adapters.SmartThings;
        //     // services.AddSmartThingsAdapters(configuration);
        //     // HMAC-SHA256 signature validation
        //     // SmartApp lifecycle: PING, CONFIGURATION, INSTALL, UPDATE, EVENT, UNINSTALL
        //     // Device capability model integration
        // }

        // if (registry.IsLive("IFTTT"))
        // {
        //     // using TheWatch.Adapters.IFTTT;
        //     // services.AddIFTTTAdapters(configuration);
        //     // IFTTT-Service-Key validation
        //     // Trigger/Action API for IFTTT service integration
        // }

        // if (registry.IsLive("Matter"))
        // {
        //     // using TheWatch.Adapters.Matter;
        //     // services.AddMatterAdapters(configuration);
        //     // Matter SDK integration for Thread/WiFi devices
        //     // Supports commissioning flow and device binding
        // }

        return services;
    }
}
