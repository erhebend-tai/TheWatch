// StructureModels.cs — Domain models for building data, room inventory, fire protection,
// hazard conditions, weather alerts, hazmat lookup, and seismic alerts.
//
// Architecture:
//   These models are the core data contracts for the Structure & Hazard domain.
//   They flow through IStructurePort (the domain port) and are persisted by adapters
//   (CosmosDB, SQL Server, PostgreSQL, Mock). Mobile clients (iOS/Android) maintain
//   mirror models in Swift/Kotlin for offline-first capability.
//
//   ┌──────────────┐     ┌──────────────────┐     ┌────────────────────────┐
//   │ Mobile Client │────▶│ Dashboard.Api    │────▶│ IStructurePort         │
//   │ (iOS/Android) │     │ Controllers      │     │ (GetBuildingAsync, etc)│
//   └──────────────┘     └──────────────────┘     └────────────────────────┘
//                                                          │
//                                                  ┌───────┴───────┐
//                                                  │ Adapter Layer  │
//                                                  │ (SQL, Cosmos,  │
//                                                  │  Mock, etc.)   │
//                                                  └───────────────┘
//
// Standards referenced:
//   IBC — International Building Code (occupant load, construction type)
//   NFPA 13/13R/13D — Sprinkler system classifications
//   NFPA 72 — Fire alarm system types and monitoring
//   NFIRS 5.0 — National Fire Incident Reporting System (fire cause, area of origin)
//   CAP 1.2 — Common Alerting Protocol (weather alert severity/urgency/certainty)
//   ERG 2024 — Emergency Response Guidebook (hazmat evacuation distances)
//   ShakeAlert/USGS — Earthquake early warning (MMI, EEW arrival time)
//   HAZUS-MH — FEMA building classification for multi-hazard loss estimation
//
// Example — creating a building record:
//   var building = new BuildingData
//   {
//       BuildingId = Guid.NewGuid().ToString(),
//       APN = "123-456-789",
//       Address = "100 Main St, Springfield, IL 62701",
//       ConstructionType = ConstructionType.TypeV_WoodFrame,
//       HAZUSType = HAZUSBuildingType.W1,
//       OccupancyGroup = OccupancyGroup.R_Residential,
//       FloorCount = 2,
//       TotalSqFt = 2400,
//       YearBuilt = 1985,
//       HeatingFuelType = HeatingFuelType.NaturalGas,
//       FoundationType = FoundationType.Basement,
//       HasSprinklerSystem = false,
//       SprinklerType = SprinklerType.None,
//       HasFireAlarm = true,
//       AlarmType = AlarmType.LocalOnly,
//       MonitoringType = MonitoringType.SelfMonitored,
//       Latitude = 39.7817,
//       Longitude = -89.6501
//   };
//
// Example — reporting a hazard:
//   var hazard = new HazardCondition
//   {
//       HazardId = Guid.NewGuid().ToString(),
//       BuildingId = building.BuildingId,
//       HazardType = "Fire",
//       FireCause = FireCause.Cooking,
//       Severity = "High",
//       IsActive = true,
//       DetectedAt = DateTime.UtcNow,
//       AffectedFloors = new List<int> { 1 },
//       CorrelationId = sosRequest.RequestId
//   };
//
// Example — ERG 2024 hazmat lookup:
//   var chlorine = new HazmatInfo
//   {
//       UnNumber = "1017",
//       ProperShippingName = "Chlorine",
//       HazardClass = "2.3",
//       ERGGuideNumber = 124,
//       SmallSpillEvacMeters = 100,
//       LargeSpillEvacMeters = 800,
//       IsToxicByInhalation = true
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Core building/structure record. Captures construction classification, fire protection status,
/// occupancy, and geolocation. Keyed by BuildingId; APN provides assessor-level lookup.
/// </summary>
public class BuildingData
{
    /// <summary>Unique identifier for this building record.</summary>
    public string BuildingId { get; set; } = string.Empty;

    /// <summary>Assessor Parcel Number — county-assigned land parcel identifier (e.g., "123-456-789").</summary>
    public string APN { get; set; } = string.Empty;

    /// <summary>Full street address including city, state, ZIP.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Building name or label (e.g., "Riverside Elementary", "Tower B").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>IBC Chapter 6 construction type (Type I–V). Determines fire-resistance rating.</summary>
    public ConstructionType ConstructionType { get; set; } = ConstructionType.TypeV_WoodFrame;

    /// <summary>FEMA HAZUS-MH structural classification for multi-hazard loss estimation.</summary>
    public HAZUSBuildingType HAZUSType { get; set; } = HAZUSBuildingType.W1;

    /// <summary>IBC Chapter 3 occupancy group. Governs egress, sprinkler, and alarm requirements.</summary>
    public OccupancyGroup OccupancyGroup { get; set; } = OccupancyGroup.R_Residential;

    /// <summary>Number of stories above grade.</summary>
    public int FloorCount { get; set; }

    /// <summary>Total building area in square feet.</summary>
    public double TotalSqFt { get; set; }

    /// <summary>Year the building was constructed. Null if unknown.</summary>
    public int? YearBuilt { get; set; }

    /// <summary>Primary heating fuel type. Used for explosion risk computation.</summary>
    public HeatingFuelType HeatingFuelType { get; set; } = HeatingFuelType.None;

    /// <summary>Foundation type. Used for flood vulnerability computation.</summary>
    public FoundationType FoundationType { get; set; } = FoundationType.Slab;

    /// <summary>Whether the building has an elevator (affects evacuation planning).</summary>
    public bool HasElevator { get; set; }

    /// <summary>Whether any sprinkler system is installed.</summary>
    public bool HasSprinklerSystem { get; set; }

    /// <summary>NFPA sprinkler classification (13, 13R, 13D, or None).</summary>
    public SprinklerType SprinklerType { get; set; } = SprinklerType.None;

    /// <summary>Whether any fire alarm system is installed.</summary>
    public bool HasFireAlarm { get; set; }

    /// <summary>NFPA 72 alarm system classification.</summary>
    public AlarmType AlarmType { get; set; } = AlarmType.None;

    /// <summary>How the alarm/security system is monitored.</summary>
    public MonitoringType MonitoringType { get; set; } = MonitoringType.None;

    /// <summary>WGS84 latitude of the building centroid.</summary>
    public double Latitude { get; set; }

    /// <summary>WGS84 longitude of the building centroid.</summary>
    public double Longitude { get; set; }

    /// <summary>Date of last fire/safety inspection. Null if never inspected or unknown.</summary>
    public DateTime? LastInspectionDate { get; set; }

    /// <summary>Free-text notes (access issues, hazards, special considerations).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Correlation ID linking this record to an SOS/emergency flow.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Individual room within a building. Captures occupant load (per IBC Table 1004.5),
/// concealment options (for active-shooter/shelter-in-place), and life-safety devices.
/// </summary>
public class RoomData
{
    /// <summary>Unique identifier for this room.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Parent building identifier.</summary>
    public string BuildingId { get; set; } = string.Empty;

    /// <summary>Floor level (0 = ground, negative = below grade, positive = above grade).</summary>
    public int FloorLevel { get; set; }

    /// <summary>Room type description (e.g., "Bedroom", "Kitchen", "Office", "Classroom", "Server Room").</summary>
    public string RoomType { get; set; } = string.Empty;

    /// <summary>Room area in square feet.</summary>
    public double AreaSqFt { get; set; }

    /// <summary>Maximum occupant load per IBC Table 1004.5 (area / occupant load factor).</summary>
    public int OccupantLoad { get; set; }

    /// <summary>
    /// Concealment options available in this room for shelter-in-place scenarios.
    /// Examples: "Closet", "Under desk", "Behind furniture", "Safe room".
    /// </summary>
    public List<string> ConcealmentOptions { get; set; } = new();

    /// <summary>Whether the room has at least one window.</summary>
    public bool HasWindow { get; set; }

    /// <summary>Number of windows (relevant for egress and ventilation).</summary>
    public int WindowCount { get; set; }

    /// <summary>Whether a secondary escape route exists (e.g., fire escape, second door).</summary>
    public bool HasSecondaryEscape { get; set; }

    /// <summary>Number of doors in the room.</summary>
    public int DoorCount { get; set; }

    /// <summary>Whether a sprinkler head is present in this room.</summary>
    public bool HasSprinkler { get; set; }

    /// <summary>Whether a smoke detector is present in this room.</summary>
    public bool HasSmokeDetector { get; set; }

    /// <summary>Whether a carbon monoxide detector is present in this room.</summary>
    public bool HasCODetector { get; set; }
}

/// <summary>
/// Fire protection system summary for a building. Aggregates sprinkler, alarm,
/// monitoring, standpipe, and emergency lighting/exit sign compliance.
/// </summary>
public class FireProtectionSystem
{
    /// <summary>Unique identifier for this fire protection record.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Parent building identifier.</summary>
    public string BuildingId { get; set; } = string.Empty;

    /// <summary>NFPA sprinkler classification.</summary>
    public SprinklerType SprinklerType { get; set; } = SprinklerType.None;

    /// <summary>Sprinkler coverage extent: "Full", "Partial", or "None".</summary>
    public string SprinklerCoverage { get; set; } = "None";

    /// <summary>NFPA 72 alarm system type.</summary>
    public AlarmType AlarmType { get; set; } = AlarmType.None;

    /// <summary>Alarm monitoring service type.</summary>
    public MonitoringType MonitoringType { get; set; } = MonitoringType.None;

    /// <summary>Whether the building has a standpipe system (Class I, II, or III).</summary>
    public bool HasStandpipe { get; set; }

    /// <summary>Whether portable fire extinguishers are present and maintained.</summary>
    public bool HasFireExtinguishers { get; set; }

    /// <summary>Whether emergency lighting is installed per IBC 1008 / NFPA 101.</summary>
    public bool HasEmergencyLighting { get; set; }

    /// <summary>Whether illuminated exit signs are installed per IBC 1013 / NFPA 101.</summary>
    public bool HasExitSigns { get; set; }

    /// <summary>Date of last system test/inspection.</summary>
    public DateTime? LastTestDate { get; set; }

    /// <summary>Next scheduled test/inspection date.</summary>
    public DateTime? NextTestDue { get; set; }

    /// <summary>Whether the system is currently compliant with applicable NFPA/IBC codes.</summary>
    public bool IsCompliant { get; set; }
}

/// <summary>
/// Active or historical hazard condition at a building. Covers fire, flood, hazmat,
/// structural, weather, and seismic hazards. Linked to SOS flow via CorrelationId.
/// </summary>
public class HazardCondition
{
    /// <summary>Unique identifier for this hazard record.</summary>
    public string HazardId { get; set; } = string.Empty;

    /// <summary>Building where the hazard is located.</summary>
    public string BuildingId { get; set; } = string.Empty;

    /// <summary>Hazard category: "Fire", "Flood", "Hazmat", "Structural", "Weather", "Seismic".</summary>
    public string HazardType { get; set; } = string.Empty;

    /// <summary>NFIRS 5.0 fire cause category. Null for non-fire hazards.</summary>
    public FireCause? FireCause { get; set; }

    /// <summary>Heat source description per NFIRS 5.0 (e.g., "Operating equipment"). Null for non-fire hazards.</summary>
    public string? HeatSource { get; set; }

    /// <summary>Area of origin per NFIRS 5.0 coding (e.g., "Kitchen", "Garage"). Null for non-fire hazards.</summary>
    public string? AreaOfOrigin { get; set; }

    /// <summary>Severity level: "Low", "Medium", "High", "Critical".</summary>
    public string Severity { get; set; } = "Low";

    /// <summary>Whether this hazard is currently active/ongoing.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp when the hazard was first detected or reported.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the hazard was resolved. Null if still active.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Free-text description of the hazard condition.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>List of floor levels affected by this hazard.</summary>
    public List<int> AffectedFloors { get; set; } = new();

    /// <summary>List of room IDs affected by this hazard.</summary>
    public List<string> AffectedRoomIds { get; set; } = new();

    /// <summary>Correlation ID linking this hazard to an SOS/emergency flow.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Weather alert from NWS/IPAWS via Common Alerting Protocol (CAP 1.2).
/// Includes severity/urgency/certainty triplet for decision support.
/// </summary>
public class WeatherAlert
{
    /// <summary>Unique alert identifier (typically NWS product ID).</summary>
    public string AlertId { get; set; } = string.Empty;

    /// <summary>NWS event type string (e.g., "Tornado Warning", "Flash Flood Watch", "Winter Storm Warning").</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Short headline (e.g., "Tornado Warning issued until 5:00 PM CDT").</summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>Full description/body text of the alert.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>CAP 1.2 severity classification.</summary>
    public CAPSeverity Severity { get; set; } = CAPSeverity.Unknown;

    /// <summary>CAP 1.2 urgency classification.</summary>
    public CAPUrgency Urgency { get; set; } = CAPUrgency.Unknown;

    /// <summary>CAP 1.2 certainty classification.</summary>
    public CAPCertainty Certainty { get; set; } = CAPCertainty.Unknown;

    /// <summary>UTC timestamp when the alert becomes effective.</summary>
    public DateTime Effective { get; set; }

    /// <summary>UTC timestamp when the alert expires.</summary>
    public DateTime Expires { get; set; }

    /// <summary>Human-readable description of the affected area (e.g., "Sangamon County, IL").</summary>
    public string AreaDescription { get; set; } = string.Empty;

    /// <summary>NWS forecast zone codes affected (e.g., "ILZ046", "ILZ047").</summary>
    public List<string> AffectedZones { get; set; } = new();

    /// <summary>Reference latitude for the alert area centroid.</summary>
    public double Latitude { get; set; }

    /// <summary>Reference longitude for the alert area centroid.</summary>
    public double Longitude { get; set; }

    /// <summary>Radius of the alert area in meters. Null if area is polygon-based.</summary>
    public double? RadiusMeters { get; set; }

    /// <summary>Originating source (e.g., "NWS Springfield IL", "NWS Storm Prediction Center").</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Hazardous material information per ERG 2024 (Emergency Response Guidebook, USDOT/PHMSA).
/// Provides evacuation distances for small/large spills and toxic-by-inhalation scenarios.
/// </summary>
public class HazmatInfo
{
    /// <summary>UN Number — 4-digit hazmat identifier (e.g., "1017" for Chlorine, "1203" for Gasoline).</summary>
    public string UnNumber { get; set; } = string.Empty;

    /// <summary>Proper shipping name per 49 CFR 172.101 (e.g., "Chlorine", "Gasoline").</summary>
    public string ProperShippingName { get; set; } = string.Empty;

    /// <summary>DOT hazard class (e.g., "2.3" = Toxic Gas, "3" = Flammable Liquid).</summary>
    public string HazardClass { get; set; } = string.Empty;

    /// <summary>Subsidiary risk if applicable (e.g., "5.1" = Oxidizer, "8" = Corrosive). Null if none.</summary>
    public string? SubsidiaryRisk { get; set; }

    /// <summary>ERG Guide Number — orange-bordered page in ERG 2024 (e.g., 124 for Chlorine).</summary>
    public int ERGGuideNumber { get; set; }

    /// <summary>Small spill initial isolation/evacuation distance in meters.</summary>
    public double SmallSpillEvacMeters { get; set; }

    /// <summary>Large spill initial isolation/evacuation distance in meters.</summary>
    public double LargeSpillEvacMeters { get; set; }

    /// <summary>Daytime initial isolation distance in meters (Table 1 — green pages).</summary>
    public double DayInitialIsolationMeters { get; set; }

    /// <summary>Nighttime initial isolation distance in meters (Table 1 — green pages).</summary>
    public double NightInitialIsolationMeters { get; set; }

    /// <summary>Daytime protective action distance in kilometers (Table 1 — green pages).</summary>
    public double DayProtectiveDistanceKm { get; set; }

    /// <summary>Nighttime protective action distance in kilometers (Table 1 — green pages).</summary>
    public double NightProtectiveDistanceKm { get; set; }

    /// <summary>Whether the material reacts dangerously with water (ERG Table 2 — blue pages).</summary>
    public bool IsWaterReactive { get; set; }

    /// <summary>Whether the material is Toxic by Inhalation (TIH) per 49 CFR 171.8. Requires larger evacuation zones.</summary>
    public bool IsToxicByInhalation { get; set; }
}

/// <summary>
/// Seismic alert from ShakeAlert EEW or USGS ShakeMap. Provides magnitude,
/// Modified Mercalli Intensity, epicenter location, and estimated arrival time.
/// </summary>
public class SeismicAlert
{
    /// <summary>Unique alert identifier (e.g., USGS event ID "us7000abcd").</summary>
    public string AlertId { get; set; } = string.Empty;

    /// <summary>Moment magnitude (Mw) of the earthquake.</summary>
    public double Magnitude { get; set; }

    /// <summary>Modified Mercalli Intensity at the user's location (estimated shaking).</summary>
    public SeismicMMI MMI { get; set; } = SeismicMMI.I;

    /// <summary>WGS84 latitude of the earthquake epicenter.</summary>
    public double EpicenterLatitude { get; set; }

    /// <summary>WGS84 longitude of the earthquake epicenter.</summary>
    public double EpicenterLongitude { get; set; }

    /// <summary>Depth of the hypocenter in kilometers below surface.</summary>
    public double DepthKm { get; set; }

    /// <summary>
    /// Estimated seconds until S-wave arrival at user's location (ShakeAlert EEW).
    /// Null if alert is post-event (USGS ShakeMap) rather than early warning.
    /// </summary>
    public int? EstimatedArrivalSeconds { get; set; }

    /// <summary>Expected damage level description (e.g., "None", "Light", "Moderate", "Heavy", "Very Heavy").</summary>
    public string ExpectedDamageLevel { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the earthquake origin time.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Data source: "ShakeAlert" for EEW, "USGS" for post-event ShakeMap.</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// ERG 2024 default evacuation distances. Used when material-specific data is unavailable.
/// Values from USDOT/PHMSA Emergency Response Guidebook, 2024 edition.
/// </summary>
public static class ERG2024Defaults
{
    /// <summary>Default small spill initial isolation distance (meters). ERG general guidance.</summary>
    public const double DefaultSmallSpillEvacMeters = 30;

    /// <summary>Default large spill initial isolation distance (meters). ERG general guidance.</summary>
    public const double DefaultLargeSpillEvacMeters = 100;

    /// <summary>Water-reactive material evacuation distance (meters). ERG Table 2 blue pages.</summary>
    public const double WaterReactiveEvacMeters = 250;

    /// <summary>Toxic-by-Inhalation daytime protective action distance (km). ERG Table 1 green pages default.</summary>
    public const double TIHDayEvacKm = 0.5;

    /// <summary>Toxic-by-Inhalation nighttime protective action distance (km). ERG Table 1 green pages default. Larger due to atmospheric stability.</summary>
    public const double TIHNightEvacKm = 1.1;
}
