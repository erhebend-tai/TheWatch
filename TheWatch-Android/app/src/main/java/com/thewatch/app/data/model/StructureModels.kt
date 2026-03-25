// StructureModels.kt — Android domain models for building data, hazard conditions,
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
//   val building = BuildingData(
//       buildingId = UUID.randomUUID().toString(),
//       apn = "123-456-789",
//       address = "100 Main St, Springfield, IL 62701",
//       constructionType = ConstructionType.TypeV_WoodFrame,
//       hazusType = HAZUSBuildingType.W1,
//       occupancyGroup = OccupancyGroup.R_Residential,
//       floorCount = 2,
//       totalSqFt = 2400.0,
//       heatingFuelType = HeatingFuelType.NaturalGas,
//       foundationType = FoundationType.Basement
//   )

package com.thewatch.app.data.model

// ═══════════════════════════════════════════════════════════════
// Enums — Structure & Hazard Domain
// ═══════════════════════════════════════════════════════════════

/**
 * IBC Chapter 6 construction classifications.
 * Determines fire-resistance ratings of structural elements.
 */
enum class ConstructionType {
    /** Type I — Fire Resistive. Noncombustible, 3-4 hr rating. High-rises, hospitals. */
    TypeI_FireResistive,
    /** Type II — Non-Combustible. Noncombustible, 0-2 hr rating. Warehouses, commercial. */
    TypeII_NonCombustible,
    /** Type III — Ordinary. Noncombustible exterior, combustible interior. Mixed-use. */
    TypeIII_Ordinary,
    /** Type IV — Heavy Timber. Large-dimension lumber, min 8" columns. Mills, churches. */
    TypeIV_HeavyTimber,
    /** Type V — Wood Frame. Most common residential. Stick-built homes. */
    TypeV_WoodFrame
}

/**
 * FEMA HAZUS-MH building types for multi-hazard loss estimation.
 * Encodes structural system + height class (L=Low 1-3, M=Mid 4-7, H=High 8+).
 */
enum class HAZUSBuildingType {
    /** Wood, Light-Frame (≤5,000 sqft). Single/multi-family residential. */
    W1,
    /** Wood, Commercial/Industrial (>5,000 sqft). */
    W2,
    /** Steel Moment Frame, Low-Rise (1-3 stories). */
    S1L,
    /** Steel Moment Frame, Mid-Rise (4-7 stories). */
    S1M,
    /** Steel Moment Frame, High-Rise (8+ stories). */
    S1H,
    /** Steel Braced Frame, Low-Rise (1-3 stories). */
    S2L,
    /** Steel Braced Frame, Mid-Rise (4-7 stories). */
    S2M,
    /** Steel Braced Frame, High-Rise (8+ stories). */
    S2H,
    /** Concrete Moment Frame, Low-Rise (1-3 stories). */
    C1L,
    /** Concrete Moment Frame, Mid-Rise (4-7 stories). */
    C1M,
    /** Concrete Moment Frame, High-Rise (8+ stories). */
    C1H,
    /** Concrete Shear Wall, Low-Rise (1-3 stories). */
    C2L,
    /** Concrete Shear Wall, Mid-Rise (4-7 stories). */
    C2M,
    /** Concrete Shear Wall, High-Rise (8+ stories). */
    C2H,
    /** Concrete Frame with URM Infill, Low-Rise (1-3 stories). */
    C3L,
    /** Concrete Frame with URM Infill, Mid-Rise (4-7 stories). */
    C3M,
    /** Concrete Frame with URM Infill, High-Rise (8+ stories). */
    C3H,
    /** Precast Concrete Tilt-Up Walls. Common warehouse/big-box retail. */
    PC1,
    /** Precast Concrete Frame with Shear Walls, Low-Rise (1-3 stories). */
    PC2L,
    /** Precast Concrete Frame with Shear Walls, Mid-Rise (4-7 stories). */
    PC2M,
    /** Precast Concrete Frame with Shear Walls, High-Rise (8+ stories). */
    PC2H,
    /** Reinforced Masonry, Wood/Metal Diaphragm, Low-Rise (1-3 stories). */
    RM1L,
    /** Reinforced Masonry, Wood/Metal Diaphragm, Mid-Rise (4+ stories). */
    RM1M,
    /** Reinforced Masonry, Concrete Diaphragm, Low-Rise (1-3 stories). */
    RM2L,
    /** Reinforced Masonry, Concrete Diaphragm, Mid-Rise (4-7 stories). */
    RM2M,
    /** Reinforced Masonry, Concrete Diaphragm, High-Rise (8+ stories). */
    RM2H,
    /** Unreinforced Masonry, Low-Rise (1-2 stories). High seismic vulnerability. */
    URML,
    /** Unreinforced Masonry, Mid-Rise (3+ stories). Highest seismic vulnerability. */
    URMM,
    /** Manufactured Housing (mobile/modular). High wind vulnerability. */
    MH
}

/**
 * IBC Chapter 3 occupancy group classifications.
 */
enum class OccupancyGroup {
    /** Group A — Assembly. Theaters, stadiums, churches, restaurants (≥50 occupants). */
    A_Assembly,
    /** Group B — Business. Offices, banks, outpatient clinics. */
    B_Business,
    /** Group E — Educational. K-12 schools with ≥6 persons. */
    E_Educational,
    /** Group F — Factory/Industrial. Manufacturing, assembly, repair. */
    F_Factory,
    /** Group H — High-Hazard. Explosive, flammable, toxic, or reactive materials. */
    H_HighHazard,
    /** Group I — Institutional. Hospitals, jails, nursing homes. */
    I_Institutional,
    /** Group M — Mercantile. Retail stores, markets, gas stations. */
    M_Mercantile,
    /** Group R — Residential. Hotels, apartments, dwellings. */
    R_Residential,
    /** Group S — Storage. Warehouses, parking garages, hangars. */
    S_Storage,
    /** Group U — Utility/Miscellaneous. Barns, greenhouses, towers. */
    U_Utility
}

/**
 * NFPA sprinkler system classifications (13, 13R, 13D).
 */
enum class SprinklerType {
    /** NFPA 13 — Full commercial sprinkler system. */
    NFPA13_Commercial,
    /** NFPA 13R — Residential system for buildings up to 4 stories. */
    NFPA13R_Residential,
    /** NFPA 13D — Dwelling system for 1- and 2-family homes. */
    NFPA13D_Dwelling,
    /** No sprinkler system installed. */
    None
}

/**
 * NFPA 72 fire alarm and signaling code device/system types.
 */
enum class AlarmType {
    /** Initiating Device — smoke/heat detectors, pull stations, waterflow switches. */
    NFPA72_Initiating,
    /** Notification Appliance — horns, strobes, speakers. */
    NFPA72_Notification,
    /** Supervisory Signal — monitors valve positions, pump status. */
    NFPA72_Supervisory,
    /** Local alarm only — on-site, no off-premise notification. */
    LocalOnly,
    /** Central Station — UL-listed 24/7 monitoring, dispatches fire dept. */
    CentralStation,
    /** Proprietary Station — owner's on-site control room. */
    ProprietaryStation,
    /** Remote Station — signals to fire dept or answering service. */
    RemoteStation,
    /** No fire alarm system installed. */
    None
}

/**
 * Monitoring service type for alarm/security systems.
 */
enum class MonitoringType {
    /** Self-monitored — alerts via app/SMS, no professional dispatch. */
    SelfMonitored,
    /** Central Station — UL-listed 24/7 facility dispatches emergency services. */
    CentralStation,
    /** Proprietary Station — owner-operated monitoring room on premises. */
    ProprietaryStation,
    /** Remote Station — signals routed directly to dispatch. */
    RemoteStation,
    /** No monitoring service. */
    None
}

/**
 * Primary heating fuel type. Critical for explosion risk assessment.
 */
enum class HeatingFuelType {
    /** Natural gas (CH4). Lighter than air. LEL 5%, UEL 15%. */
    NaturalGas,
    /** Propane (C3H8). Heavier than air, pools in basements. LEL 2.1%, UEL 9.5%. */
    Propane,
    /** Heating oil (#2 fuel oil). Flash point ~140F. */
    Oil,
    /** Electric heating. No combustion fuel. Lowest explosion risk. */
    Electric,
    /** Wood-burning. Creosote/chimney fire risk. */
    Wood,
    /** Coal. CO and dust explosion risk. */
    Coal,
    /** Solar thermal. No combustion fuel. */
    Solar,
    /** No heating system. */
    None
}

/**
 * Foundation type. Critical for flood vulnerability assessment.
 */
enum class FoundationType {
    /** Slab-on-grade. Moderate flood vulnerability. */
    Slab,
    /** Crawl space (vented or unvented). Moderate flood vulnerability, mold risk. */
    CrawlSpace,
    /** Basement. Highest flood vulnerability. */
    Basement,
    /** Piers and posts (elevated). Best flood resilience above BFE. */
    PiersAndPosts,
    /** Deep foundation (piles, caissons). Good flood resilience. */
    DeepFoundation
}

/**
 * NFIRS 5.0 fire cause categories.
 */
enum class FireCause {
    /** Cooking — leading cause of residential fires. */
    Cooking,
    /** Heating equipment — space heaters, furnaces, chimneys. */
    Heating,
    /** Electrical — wiring, outlets, circuit breakers. */
    Electrical,
    /** Smoking materials — leading cause of fire deaths. */
    Smoking,
    /** Intentional/Arson (NFIRS cause=1). */
    Intentional,
    /** Appliance malfunction. */
    Appliance,
    /** Natural causes — lightning, spontaneous combustion, wildfire. */
    NaturalCauses,
    /** Unknown/Under investigation. */
    Unknown
}

/**
 * CAP 1.2 severity levels (NWS/IPAWS).
 */
enum class CAPSeverity {
    /** Extraordinary threat to life or property. */
    Extreme,
    /** Significant threat to life or property. */
    Severe,
    /** Possible threat to life or property. */
    Moderate,
    /** Minimal to no known threat. */
    Minor,
    /** Severity not yet determined. */
    Unknown
}

/**
 * CAP 1.2 urgency levels.
 */
enum class CAPUrgency {
    /** Responsive action SHOULD be taken immediately. */
    Immediate,
    /** Responsive action SHOULD be taken soon (within next hour). */
    Expected,
    /** Responsive action SHOULD be taken in the near future. */
    Future,
    /** Responsive action no longer required. */
    Past,
    /** Urgency not yet determined. */
    Unknown
}

/**
 * CAP 1.2 certainty levels.
 */
enum class CAPCertainty {
    /** Determined to have occurred or to be ongoing. */
    Observed,
    /** Probability > ~50%. */
    Likely,
    /** Probability <= ~50%. */
    Possible,
    /** Probability is minimal. */
    Unlikely,
    /** Certainty not yet determined. */
    Unknown
}

/**
 * Modified Mercalli Intensity (MMI) scale. Describes perceived shaking at a location.
 * Used by ShakeAlert EEW and USGS ShakeMap.
 */
enum class SeismicMMI {
    /** I — Not felt. Recorded by instruments only. */
    I,
    /** II — Weak. Felt at rest on upper floors. */
    II,
    /** III — Weak. Felt indoors, hanging objects swing. */
    III,
    /** IV — Light. Dishes/windows rattle. */
    IV,
    /** V — Moderate. Small objects displaced. */
    V,
    /** VI — Strong. Furniture moves, slight damage. */
    VI,
    /** VII — Very Strong. Considerable damage to poor construction. */
    VII,
    /** VIII — Severe. Heavy damage to ordinary buildings. */
    VIII,
    /** IX — Violent. Heavy damage, structures shifted off foundations. */
    IX,
    /** X — Extreme. Most structures destroyed, ground cracked. */
    X
}

// ═══════════════════════════════════════════════════════════════
// Data Models — Structure & Hazard Domain
// ═══════════════════════════════════════════════════════════════

/**
 * Core building/structure record. Captures construction classification, fire protection,
 * occupancy, and geolocation.
 *
 * Example:
 *   val building = BuildingData(
 *       buildingId = UUID.randomUUID().toString(),
 *       apn = "123-456-789",
 *       address = "100 Main St, Springfield, IL 62701",
 *       constructionType = ConstructionType.TypeV_WoodFrame,
 *       hazusType = HAZUSBuildingType.W1,
 *       occupancyGroup = OccupancyGroup.R_Residential
 *   )
 */
data class BuildingData(
    val buildingId: String = "",
    val apn: String = "",
    val address: String = "",
    val name: String = "",
    val constructionType: ConstructionType = ConstructionType.TypeV_WoodFrame,
    val hazusType: HAZUSBuildingType = HAZUSBuildingType.W1,
    val occupancyGroup: OccupancyGroup = OccupancyGroup.R_Residential,
    val floorCount: Int = 1,
    val totalSqFt: Double = 0.0,
    val yearBuilt: Int? = null,
    val heatingFuelType: HeatingFuelType = HeatingFuelType.None,
    val foundationType: FoundationType = FoundationType.Slab,
    val hasElevator: Boolean = false,
    val hasSprinklerSystem: Boolean = false,
    val sprinklerType: SprinklerType = SprinklerType.None,
    val hasFireAlarm: Boolean = false,
    val alarmType: AlarmType = AlarmType.None,
    val monitoringType: MonitoringType = MonitoringType.None,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val lastInspectionDate: String? = null,
    val notes: String = "",
    val correlationId: String = ""
)

/**
 * Individual room within a building. Captures occupant load (IBC Table 1004.5),
 * concealment options for shelter-in-place, and life-safety devices.
 */
data class RoomData(
    val roomId: String = "",
    val buildingId: String = "",
    val floorLevel: Int = 0,
    val roomType: String = "",
    val areaSqFt: Double = 0.0,
    val occupantLoad: Int = 0,
    val concealmentOptions: List<String> = emptyList(),
    val hasWindow: Boolean = false,
    val windowCount: Int = 0,
    val hasSecondaryEscape: Boolean = false,
    val doorCount: Int = 1,
    val hasSprinkler: Boolean = false,
    val hasSmokeDetector: Boolean = false,
    val hasCODetector: Boolean = false
)

/**
 * Fire protection system summary for a building. Aggregates sprinkler, alarm,
 * monitoring, standpipe, and emergency lighting/exit sign compliance.
 */
data class FireProtectionSystem(
    val systemId: String = "",
    val buildingId: String = "",
    val sprinklerType: SprinklerType = SprinklerType.None,
    val sprinklerCoverage: String = "None",
    val alarmType: AlarmType = AlarmType.None,
    val monitoringType: MonitoringType = MonitoringType.None,
    val hasStandpipe: Boolean = false,
    val hasFireExtinguishers: Boolean = false,
    val hasEmergencyLighting: Boolean = false,
    val hasExitSigns: Boolean = false,
    val lastTestDate: String? = null,
    val nextTestDue: String? = null,
    val isCompliant: Boolean = false
)

/**
 * Active or historical hazard condition at a building.
 * Covers fire, flood, hazmat, structural, weather, and seismic hazards.
 *
 * Example:
 *   val hazard = HazardCondition(
 *       hazardId = UUID.randomUUID().toString(),
 *       buildingId = "bld-001",
 *       hazardType = "Fire",
 *       fireCause = FireCause.Cooking,
 *       severity = "High",
 *       isActive = true
 *   )
 */
data class HazardCondition(
    val hazardId: String = "",
    val buildingId: String = "",
    val hazardType: String = "",
    val fireCause: FireCause? = null,
    val heatSource: String? = null,
    val areaOfOrigin: String? = null,
    val severity: String = "Low",
    val isActive: Boolean = true,
    val detectedAt: String = "",
    val resolvedAt: String? = null,
    val description: String = "",
    val affectedFloors: List<Int> = emptyList(),
    val affectedRoomIds: List<String> = emptyList(),
    val correlationId: String = ""
)

/**
 * Weather alert from NWS/IPAWS via Common Alerting Protocol (CAP 1.2).
 * Includes severity/urgency/certainty triplet for decision support.
 */
data class WeatherAlert(
    val alertId: String = "",
    val eventType: String = "",
    val headline: String = "",
    val description: String = "",
    val severity: CAPSeverity = CAPSeverity.Unknown,
    val urgency: CAPUrgency = CAPUrgency.Unknown,
    val certainty: CAPCertainty = CAPCertainty.Unknown,
    val effective: String = "",
    val expires: String = "",
    val areaDescription: String = "",
    val affectedZones: List<String> = emptyList(),
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val radiusMeters: Double? = null,
    val source: String = ""
)

/**
 * Hazardous material information per ERG 2024 (Emergency Response Guidebook, USDOT/PHMSA).
 * Provides evacuation distances for small/large spills and TIH scenarios.
 *
 * Example:
 *   val chlorine = HazmatInfo(
 *       unNumber = "1017",
 *       properShippingName = "Chlorine",
 *       hazardClass = "2.3",
 *       ergGuideNumber = 124,
 *       isToxicByInhalation = true
 *   )
 */
data class HazmatInfo(
    val unNumber: String = "",
    val properShippingName: String = "",
    val hazardClass: String = "",
    val subsidiaryRisk: String? = null,
    val ergGuideNumber: Int = 0,
    val smallSpillEvacMeters: Double = ERG2024Defaults.DEFAULT_SMALL_SPILL_EVAC_METERS,
    val largeSpillEvacMeters: Double = ERG2024Defaults.DEFAULT_LARGE_SPILL_EVAC_METERS,
    val dayInitialIsolationMeters: Double = 0.0,
    val nightInitialIsolationMeters: Double = 0.0,
    val dayProtectiveDistanceKm: Double = 0.0,
    val nightProtectiveDistanceKm: Double = 0.0,
    val isWaterReactive: Boolean = false,
    val isToxicByInhalation: Boolean = false
)

/**
 * Seismic alert from ShakeAlert EEW or USGS ShakeMap.
 */
data class SeismicAlert(
    val alertId: String = "",
    val magnitude: Double = 0.0,
    val mmi: SeismicMMI = SeismicMMI.I,
    val epicenterLatitude: Double = 0.0,
    val epicenterLongitude: Double = 0.0,
    val depthKm: Double = 0.0,
    val estimatedArrivalSeconds: Int? = null,
    val expectedDamageLevel: String = "None",
    val timestamp: String = "",
    val source: String = "USGS"
)

// ═══════════════════════════════════════════════════════════════
// ERG 2024 Defaults
// ═══════════════════════════════════════════════════════════════

/**
 * ERG 2024 default evacuation distances (USDOT/PHMSA).
 * Used when material-specific data is unavailable.
 */
object ERG2024Defaults {
    /** Default small spill initial isolation distance (meters). */
    const val DEFAULT_SMALL_SPILL_EVAC_METERS: Double = 30.0
    /** Default large spill initial isolation distance (meters). */
    const val DEFAULT_LARGE_SPILL_EVAC_METERS: Double = 100.0
    /** Water-reactive material evacuation distance (meters). */
    const val WATER_REACTIVE_EVAC_METERS: Double = 250.0
    /** Toxic-by-Inhalation daytime protective action distance (km). */
    const val TIH_DAY_EVAC_KM: Double = 0.5
    /** Toxic-by-Inhalation nighttime protective action distance (km). Larger due to atmospheric stability. */
    const val TIH_NIGHT_EVAC_KM: Double = 1.1
}
