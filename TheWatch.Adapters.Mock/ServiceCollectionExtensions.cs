using Microsoft.Extensions.DependencyInjection;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Provides extension methods to register mock adapters with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all mock adapters as their port interfaces.
    /// </summary>
    public static IServiceCollection AddMockAdapters(this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Register mock infrastructure health provider
        services.AddSingleton<IInfrastructureHealthProvider, MockInfrastructureHealthProvider>();

        // Register health aggregator
        services.AddSingleton<IInfrastructureHealthPort, InfrastructureHealthAggregator>();

        // Register mock cloud adapters
        services.AddSingleton<IAzurePort, MockAzureAdapter>();
        services.AddSingleton<IAwsPort, MockAwsAdapter>();
        services.AddSingleton<IFirestorePort, MockFirestoreAdapter>();
        services.AddSingleton<IGitHubPort, MockGitHubAdapter>();

        // Register mock response coordination adapters
        services.AddSingleton<IResponseRequestPort, MockResponseRequestAdapter>();
        services.AddSingleton<IResponseDispatchPort, MockResponseDispatchAdapter>();
        services.AddSingleton<IResponseTrackingPort, MockResponseTrackingAdapter>();
        services.AddSingleton<IEscalationPort, MockEscalationAdapter>();
        services.AddSingleton<IParticipationPort, MockParticipationAdapter>();
        services.AddSingleton<INavigationPort, MockNavigationAdapter>();
        services.AddSingleton<IMessageGuardrailsPort, MockMessageGuardrailsAdapter>();
        services.AddSingleton<IResponderCommunicationPort, MockResponderCommunicationAdapter>();

        // Register mock CCTV / security camera adapter
        services.AddSingleton<ICCTVPort, MockCCTVAdapter>();

        // Register mock guard reporting adapter
        services.AddSingleton<IGuardReportPort, MockGuardReportAdapter>();

        // Register mock notification & SMS adapters
        services.AddSingleton<INotificationSendPort, MockNotificationSendAdapter>();
        services.AddSingleton<ISmsPort, MockSmsAdapter>();
        services.AddSingleton<INotificationRegistrationPort, MockNotificationRegistrationAdapter>();
        services.AddSingleton<INotificationTrackingPort, MockNotificationTrackingAdapter>();

        // Register mock IoT alert & webhook adapters
        // Covers: Alexa, Google Home, SmartThings, HomeKit, IFTTT, custom webhooks,
        //         Ring, Wyze, Tuya, Zigbee, Z-Wave, Matter
        services.AddSingleton<IIoTAlertPort, MockIoTAlertAdapter>();
        services.AddSingleton<IIoTWebhookPort, MockIoTWebhookAdapter>();

        // Register mock Watch Call adapters
        // Watch Calls: enrollment, mock training scenarios, live video calls, WebRTC signaling
        services.AddSingleton<IWatchCallPort, MockWatchCallAdapter>();

        // Scene narration: AI vision-powered neutral scene description (parrot-back)
        services.AddSingleton<ISceneNarrationPort, MockSceneNarrationAdapter>();

        // Swarm agent: interactive conversational agent for CLI swarm guidance
        services.AddSingleton<ISwarmAgentPort, MockSwarmAgentAdapter>();

        return services;
    }
}
