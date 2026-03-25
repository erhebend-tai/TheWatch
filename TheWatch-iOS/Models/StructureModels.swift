// StructureModels.swift — iOS domain models for building data, hazard conditions,
// weather alerts, hazmat lookup, and seismic alerts.
//
// Mirrors C# models in TheWatch.Shared.Domain.Models.StructureModels and
// enums in TheWatch.Shared.Enums.StructureType for offline-first mobile capability.
//
// Standards referenced:
//   IBC — International Building Code (construction types, occupancy groups)
//   HAZUS-MH — FEMA building classification for loss estimation
//   NFPA 13/13R/13D — Sprinkler system classifications
//   NFPA 72 — National Fire Alarm and Signaling Code
//   NFIRS 5.0 — National Fire Incident Reporting System
//   CAP 1.2 — Common Alerting Protocol (NWS weather alerts)
//   ERG 2024 — Emergency Response Guidebook (USDOT/PHMSA)
//   ShakeAlert/USGS — Earthquake Early Warning / Modified Mercalli Intensity
//
// Example:
//   let building = BuildingData(
//       buildingId: UUID().uuidString,
//       apn: "123-456-789",
//       address: "100 Main St, Springfield, IL 62701",
//       constructionType: .typeV_WoodFrame,
//       hazusType: .w1,
//       occupancyGroup: .r_Residential,
//       floorCount: 2,
//       totalSqFt: 2400,
//       heatingFuelType: .naturalGas,
//       foundationType: .basement
//   )

import Foundation

// MARK: - Enums

/// IBC Chapter 6 construction classifications. Determines fire-resistance ratings.
enum ConstructionType: String, Codable, CaseIterable {
    case typeI_FireResistive = "TypeI_FireResistive"
    case typeII_NonCombustible = "TypeII_NonCombustible"
    case typeIII_Ordinary = "TypeIII_Ordinary"
    case typeIV_HeavyTimber = "TypeIV_HeavyTimber"
    case typeV_WoodFrame = "TypeV_WoodFrame"
}

/// FEMA HAZUS-MH building types for earthquake/flood/wind loss estimation.
/// Each code encodes structural system + height class (L=Low 1-3, M=Mid 4-7, H=High 8+).
enum HAZUSBuildingType: String, Codable, CaseIterable {
    case w1 = "W1"       // Wood, Light-Frame (≤5,000 sqft)
    case w2 = "W2"       // Wood, Commercial/Industrial (>5,000 sqft)
    case s1L = "S1L"     // Steel Moment Frame, Low-Rise
    case s1M = "S1M"     // Steel Moment Frame, Mid-Rise
    case s1H = "S1H"     // Steel Moment Frame, High-Rise
    case s2L = "S2L"     // Steel Braced Frame, Low-Rise
    case s2M = "S2M"     // Steel Braced Frame, Mid-Rise
    case s2H = "S2H"     // Steel Braced Frame, High-Rise
    case c1L = "C1L"     // Concrete Moment Frame, Low-Rise
    case c1M = "C1M"     // Concrete Moment Frame, Mid-Rise
    case c1H = "C1H"     // Concrete Moment Frame, High-Rise
    case c2L = "C2L"     // Concrete Shear Wall, Low-Rise
    case c2M = "C2M"     // Concrete Shear Wall, Mid-Rise
    case c2H = "C2H"     // Concrete Shear Wall, High-Rise
    case c3L = "C3L"     // Concrete Frame with URM Infill, Low-Rise
    case c3M = "C3M"     // Concrete Frame with URM Infill, Mid-Rise
    case c3H = "C3H"     // Concrete Frame with URM Infill, High-Rise
    case pc1 = "PC1"     // Precast Concrete Tilt-Up Walls
    case pc2L = "PC2L"   // Precast Concrete Frame, Low-Rise
    case pc2M = "PC2M"   // Precast Concrete Frame, Mid-Rise
    case pc2H = "PC2H"   // Precast Concrete Frame, High-Rise
    case rm1L = "RM1L"   // Reinforced Masonry, Wood/Metal Diaphragm, Low-Rise
    case rm1M = "RM1M"   // Reinforced Masonry, Wood/Metal Diaphragm, Mid-Rise
    case rm2L = "RM2L"   // Reinforced Masonry, Concrete Diaphragm, Low-Rise
    case rm2M = "RM2M"   // Reinforced Masonry, Concrete Diaphragm, Mid-Rise
    case rm2H = "RM2H"   // Reinforced Masonry, Concrete Diaphragm, High-Rise
    case urml = "URML"   // Unreinforced Masonry, Low-Rise (high seismic vulnerability)
    case urmm = "URMM"   // Unreinforced Masonry, Mid-Rise (highest seismic vulnerability)
    case mh = "MH"       // Manufactured Housing (mobile/modular)
}

/// IBC Chapter 3 occupancy group classifications.
enum OccupancyGroup: String, Codable, CaseIterable {
    case a_Assembly = "A_Assembly"
    case b_Business = "B_Business"
    case e_Educational = "E_Educational"
    case f_Factory = "F_Factory"
    case h_HighHazard = "H_HighHazard"
    case i_Institutional = "I_Institutional"
    case m_Mercantile = "M_Mercantile"
    case r_Residential = "R_Residential"
    case s_Storage = "S_Storage"
    case u_Utility = "U_Utility"
}

/// NFPA sprinkler system classifications (13, 13R, 13D).
enum SprinklerType: String, Codable, CaseIterable {
    case nfpa13_Commercial = "NFPA13_Commercial"
    case nfpa13R_Residential = "NFPA13R_Residential"
    case nfpa13D_Dwelling = "NFPA13D_Dwelling"
    case none = "None"
}

/// NFPA 72 fire alarm and signaling code device/system types.
enum AlarmType: String, Codable, CaseIterable {
    case nfpa72_Initiating = "NFPA72_Initiating"
    case nfpa72_Notification = "NFPA72_Notification"
    case nfpa72_Supervisory = "NFPA72_Supervisory"
    case localOnly = "LocalOnly"
    case centralStation = "CentralStation"
    case proprietaryStation = "ProprietaryStation"
    case remoteStation = "RemoteStation"
    case none = "None"
}

/// Monitoring service type for alarm/security systems.
enum MonitoringType: String, Codable, CaseIterable {
    case selfMonitored = "SelfMonitored"
    case centralStation = "CentralStation"
    case proprietaryStation = "ProprietaryStation"
    case remoteStation = "RemoteStation"
    case none = "None"
}

/// Primary heating fuel type. Critical for explosion risk assessment.
enum HeatingFuelType: String, Codable, CaseIterable {
    case naturalGas = "NaturalGas"
    case propane = "Propane"
    case oil = "Oil"
    case electric = "Electric"
    case wood = "Wood"
    case coal = "Coal"
    case solar = "Solar"
    case none = "None"
}

/// Foundation type. Critical for flood vulnerability assessment.
enum FoundationType: String, Codable, CaseIterable {
    case slab = "Slab"
    case crawlSpace = "CrawlSpace"
    case basement = "Basement"
    case piersAndPosts = "PiersAndPosts"
    case deepFoundation = "DeepFoundation"
}

/// NFIRS 5.0 fire cause categories.
enum FireCause: String, Codable, CaseIterable {
    case cooking = "Cooking"
    case heating = "Heating"
    case electrical = "Electrical"
    case smoking = "Smoking"
    case intentional = "Intentional"
    case appliance = "Appliance"
    case naturalCauses = "NaturalCauses"
    case unknown = "Unknown"
}

/// CAP 1.2 severity levels (NWS/IPAWS).
enum CAPSeverity: String, Codable, CaseIterable {
    case extreme = "Extreme"
    case severe = "Severe"
    case moderate = "Moderate"
    case minor = "Minor"
    case unknown = "Unknown"
}

/// CAP 1.2 urgency levels.
enum CAPUrgency: String, Codable, CaseIterable {
    case immediate = "Immediate"
    case expected = "Expected"
    case future = "Future"
    case past = "Past"
    case unknown = "Unknown"
}

/// CAP 1.2 certainty levels.
enum CAPCertainty: String, Codable, CaseIterable {
    case observed = "Observed"
    case likely = "Likely"
    case possible = "Possible"
    case unlikely = "Unlikely"
    case unknown = "Unknown"
}

/// Modified Mercalli Intensity scale (ShakeAlert/USGS).
enum SeismicMMI: String, Codable, CaseIterable {
    case i = "I"           // Not felt
    case ii = "II"         // Weak — felt at rest on upper floors
    case iii = "III"       // Weak — felt indoors, hanging objects swing
    case iv = "IV"         // Light — dishes/windows rattle
    case v = "V"           // Moderate — small objects displaced
    case vi = "VI"         // Strong — furniture moves, slight damage
    case vii = "VII"       // Very Strong — considerable damage to poor construction
    case viii = "VIII"     // Severe — heavy damage to ordinary buildings
    case ix = "IX"         // Violent — heavy damage, structures shifted off foundations
    case x = "X"           // Extreme — most structures destroyed, ground cracked
}

// MARK: - Models

/// Core building/structure record. Captures construction classification, fire protection,
/// occupancy, and geolocation.
struct BuildingData: Codable, Hashable, Identifiable {
    var id: String { buildingId }
    var buildingId: String
    var apn: String
    var address: String
    var name: String
    var constructionType: ConstructionType
    var hazusType: HAZUSBuildingType
    var occupancyGroup: OccupancyGroup
    var floorCount: Int
    var totalSqFt: Double
    var yearBuilt: Int?
    var heatingFuelType: HeatingFuelType
    var foundationType: FoundationType
    var hasElevator: Bool
    var hasSprinklerSystem: Bool
    var sprinklerType: SprinklerType
    var hasFireAlarm: Bool
    var alarmType: AlarmType
    var monitoringType: MonitoringType
    var latitude: Double
    var longitude: Double
    var lastInspectionDate: Date?
    var notes: String
    var correlationId: String

    init(
        buildingId: String = UUID().uuidString,
        apn: String = "",
        address: String = "",
        name: String = "",
        constructionType: ConstructionType = .typeV_WoodFrame,
        hazusType: HAZUSBuildingType = .w1,
        occupancyGroup: OccupancyGroup = .r_Residential,
        floorCount: Int = 1,
        totalSqFt: Double = 0,
        yearBuilt: Int? = nil,
        heatingFuelType: HeatingFuelType = .none,
        foundationType: FoundationType = .slab,
        hasElevator: Bool = false,
        hasSprinklerSystem: Bool = false,
        sprinklerType: SprinklerType = .none,
        hasFireAlarm: Bool = false,
        alarmType: AlarmType = .none,
        monitoringType: MonitoringType = .none,
        latitude: Double = 0,
        longitude: Double = 0,
        lastInspectionDate: Date? = nil,
        notes: String = "",
        correlationId: String = ""
    ) {
        self.buildingId = buildingId
        self.apn = apn
        self.address = address
        self.name = name
        self.constructionType = constructionType
        self.hazusType = hazusType
        self.occupancyGroup = occupancyGroup
        self.floorCount = floorCount
        self.totalSqFt = totalSqFt
        self.yearBuilt = yearBuilt
        self.heatingFuelType = heatingFuelType
        self.foundationType = foundationType
        self.hasElevator = hasElevator
        self.hasSprinklerSystem = hasSprinklerSystem
        self.sprinklerType = sprinklerType
        self.hasFireAlarm = hasFireAlarm
        self.alarmType = alarmType
        self.monitoringType = monitoringType
        self.latitude = latitude
        self.longitude = longitude
        self.lastInspectionDate = lastInspectionDate
        self.notes = notes
        self.correlationId = correlationId
    }
}

/// Individual room within a building. Captures occupant load (IBC Table 1004.5),
/// concealment options, and life-safety devices.
struct RoomData: Codable, Hashable, Identifiable {
    var id: String { roomId }
    var roomId: String
    var buildingId: String
    var floorLevel: Int
    var roomType: String
    var areaSqFt: Double
    var occupantLoad: Int
    var concealmentOptions: [String]
    var hasWindow: Bool
    var windowCount: Int
    var hasSecondaryEscape: Bool
    var doorCount: Int
    var hasSprinkler: Bool
    var hasSmokeDetector: Bool
    var hasCODetector: Bool

    init(
        roomId: String = UUID().uuidString,
        buildingId: String = "",
        floorLevel: Int = 0,
        roomType: String = "",
        areaSqFt: Double = 0,
        occupantLoad: Int = 0,
        concealmentOptions: [String] = [],
        hasWindow: Bool = false,
        windowCount: Int = 0,
        hasSecondaryEscape: Bool = false,
        doorCount: Int = 1,
        hasSprinkler: Bool = false,
        hasSmokeDetector: Bool = false,
        hasCODetector: Bool = false
    ) {
        self.roomId = roomId
        self.buildingId = buildingId
        self.floorLevel = floorLevel
        self.roomType = roomType
        self.areaSqFt = areaSqFt
        self.occupantLoad = occupantLoad
        self.concealmentOptions = concealmentOptions
        self.hasWindow = hasWindow
        self.windowCount = windowCount
        self.hasSecondaryEscape = hasSecondaryEscape
        self.doorCount = doorCount
        self.hasSprinkler = hasSprinkler
        self.hasSmokeDetector = hasSmokeDetector
        self.hasCODetector = hasCODetector
    }
}

/// Fire protection system summary for a building.
struct FireProtectionSystem: Codable, Hashable, Identifiable {
    var id: String { systemId }
    var systemId: String
    var buildingId: String
    var sprinklerType: SprinklerType
    var sprinklerCoverage: String
    var alarmType: AlarmType
    var monitoringType: MonitoringType
    var hasStandpipe: Bool
    var hasFireExtinguishers: Bool
    var hasEmergencyLighting: Bool
    var hasExitSigns: Bool
    var lastTestDate: Date?
    var nextTestDue: Date?
    var isCompliant: Bool

    init(
        systemId: String = UUID().uuidString,
        buildingId: String = "",
        sprinklerType: SprinklerType = .none,
        sprinklerCoverage: String = "None",
        alarmType: AlarmType = .none,
        monitoringType: MonitoringType = .none,
        hasStandpipe: Bool = false,
        hasFireExtinguishers: Bool = false,
        hasEmergencyLighting: Bool = false,
        hasExitSigns: Bool = false,
        lastTestDate: Date? = nil,
        nextTestDue: Date? = nil,
        isCompliant: Bool = false
    ) {
        self.systemId = systemId
        self.buildingId = buildingId
        self.sprinklerType = sprinklerType
        self.sprinklerCoverage = sprinklerCoverage
        self.alarmType = alarmType
        self.monitoringType = monitoringType
        self.hasStandpipe = hasStandpipe
        self.hasFireExtinguishers = hasFireExtinguishers
        self.hasEmergencyLighting = hasEmergencyLighting
        self.hasExitSigns = hasExitSigns
        self.lastTestDate = lastTestDate
        self.nextTestDue = nextTestDue
        self.isCompliant = isCompliant
    }
}

/// Active or historical hazard condition at a building.
struct HazardCondition: Codable, Hashable, Identifiable {
    var id: String { hazardId }
    var hazardId: String
    var buildingId: String
    var hazardType: String
    var fireCause: FireCause?
    var heatSource: String?
    var areaOfOrigin: String?
    var severity: String
    var isActive: Bool
    var detectedAt: Date
    var resolvedAt: Date?
    var description: String
    var affectedFloors: [Int]
    var affectedRoomIds: [String]
    var correlationId: String

    init(
        hazardId: String = UUID().uuidString,
        buildingId: String = "",
        hazardType: String = "",
        fireCause: FireCause? = nil,
        heatSource: String? = nil,
        areaOfOrigin: String? = nil,
        severity: String = "Low",
        isActive: Bool = true,
        detectedAt: Date = Date(),
        resolvedAt: Date? = nil,
        description: String = "",
        affectedFloors: [Int] = [],
        affectedRoomIds: [String] = [],
        correlationId: String = ""
    ) {
        self.hazardId = hazardId
        self.buildingId = buildingId
        self.hazardType = hazardType
        self.fireCause = fireCause
        self.heatSource = heatSource
        self.areaOfOrigin = areaOfOrigin
        self.severity = severity
        self.isActive = isActive
        self.detectedAt = detectedAt
        self.resolvedAt = resolvedAt
        self.description = description
        self.affectedFloors = affectedFloors
        self.affectedRoomIds = affectedRoomIds
        self.correlationId = correlationId
    }
}

/// Weather alert from NWS/IPAWS via Common Alerting Protocol (CAP 1.2).
struct WeatherAlert: Codable, Hashable, Identifiable {
    var id: String { alertId }
    var alertId: String
    var eventType: String
    var headline: String
    var description: String
    var severity: CAPSeverity
    var urgency: CAPUrgency
    var certainty: CAPCertainty
    var effective: Date
    var expires: Date
    var areaDescription: String
    var affectedZones: [String]
    var latitude: Double
    var longitude: Double
    var radiusMeters: Double?
    var source: String

    init(
        alertId: String = UUID().uuidString,
        eventType: String = "",
        headline: String = "",
        description: String = "",
        severity: CAPSeverity = .unknown,
        urgency: CAPUrgency = .unknown,
        certainty: CAPCertainty = .unknown,
        effective: Date = Date(),
        expires: Date = Date(),
        areaDescription: String = "",
        affectedZones: [String] = [],
        latitude: Double = 0,
        longitude: Double = 0,
        radiusMeters: Double? = nil,
        source: String = ""
    ) {
        self.alertId = alertId
        self.eventType = eventType
        self.headline = headline
        self.description = description
        self.severity = severity
        self.urgency = urgency
        self.certainty = certainty
        self.effective = effective
        self.expires = expires
        self.areaDescription = areaDescription
        self.affectedZones = affectedZones
        self.latitude = latitude
        self.longitude = longitude
        self.radiusMeters = radiusMeters
        self.source = source
    }
}

/// Hazardous material information per ERG 2024 (Emergency Response Guidebook).
struct HazmatInfo: Codable, Hashable, Identifiable {
    var id: String { unNumber }
    var unNumber: String
    var properShippingName: String
    var hazardClass: String
    var subsidiaryRisk: String?
    var ergGuideNumber: Int
    var smallSpillEvacMeters: Double
    var largeSpillEvacMeters: Double
    var dayInitialIsolationMeters: Double
    var nightInitialIsolationMeters: Double
    var dayProtectiveDistanceKm: Double
    var nightProtectiveDistanceKm: Double
    var isWaterReactive: Bool
    var isToxicByInhalation: Bool

    init(
        unNumber: String = "",
        properShippingName: String = "",
        hazardClass: String = "",
        subsidiaryRisk: String? = nil,
        ergGuideNumber: Int = 0,
        smallSpillEvacMeters: Double = ERG2024Defaults.defaultSmallSpillEvacMeters,
        largeSpillEvacMeters: Double = ERG2024Defaults.defaultLargeSpillEvacMeters,
        dayInitialIsolationMeters: Double = 0,
        nightInitialIsolationMeters: Double = 0,
        dayProtectiveDistanceKm: Double = 0,
        nightProtectiveDistanceKm: Double = 0,
        isWaterReactive: Bool = false,
        isToxicByInhalation: Bool = false
    ) {
        self.unNumber = unNumber
        self.properShippingName = properShippingName
        self.hazardClass = hazardClass
        self.subsidiaryRisk = subsidiaryRisk
        self.ergGuideNumber = ergGuideNumber
        self.smallSpillEvacMeters = smallSpillEvacMeters
        self.largeSpillEvacMeters = largeSpillEvacMeters
        self.dayInitialIsolationMeters = dayInitialIsolationMeters
        self.nightInitialIsolationMeters = nightInitialIsolationMeters
        self.dayProtectiveDistanceKm = dayProtectiveDistanceKm
        self.nightProtectiveDistanceKm = nightProtectiveDistanceKm
        self.isWaterReactive = isWaterReactive
        self.isToxicByInhalation = isToxicByInhalation
    }
}

/// Seismic alert from ShakeAlert EEW or USGS ShakeMap.
struct SeismicAlert: Codable, Hashable, Identifiable {
    var id: String { alertId }
    var alertId: String
    var magnitude: Double
    var mmi: SeismicMMI
    var epicenterLatitude: Double
    var epicenterLongitude: Double
    var depthKm: Double
    var estimatedArrivalSeconds: Int?
    var expectedDamageLevel: String
    var timestamp: Date
    var source: String

    init(
        alertId: String = UUID().uuidString,
        magnitude: Double = 0,
        mmi: SeismicMMI = .i,
        epicenterLatitude: Double = 0,
        epicenterLongitude: Double = 0,
        depthKm: Double = 0,
        estimatedArrivalSeconds: Int? = nil,
        expectedDamageLevel: String = "None",
        timestamp: Date = Date(),
        source: String = "USGS"
    ) {
        self.alertId = alertId
        self.magnitude = magnitude
        self.mmi = mmi
        self.epicenterLatitude = epicenterLatitude
        self.epicenterLongitude = epicenterLongitude
        self.depthKm = depthKm
        self.estimatedArrivalSeconds = estimatedArrivalSeconds
        self.expectedDamageLevel = expectedDamageLevel
        self.timestamp = timestamp
        self.source = source
    }
}

// MARK: - ERG 2024 Defaults

/// ERG 2024 default evacuation distances (USDOT/PHMSA).
/// Used when material-specific data is unavailable.
enum ERG2024Defaults {
    static let defaultSmallSpillEvacMeters: Double = 30
    static let defaultLargeSpillEvacMeters: Double = 100
    static let waterReactiveEvacMeters: Double = 250
    static let tihDayEvacKm: Double = 0.5
    static let tihNightEvacKm: Double = 1.1
}
