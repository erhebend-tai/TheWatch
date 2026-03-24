// =============================================================================
// ServicePanel — Shows online/offline status of all infrastructure services.
// =============================================================================
// Renders services with health indicators:
//   ● SQL Server         HEALTHY    localhost,14330
//   ● PostgreSQL/PostGIS HEALTHY    localhost:5432
//   ● Redis              HEALTHY    localhost:6379
//   ● Cosmos DB Emulator HEALTHY    localhost:8081
//   ● RabbitMQ           HEALTHY    localhost:5672
//   ● Qdrant             HEALTHY    localhost:6333
//   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//   ● Dashboard API      ONLINE     https://localhost:5001
//   ● Dashboard Web      ONLINE     https://localhost:5002
//   ● DocGen Worker      ONLINE     (worker service)
//   ● Azure Functions    ONLINE     (isolated worker)
//   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//   ● Android App        CONNECTED  (last ping: 2s ago)
//   ○ iOS App            OFFLINE    (last ping: 5m ago)
//
// Color coding:
//   Green  = Healthy/Online
//   Yellow = Degraded/Reconnecting
//   Red    = Unhealthy/Offline
//   Gray   = NotConfigured/Unknown
//
// WAL: Services list is a superset of what the health API returns.
//      Static entries (mobile apps, cloud providers) show NotConfigured until
//      the first health check response arrives.
// =============================================================================

using Terminal.Gui;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Cli.Panels;

public class ServicePanel : FrameView
{
    private readonly ListView _listView;
    private readonly Label _summaryLabel;
    private List<ServiceDisplayItem> _services = new();
    private List<string> _displayLines = new();
    private bool _offlineMode;

    public ServicePanel()
    {
        Title = "Services & Infrastructure";
        BorderStyle = LineStyle.Single;

        _summaryLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Checking services..."
        };

        _listView = new ListView()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            CanFocus = true
        };

        // Seed with known infrastructure services (before API responds)
        _services = GetDefaultServices();
        RebuildDisplay();

        Add(_summaryLabel, _listView);
    }

    public void SetServices(List<InfrastructureServiceHealth> healthList)
    {
        _offlineMode = false;

        // Merge API health data with our known service list
        foreach (var health in healthList)
        {
            var existing = _services.FirstOrDefault(s => s.ServiceId == health.ServiceId);
            if (existing != null)
            {
                existing.State = health.State;
                existing.StatusMessage = health.StatusMessage;
                existing.LastChecked = health.LastChecked;
                existing.Provider = health.Provider;
            }
            else
            {
                _services.Add(new ServiceDisplayItem
                {
                    ServiceId = health.ServiceId,
                    ServiceName = health.ServiceName,
                    Category = health.Category,
                    State = health.State,
                    StatusMessage = health.StatusMessage,
                    Provider = health.Provider,
                    LastChecked = health.LastChecked,
                    Endpoint = health.Metadata?.GetValueOrDefault("endpoint") ?? ""
                });
            }
        }

        RebuildDisplay();
    }

    public void UpdateService(InfrastructureServiceHealth health)
    {
        var existing = _services.FirstOrDefault(s => s.ServiceId == health.ServiceId);
        if (existing != null)
        {
            existing.State = health.State;
            existing.StatusMessage = health.StatusMessage;
            existing.LastChecked = health.LastChecked;
        }
        RebuildDisplay();
    }

    public void SetOfflineMode()
    {
        _offlineMode = true;
        _summaryLabel.Text = " !! API UNREACHABLE — showing last known state";
        _summaryLabel.ColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.Red, Color.Black)
        };
    }

    private void RebuildDisplay()
    {
        var healthy = _services.Count(s => s.State == HealthState.Healthy);
        var degraded = _services.Count(s => s.State == HealthState.Degraded);
        var unhealthy = _services.Count(s => s.State == HealthState.Unhealthy);
        var unknown = _services.Count(s => s.State is HealthState.Unknown or HealthState.NotConfigured);

        if (!_offlineMode)
        {
            _summaryLabel.Text = $" {healthy} healthy | {degraded} degraded | {unhealthy} down | {unknown} unknown";
            _summaryLabel.ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(
                    unhealthy > 0 ? Color.Red : (degraded > 0 ? Color.BrightYellow : Color.Green),
                    Color.Black)
            };
        }

        _displayLines = new List<string>();

        // Group by category
        var groups = _services
            .GroupBy(s => s.Category)
            .OrderBy(g => CategoryOrder(g.Key));

        foreach (var group in groups)
        {
            _displayLines.Add($" ─── {group.Key} ───");
            foreach (var svc in group.OrderBy(s => s.ServiceName))
            {
                _displayLines.Add(FormatServiceLine(svc));
            }
        }

        _listView.SetSource<string>(new System.Collections.ObjectModel.ObservableCollection<string>(_displayLines));
    }

    private static string FormatServiceLine(ServiceDisplayItem svc)
    {
        var icon = svc.State switch
        {
            HealthState.Healthy => "●",
            HealthState.Degraded => "◐",
            HealthState.Unhealthy => "○",
            HealthState.Unknown => "?",
            HealthState.NotConfigured => "—",
            _ => "?"
        };

        var state = svc.State.ToString().ToUpper().PadRight(14);
        var name = svc.ServiceName.Length > 22 ? svc.ServiceName[..19] + "..." : svc.ServiceName.PadRight(22);
        var endpoint = svc.Endpoint ?? "";
        var ago = svc.LastChecked.HasValue
            ? $"({(int)(DateTime.UtcNow - svc.LastChecked.Value).TotalSeconds}s ago)"
            : "";

        return $" {icon} {name} {state} {endpoint} {ago}";
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Database" => 0,
        "Messaging" => 1,
        "Cache" => 2,
        "Compute" => 3,
        "Application" => 4,
        "Mobile" => 5,
        "IoT" => 6,
        "CDN" => 7,
        "Cloud" => 8,
        _ => 99
    };

    private static List<ServiceDisplayItem> GetDefaultServices() => new()
    {
        // Infrastructure (from AppHost)
        new() { ServiceId = "thewatch-sqlserver", ServiceName = "SQL Server", Category = "Database", Endpoint = "localhost,14330", State = HealthState.Unknown },
        new() { ServiceId = "thewatch-postgresql", ServiceName = "PostgreSQL/PostGIS", Category = "Database", Endpoint = "localhost:5432", State = HealthState.Unknown },
        new() { ServiceId = "thewatch-redis", ServiceName = "Redis", Category = "Cache", Endpoint = "localhost:6379", State = HealthState.Unknown },
        new() { ServiceId = "thewatch-cosmos", ServiceName = "Cosmos DB Emulator", Category = "Database", Endpoint = "localhost:8081", State = HealthState.Unknown },
        new() { ServiceId = "thewatch-rabbitmq", ServiceName = "RabbitMQ", Category = "Messaging", Endpoint = "localhost:5672", State = HealthState.Unknown },
        new() { ServiceId = "thewatch-qdrant", ServiceName = "Qdrant Vector DB", Category = "Database", Endpoint = "localhost:6333", State = HealthState.Unknown },

        // Application services
        new() { ServiceId = "dashboard-api", ServiceName = "Dashboard API", Category = "Application", Endpoint = "https://localhost:5001", State = HealthState.Unknown },
        new() { ServiceId = "dashboard-web", ServiceName = "Dashboard Web (Blazor)", Category = "Application", Endpoint = "https://localhost:5002", State = HealthState.Unknown },
        new() { ServiceId = "docgen-worker", ServiceName = "DocGen Worker", Category = "Application", Endpoint = "(worker service)", State = HealthState.Unknown },
        new() { ServiceId = "response-functions", ServiceName = "Azure Functions", Category = "Application", Endpoint = "(isolated worker)", State = HealthState.Unknown },

        // Mobile clients (show connection status from SignalR tracking)
        new() { ServiceId = "mobile-android", ServiceName = "Android App", Category = "Mobile", Endpoint = "(Jetpack Compose)", State = HealthState.NotConfigured },
        new() { ServiceId = "mobile-ios", ServiceName = "iOS App", Category = "Mobile", Endpoint = "(SwiftUI)", State = HealthState.NotConfigured },

        // IoT / Voice Assistants
        new() { ServiceId = "iot-alexa", ServiceName = "Alexa Skill", Category = "IoT", Endpoint = "(Lambda)", State = HealthState.NotConfigured },
        new() { ServiceId = "iot-google-home", ServiceName = "Google Home Action", Category = "IoT", Endpoint = "(Cloud Function)", State = HealthState.NotConfigured },
        new() { ServiceId = "iot-smartthings", ServiceName = "SmartThings", Category = "IoT", Endpoint = "(SmartApp)", State = HealthState.NotConfigured },
        new() { ServiceId = "iot-ifttt", ServiceName = "IFTTT", Category = "IoT", Endpoint = "(webhook)", State = HealthState.NotConfigured },

        // Cloud providers
        new() { ServiceId = "cloud-azure", ServiceName = "Azure", Category = "Cloud", Provider = "Azure", State = HealthState.NotConfigured },
        new() { ServiceId = "cloud-aws", ServiceName = "AWS", Category = "Cloud", Provider = "AWS", State = HealthState.NotConfigured },
        new() { ServiceId = "cloud-google", ServiceName = "Google Cloud", Category = "Cloud", Provider = "Google", State = HealthState.NotConfigured },
        new() { ServiceId = "cloud-oracle", ServiceName = "Oracle", Category = "Cloud", Provider = "Oracle", State = HealthState.NotConfigured },
        new() { ServiceId = "cloud-cloudflare", ServiceName = "Cloudflare", Category = "Cloud", Provider = "Cloudflare", State = HealthState.NotConfigured },
    };
}

internal class ServiceDisplayItem
{
    public string ServiceId { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Category { get; set; } = "";
    public HealthState State { get; set; } = HealthState.Unknown;
    public string? StatusMessage { get; set; }
    public string? Provider { get; set; }
    public string? Endpoint { get; set; }
    public DateTime? LastChecked { get; set; }
}
