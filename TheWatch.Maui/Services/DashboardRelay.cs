using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TheWatch.Maui.ViewModels;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Maui.Services;

public class DashboardRelay : IDashboardRelay
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DashboardRelay> _logger;
    private HubConnection? _hubConnection;
    private string? _apiBaseUrl;
    private bool _isConnected = false;

    public event EventHandler<SimulationEventDto>? EventReceived;
    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<TestStepResultDto>? TestStepCompleted;
    public event EventHandler<TestRunDto>? TestRunCompleted;

    public DashboardRelay(ILogger<DashboardRelay> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<bool> ConnectAsync(string apiBaseUrl = "https://localhost:5001")
    {
        try
        {
            _apiBaseUrl = apiBaseUrl;
            _httpClient.BaseAddress = new Uri(apiBaseUrl);

            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{apiBaseUrl}/hubs/dashboard")
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10) })
                .Build();

            _hubConnection.On<SimulationEventDto>("ReceiveSimulationEvent", (eventDto) =>
            {
                EventReceived?.Invoke(this, eventDto);
            });

            _hubConnection.Reconnecting += (error) =>
            {
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, "Reconnecting...");
                _logger.LogWarning($"Hub connection lost: {error?.Message}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                ConnectionStatusChanged?.Invoke(this, "Connected");
                _logger.LogInformation("Hub connection restored");
                return Task.CompletedTask;
            };

            _hubConnection.Closed += (error) =>
            {
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, "Disconnected");
                _logger.LogError($"Hub connection closed: {error?.Message}");
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync();
            _isConnected = true;
            ConnectionStatusChanged?.Invoke(this, "Connected");
            _logger.LogInformation("Dashboard relay connected");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to dashboard relay");
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, "Failed to connect");
            return false;
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, "Disconnected");
            _logger.LogInformation("Dashboard relay disconnected");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from dashboard relay");
            return false;
        }
    }

    public Task<bool> IsConnectedAsync()
    {
        return Task.FromResult(_isConnected && _hubConnection?.State == HubConnectionState.Connected);
    }

    public async Task<bool> SendSimulationEventAsync(SimulationEventDto eventDto)
    {
        try
        {
            if (!_isConnected || _hubConnection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Hub connection not active, using HTTP fallback");
                var response = await _httpClient.PostAsJsonAsync("api/simulation/events", eventDto);
                return response.IsSuccessStatusCode;
            }

            await _hubConnection!.InvokeAsync("PublishSimulationEvent", eventDto);
            _logger.LogInformation($"Simulation event sent: {eventDto.EventType}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending simulation event");
            return false;
        }
    }

    public async IAsyncEnumerable<SimulationEventDto> SubscribeToEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<SimulationEventDto>? events = null;

        try
        {
            if (_apiBaseUrl == null)
            {
                yield break;
            }

            var response = await _httpClient.GetAsync("api/simulation/log?limit=100", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                yield break;
            }

            events = await response.Content.ReadFromJsonAsync<List<SimulationEventDto>>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to events");
        }

        if (events != null)
        {
            foreach (var eventDto in events.OrderByDescending(e => e.Timestamp))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                yield return eventDto;
            }
        }
    }

    public async Task<List<TestSuiteDto>> GetTestSuitesAsync()
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<TestSuiteDto>>("api/test/suites");
            return result ?? new List<TestSuiteDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching test suites");
            return new List<TestSuiteDto>();
        }
    }

    public async Task<TestRunDto?> StartTestRunAsync(string suiteId, string targetDevice)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/test/runs", new { SuiteId = suiteId, TargetDevice = targetDevice });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TestRunDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting test run");
            return null;
        }
    }

    public async Task CancelTestRunAsync(string runId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/test/runs/{runId}/cancel", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling test run");
            throw;
        }
    }

    public async Task<List<TestRunDto>> GetTestRunsAsync()
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<TestRunDto>>("api/test/runs");
            return result ?? new List<TestRunDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching test runs");
            return new List<TestRunDto>();
        }
    }

    public async Task<AdapterRegistryDto?> GetAdapterRegistryAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AdapterRegistryDto>("api/adapters/registry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching adapter registry");
            return null;
        }
    }

    public async Task<bool> SetAdapterTierAsync(string slotName, string tier)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/adapters/tier", new { SlotName = slotName, Tier = tier });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting adapter tier for {Slot}", slotName);
            return false;
        }
    }

    public async Task<bool> ResetAdapterTiersAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("api/adapters/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting adapter tiers");
            return false;
        }
    }
}
