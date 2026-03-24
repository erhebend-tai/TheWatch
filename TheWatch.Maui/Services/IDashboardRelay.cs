using TheWatch.Maui.ViewModels;
using TheWatch.Shared.Dtos;

namespace TheWatch.Maui.Services;

/// <summary>
/// DTO for the adapter registry response from the Dashboard API.
/// Each dictionary maps slot name to tier name (e.g., "AzureBlobStorage" -> "Mock").
///
/// Example JSON response:
/// {
///   "cloudProviders": { "AzureBlobStorage": "Mock", "S3": "Live" },
///   "dataLayer": { "CosmosDb": "Native" },
///   "features": { "SpeechToText": "Mock" },
///   "ai": { "OpenAI": "Live" }
/// }
/// </summary>
public class AdapterRegistryDto
{
    public Dictionary<string, string>? CloudProviders { get; set; }
    public Dictionary<string, string>? DataLayer { get; set; }
    public Dictionary<string, string>? Features { get; set; }
    public Dictionary<string, string>? AI { get; set; }
}

public interface IDashboardRelay
{
    Task<bool> ConnectAsync(string apiBaseUrl = "https://localhost:5001");
    Task<bool> DisconnectAsync();
    Task<bool> IsConnectedAsync();
    Task<bool> SendSimulationEventAsync(SimulationEventDto eventDto);
    IAsyncEnumerable<SimulationEventDto> SubscribeToEventsAsync(CancellationToken cancellationToken = default);

    // Test orchestration methods
    Task<List<TestSuiteDto>> GetTestSuitesAsync();
    Task<TestRunDto?> StartTestRunAsync(string suiteId, string targetDevice);
    Task CancelTestRunAsync(string runId);
    Task<List<TestRunDto>> GetTestRunsAsync();

    // Adapter tier management methods
    Task<AdapterRegistryDto?> GetAdapterRegistryAsync();
    Task<bool> SetAdapterTierAsync(string slotName, string tier);
    Task<bool> ResetAdapterTiersAsync();

    event EventHandler<SimulationEventDto>? EventReceived;
    event EventHandler<string>? ConnectionStatusChanged;

    // Test orchestration events
    event EventHandler<TestStepResultDto>? TestStepCompleted;
    event EventHandler<TestRunDto>? TestRunCompleted;
}
