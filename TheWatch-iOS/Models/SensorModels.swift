// SensorModels.swift — iOS domain models for TheWatch sensor fusion subsystem.
//
// Mirrors the C# models in TheWatch.Shared for cross-platform consistency.
// All sensor readings are captured on-device and transmitted to the backend API.
// No raw audio or video leaves the device — only classified events and telemetry.
//
// Standards referenced:
//   - ANSI S1.4-2014: dBA weighting for acoustic decibel measurements
//   - MIL-STD-1474E: Impulse noise thresholds for gunshot classification
//   - NFPA 1584: Stress index thresholds for responder rehabilitation
//   - FCC E911 Z-axis: Floor level for vertical location in multi-story buildings
//
// Example — recording a fall:
//   let reading = AccelerometerReading(
//       userId: "u-123", deviceId: "iphone-14-abc",
//       gForce: 4.2, isFallDetected: true,
//       fallAngleDegrees: 78.5, freeFallDurationMs: 420,
//       latitude: 32.7767, longitude: -96.7970
//   )
//   try await sensorService.recordAccelerometer(reading)
//
// Example — fusing sensor data:
//   let events = try await sensorService.fuseSensorData(userId: "u-123", windowSeconds: 10)
//   for event in events where event.requiresImmediateAction {
//       await sosTrigger.fire(event)
//   }

import Foundation

// MARK: - Enums

/// Physical or virtual sensor type producing readings on the user's device.
/// Maps to CoreMotion, AVAudioSession, CoreLocation, HealthKit on iOS.
enum SensorType: String, Codable, CaseIterable {
    case accelerometer = "Accelerometer"
    case microphone = "Microphone"
    case camera = "Camera"
    case barometer = "Barometer"
    case ambientSound = "AmbientSound"
    case wearableHealth = "WearableHealth"
    case gps = "GPS"
    case gyroscope = "Gyroscope"
    case magnetometer = "Magnetometer"
    case proximity = "Proximity"
    case lightSensor = "LightSensor"
    case thermometer = "Thermometer"
}

/// Composite events produced by fusing multiple sensor readings within a time window.
/// Each type has defined contributing sensor combinations and confidence thresholds.
enum CompositeEventType: String, Codable, CaseIterable {
    case gunshot = "Gunshot"
    case fall = "Fall"
    case fireVisual = "FireVisual"
    case explosion = "Explosion"
    case glassBreak = "GlassBreak"
    case crash = "Crash"
    case floodVisual = "FloodVisual"
    case scream = "Scream"
    case medicalDistress = "MedicalDistress"
    case intruderDetected = "IntruderDetected"
    case environmentalAnomaly = "EnvironmentalAnomaly"
}

/// Operational status of a sensor on a specific device.
enum SensorStatus: String, Codable, CaseIterable {
    case active = "Active"
    case inactive = "Inactive"
    case calibrating = "Calibrating"
    case error = "Error"
    case permissionDenied = "PermissionDenied"
    case notAvailable = "NotAvailable"
}

/// Acoustic event classification from on-device audio ML model (Core ML).
/// Reference: ANSI S1.4-2014 (dBA weighting), MIL-STD-1474E (impulse noise).
enum AcousticClassification: String, Codable, CaseIterable {
    case gunshot = "Gunshot"
    case glassBreak = "GlassBreak"
    case scream = "Scream"
    case raisedVoices = "RaisedVoices"
    case impactSound = "ImpactSound"
    case explosion = "Explosion"
    case silence = "Silence"
    case ambientNormal = "AmbientNormal"
    case alarm = "Alarm"
    case siren = "Siren"
}

// MARK: - Threshold Constants

/// Threshold constants used by the sensor fusion engine.
/// Derived from published standards and device manufacturer specifications.
struct SensorThresholds {
    /// Minimum g-force to classify as a fall (Apple Watch uses ~3.0g).
    static let fallGForceThreshold: Double = 3.0
    /// Minimum g-force to classify as a vehicle crash (NHTSA frontal: 10-30g).
    static let crashGForceThreshold: Double = 10.0
    /// Minimum acoustic confidence to report gunshot (per MIL-STD-1474E impulse criteria).
    static let gunshotConfidenceMin: Double = 0.85
    /// Minimum dBA for scream classification (per ANSI S1.4 A-weighted).
    static let screamDecibelMin: Double = 85.0
    /// HR spike multiplier relative to resting (HR > resting * 1.5 without exercise).
    static let heartRateSpikeMultiplier: Double = 1.5
    /// Stress index critical threshold (adapted from NFPA 1584).
    static let stressIndexCritical: Int = 80
    /// SpO2 critical low percentage (per WHO pulse oximetry guidelines).
    static let spO2CriticalLow: Double = 90.0
    /// Skin temperature high threshold in Celsius.
    static let skinTempHigh: Double = 39.0
}

// MARK: - Sensor Reading Models

/// Base sensor reading from any device sensor.
struct SensorReading: Codable, Hashable, Identifiable {
    var id: String { readingId }

    let readingId: String
    let sensorType: SensorType
    let deviceId: String
    let userId: String
    let timestamp: Date
    let rawValue: Double
    let unit: String
    let confidence: Float
    let calibrationOffset: Double
    let latitude: Double
    let longitude: Double
    let altitudeMeters: Double
    /// Floor level per FCC E911 Z-axis. Nil if undetermined.
    let floorLevel: Int?

    init(
        readingId: String = UUID().uuidString,
        sensorType: SensorType = .accelerometer,
        deviceId: String = "",
        userId: String = "",
        timestamp: Date = Date(),
        rawValue: Double = 0,
        unit: String = "",
        confidence: Float = 1.0,
        calibrationOffset: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        altitudeMeters: Double = 0,
        floorLevel: Int? = nil
    ) {
        self.readingId = readingId
        self.sensorType = sensorType
        self.deviceId = deviceId
        self.userId = userId
        self.timestamp = timestamp
        self.rawValue = rawValue
        self.unit = unit
        self.confidence = confidence
        self.calibrationOffset = calibrationOffset
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.floorLevel = floorLevel
    }
}

/// Accelerometer reading with fall and crash detection.
/// Fall: free-fall phase + impact >3.0g. Crash: sudden deceleration >10.0g with GPS speed.
struct AccelerometerReading: Codable, Hashable, Identifiable {
    var id: String { readingId }

    let readingId: String
    let sensorType: SensorType
    let deviceId: String
    let userId: String
    let timestamp: Date
    let rawValue: Double
    let unit: String
    let confidence: Float
    let calibrationOffset: Double
    let latitude: Double
    let longitude: Double
    let altitudeMeters: Double
    let floorLevel: Int?

    /// Peak g-force magnitude: sqrt(x² + y² + z²).
    let gForce: Double
    /// True when GForce > FallGForceThreshold (3.0g) with preceding free-fall.
    let isFallDetected: Bool
    /// True when GForce > CrashGForceThreshold (10.0g) with GPS speed context.
    let isCrashDetected: Bool
    /// Peak single-sample impact g-force.
    let impactGForce: Double
    /// Angle of fall in degrees from vertical (0=upright, 90=horizontal).
    let fallAngleDegrees: Double
    /// Free-fall duration in milliseconds before impact.
    let freeFallDurationMs: Double

    init(
        readingId: String = UUID().uuidString,
        sensorType: SensorType = .accelerometer,
        deviceId: String = "",
        userId: String = "",
        timestamp: Date = Date(),
        rawValue: Double = 0,
        unit: String = "g",
        confidence: Float = 1.0,
        calibrationOffset: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        altitudeMeters: Double = 0,
        floorLevel: Int? = nil,
        gForce: Double = 0,
        isFallDetected: Bool = false,
        isCrashDetected: Bool = false,
        impactGForce: Double = 0,
        fallAngleDegrees: Double = 0,
        freeFallDurationMs: Double = 0
    ) {
        self.readingId = readingId
        self.sensorType = sensorType
        self.deviceId = deviceId
        self.userId = userId
        self.timestamp = timestamp
        self.rawValue = rawValue
        self.unit = unit
        self.confidence = confidence
        self.calibrationOffset = calibrationOffset
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.floorLevel = floorLevel
        self.gForce = gForce
        self.isFallDetected = isFallDetected
        self.isCrashDetected = isCrashDetected
        self.impactGForce = impactGForce
        self.fallAngleDegrees = fallAngleDegrees
        self.freeFallDurationMs = freeFallDurationMs
    }
}

/// Acoustic event reading from microphone or ambient sound sensor.
/// Classification by Core ML model. Audio processed in 50ms windows. No raw audio leaves device.
struct AcousticReading: Codable, Hashable, Identifiable {
    var id: String { readingId }

    let readingId: String
    let sensorType: SensorType
    let deviceId: String
    let userId: String
    let timestamp: Date
    let rawValue: Double
    let unit: String
    let confidence: Float
    let calibrationOffset: Double
    let latitude: Double
    let longitude: Double
    let altitudeMeters: Double
    let floorLevel: Int?

    /// Classified acoustic event type.
    let classification: AcousticClassification
    /// Sound pressure level in dBA (A-weighted per ANSI S1.4-2014).
    let decibelLevel: Double
    /// Gunshot confidence (0.0-1.0). Must exceed 0.85 to trigger composite event.
    let gunshotConfidence: Float
    /// Dominant frequency in Hz.
    let frequencyHz: Double
    /// Event duration in milliseconds.
    let durationMs: Double
    /// Direction to sound source in degrees (0-360, 0=device front).
    let directionDegrees: Double
    /// Estimated distance to sound source in meters.
    let distanceEstimateMeters: Double

    init(
        readingId: String = UUID().uuidString,
        sensorType: SensorType = .microphone,
        deviceId: String = "",
        userId: String = "",
        timestamp: Date = Date(),
        rawValue: Double = 0,
        unit: String = "dBA",
        confidence: Float = 1.0,
        calibrationOffset: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        altitudeMeters: Double = 0,
        floorLevel: Int? = nil,
        classification: AcousticClassification = .ambientNormal,
        decibelLevel: Double = 0,
        gunshotConfidence: Float = 0,
        frequencyHz: Double = 0,
        durationMs: Double = 0,
        directionDegrees: Double = 0,
        distanceEstimateMeters: Double = 0
    ) {
        self.readingId = readingId
        self.sensorType = sensorType
        self.deviceId = deviceId
        self.userId = userId
        self.timestamp = timestamp
        self.rawValue = rawValue
        self.unit = unit
        self.confidence = confidence
        self.calibrationOffset = calibrationOffset
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.floorLevel = floorLevel
        self.classification = classification
        self.decibelLevel = decibelLevel
        self.gunshotConfidence = gunshotConfidence
        self.frequencyHz = frequencyHz
        self.durationMs = durationMs
        self.directionDegrees = directionDegrees
        self.distanceEstimateMeters = distanceEstimateMeters
    }
}

/// Wearable health reading from smartwatch or fitness band.
/// Combines heart rate, SpO2, skin temperature, and stress index.
struct WearableHealthReading: Codable, Hashable, Identifiable {
    var id: String { readingId }

    let readingId: String
    let sensorType: SensorType
    let deviceId: String
    let userId: String
    let timestamp: Date
    let rawValue: Double
    let unit: String
    let confidence: Float
    let calibrationOffset: Double
    let latitude: Double
    let longitude: Double
    let altitudeMeters: Double
    let floorLevel: Int?

    /// Current heart rate in bpm.
    let heartRateBpm: Double
    /// Resting heart rate in bpm (7-day rolling average during sleep).
    let restingHeartRateBpm: Double
    /// True when HR > resting * 1.5 without exercise.
    let isHeartRateSpike: Bool
    /// Blood oxygen saturation percentage. Below 90% = critical.
    let spO2Percent: Double
    /// Skin temperature in Celsius. Above 39°C = fever/hyperthermia.
    let skinTempCelsius: Double
    /// Composite stress index 0-100 (per NFPA 1584). Above 80 = critical.
    let stressIndex: Int
    /// Whether user is currently exercising (suppresses HR spike alerts).
    let isExercising: Bool
    /// Steps in last 5 minutes. Brisk walking: 500-650.
    let stepCountLast5Min: Int

    init(
        readingId: String = UUID().uuidString,
        sensorType: SensorType = .wearableHealth,
        deviceId: String = "",
        userId: String = "",
        timestamp: Date = Date(),
        rawValue: Double = 0,
        unit: String = "bpm",
        confidence: Float = 1.0,
        calibrationOffset: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        altitudeMeters: Double = 0,
        floorLevel: Int? = nil,
        heartRateBpm: Double = 0,
        restingHeartRateBpm: Double = 0,
        isHeartRateSpike: Bool = false,
        spO2Percent: Double = 0,
        skinTempCelsius: Double = 0,
        stressIndex: Int = 0,
        isExercising: Bool = false,
        stepCountLast5Min: Int = 0
    ) {
        self.readingId = readingId
        self.sensorType = sensorType
        self.deviceId = deviceId
        self.userId = userId
        self.timestamp = timestamp
        self.rawValue = rawValue
        self.unit = unit
        self.confidence = confidence
        self.calibrationOffset = calibrationOffset
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.floorLevel = floorLevel
        self.heartRateBpm = heartRateBpm
        self.restingHeartRateBpm = restingHeartRateBpm
        self.isHeartRateSpike = isHeartRateSpike
        self.spO2Percent = spO2Percent
        self.skinTempCelsius = skinTempCelsius
        self.stressIndex = stressIndex
        self.isExercising = isExercising
        self.stepCountLast5Min = stepCountLast5Min
    }
}

/// Visual classification from on-device camera ML model (Core ML).
/// No raw images transmitted — only classification results.
struct VisualClassification: Codable, Hashable, Identifiable {
    var id: String { readingId }

    let readingId: String
    let sensorType: SensorType
    let deviceId: String
    let userId: String
    let timestamp: Date
    let rawValue: Double
    let unit: String
    let confidence: Float
    let calibrationOffset: Double
    let latitude: Double
    let longitude: Double
    let altitudeMeters: Double
    let floorLevel: Int?

    /// Detection type: "Smoke", "Fire", "Flood", "Person", "Vehicle", "Weapon".
    let classificationType: String
    /// Model confidence 0.0-1.0. Production threshold: >0.70 alerting, >0.90 autonomous.
    let classificationConfidence: Float
    /// Bounding box X (normalized 0.0-1.0, left=0).
    let boundingBoxX: Double
    /// Bounding box Y (normalized 0.0-1.0, top=0).
    let boundingBoxY: Double
    /// Bounding box width (normalized 0.0-1.0).
    let boundingBoxW: Double
    /// Bounding box height (normalized 0.0-1.0).
    let boundingBoxH: Double
    /// Timestamp of the classified video frame.
    let frameTimestamp: Date
    /// Camera ID: "front", "rear", "external-001", or CCTV camera ID.
    let cameraId: String

    init(
        readingId: String = UUID().uuidString,
        sensorType: SensorType = .camera,
        deviceId: String = "",
        userId: String = "",
        timestamp: Date = Date(),
        rawValue: Double = 0,
        unit: String = "",
        confidence: Float = 1.0,
        calibrationOffset: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        altitudeMeters: Double = 0,
        floorLevel: Int? = nil,
        classificationType: String = "",
        classificationConfidence: Float = 0,
        boundingBoxX: Double = 0,
        boundingBoxY: Double = 0,
        boundingBoxW: Double = 0,
        boundingBoxH: Double = 0,
        frameTimestamp: Date = Date(),
        cameraId: String = ""
    ) {
        self.readingId = readingId
        self.sensorType = sensorType
        self.deviceId = deviceId
        self.userId = userId
        self.timestamp = timestamp
        self.rawValue = rawValue
        self.unit = unit
        self.confidence = confidence
        self.calibrationOffset = calibrationOffset
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.floorLevel = floorLevel
        self.classificationType = classificationType
        self.classificationConfidence = classificationConfidence
        self.boundingBoxX = boundingBoxX
        self.boundingBoxY = boundingBoxY
        self.boundingBoxW = boundingBoxW
        self.boundingBoxH = boundingBoxH
        self.frameTimestamp = frameTimestamp
        self.cameraId = cameraId
    }
}

/// Composite sensor event produced by fusing multiple raw readings within a time window.
/// The fusion engine correlates readings by time, location, and type to produce high-confidence events.
struct CompositeSensorEvent: Codable, Hashable, Identifiable {
    var id: String { eventId }

    let eventId: String
    let eventType: CompositeEventType
    /// Combined confidence from all contributing sensors (0.0-1.0).
    let confidence: Float
    /// IDs of sensor readings that contributed to this event.
    let contributingSensorIds: [String]
    /// UTC timestamp when the fusion engine produced this event.
    let timestamp: Date
    /// Latitude of the event (centroid of contributing readings).
    let latitude: Double
    /// Longitude of the event (centroid of contributing readings).
    let longitude: Double
    /// Severity 0-100. 0-30=info, 31-60=warning, 61-80=urgent, 81-100=critical.
    let severityScore: Int
    /// True when SeverityScore >= 80 or event type is inherently life-threatening.
    let requiresImmediateAction: Bool
    /// Human-readable description for display.
    let description: String
    /// Correlation ID linking to SOS/response chain.
    let correlationId: String

    init(
        eventId: String = UUID().uuidString,
        eventType: CompositeEventType = .fall,
        confidence: Float = 0,
        contributingSensorIds: [String] = [],
        timestamp: Date = Date(),
        latitude: Double = 0,
        longitude: Double = 0,
        severityScore: Int = 0,
        requiresImmediateAction: Bool = false,
        description: String = "",
        correlationId: String = ""
    ) {
        self.eventId = eventId
        self.eventType = eventType
        self.confidence = confidence
        self.contributingSensorIds = contributingSensorIds
        self.timestamp = timestamp
        self.latitude = latitude
        self.longitude = longitude
        self.severityScore = severityScore
        self.requiresImmediateAction = requiresImmediateAction
        self.description = description
        self.correlationId = correlationId
    }

    /// Display-friendly severity label.
    var severityLabel: String {
        switch severityScore {
        case 0...30: return "Info"
        case 31...60: return "Warning"
        case 61...80: return "Urgent"
        default: return "Critical"
        }
    }
}
