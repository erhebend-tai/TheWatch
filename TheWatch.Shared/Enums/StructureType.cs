// StructureType.cs — Enumerations for building construction, hazard classification,
// fire protection, weather alerting, seismic intensity, and hazmat response.
//
// Standards referenced:
//   IBC (International Building Code) Chapter 3 — Occupancy groups
//   IBC Chapter 6 — Construction types (Type I–V)
//   HAZUS-MH — FEMA building classification for loss estimation
//   NFPA 13/13R/13D — Sprinkler system classifications
//   NFPA 72 — National Fire Alarm and Signaling Code
//   NFIRS 5.0 — National Fire Incident Reporting System (fire cause coding)
//   CAP 1.2 — Common Alerting Protocol (OASIS standard, used by NWS/IPAWS)
//   ShakeAlert/USGS — Modified Mercalli Intensity scale
//   ERG 2024 — Emergency Response Guidebook (USDOT/PHMSA)
//
// Value range: 800–899 (allocated block for Structure & Hazard domain).
//
// Example:
//   var building = new BuildingData
//   {
//       ConstructionType = ConstructionType.TypeIII_Ordinary,
//       HAZUSType = HAZUSBuildingType.C1M,
//       OccupancyGroup = OccupancyGroup.R_Residential
//   };

namespace TheWatch.Shared.Enums;

/// <summary>
/// IBC Chapter 6 construction classifications. Determines fire-resistance ratings
/// of structural elements (walls, floors, roof) and governs allowable height/area.
/// </summary>
public enum ConstructionType
{
    /// <summary>Type I — Fire Resistive. Noncombustible materials, highest fire-resistance rating (3–4 hr). High-rises, hospitals.</summary>
    TypeI_FireResistive = 800,

    /// <summary>Type II — Non-Combustible. Noncombustible materials, lower fire-resistance ratings (0–2 hr). Warehouses, commercial.</summary>
    TypeII_NonCombustible = 801,

    /// <summary>Type III — Ordinary. Noncombustible exterior walls, interior may be combustible. Mixed-use, older urban buildings.</summary>
    TypeIII_Ordinary = 802,

    /// <summary>Type IV — Heavy Timber. Large-dimension lumber or glue-laminated timber, min 8" columns. Mills, churches.</summary>
    TypeIV_HeavyTimber = 803,

    /// <summary>Type V — Wood Frame. Most common residential. Combustible walls, floors, roof. Stick-built homes.</summary>
    TypeV_WoodFrame = 804
}

/// <summary>
/// FEMA HAZUS-MH building types for earthquake/flood/wind loss estimation.
/// Each code encodes structural system + height class (L=Low 1-3, M=Mid 4-7, H=High 8+).
/// Used by FEMA's Hazus software to compute expected damage/loss ratios.
/// </summary>
public enum HAZUSBuildingType
{
    /// <summary>W1 — Wood, Light-Frame (≤5,000 sqft). Single/multi-family residential.</summary>
    W1 = 805,

    /// <summary>W2 — Wood, Commercial and Industrial (>5,000 sqft). Warehouses, large retail.</summary>
    W2 = 806,

    /// <summary>S1L — Steel Moment Frame, Low-Rise (1–3 stories).</summary>
    S1L = 807,

    /// <summary>S1M — Steel Moment Frame, Mid-Rise (4–7 stories).</summary>
    S1M = 808,

    /// <summary>S1H — Steel Moment Frame, High-Rise (8+ stories).</summary>
    S1H = 809,

    /// <summary>S2L — Steel Braced Frame, Low-Rise (1–3 stories).</summary>
    S2L = 810,

    /// <summary>S2M — Steel Braced Frame, Mid-Rise (4–7 stories).</summary>
    S2M = 811,

    /// <summary>S2H — Steel Braced Frame, High-Rise (8+ stories).</summary>
    S2H = 812,

    /// <summary>C1L — Concrete Moment Frame, Low-Rise (1–3 stories).</summary>
    C1L = 813,

    /// <summary>C1M — Concrete Moment Frame, Mid-Rise (4–7 stories).</summary>
    C1M = 814,

    /// <summary>C1H — Concrete Moment Frame, High-Rise (8+ stories).</summary>
    C1H = 815,

    /// <summary>C2L — Concrete Shear Wall, Low-Rise (1–3 stories).</summary>
    C2L = 816,

    /// <summary>C2M — Concrete Shear Wall, Mid-Rise (4–7 stories).</summary>
    C2M = 817,

    /// <summary>C2H — Concrete Shear Wall, High-Rise (8+ stories).</summary>
    C2H = 818,

    /// <summary>C3L — Concrete Frame with URM Infill, Low-Rise (1–3 stories).</summary>
    C3L = 819,

    /// <summary>C3M — Concrete Frame with URM Infill, Mid-Rise (4–7 stories).</summary>
    C3M = 820,

    /// <summary>C3H — Concrete Frame with URM Infill, High-Rise (8+ stories).</summary>
    C3H = 821,

    /// <summary>PC1 — Precast Concrete Tilt-Up Walls. Common warehouse/big-box retail.</summary>
    PC1 = 822,

    /// <summary>PC2L — Precast Concrete Frame with Shear Walls, Low-Rise (1–3 stories).</summary>
    PC2L = 823,

    /// <summary>PC2M — Precast Concrete Frame with Shear Walls, Mid-Rise (4–7 stories).</summary>
    PC2M = 824,

    /// <summary>PC2H — Precast Concrete Frame with Shear Walls, High-Rise (8+ stories).</summary>
    PC2H = 825,

    /// <summary>RM1L — Reinforced Masonry with Wood/Metal Diaphragm, Low-Rise (1–3 stories).</summary>
    RM1L = 826,

    /// <summary>RM1M — Reinforced Masonry with Wood/Metal Diaphragm, Mid-Rise (4+ stories).</summary>
    RM1M = 827,

    /// <summary>RM2L — Reinforced Masonry with Concrete Diaphragm, Low-Rise (1–3 stories).</summary>
    RM2L = 828,

    /// <summary>RM2M — Reinforced Masonry with Concrete Diaphragm, Mid-Rise (4–7 stories).</summary>
    RM2M = 829,

    /// <summary>RM2H — Reinforced Masonry with Concrete Diaphragm, High-Rise (8+ stories).</summary>
    RM2H = 830,

    /// <summary>URML — Unreinforced Masonry Bearing Wall, Low-Rise (1–2 stories). High seismic vulnerability.</summary>
    URML = 831,

    /// <summary>URMM — Unreinforced Masonry Bearing Wall, Mid-Rise (3+ stories). Highest seismic vulnerability.</summary>
    URMM = 832,

    /// <summary>MH — Manufactured Housing (mobile/modular homes). High wind vulnerability.</summary>
    MH = 833
}

/// <summary>
/// IBC Chapter 3 occupancy group classifications. Determines fire/life-safety
/// requirements, egress capacity, sprinkler mandates, and allowable building size.
/// </summary>
public enum OccupancyGroup
{
    /// <summary>Group A — Assembly. Theaters, stadiums, churches, restaurants (≥50 occupants).</summary>
    A_Assembly = 834,

    /// <summary>Group B — Business. Offices, banks, outpatient clinics, educational above 12th grade.</summary>
    B_Business = 835,

    /// <summary>Group E — Educational. K–12 schools with ≥6 persons.</summary>
    E_Educational = 836,

    /// <summary>Group F — Factory/Industrial. Manufacturing, assembly, repair operations.</summary>
    F_Factory = 837,

    /// <summary>Group H — High-Hazard. Facilities with explosive, flammable, toxic, or reactive materials.</summary>
    H_HighHazard = 838,

    /// <summary>Group I — Institutional. Hospitals, jails, nursing homes — occupants under restraint or incapable of self-preservation.</summary>
    I_Institutional = 839,

    /// <summary>Group M — Mercantile. Retail stores, markets, drug stores, gas stations.</summary>
    M_Mercantile = 840,

    /// <summary>Group R — Residential. Hotels, apartments, dormitories, 1- and 2-family dwellings.</summary>
    R_Residential = 841,

    /// <summary>Group S — Storage. Warehouses, parking garages, aircraft hangars.</summary>
    S_Storage = 842,

    /// <summary>Group U — Utility/Miscellaneous. Barns, greenhouses, fences, tanks, towers.</summary>
    U_Utility = 843
}

/// <summary>
/// NFPA sprinkler system classifications.
///   NFPA 13 — Standard for Installation of Sprinkler Systems (commercial/industrial).
///   NFPA 13R — Residential occupancies up to 4 stories.
///   NFPA 13D — One- and two-family dwellings and manufactured homes.
/// </summary>
public enum SprinklerType
{
    /// <summary>NFPA 13 — Full commercial sprinkler system. Covers all areas including concealed spaces.</summary>
    NFPA13_Commercial = 844,

    /// <summary>NFPA 13R — Residential system for buildings up to 4 stories. Attics/closets may be omitted.</summary>
    NFPA13R_Residential = 845,

    /// <summary>NFPA 13D — Dwelling system for 1- and 2-family homes. Lowest water demand.</summary>
    NFPA13D_Dwelling = 846,

    /// <summary>No sprinkler system installed.</summary>
    None = 847
}

/// <summary>
/// NFPA 72 — National Fire Alarm and Signaling Code device/system types.
/// Categorizes how fire conditions are detected and how occupants/authorities are notified.
/// </summary>
public enum AlarmType
{
    /// <summary>NFPA 72 Initiating Device — Smoke detectors, heat detectors, pull stations, waterflow switches.</summary>
    NFPA72_Initiating = 848,

    /// <summary>NFPA 72 Notification Appliance — Horns, strobes, speakers, chimes for occupant alert.</summary>
    NFPA72_Notification = 849,

    /// <summary>NFPA 72 Supervisory Signal — Monitors valve positions, pump status, low air pressure.</summary>
    NFPA72_Supervisory = 850,

    /// <summary>Local alarm only — audible/visual on-site, no off-premise notification.</summary>
    LocalOnly = 851,

    /// <summary>Central Station — UL-listed facility monitors alarms 24/7 and dispatches fire dept (NFPA 72 Ch. 26).</summary>
    CentralStation = 852,

    /// <summary>Proprietary Station — Monitoring performed at owner's on-site control room (campus, hospital).</summary>
    ProprietaryStation = 853,

    /// <summary>Remote Station — Signals transmitted directly to fire department or answering service.</summary>
    RemoteStation = 854,

    /// <summary>No fire alarm system installed.</summary>
    None = 855
}

/// <summary>
/// Monitoring service type for the alarm/security system at a structure.
/// Determines notification path when an alarm activates.
/// </summary>
public enum MonitoringType
{
    /// <summary>Self-monitored — owner receives alerts via app/SMS, no professional dispatch.</summary>
    SelfMonitored = 856,

    /// <summary>Central Station monitoring — UL-listed 24/7 facility dispatches emergency services.</summary>
    CentralStation = 857,

    /// <summary>Proprietary Station — owner-operated monitoring room on premises (campus security).</summary>
    ProprietaryStation = 858,

    /// <summary>Remote Station — signals routed directly to fire/police dispatch.</summary>
    RemoteStation = 859,

    /// <summary>No monitoring service.</summary>
    None = 860
}

/// <summary>
/// Primary heating fuel type for a structure. Critical for explosion risk assessment —
/// gas and propane leaks are leading causes of residential explosions.
/// </summary>
public enum HeatingFuelType
{
    /// <summary>Natural gas (methane CH₄). Lighter than air, dissipates upward. LEL 5%, UEL 15%.</summary>
    NaturalGas = 861,

    /// <summary>Propane (C₃H₈). Heavier than air, pools in basements. LEL 2.1%, UEL 9.5%. Higher explosion risk.</summary>
    Propane = 862,

    /// <summary>Heating oil (#2 fuel oil). Flash point ~140°F. Lower explosion risk than gas.</summary>
    Oil = 863,

    /// <summary>Electric heating. No combustion fuel on-site. Lowest explosion risk.</summary>
    Electric = 864,

    /// <summary>Wood-burning (fireplace, stove, pellet). Creosote/chimney fire risk.</summary>
    Wood = 865,

    /// <summary>Coal. Rare in modern construction. CO and dust explosion risk.</summary>
    Coal = 866,

    /// <summary>Solar thermal or solar-assisted heating. No combustion fuel.</summary>
    Solar = 867,

    /// <summary>No heating system or fuel type not applicable.</summary>
    None = 868
}

/// <summary>
/// Foundation type for a structure. Critical for flood vulnerability assessment —
/// crawl spaces and basements are most susceptible to flood damage.
/// </summary>
public enum FoundationType
{
    /// <summary>Slab-on-grade. Concrete poured directly on ground. Moderate flood vulnerability.</summary>
    Slab = 869,

    /// <summary>Crawl space (vented or unvented). Elevated 18–48 inches. Moderate flood vulnerability, mold risk.</summary>
    CrawlSpace = 870,

    /// <summary>Basement (full or partial). Highest flood vulnerability — below-grade living/storage space.</summary>
    Basement = 871,

    /// <summary>Piers and posts (elevated). Common in coastal/flood zones. Best flood resilience if above BFE.</summary>
    PiersAndPosts = 872,

    /// <summary>Deep foundation (piles, caissons, drilled shafts). Used in poor soil or high-rise. Good flood resilience.</summary>
    DeepFoundation = 873
}

/// <summary>
/// NFIRS 5.0 fire cause categories. Used in post-incident reporting and
/// statistical analysis of fire origin for TheWatch hazard tracking.
/// </summary>
public enum FireCause
{
    /// <summary>Cooking — leading cause of residential fires (NFPA). Unattended stove/oven.</summary>
    Cooking = 874,

    /// <summary>Heating equipment — space heaters, furnaces, chimneys, fireplaces.</summary>
    Heating = 875,

    /// <summary>Electrical — wiring, outlets, circuit breakers, appliance cords.</summary>
    Electrical = 876,

    /// <summary>Smoking materials — cigarettes, cigars, pipes. Leading cause of fire deaths.</summary>
    Smoking = 877,

    /// <summary>Intentional/Arson (NFIRS cause=1). Criminal investigation required.</summary>
    Intentional = 878,

    /// <summary>Appliance malfunction — washer, dryer, dishwasher, HVAC unit.</summary>
    Appliance = 879,

    /// <summary>Natural causes — lightning, spontaneous combustion, wildfire exposure.</summary>
    NaturalCauses = 880,

    /// <summary>Unknown/Under investigation.</summary>
    Unknown = 881
}

/// <summary>
/// CAP 1.2 severity levels. Common Alerting Protocol (OASIS Standard) used by
/// NWS, IPAWS, and all WEA-capable alert originators.
/// </summary>
public enum CAPSeverity
{
    /// <summary>Extreme — extraordinary threat to life or property.</summary>
    Extreme = 882,

    /// <summary>Severe — significant threat to life or property.</summary>
    Severe = 883,

    /// <summary>Moderate — possible threat to life or property.</summary>
    Moderate = 884,

    /// <summary>Minor — minimal to no known threat to life or property.</summary>
    Minor = 885,

    /// <summary>Unknown — severity not yet determined.</summary>
    Unknown = 886
}

/// <summary>
/// CAP 1.2 urgency levels. Indicates time-frame for protective action.
/// </summary>
public enum CAPUrgency
{
    /// <summary>Immediate — responsive action SHOULD be taken immediately.</summary>
    Immediate = 887,

    /// <summary>Expected — responsive action SHOULD be taken soon (within next hour).</summary>
    Expected = 888,

    /// <summary>Future — responsive action SHOULD be taken in the near future.</summary>
    Future = 889,

    /// <summary>Past — responsive action no longer required.</summary>
    Past = 890,

    /// <summary>Unknown — urgency not yet determined.</summary>
    Unknown = 891
}

/// <summary>
/// CAP 1.2 certainty levels. Indicates confidence in the observation or prediction.
/// </summary>
public enum CAPCertainty
{
    /// <summary>Observed — determined to have occurred or to be ongoing.</summary>
    Observed = 892,

    /// <summary>Likely — probability > ~50%.</summary>
    Likely = 893,

    /// <summary>Possible — probability ≤ ~50%.</summary>
    Possible = 894,

    /// <summary>Unlikely — probability is minimal.</summary>
    Unlikely = 895,

    /// <summary>Unknown — certainty not yet determined.</summary>
    Unknown = 896
}

/// <summary>
/// Modified Mercalli Intensity (MMI) scale. Describes perceived shaking and damage at a location.
/// Used by ShakeAlert EEW and USGS ShakeMap. Roman numeral naming avoids confusion with magnitude.
/// </summary>
public enum SeismicMMI
{
    /// <summary>I — Not felt. Recorded by instruments only.</summary>
    I = 897,

    /// <summary>II — Weak. Felt by persons at rest on upper floors.</summary>
    II = 898,

    /// <summary>III — Weak. Felt indoors, hanging objects swing. Vibration like passing truck.</summary>
    III = 899,

    /// <summary>IV — Light. Felt indoors by many, outdoors by few. Dishes/windows rattle.</summary>
    IV = 850,

    /// <summary>V — Moderate. Felt by nearly everyone. Small objects displaced, some dishes broken.</summary>
    V = 851,

    /// <summary>VI — Strong. Felt by all. Furniture moves, slight structural damage to weak buildings.</summary>
    VI = 852,

    /// <summary>VII — Very Strong. Considerable damage to poorly built structures, slight in well-built.</summary>
    VII = 853,

    /// <summary>VIII — Severe. Considerable damage to ordinary buildings, heavy damage to poor construction.</summary>
    VIII = 854,

    /// <summary>IX — Violent. Heavy damage to most structures, some shifted off foundations.</summary>
    IX = 855,

    /// <summary>X — Extreme. Most masonry and frame structures destroyed. Ground badly cracked. Landslides.</summary>
    X = 856
}
