// IQuickTapDetectionPort — port interface for quick-tap SOS detection.
// Defines the contract for detecting rapid multi-tap patterns (volume button,
// power button, screen tap, device shake) that trigger emergency SOS.
//
// Platform adapters (Android/iOS) implement these ports natively.
// The Aspire dashboard can monitor tap detection status across devices.
//
// Design: Configurable tap count and window duration. Deterministic —
// N taps within M seconds = trigger. No ML, no fuzzy matching.

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Supported trigger input types for quick-tap detection.
/// </summary>
public enum TapTriggerType
{
    /// <summary>Volume button (up or down) rapid presses.</summary>
    VolumeButton,
    /// <summary>Power/lock button rapid presses.</summary>
    PowerButton,
    /// <summary>Screen tap (foreground only).</summary>
    ScreenTap,
    /// <summary>Device shake (accelerometer).</summary>
    DeviceShake
}

/// <summary>
/// Event emitted when a quick-tap pattern is detected on a user's device.
/// Reported to the dashboard via the real-time event pipeline.
/// </summary>
public record QuickTapEvent(
    string UserId,
    string DeviceId,
    int TapCount,
    TimeSpan WindowDuration,
    TapTriggerType TriggerType,
    DateTime DetectedAt
);

/// <summary>
/// Configuration for quick-tap detection on a user's device.
/// Stored per-user, synced to device on profile load.
/// </summary>
public record QuickTapConfiguration(
    string UserId,
    int RequiredTaps = 4,
    TimeSpan? WindowDuration = null, // Default: 5 seconds
    bool IsEnabled = true,
    bool VolumeButtonEnabled = true,
    bool ScreenTapEnabled = true,
    bool DeviceShakeEnabled = false // Opt-in — can trigger accidentally
)
{
    public TimeSpan EffectiveWindowDuration => WindowDuration ?? TimeSpan.FromSeconds(5);
}

/// <summary>
/// Port for managing quick-tap detection configuration.
/// Implemented by the dashboard API for per-user settings.
/// </summary>
public interface IQuickTapConfigurationPort
{
    /// <summary>Get configuration for a specific user.</summary>
    Task<QuickTapConfiguration> GetConfigurationAsync(string userId, CancellationToken ct = default);

    /// <summary>Update configuration for a specific user.</summary>
    Task UpdateConfigurationAsync(QuickTapConfiguration config, CancellationToken ct = default);
}

/// <summary>
/// Port for receiving quick-tap events from devices.
/// The dashboard subscribes to these events for real-time monitoring.
/// </summary>
public interface IQuickTapEventPort
{
    /// <summary>
    /// Report a quick-tap event from a device.
    /// Called by the mobile app when a tap pattern is detected.
    /// </summary>
    Task ReportEventAsync(QuickTapEvent tapEvent, CancellationToken ct = default);

    /// <summary>
    /// Get recent quick-tap events for a user (audit trail).
    /// </summary>
    Task<IReadOnlyList<QuickTapEvent>> GetRecentEventsAsync(
        string userId,
        int maxResults = 10,
        CancellationToken ct = default);
}
