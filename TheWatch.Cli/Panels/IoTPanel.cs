// =============================================================================
// IoTPanel — Shows connected IoT devices, live alert feed, and check-in status.
// =============================================================================
// Renders a real-time view of all IoT voice assistant and smart home integrations:
//
//   ─── CONNECTED DEVICES (6) ───
//   ● Alexa  Echo Dot (Kitchen)        VOICE_COMMAND     Online   85%
//   ● Alexa  Echo Show (Bedroom)       VOICE_COMMAND     Online   —
//   ● Google Nest Hub (Living Room)    VOICE_COMMAND     Online   —
//   ● SmartThings  Panic Button        PANIC_BUTTON      Online   72%
//   ○ Ring   Alarm Keypad              PANIC_BUTTON      Offline  —
//   ● Zigbee Smoke Detector            SMOKE_DETECTOR    Online   48%
//   ─── RECENT ALERTS ───
//   ! 12:05  Alexa  VOICE_COMMAND  → Dispatched (5 responders)
//   ✓ 11:30  Google VOICE_COMMAND  → Cancelled (user safe)
//   ─── CHECK-INS ───
//   ✓ 10:00  Alexa  "I'm okay"    → Recorded
//   ! 09:00  Missed (auto)         → Escalation triggered
//   ─── LINKED ACCOUNTS ───
//   ✓ Amazon Alexa    (amzn1.ask.account...XXXX)   linked 2026-03-15
//   ✓ Google Home     (google-uid-XXXX)             linked 2026-03-18
//   ○ SmartThings     not linked
//
// Updates via:
//   1. REST polling: GET /api/iot/status/{userId}, GET /api/iot/devices/{userId}
//   2. SignalR events: IoTAlertReceived, IoTAlertCancelled, IoTCheckInEscalation,
//      IoTWebhookAlertReceived
//
// Example:
//   var panel = new IoTPanel();
//   panel.SetDeviceStatus(status);
//   panel.AddAlert(alertEvent);
//   panel.AddCheckIn(checkInEvent);
//
// WAL: The panel auto-scrolls to the most recent alert when a new one arrives.
//      Device list is sorted: online first, then by source, then by name.
//      Alert feed is a ring buffer — oldest entries drop off after 100 entries.
// =============================================================================

using Terminal.Gui;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Cli.Panels;

public class IoTPanel : FrameView
{
    private readonly ListView _listView;
    private readonly Label _summaryLabel;

    // State
    private readonly List<IoTDeviceDisplayItem> _devices = new();
    private readonly LinkedList<IoTAlertDisplayItem> _alerts = new();
    private readonly LinkedList<IoTCheckInDisplayItem> _checkIns = new();
    private readonly List<IoTMappingDisplayItem> _mappings = new();
    private List<string> _displayLines = new();

    private const int MaxAlertHistory = 100;
    private const int MaxCheckInHistory = 50;

    // Current view mode (cycled with Tab)
    private IoTPanelView _currentView = IoTPanelView.All;

    public IoTPanel()
    {
        Title = "IoT / Voice Assistants";
        BorderStyle = LineStyle.Single;

        _summaryLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Discovering IoT devices..."
        };

        var filterLabel = new Label()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            Text = "[Tab] cycle view | [Enter] details | [T] test alert",
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black)
            }
        };

        _listView = new ListView()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            CanFocus = true
        };

        _listView.KeyDown += HandleKeyPress;

        Add(_summaryLabel, filterLabel, _listView);
    }

    // ── Data Loading ────────────────────────────────────────────────

    /// <summary>
    /// Bulk load device status from GET /api/iot/status/{userId}.
    /// </summary>
    public void SetDeviceStatus(IoTDeviceStatus status)
    {
        _devices.Clear();
        foreach (var dev in status.RegisteredDevices)
        {
            _devices.Add(new IoTDeviceDisplayItem
            {
                DeviceId = dev.DeviceId,
                Source = dev.Source,
                DeviceName = dev.DeviceName,
                Capabilities = dev.Capabilities,
                IsOnline = dev.IsOnline,
                BatteryLevel = dev.BatteryLevel,
                InstallationZone = dev.InstallationZone,
                LastSeenAt = dev.LastSeenAt
            });
        }

        // Inject active alerts as display items
        foreach (var alert in status.ActiveAlertDetails)
        {
            AddAlertInternal(new IoTAlertDisplayItem
            {
                AlertId = alert.AlertId,
                Source = alert.Source,
                TriggerMethod = alert.TriggerMethod,
                Status = alert.Status.ToString(),
                RespondersNotified = 0,
                Timestamp = alert.TriggeredAt,
                IsActive = alert.Status == IoTAlertStatus.Dispatched
            });
        }

        RebuildDisplay();
    }

    /// <summary>
    /// Set devices from GET /api/iot/devices/{userId}.
    /// </summary>
    public void SetDevices(IReadOnlyList<IoTDeviceRegistration> devices)
    {
        _devices.Clear();
        foreach (var dev in devices)
        {
            _devices.Add(new IoTDeviceDisplayItem
            {
                DeviceId = dev.DeviceId,
                Source = dev.Source,
                DeviceName = dev.DeviceName,
                Capabilities = dev.Capabilities,
                IsOnline = dev.IsOnline,
                BatteryLevel = dev.BatteryLevel,
                InstallationZone = dev.InstallationZone,
                LastSeenAt = dev.LastSeenAt
            });
        }
        RebuildDisplay();
    }

    /// <summary>
    /// Set linked account mappings.
    /// </summary>
    public void SetMappings(IReadOnlyList<IoTUserMapping> mappings)
    {
        _mappings.Clear();
        foreach (var m in mappings)
        {
            _mappings.Add(new IoTMappingDisplayItem
            {
                Source = m.Source,
                ExternalUserId = m.ExternalUserId,
                LinkedAt = m.LinkedAt,
                LastUsedAt = m.LastUsedAt
            });
        }
        RebuildDisplay();
    }

    // ── Real-Time Events (from SignalR) ──────────────────────────────

    /// <summary>
    /// Called when SignalR receives "IoTAlertReceived" event.
    /// </summary>
    public void AddAlert(IoTAlertEvent alertEvent)
    {
        AddAlertInternal(new IoTAlertDisplayItem
        {
            AlertId = alertEvent.AlertId,
            Source = Enum.TryParse<IoTSource>(alertEvent.Source, true, out var s) ? s : IoTSource.CustomWebhook,
            TriggerMethod = alertEvent.TriggerMethod,
            Status = alertEvent.Status,
            RespondersNotified = alertEvent.RespondersNotified,
            Timestamp = alertEvent.Timestamp,
            IsActive = alertEvent.Status == "Dispatched"
        });
        RebuildDisplay();
    }

    /// <summary>
    /// Called when SignalR receives "IoTAlertCancelled" event.
    /// </summary>
    public void CancelAlert(string alertId)
    {
        var alert = _alerts.FirstOrDefault(a => a.AlertId == alertId);
        if (alert != null)
        {
            alert.Status = "Cancelled";
            alert.IsActive = false;
        }
        RebuildDisplay();
    }

    /// <summary>
    /// Called when SignalR receives "IoTCheckInEscalation" event.
    /// </summary>
    public void AddCheckIn(IoTCheckInEvent checkInEvent)
    {
        _checkIns.AddFirst(new IoTCheckInDisplayItem
        {
            CheckInId = checkInEvent.CheckInId,
            Source = Enum.TryParse<IoTSource>(checkInEvent.Source, true, out var s) ? s : IoTSource.CustomWebhook,
            Status = checkInEvent.CheckInStatus,
            Message = checkInEvent.Message,
            Timestamp = checkInEvent.Timestamp,
            IsEscalation = checkInEvent.CheckInStatus is "Missed" or "NeedHelp"
        });

        while (_checkIns.Count > MaxCheckInHistory)
            _checkIns.RemoveLast();

        RebuildDisplay();
    }

    /// <summary>
    /// Called when SignalR receives "IoTWebhookAlertReceived" event.
    /// </summary>
    public void AddWebhookAlert(IoTWebhookEvent webhookEvent)
    {
        AddAlertInternal(new IoTAlertDisplayItem
        {
            AlertId = webhookEvent.AlertId,
            Source = Enum.TryParse<IoTSource>(webhookEvent.Source, true, out var s) ? s : IoTSource.CustomWebhook,
            TriggerMethod = "WEBHOOK",
            Status = "Dispatched",
            RespondersNotified = 0,
            Timestamp = webhookEvent.Timestamp,
            IsActive = true
        });
        RebuildDisplay();
    }

    // ── Internal Helpers ─────────────────────────────────────────────

    private void AddAlertInternal(IoTAlertDisplayItem alert)
    {
        _alerts.AddFirst(alert);
        while (_alerts.Count > MaxAlertHistory)
            _alerts.RemoveLast();
    }

    private void RebuildDisplay()
    {
        var onlineCount = _devices.Count(d => d.IsOnline);
        var activeAlerts = _alerts.Count(a => a.IsActive);
        var recentCheckIns = _checkIns.Count(c =>
            (DateTime.UtcNow - c.Timestamp).TotalHours < 24);

        _summaryLabel.Text = $" {onlineCount}/{_devices.Count} online | {activeAlerts} active alerts | {recentCheckIns} check-ins (24h)";
        _summaryLabel.ColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(
                activeAlerts > 0 ? Color.Red : (onlineCount > 0 ? Color.Green : Color.Gray),
                Color.Black)
        };

        _displayLines = _currentView switch
        {
            IoTPanelView.Devices => BuildDeviceLines(),
            IoTPanelView.Alerts => BuildAlertLines(),
            IoTPanelView.CheckIns => BuildCheckInLines(),
            IoTPanelView.Mappings => BuildMappingLines(),
            _ => BuildAllLines()
        };

        _listView.SetSource<string>(new System.Collections.ObjectModel.ObservableCollection<string>(_displayLines));
    }

    private List<string> BuildAllLines()
    {
        var lines = new List<string>();

        // Devices section (compact — top 6)
        lines.Add($" --- DEVICES ({_devices.Count}) ---");
        foreach (var dev in _devices.OrderByDescending(d => d.IsOnline).ThenBy(d => d.Source).Take(6))
            lines.Add(FormatDeviceLine(dev));
        if (_devices.Count > 6)
            lines.Add($"   ... +{_devices.Count - 6} more (Tab to view all)");

        // Alerts section (recent 5)
        lines.Add($" --- ALERTS ({_alerts.Count}) ---");
        if (_alerts.Count == 0)
            lines.Add("   No recent alerts");
        else
            foreach (var alert in _alerts.Take(5))
                lines.Add(FormatAlertLine(alert));

        // Check-ins section (recent 3)
        lines.Add($" --- CHECK-INS ({_checkIns.Count}) ---");
        if (_checkIns.Count == 0)
            lines.Add("   No recent check-ins");
        else
            foreach (var ci in _checkIns.Take(3))
                lines.Add(FormatCheckInLine(ci));

        // Linked accounts
        if (_mappings.Count > 0)
        {
            lines.Add($" --- LINKED ACCOUNTS ({_mappings.Count}) ---");
            foreach (var m in _mappings)
                lines.Add(FormatMappingLine(m));
        }

        return lines;
    }

    private List<string> BuildDeviceLines()
    {
        var lines = new List<string> { $" --- ALL DEVICES ({_devices.Count}) ---" };
        foreach (var dev in _devices.OrderByDescending(d => d.IsOnline).ThenBy(d => d.Source).ThenBy(d => d.DeviceName))
            lines.Add(FormatDeviceLine(dev));
        if (_devices.Count == 0)
            lines.Add("   No IoT devices registered");
        return lines;
    }

    private List<string> BuildAlertLines()
    {
        var lines = new List<string> { $" --- ALL ALERTS ({_alerts.Count}) ---" };
        foreach (var alert in _alerts)
            lines.Add(FormatAlertLine(alert));
        if (_alerts.Count == 0)
            lines.Add("   No alert history");
        return lines;
    }

    private List<string> BuildCheckInLines()
    {
        var lines = new List<string> { $" --- ALL CHECK-INS ({_checkIns.Count}) ---" };
        foreach (var ci in _checkIns)
            lines.Add(FormatCheckInLine(ci));
        if (_checkIns.Count == 0)
            lines.Add("   No check-in history");
        return lines;
    }

    private List<string> BuildMappingLines()
    {
        var lines = new List<string> { $" --- LINKED ACCOUNTS ({_mappings.Count}) ---" };
        // Show all known platforms, linked or not
        var allSources = new[] { IoTSource.Alexa, IoTSource.GoogleHome, IoTSource.SmartThings,
                                 IoTSource.HomeKit, IoTSource.IFTTT, IoTSource.Ring,
                                 IoTSource.Wyze, IoTSource.Matter };
        foreach (var source in allSources)
        {
            var mapping = _mappings.FirstOrDefault(m => m.Source == source);
            if (mapping != null)
                lines.Add(FormatMappingLine(mapping));
            else
                lines.Add($" ○ {source,-15} not linked");
        }
        return lines;
    }

    // ── Line Formatters ─────────────────────────────────────────────

    private static string FormatDeviceLine(IoTDeviceDisplayItem dev)
    {
        var icon = dev.IsOnline ? "●" : "○";
        var src = dev.Source.ToString();
        src = src.Length > 12 ? src[..12] : src.PadRight(12);
        var name = dev.DeviceName;
        if (!string.IsNullOrEmpty(dev.InstallationZone))
            name += $" ({dev.InstallationZone})";
        name = name.Length > 25 ? name[..22] + "..." : name.PadRight(25);
        var cap = dev.Capabilities.Count > 0 ? dev.Capabilities[0] : "—";
        cap = cap.Length > 16 ? cap[..16] : cap.PadRight(16);
        var online = dev.IsOnline ? "Online " : "Offline";
        var battery = dev.BatteryLevel.HasValue ? $"{dev.BatteryLevel}%" : "—";

        return $" {icon} {src} {name} {cap} {online} {battery}";
    }

    private static string FormatAlertLine(IoTAlertDisplayItem alert)
    {
        var icon = alert.IsActive ? "!" : (alert.Status == "Cancelled" ? "✓" : "●");
        var time = alert.Timestamp.ToLocalTime().ToString("HH:mm");
        var src = alert.Source.ToString();
        src = src.Length > 8 ? src[..8] : src.PadRight(8);
        var trigger = alert.TriggerMethod;
        trigger = trigger.Length > 16 ? trigger[..16] : trigger.PadRight(16);
        var status = alert.Status;
        var responders = alert.RespondersNotified > 0 ? $"({alert.RespondersNotified} responders)" : "";

        return $" {icon} {time}  {src} {trigger} -> {status} {responders}";
    }

    private static string FormatCheckInLine(IoTCheckInDisplayItem ci)
    {
        var icon = ci.IsEscalation ? "!" : "✓";
        var time = ci.Timestamp.ToLocalTime().ToString("HH:mm");
        var src = ci.Source.ToString();
        src = src.Length > 8 ? src[..8] : src.PadRight(8);
        var status = ci.Status;
        var msg = ci.Message ?? "";
        msg = msg.Length > 30 ? msg[..27] + "..." : msg;

        return $" {icon} {time}  {src} {status,-12} {msg}";
    }

    private static string FormatMappingLine(IoTMappingDisplayItem m)
    {
        var icon = "✓";
        var src = m.Source.ToString();
        src = src.Length > 15 ? src[..15] : src.PadRight(15);
        var extId = m.ExternalUserId;
        extId = extId.Length > 25 ? extId[..10] + "..." + extId[^8..] : extId;
        var linked = m.LinkedAt != default ? $"linked {m.LinkedAt:yyyy-MM-dd}" : "";

        return $" {icon} {src} ({extId})  {linked}";
    }

    // ── Key Handling ─────────────────────────────────────────────────

    private void HandleKeyPress(object? sender, Key e)
    {
        if (e == Key.Tab)
        {
            _currentView = _currentView switch
            {
                IoTPanelView.All => IoTPanelView.Devices,
                IoTPanelView.Devices => IoTPanelView.Alerts,
                IoTPanelView.Alerts => IoTPanelView.CheckIns,
                IoTPanelView.CheckIns => IoTPanelView.Mappings,
                IoTPanelView.Mappings => IoTPanelView.All,
                _ => IoTPanelView.All
            };
            RebuildDisplay();
            e.Handled = true;
        }
        else if (e == Key.Enter)
        {
            ShowDetails();
            e.Handled = true;
        }
    }

    private void ShowDetails()
    {
        // Show details based on current view and selected item
        if (_currentView == IoTPanelView.Devices || _currentView == IoTPanelView.All)
        {
            // Find device index — offset by section headers
            var idx = _listView.SelectedItem;
            if (idx > 0 && idx <= _devices.Count)
            {
                var dev = _devices.OrderByDescending(d => d.IsOnline).ThenBy(d => d.Source).ElementAtOrDefault(idx - 1);
                if (dev != null)
                {
                    MessageBox.Query("IoT Device Details", $@"
Device:    {dev.DeviceName}
ID:        {dev.DeviceId}
Source:    {dev.Source}
Zone:      {dev.InstallationZone ?? "N/A"}
Status:    {(dev.IsOnline ? "Online" : "Offline")}
Battery:   {(dev.BatteryLevel.HasValue ? $"{dev.BatteryLevel}%" : "N/A (mains powered)")}
Caps:      {string.Join(", ", dev.Capabilities)}
Last Seen: {dev.LastSeenAt:yyyy-MM-dd HH:mm:ss}
", "OK");
                }
            }
        }
    }
}

// ── View Mode ────────────────────────────────────────────────────

internal enum IoTPanelView
{
    All,
    Devices,
    Alerts,
    CheckIns,
    Mappings
}

// ── Display Items ────────────────────────────────────────────────

internal class IoTDeviceDisplayItem
{
    public string DeviceId { get; set; } = "";
    public IoTSource Source { get; set; }
    public string DeviceName { get; set; } = "";
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public bool IsOnline { get; set; }
    public int? BatteryLevel { get; set; }
    public string? InstallationZone { get; set; }
    public DateTime LastSeenAt { get; set; }
}

internal class IoTAlertDisplayItem
{
    public string AlertId { get; set; } = "";
    public IoTSource Source { get; set; }
    public string TriggerMethod { get; set; } = "";
    public string Status { get; set; } = "";
    public int RespondersNotified { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsActive { get; set; }
}

internal class IoTCheckInDisplayItem
{
    public string CheckInId { get; set; } = "";
    public IoTSource Source { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsEscalation { get; set; }
}

internal class IoTMappingDisplayItem
{
    public IoTSource Source { get; set; }
    public string ExternalUserId { get; set; } = "";
    public DateTime LinkedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

// ── SignalR Event DTOs ───────────────────────────────────────────
// These match the anonymous objects broadcast by IoTAlertController.

public record IoTAlertEvent(
    string AlertId,
    string RequestId,
    string Source,
    string TriggerMethod,
    string DeviceType,
    string Scope,
    string Status,
    int RespondersNotified,
    string? ResponseRequestId,
    DateTime Timestamp
);

public record IoTCheckInEvent(
    string CheckInId,
    string Source,
    string CheckInStatus,
    string Message,
    DateTime Timestamp
);

public record IoTWebhookEvent(
    string AlertId,
    string Source,
    string? WebhookId,
    double? ProcessingMs,
    DateTime Timestamp
);

public record IoTCancelledEvent(
    string AlertId,
    string RequestId,
    string? Reason,
    DateTime Timestamp
);
