// SensorType.cs — enumerates all sensor types, composite event types, sensor statuses,
// and acoustic classifications used in TheWatch's sensor fusion domain.
//
// The sensor fusion subsystem ingests raw readings from device sensors (accelerometer,
// microphone, camera, wearable health monitors, etc.) and fuses them into composite
// events that trigger emergency response workflows.
//
// Domain range: 400-499 for sensor-related enum values.
//
// Standards referenced:
//   - ANSI S1.4-2014 (Electroacoustics — Sound Level Meters) for dBA weighting
//   - MIL-STD-1474E (Noise Limits) for impulse noise classification (gunshot detection)
//   - NFPA 1584 (Standard on the Rehabilitation Process for Members During Emergency
//     Operations and Training Exercises) for firefighter/responder stress index
//   - FCC E911 Z-axis requirements for vertical location (floor level)
//   - IEEE 1451 (Smart Transducer Interface) for sensor metadata conventions
//
// Example: SensorType.Accelerometer when a fall is detected at >3.0g.
// Example: CompositeEventType.Gunshot when acoustic + accelerometer readings fuse.
// Example: AcousticClassification.Gunshot with confidence >0.85 per detection threshold.

namespace TheWatch.Shared.Enums;

/// <summary>
/// Physical or virtual sensor type producing readings on the user's device or environment.
/// Maps to hardware sensor APIs on iOS (CoreMotion, AVAudioSession) and Android (SensorManager).
/// </summary>
public enum SensorType
{
    /// <summary>
    /// MEMS accelerometer measuring linear acceleration in 3 axes (g-force).
    /// Primary source for fall detection (>3.0g) and crash detection (>10.0g).
    /// iOS: CMAccelerometerData, Android: Sensor.TYPE_ACCELEROMETER.
    /// </summary>
    Accelerometer = 400,

    /// <summary>
    /// Device microphone capturing audio for acoustic classification.
    /// Used for gunshot detection, scream detection, glass break, raised voices.
    /// Requires RECORD_AUDIO (Android) / NSMicrophoneUsageDescription (iOS).
    /// </summary>
    Microphone = 401,

    /// <summary>
    /// Device camera (front or rear) for visual classification.
    /// Detects smoke, fire, flood, persons, vehicles, weapons via on-device ML.
    /// Requires CAMERA permission. Frames processed locally — no cloud upload of raw video.
    /// </summary>
    Camera = 402,

    /// <summary>
    /// Barometric pressure sensor measuring atmospheric pressure (hPa/mbar).
    /// Used for floor-level estimation (FCC E911 Z-axis) and weather anomaly detection.
    /// iOS: CMAltimeter, Android: Sensor.TYPE_PRESSURE.
    /// </summary>
    Barometer = 403,

    /// <summary>
    /// Ambient sound level monitoring (dBA) without full audio capture.
    /// Lower privacy impact than Microphone — captures decibel levels only, no waveforms.
    /// Used for environmental noise baseline and anomaly detection.
    /// </summary>
    AmbientSound = 404,

    /// <summary>
    /// Wearable health sensor (smartwatch, fitness band) providing biometric data.
    /// Heart rate, SpO2, skin temperature, stress index.
    /// iOS: HealthKit, Android: Health Connect / Samsung Health SDK.
    /// </summary>
    WearableHealth = 405,

    /// <summary>
    /// Global Positioning System receiver for latitude/longitude/altitude.
    /// Always-on background location required for geofencing and responder proximity.
    /// iOS: CLLocationManager, Android: FusedLocationProviderClient.
    /// </summary>
    GPS = 406,

    /// <summary>
    /// MEMS gyroscope measuring angular velocity (rad/s) in 3 axes.
    /// Complements accelerometer for orientation tracking and fall angle estimation.
    /// iOS: CMGyroData, Android: Sensor.TYPE_GYROSCOPE.
    /// </summary>
    Gyroscope = 407,

    /// <summary>
    /// Magnetometer (digital compass) measuring magnetic field strength (µT).
    /// Used for heading estimation and indoor positioning augmentation.
    /// iOS: CMMagnetometerData, Android: Sensor.TYPE_MAGNETIC_FIELD.
    /// </summary>
    Magnetometer = 408,

    /// <summary>
    /// Proximity sensor detecting nearby objects (typically face-to-screen distance).
    /// Used to detect pocket/purse state and suppress accidental triggers.
    /// Android: Sensor.TYPE_PROXIMITY, iOS: UIDevice.proximityState.
    /// </summary>
    Proximity = 409,

    /// <summary>
    /// Ambient light sensor measuring illuminance (lux).
    /// Used for day/night context and flash-based visual anomaly detection.
    /// Android: Sensor.TYPE_LIGHT, iOS: not directly exposed (use camera exposure).
    /// </summary>
    LightSensor = 410,

    /// <summary>
    /// Temperature sensor (ambient or device skin temperature).
    /// Not available on all devices. Used for environmental anomaly detection.
    /// Android: Sensor.TYPE_AMBIENT_TEMPERATURE, iOS: not directly available.
    /// </summary>
    Thermometer = 411
}

/// <summary>
/// Composite events produced by fusing multiple sensor readings within a time window.
/// Each composite event type has defined contributing sensor combinations and confidence thresholds.
/// These events feed directly into the SOS trigger pipeline and responder dispatch.
/// </summary>
public enum CompositeEventType
{
    /// <summary>
    /// Gunshot detected by acoustic classification (impulse noise signature per MIL-STD-1474E)
    /// optionally corroborated by accelerometer shock and visual muzzle flash.
    /// Minimum acoustic confidence: 0.85. Triggers immediate SOS if user opts in.
    /// </summary>
    Gunshot = 420,

    /// <summary>
    /// Fall detected by accelerometer free-fall followed by impact (>3.0g).
    /// Corroborated by gyroscope orientation change and post-impact stillness.
    /// Similar to Apple Fall Detection and Google Pixel fall detection algorithms.
    /// </summary>
    Fall = 421,

    /// <summary>
    /// Fire or smoke detected visually by on-device camera ML model.
    /// May be corroborated by thermometer spike and barometric pressure change.
    /// </summary>
    FireVisual = 422,

    /// <summary>
    /// Explosion detected by acoustic impulse (low-frequency boom) combined with
    /// accelerometer shock wave and possible barometric pressure spike.
    /// </summary>
    Explosion = 423,

    /// <summary>
    /// Glass break detected by acoustic classification (high-frequency transient).
    /// Common in home security — indicates forced entry or structural damage.
    /// </summary>
    GlassBreak = 424,

    /// <summary>
    /// Vehicle crash detected by accelerometer impact (>10.0g) combined with
    /// GPS speed context (was moving >25 mph before impact) and sudden deceleration.
    /// Similar to Apple Crash Detection and Google Pixel car crash detection.
    /// </summary>
    Crash = 425,

    /// <summary>
    /// Flood or rising water detected visually by camera ML model.
    /// May be corroborated by barometric pressure drop (storm) and weather API data.
    /// </summary>
    FloodVisual = 426,

    /// <summary>
    /// Human scream detected by acoustic classification (>85 dBA, characteristic formant pattern).
    /// Corroborated by duration (sustained >0.5s) and frequency range (2-4 kHz fundamental).
    /// </summary>
    Scream = 427,

    /// <summary>
    /// Medical distress inferred from wearable health data: heart rate spike without exercise,
    /// SpO2 drop below 90%, skin temperature >39C, or stress index >80.
    /// Per NFPA 1584 rehabilitation thresholds adapted for civilian use.
    /// </summary>
    MedicalDistress = 428,

    /// <summary>
    /// Intruder detected by visual classification (person in restricted zone)
    /// combined with proximity sensor activation and/or glass break audio.
    /// </summary>
    IntruderDetected = 429,

    /// <summary>
    /// Catch-all for environmental anomalies that don't fit specific categories:
    /// unusual barometric pressure changes, temperature spikes, electromagnetic anomalies.
    /// </summary>
    EnvironmentalAnomaly = 430
}

/// <summary>
/// Operational status of a sensor on a specific device.
/// Used by the dashboard and mobile app to show sensor health and troubleshoot issues.
/// </summary>
public enum SensorStatus
{
    /// <summary>Sensor is active and producing readings within expected parameters.</summary>
    Active = 440,

    /// <summary>Sensor is present but not currently producing readings (powered down or paused).</summary>
    Inactive = 441,

    /// <summary>Sensor is in calibration mode — readings may be unreliable during this period.</summary>
    Calibrating = 442,

    /// <summary>Sensor is reporting errors or producing out-of-range readings.</summary>
    Error = 443,

    /// <summary>
    /// The OS-level permission required by this sensor has been denied by the user.
    /// The app should prompt the user to grant permission in system settings.
    /// </summary>
    PermissionDenied = 444,

    /// <summary>
    /// The sensor hardware is not available on this device.
    /// Example: Thermometer on most iPhones, or Barometer on older Android devices.
    /// </summary>
    NotAvailable = 445
}

/// <summary>
/// Acoustic event classification produced by on-device audio ML models.
/// Classification follows impulse noise taxonomy from MIL-STD-1474E and
/// continuous noise measurement per ANSI S1.4-2014 (Type 1/Type 2 sound level meters).
///
/// The classifier runs on-device using TensorFlow Lite (Android) or Core ML (iOS).
/// Audio is processed in 50ms windows with 25ms overlap. No raw audio leaves the device.
///
/// Reference standards:
///   - ANSI S1.4-2014: Sound level meter specifications, A-weighting for human hearing
///   - MIL-STD-1474E: Department of Defense design criteria for noise limits,
///     Section 5.3 impulse noise (peak >140 dB SPL, rise time <10ms = gunshot signature)
///   - IEC 61672-1: Electroacoustics — Sound level meters (international equivalent)
/// </summary>
public enum AcousticClassification
{
    /// <summary>
    /// Gunshot impulse noise: peak >140 dB SPL, rise time <10ms, duration <2ms.
    /// Per MIL-STD-1474E Section 5.3 impulse noise criteria.
    /// Confidence must exceed 0.85 threshold to trigger composite event.
    /// Frequency signature: broadband impulse with energy concentrated 500Hz-4kHz.
    /// </summary>
    Gunshot = 460,

    /// <summary>
    /// Glass break: high-frequency transient (5kHz-20kHz) with characteristic
    /// shattering pattern. Duration typically 100-500ms.
    /// Common in residential/commercial security systems.
    /// </summary>
    GlassBreak = 461,

    /// <summary>
    /// Human scream: sustained vocalization >85 dBA with fundamental frequency
    /// typically 2-4kHz. Duration >0.5s distinguishes from brief exclamations.
    /// Per ANSI S1.4 A-weighted measurement.
    /// </summary>
    Scream = 462,

    /// <summary>
    /// Raised voices: speech at elevated volume (>75 dBA) with aggressive prosody.
    /// Distinct from normal conversation (55-65 dBA) and screaming (>85 dBA).
    /// May indicate verbal altercation or distress.
    /// </summary>
    RaisedVoices = 463,

    /// <summary>
    /// Physical impact sound: collision, punch, kick, or object striking surface.
    /// Broadband transient with lower frequency content than glass break.
    /// Duration typically <100ms.
    /// </summary>
    ImpactSound = 464,

    /// <summary>
    /// Explosion: low-frequency impulse (<500Hz dominant) with sustained rumble.
    /// Longer duration than gunshot (>50ms), often followed by structural sounds.
    /// Per MIL-STD-1474E extended impulse noise criteria.
    /// </summary>
    Explosion = 465,

    /// <summary>
    /// Silence: ambient level below expected baseline for environment.
    /// May indicate microphone obstruction, device in sealed container,
    /// or genuinely quiet environment. Context-dependent interpretation.
    /// </summary>
    Silence = 466,

    /// <summary>
    /// Normal ambient sound within expected parameters for the environment.
    /// Typically 40-60 dBA for residential, 60-75 dBA for urban outdoor.
    /// Baseline against which anomalies are detected.
    /// </summary>
    AmbientNormal = 467,

    /// <summary>
    /// Alarm sound: repetitive tonal pattern (smoke detector, burglar alarm, CO detector).
    /// Characteristic frequency patterns: smoke alarms typically 3.15kHz ± 10%.
    /// Per NFPA 72 (National Fire Alarm and Signaling Code) alarm sound specs.
    /// </summary>
    Alarm = 468,

    /// <summary>
    /// Emergency vehicle siren: characteristic frequency sweep patterns.
    /// Wail (slow sweep 650-1800Hz), Yelp (fast sweep), Hi-Lo (alternating two-tone).
    /// Per SAE J1849 (Emergency Vehicle Sirens).
    /// </summary>
    Siren = 469
}
