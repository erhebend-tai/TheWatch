// ISensorFusionPort — domain port for ingesting sensor readings and fusing them
// into composite events that trigger emergency response workflows.
//
// Architecture:
//   ┌─────────────────┐     ┌───────────────────────┐     ┌──────────────────────┐
//   │ Mobile App       │────▶│ ISensorFusionPort      │────▶│ Adapter              │
//   │ (iOS / Android)  │     │ .RecordXxxAsync()      │     │ (Mock, Azure, etc.)  │
//   └─────────────────┘     │ .FuseSensorDataAsync() │     └──────────────────────┘
//                           └───────────┬───────────┘
//                                       │
//                              CompositeSensorEvent
//                                       │
//                              ┌────────▼────────┐
//                              │ SOS Trigger      │
//                              │ Pipeline         │
//                              └─────────────────┘
//
// The fusion engine correlates raw readings within a configurable time window:
//   1. Collect all readings for a user within the window
//   2. Group by compatible sensor types (acoustic + accelerometer for gunshot, etc.)
//   3. Apply Dempster-Shafer or weighted Bayesian combination for confidence
//   4. Emit CompositeSensorEvent when combined confidence exceeds type-specific threshold
//   5. Route high-severity events to SOS trigger pipeline
//
// Standards:
//   - ANSI S1.4-2014: dBA weighting for acoustic readings
//   - MIL-STD-1474E: Impulse noise classification for gunshot detection
//   - NFPA 1584: Stress index thresholds for wearable health
//   - FCC E911 Z-axis: Floor level in sensor readings
//   - IEEE 1451: Smart transducer interface conventions
//
// Example — record and fuse:
//   await port.RecordAccelerometerAsync(accelReading);
//   await port.RecordAcousticEventAsync(acousticReading);
//   var events = await port.FuseSensorDataAsync("u-123", TimeSpan.FromSeconds(10));
//   foreach (var evt in events.Where(e => e.RequiresImmediateAction))
//       await sosTrigger.FireAsync(evt);
//
// Example — check sensor health:
//   var statuses = await port.GetSensorStatusAsync("u-123", "iphone-14-abc");
//   if (statuses[SensorType.Microphone] == SensorStatus.PermissionDenied)
//       await notifyUser("Microphone permission required for gunshot detection");

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Domain port for sensor data ingestion, fusion, and composite event detection.
/// Implementations: MockSensorFusionAdapter (testing), AzureSensorFusionAdapter (production).
/// </summary>
public interface ISensorFusionPort
{
    // ── Record Raw Readings ──────────────────────────────────────

    /// <summary>
    /// Record a generic sensor reading from any sensor type.
    /// The reading is stored and becomes available for fusion within its time window.
    /// </summary>
    /// <param name="reading">The sensor reading to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recorded reading with any server-side enrichments (e.g., Id assignment).</returns>
    Task<SensorReading> RecordSensorReadingAsync(SensorReading reading, CancellationToken ct = default);

    /// <summary>
    /// Record an accelerometer reading with fall/crash detection fields.
    /// Automatically evaluates IsFallDetected and IsCrashDetected against SensorThresholds
    /// if not already set by the client.
    /// </summary>
    /// <param name="reading">The accelerometer reading to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recorded reading with server-side threshold evaluation applied.</returns>
    Task<AccelerometerReading> RecordAccelerometerAsync(AccelerometerReading reading, CancellationToken ct = default);

    /// <summary>
    /// Record an acoustic event from the microphone or ambient sound sensor.
    /// Acoustic readings with GunshotConfidence above SensorThresholds.GunshotConfidenceMin (0.85)
    /// are flagged for immediate fusion processing.
    /// </summary>
    /// <param name="reading">The acoustic reading to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recorded reading with any server-side reclassification applied.</returns>
    Task<AcousticReading> RecordAcousticEventAsync(AcousticReading reading, CancellationToken ct = default);

    /// <summary>
    /// Record a wearable health reading from a smartwatch or fitness band.
    /// Automatically evaluates IsHeartRateSpike against resting rate and exercise state
    /// if not already set by the client.
    /// </summary>
    /// <param name="reading">The wearable health reading to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recorded reading with server-side threshold evaluation applied.</returns>
    Task<WearableHealthReading> RecordWearableHealthAsync(WearableHealthReading reading, CancellationToken ct = default);

    /// <summary>
    /// Record a visual classification from the on-device camera ML model.
    /// Classifications of "Weapon", "Fire", or "Smoke" with high confidence are flagged
    /// for immediate fusion processing.
    /// </summary>
    /// <param name="classification">The visual classification to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recorded classification with any server-side enrichments.</returns>
    Task<VisualClassification> RecordVisualClassificationAsync(VisualClassification classification, CancellationToken ct = default);

    // ── Fusion ───────────────────────────────────────────────────

    /// <summary>
    /// Fuse recent sensor readings for a user within the specified time window into composite events.
    /// The fusion engine:
    ///   1. Retrieves all readings within [now - window, now] for the user
    ///   2. Groups compatible readings (e.g., acoustic + accelerometer for gunshot)
    ///   3. Applies evidence combination (Dempster-Shafer or weighted Bayesian)
    ///   4. Emits CompositeSensorEvent for each detected composite pattern
    ///   5. Sets RequiresImmediateAction for life-safety events
    /// </summary>
    /// <param name="userId">User whose sensor data to fuse.</param>
    /// <param name="window">Time window to look back from now (e.g., 10 seconds for real-time, 60 seconds for batch).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of composite events detected in the window, ordered by severity descending.</returns>
    Task<IReadOnlyList<CompositeSensorEvent>> FuseSensorDataAsync(string userId, TimeSpan window, CancellationToken ct = default);

    // ── Query ────────────────────────────────────────────────────

    /// <summary>
    /// Get recent raw sensor readings for a user, optionally filtered by sensor type.
    /// Used by the dashboard to display real-time sensor telemetry.
    /// </summary>
    /// <param name="userId">User whose readings to retrieve.</param>
    /// <param name="type">Optional sensor type filter. Null returns all types.</param>
    /// <param name="limit">Maximum number of readings to return (most recent first).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Readings ordered by Timestamp descending, up to limit.</returns>
    Task<IReadOnlyList<SensorReading>> GetRecentReadingsAsync(string userId, SensorType? type, int limit, CancellationToken ct = default);

    /// <summary>
    /// Get all active (unresolved) composite events for a user.
    /// Active events have RequiresImmediateAction = true and have not been resolved or expired.
    /// Used by the mobile app to show current alerts and by the SOS pipeline to avoid duplicates.
    /// </summary>
    /// <param name="userId">User whose active events to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active composite events ordered by SeverityScore descending.</returns>
    Task<IReadOnlyList<CompositeSensorEvent>> GetActiveCompositeEventsAsync(string userId, CancellationToken ct = default);

    // ── Sensor Management ────────────────────────────────────────

    /// <summary>
    /// Get the operational status of all sensors on a specific device.
    /// Returns a dictionary mapping each SensorType to its current SensorStatus.
    /// Used by the app settings screen to show sensor health and prompt for permissions.
    /// </summary>
    /// <param name="userId">User who owns the device.</param>
    /// <param name="deviceId">Device to query sensor statuses for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of sensor type to current status.</returns>
    Task<IReadOnlyDictionary<SensorType, SensorStatus>> GetSensorStatusAsync(string userId, string deviceId, CancellationToken ct = default);

    /// <summary>
    /// Trigger calibration of a specific sensor on a device.
    /// Calibration computes a CalibrationOffset that is applied to future readings.
    /// The device must be in a known state during calibration (e.g., stationary for accelerometer).
    /// </summary>
    /// <param name="userId">User who owns the device.</param>
    /// <param name="deviceId">Device containing the sensor to calibrate.</param>
    /// <param name="type">Sensor type to calibrate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if calibration succeeded, false if the sensor is not available or calibration failed.</returns>
    Task<bool> CalibrateSensorAsync(string userId, string deviceId, SensorType type, CancellationToken ct = default);
}
