// =============================================================================
// DashboardApiClient — HTTP + SignalR client for the Dashboard API.
// =============================================================================
// Connects to the TheWatch.Dashboard.Api for:
//   1. REST polling: GET /api/features, GET /api/agents, GET /api/health
//   2. SignalR real-time: /hubs/dashboard for live updates
//
// Events exposed to panels:
//   OnFeatureUpdated     — FeatureImplementation changed
//   OnAgentActivity      — Agent started/stopped/progressed
//   OnServiceHealthChanged — Infrastructure service health changed
//
// Example:
//   var client = new DashboardApiClient(config);
//   await client.ConnectSignalRAsync(ct);
//   client.OnFeatureUpdated += f => Console.WriteLine(f.Name);
//   var features = await client.GetFeaturesAsync(ct);
//
// WAL: HttpClient and HubConnection are long-lived — created once, reused.
//      Reconnection logic is built into HubConnectionBuilder.WithAutomaticReconnect().
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using TheWatch.Cli.App;
using TheWatch.Cli.Panels;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Cli.Services;

public class DashboardApiClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private HubConnection? _hub;
    private readonly DashboardConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;

    // Events for panel updates
    public event Action<FeatureImplementation>? OnFeatureUpdated;
    public event Action<AgentActivity>? OnAgentActivity;
    public event Action<InfrastructureServiceHealth>? OnServiceHealthChanged;

    // IoT events (from IoTAlertController SignalR broadcasts)
    public event Action<IoTAlertEvent>? OnIoTAlertReceived;
    public event Action<IoTCancelledEvent>? OnIoTAlertCancelled;
    public event Action<IoTCheckInEvent>? OnIoTCheckInEscalation;
    public event Action<IoTWebhookEvent>? OnIoTWebhookAlertReceived;

    public DashboardApiClient(DashboardConfig config)
    {
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var handler = new HttpClientHandler
        {
            // Accept self-signed certs in development
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // ── REST Polling ────────────────────────────────────────────────

    public async Task<List<FeatureImplementation>> GetFeaturesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/features", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<FeatureImplementation>>(_jsonOptions, ct)
                    ?? new List<FeatureImplementation>();
            }
        }
        catch (HttpRequestException) { /* API offline */ }
        catch (TaskCanceledException) { /* timeout or cancellation */ }

        return GetFallbackFeatures();
    }

    public async Task<List<AgentActivity>> GetAgentsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/agents", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<AgentActivity>>(_jsonOptions, ct)
                    ?? new List<AgentActivity>();
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        return new List<AgentActivity>();
    }

    public async Task<List<InfrastructureServiceHealth>> GetServicesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/health/infrastructure", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<InfrastructureServiceHealth>>(_jsonOptions, ct)
                    ?? new List<InfrastructureServiceHealth>();
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        return new List<InfrastructureServiceHealth>();
    }

    // ── IoT REST Polling ──────────────────────────────────────────

    /// <summary>
    /// Get IoT device status for a user (active alerts, devices, check-ins).
    /// Calls GET /api/iot/status/{userId}
    /// </summary>
    public async Task<IoTDeviceStatus?> GetIoTStatusAsync(string userId = "mock-user-001", CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/api/iot/status/{userId}", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<IoTDeviceStatus>(_jsonOptions, ct);
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        return null;
    }

    /// <summary>
    /// Get registered IoT devices for a user.
    /// Calls GET /api/iot/devices/{userId}
    /// </summary>
    public async Task<IReadOnlyList<IoTDeviceRegistration>> GetIoTDevicesAsync(string userId = "mock-user-001", CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/api/iot/devices/{userId}", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<IoTDeviceRegistration>>(_jsonOptions, ct)
                    ?? new List<IoTDeviceRegistration>();
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        return new List<IoTDeviceRegistration>();
    }

    /// <summary>
    /// Trigger a test IoT alert (for diagnostics from the CLI dashboard).
    /// Calls POST /api/iot/alert with source=CustomWebhook.
    /// </summary>
    public async Task<IoTAlertResult?> TriggerTestIoTAlertAsync(
        string source = "Alexa",
        string triggerMethod = "VOICE_COMMAND",
        CancellationToken ct = default)
    {
        try
        {
            var request = new
            {
                Source = source,
                ExternalUserId = "test-cli-user",
                TriggerMethod = triggerMethod,
                DeviceType = "CLI_DASHBOARD_TEST",
                Scope = "CheckIn",
                Timestamp = DateTime.UtcNow,
                PlatformRequestId = $"cli-test-{Guid.NewGuid():N}"
            };

            var response = await _http.PostAsJsonAsync("/api/iot/alert", request, _jsonOptions, ct);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 422)
            {
                return await response.Content.ReadFromJsonAsync<IoTAlertResult>(_jsonOptions, ct);
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        return null;
    }

    // ── SignalR Real-Time ───────────────────────────────────────────

    public async Task ConnectSignalRAsync(CancellationToken ct = default)
    {
        try
        {
            _hub = new HubConnectionBuilder()
                .WithUrl($"{_config.ApiBaseUrl}/hubs/dashboard", options =>
                {
                    // Accept self-signed certs in dev
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    };
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            // Subscribe to hub events that match DashboardHub broadcast methods
            _hub.On<FeatureImplementation>("FeatureUpdated", feature =>
                OnFeatureUpdated?.Invoke(feature));

            _hub.On<AgentActivity>("AgentActivityRecorded", activity =>
                OnAgentActivity?.Invoke(activity));

            _hub.On<BuildStatus>("BuildCompleted", build =>
            {
                // Build completions affect feature status — treat as a feature refresh trigger
                // The panel will handle deduplication
            });

            _hub.On<InfrastructureServiceHealth>("ServiceHealthChanged", health =>
                OnServiceHealthChanged?.Invoke(health));

            // IoT alert events (broadcast by IoTAlertController)
            _hub.On<IoTAlertEvent>("IoTAlertReceived", alert =>
                OnIoTAlertReceived?.Invoke(alert));

            _hub.On<IoTCancelledEvent>("IoTAlertCancelled", cancelled =>
                OnIoTAlertCancelled?.Invoke(cancelled));

            _hub.On<IoTCheckInEvent>("IoTCheckInEscalation", checkIn =>
                OnIoTCheckInEscalation?.Invoke(checkIn));

            _hub.On<IoTWebhookEvent>("IoTWebhookAlertReceived", webhook =>
                OnIoTWebhookAlertReceived?.Invoke(webhook));

            _hub.Reconnecting += error =>
            {
                // Could update a status indicator
                return Task.CompletedTask;
            };

            _hub.Reconnected += connectionId =>
            {
                return Task.CompletedTask;
            };

            await _hub.StartAsync(ct);
        }
        catch (Exception)
        {
            // SignalR connection failed — fall back to polling only
            _hub = null;
        }
    }

    // ── Fallback Data ───────────────────────────────────────────────
    // When the API is unreachable, show a representative feature list
    // so the dashboard isn't blank on first launch.

    private static List<FeatureImplementation> GetFallbackFeatures() => new()
    {
        // Authentication & Onboarding
        Feat("Login Page", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Signup Page", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Forgot Password", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Reset Password", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("2FA Verification", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("EULA Page", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.InProgress, 60),
        Feat("Legal Page", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.InProgress, 40),

        // Core Features
        Feat("Profile / Volunteering", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Emergency Phrase Detection", Shared.Enums.FeatureCategory.ResponseCoordination, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Volunteer Dispatch (H3/Geohash)", Shared.Enums.FeatureCategory.SpatialIndexing, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Evidence Submission", Shared.Enums.FeatureCategory.EvidenceSystem, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("SignalR Real-Time Hub", Shared.Enums.FeatureCategory.CoreInfrastructure, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Offline Queue / Sync", Shared.Enums.FeatureCategory.OfflineSupport, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Push Notifications", Shared.Enums.FeatureCategory.Notifications, Shared.Enums.FeatureStatus.InProgress, 70),
        Feat("First Responder Alerts", Shared.Enums.FeatureCategory.ResponseCoordination, Shared.Enums.FeatureStatus.InProgress, 50),
        Feat("Data Export", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.InProgress, 30),
        Feat("Account Deletion (GDPR)", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.InProgress, 40),
        Feat("Diagnostics Screen", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Planned, 0),

        // Infrastructure
        Feat("Aspire AppHost Orchestration", Shared.Enums.FeatureCategory.CoreInfrastructure, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Port/Adapter Registry", Shared.Enums.FeatureCategory.CoreInfrastructure, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("7-Adapter Plugin System", Shared.Enums.FeatureCategory.CoreInfrastructure, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Blazor Dashboard Web", Shared.Enums.FeatureCategory.DashboardWeb, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Dashboard REST API", Shared.Enums.FeatureCategory.ApiEndpoints, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("DocGen Worker", Shared.Enums.FeatureCategory.WorkerServices, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Azure Functions (Response)", Shared.Enums.FeatureCategory.AzureFunctions, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("CLI Command Center", Shared.Enums.FeatureCategory.ClaudeCodeIntegration, Shared.Enums.FeatureStatus.InProgress, 80),

        // IoT / Voice Assistants
        Feat("Alexa Skills Integration", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("Google Home Integration", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("IoT Alert Ingestion API", Shared.Enums.FeatureCategory.ApiEndpoints, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("IoT Account Linking (OAuth2)", Shared.Enums.FeatureCategory.Authentication, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("IoT Webhook Processing", Shared.Enums.FeatureCategory.ApiEndpoints, Shared.Enums.FeatureStatus.Completed, 100),
        Feat("SmartThings Integration", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.InProgress, 30),
        Feat("Matter Device Support", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Planned, 0),
        Feat("IFTTT Applet Service", Shared.Enums.FeatureCategory.MobileApp, Shared.Enums.FeatureStatus.Planned, 0),

        // Standards & Analytics
        Feat("Standards Inferencing System", Shared.Enums.FeatureCategory.Standards, Shared.Enums.FeatureStatus.Planned, 0),
        Feat("Keyword Cluster Caching", Shared.Enums.FeatureCategory.Analytics, Shared.Enums.FeatureStatus.Planned, 0),
        Feat("Valuation Engine", Shared.Enums.FeatureCategory.Analytics, Shared.Enums.FeatureStatus.Planned, 0),
    };

    private static FeatureImplementation Feat(string name, Shared.Enums.FeatureCategory cat,
        Shared.Enums.FeatureStatus status, int pct) => new()
    {
        Name = name,
        Category = cat,
        Status = status,
        ProgressPercent = pct,
        LastUpdatedAt = DateTime.UtcNow
    };

    // ── Cleanup ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
        _http.Dispose();
    }
}
