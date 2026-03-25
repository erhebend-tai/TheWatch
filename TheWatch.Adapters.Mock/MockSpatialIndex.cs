// =============================================================================
// MockSpatialIndex — Full-featured mock adapter for ISpatialIndex
// =============================================================================
// Generates realistic mock responders within a queried radius using Haversine
// distance calculations. Supports indexing, removal, position updates, and
// ring-based queries. Seeds 30+ mock volunteers across a configurable area
// so that any query returns a plausible set of nearby responders.
//
// Example:
//   var query = new SpatialQuery { Latitude = 30.2672, Longitude = -97.7431, RadiusMeters = 3000 };
//   var nearby = await spatialIndex.FindNearbyAsync(query);
//   // Returns 5-15 mock responders sorted by distance, filtered by availability and age
//
// Responder metadata stored per entity:
//   "name"         — display name
//   "hasVehicle"   — "true"/"false" — vehicle-enabled can dispatch full radius; on-foot limited to 1600m
//   "isAvailable"  — "true"/"false" — only available responders returned
//   "age"          — integer string — must be >= 18
//   "capabilities" — comma-separated: "EMT,CPR,FIRST_AID,NURSE"
//   "entityType"   — "Volunteer", "Responder", "Sensor", etc.
//
// On-foot distance policy:
//   Responders without a vehicle (hasVehicle=false) are excluded from results
//   when their distance exceeds 1600m (~1 mile / ~20 min walk). This aligns
//   with DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters.
// =============================================================================

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

public class MockSpatialIndex : ISpatialIndex
{
    // In-memory spatial store: entityId → (lat, lng, entityType, metadata)
    private readonly ConcurrentDictionary<string, SpatialEntity> _entities = new();

    // Maximum walking distance for on-foot responders (meters)
    private const double MaxWalkingDistanceMeters = 1600;

    // Seed data: 35 mock volunteers spread around any queried location
    // These are indexed on first query if the store is empty
    private bool _seeded;
    private readonly object _seedLock = new();

    public MockSpatialIndex()
    {
        // Seed is deferred until first FindNearbyAsync so we can center around the queried location
    }

    /// <summary>Index an entity at a geographic position with optional metadata.</summary>
    public Task IndexAsync(string entityId, string entityType, double latitude, double longitude,
        Dictionary<string, string>? metadata, CancellationToken ct)
    {
        _entities[entityId] = new SpatialEntity(entityId, entityType, latitude, longitude, metadata ?? new());
        return Task.CompletedTask;
    }

    /// <summary>Remove an entity from the spatial index.</summary>
    public Task RemoveAsync(string entityId, CancellationToken ct)
    {
        _entities.TryRemove(entityId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Find entities near a lat/lng within a radius. Returns realistic mock responders
    /// sorted by distance ascending. Filters by: within radius, isAvailable=true, age >= 18.
    /// On-foot responders (hasVehicle=false) excluded beyond 1600m.
    /// </summary>
    public Task<List<SpatialResult>> FindNearbyAsync(SpatialQuery query, CancellationToken ct)
    {
        EnsureSeeded(query.Latitude, query.Longitude);

        var results = _entities.Values
            .Select(e =>
            {
                var dist = HaversineDistance(query.Latitude, query.Longitude, e.Latitude, e.Longitude);
                return new { Entity = e, Distance = dist };
            })
            // Filter: within requested radius
            .Where(x => x.Distance <= query.RadiusMeters)
            // Filter: available only (metadata "isAvailable" must be "true" or absent = assumed true)
            .Where(x =>
            {
                if (x.Entity.Metadata.TryGetValue("isAvailable", out var avail))
                    return string.Equals(avail, "true", StringComparison.OrdinalIgnoreCase);
                return true; // No metadata = assumed available
            })
            // Filter: age >= 18 (metadata "age" if present)
            .Where(x =>
            {
                if (x.Entity.Metadata.TryGetValue("age", out var ageStr) && int.TryParse(ageStr, out var age))
                    return age >= 18;
                return true; // No age metadata = assumed adult
            })
            // Filter: on-foot responders excluded beyond max walking distance
            .Where(x =>
            {
                if (x.Entity.Metadata.TryGetValue("hasVehicle", out var v) &&
                    string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return x.Distance <= MaxWalkingDistanceMeters;
                }
                return true; // Has vehicle or no metadata = no distance restriction
            })
            // Sort by distance ascending (nearest first)
            .OrderBy(x => x.Distance)
            // Limit results
            .Take(query.MaxResults)
            .Select(x => new SpatialResult
            {
                EntityId = x.Entity.EntityId,
                EntityType = x.Entity.EntityType,
                Latitude = x.Entity.Latitude,
                Longitude = x.Entity.Longitude,
                DistanceMeters = Math.Round(x.Distance, 1),
                RingLevel = DistanceToRing(x.Distance),
                Metadata = x.Entity.Metadata
            })
            .ToList();

        return Task.FromResult(results);
    }

    /// <summary>
    /// Get entities at a specific ring level around a center point.
    /// Ring levels: 0 = 0-250m, 1 = 250-500m, 2 = 500-1000m, 3 = 1000-2000m, 4 = 2000-5000m, 5 = 5000m+
    /// </summary>
    public Task<List<SpatialResult>> GetRingAsync(int ringLevel, double centerLat, double centerLng, CancellationToken ct)
    {
        EnsureSeeded(centerLat, centerLng);

        var (minDist, maxDist) = RingBounds(ringLevel);

        var results = _entities.Values
            .Select(e =>
            {
                var dist = HaversineDistance(centerLat, centerLng, e.Latitude, e.Longitude);
                return new { Entity = e, Distance = dist };
            })
            .Where(x => x.Distance >= minDist && x.Distance < maxDist)
            .OrderBy(x => x.Distance)
            .Select(x => new SpatialResult
            {
                EntityId = x.Entity.EntityId,
                EntityType = x.Entity.EntityType,
                Latitude = x.Entity.Latitude,
                Longitude = x.Entity.Longitude,
                DistanceMeters = Math.Round(x.Distance, 1),
                RingLevel = ringLevel,
                Metadata = x.Entity.Metadata
            })
            .ToList();

        return Task.FromResult(results);
    }

    /// <summary>Update an entity's position (e.g., volunteer is moving).</summary>
    public Task UpdatePositionAsync(string entityId, double latitude, double longitude, CancellationToken ct)
    {
        if (_entities.TryGetValue(entityId, out var existing))
        {
            _entities[entityId] = existing with { Latitude = latitude, Longitude = longitude };
        }
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════
    // Seed Data — generates 35 realistic mock volunteers
    // ═══════════════════════════════════════════════════════════════

    private void EnsureSeeded(double centerLat, double centerLng)
    {
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            SeedMockVolunteers(centerLat, centerLng);
            _seeded = true;
        }
    }

    /// <summary>
    /// Seeds 35 mock volunteers around the given center point at varying distances.
    /// Uses deterministic pseudo-random offsets so results are consistent per center.
    /// Includes a mix of:
    ///   - Certified EMTs/nurses (with vehicles)
    ///   - CPR/First Aid trained volunteers (mix of vehicle/on-foot)
    ///   - Untrained but willing neighbors (mostly on foot, close range)
    ///   - A few unavailable and underage entries to test filtering
    /// </summary>
    private void SeedMockVolunteers(double centerLat, double centerLng)
    {
        // Seed definitions: (id, name, age, hasVehicle, isAvailable, capabilities, distanceBand)
        // distanceBand: 0=very close (50-300m), 1=close (300-800m), 2=medium (800-2000m),
        //               3=far (2000-5000m), 4=very far (5000-12000m)
        var volunteers = new[]
        {
            // Band 0: Very close (50-300m) — on-foot friendly
            ("vol-001", "Marcus Chen",       32, true,  true,  "EMT,CPR",        0, 0.15),
            ("vol-002", "Priya Sharma",      28, false, true,  "FIRST_AID",      0, 0.45),
            ("vol-003", "David Kim",         45, false, true,  "",               0, 0.72),
            ("vol-004", "Sofia Martinez",    22, false, true,  "CPR",            0, 0.28),

            // Band 1: Close (300-800m) — on-foot still viable
            ("vol-005", "Sarah Williams",    35, true,  true,  "NURSE,CPR",      1, 0.10),
            ("vol-006", "James Thompson",    41, true,  true,  "EMT",            1, 0.55),
            ("vol-007", "Aisha Patel",       29, false, true,  "FIRST_AID",      1, 0.80),
            ("vol-008", "Robert Jackson",    53, false, true,  "",               1, 0.35),
            ("vol-009", "Chen Wei",          38, true,  true,  "CPR",            1, 0.62),

            // Band 2: Medium (800-2000m) — on-foot responders filtered at 1600m
            ("vol-010", "Elena Rodriguez",   31, true,  true,  "EMT,FIRST_AID",  2, 0.15),
            ("vol-011", "Michael Brown",     44, true,  true,  "CPR",            2, 0.40),
            ("vol-012", "Fatima Al-Hassan",  26, false, true,  "FIRST_AID",      2, 0.10),  // 880m, on-foot OK
            ("vol-013", "Tom Garcia",        33, false, true,  "",               2, 0.85),  // ~1700m, on-foot EXCLUDED
            ("vol-014", "Lisa Chang",        48, true,  true,  "NURSE",          2, 0.60),
            ("vol-015", "Derek Washington",  37, true,  true,  "",               2, 0.75),
            ("vol-016", "Nina Kowalski",     24, false, true,  "CPR",            2, 0.50),  // ~1200m, on-foot OK

            // Band 3: Far (2000-5000m) — vehicle required
            ("vol-017", "Carlos Hernandez",  42, true,  true,  "EMT,CPR",        3, 0.20),
            ("vol-018", "Hannah Lee",        30, true,  true,  "FIRST_AID",      3, 0.50),
            ("vol-019", "Patrick O'Brien",   55, true,  true,  "",               3, 0.75),
            ("vol-020", "Yuki Tanaka",       27, false, true,  "CPR",            3, 0.35),  // On foot, will be filtered
            ("vol-021", "Angela Davis",      39, true,  true,  "NURSE,EMT",      3, 0.90),
            ("vol-022", "Omar Saleh",        34, true,  true,  "FIRST_AID",      3, 0.60),

            // Band 4: Very far (5000-12000m) — vehicle required, community scope
            ("vol-023", "Rachel Green",      36, true,  true,  "EMT",            4, 0.15),
            ("vol-024", "Kevin Park",        43, true,  true,  "CPR",            4, 0.40),
            ("vol-025", "Maria Gonzalez",    31, true,  true,  "",               4, 0.65),
            ("vol-026", "Steve Wilson",      50, true,  true,  "EMT,NURSE",      4, 0.85),
            ("vol-027", "Ling Zhang",        29, true,  true,  "FIRST_AID",      4, 0.50),

            // Unavailable responders (should be filtered out)
            ("vol-028", "Alex Taylor",       33, true,  false, "EMT,CPR",        1, 0.30),  // isAvailable = false
            ("vol-029", "Jordan Smith",      40, true,  false, "",               2, 0.55),  // isAvailable = false
            ("vol-030", "Casey Miller",      25, false, false, "FIRST_AID",      0, 0.60),  // isAvailable = false

            // Underage (should be filtered out by age >= 18)
            ("vol-031", "Tyler Young",       16, false, true,  "",               0, 0.40),  // age 16
            ("vol-032", "Emma Johnson",      17, true,  true,  "CPR",            1, 0.25),  // age 17

            // Additional available volunteers to ensure 15+ candidates in large radius queries
            ("vol-033", "Diana Ross",        46, true,  true,  "NURSE",          3, 0.45),
            ("vol-034", "Frank Castle",      38, true,  true,  "",               4, 0.30),
            ("vol-035", "Grace Hopper",      52, true,  true,  "EMT,CPR,NURSE",  2, 0.30),
        };

        foreach (var (id, name, age, hasVehicle, isAvailable, capabilities, band, offset) in volunteers)
        {
            // Calculate position offset from center based on band and offset
            var (latOffset, lngOffset) = ComputeOffset(band, offset, centerLat);

            var metadata = new Dictionary<string, string>
            {
                ["name"] = name,
                ["age"] = age.ToString(),
                ["hasVehicle"] = hasVehicle.ToString().ToLowerInvariant(),
                ["isAvailable"] = isAvailable.ToString().ToLowerInvariant(),
                ["capabilities"] = capabilities,
                ["entityType"] = "Volunteer"
            };

            _entities[id] = new SpatialEntity(
                id, "Volunteer",
                centerLat + latOffset,
                centerLng + lngOffset,
                metadata);
        }
    }

    /// <summary>
    /// Compute a lat/lng offset from center that places the entity within the specified
    /// distance band. Uses the offset parameter (0.0-1.0) to spread entities within the band.
    /// Different offset values yield different angular directions (N, NE, E, SE, S, SW, W, NW).
    /// </summary>
    private static (double latOffset, double lngOffset) ComputeOffset(int band, double offset, double centerLat)
    {
        // Distance bands in meters
        var (minDist, maxDist) = band switch
        {
            0 => (50.0, 300.0),
            1 => (300.0, 800.0),
            2 => (800.0, 2000.0),
            3 => (2000.0, 5000.0),
            4 => (5000.0, 12000.0),
            _ => (0.0, 100.0)
        };

        var distanceMeters = minDist + (maxDist - minDist) * offset;

        // Convert offset to an angle (spread entities around the compass)
        // Use a golden-angle-like spread to avoid clustering
        var angleDeg = (offset * 360.0 + band * 137.5) % 360.0;
        var angleRad = angleDeg * Math.PI / 180.0;

        // Convert distance to approximate lat/lng offsets
        // 1 degree latitude ≈ 111,320 meters
        // 1 degree longitude ≈ 111,320 * cos(latitude) meters
        var metersPerDegreeLat = 111_320.0;
        var metersPerDegreeLng = 111_320.0 * Math.Cos(centerLat * Math.PI / 180.0);

        var latOffset = (distanceMeters * Math.Cos(angleRad)) / metersPerDegreeLat;
        var lngOffset = (distanceMeters * Math.Sin(angleRad)) / metersPerDegreeLng;

        return (latOffset, lngOffset);
    }

    // ═══════════════════════════════════════════════════════════════
    // Haversine Distance
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Haversine formula for great-circle distance between two lat/lng points.
    /// Returns distance in meters. Accuracy: ~0.5% for distances under 100km.
    /// </summary>
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // ═══════════════════════════════════════════════════════════════
    // Ring Level Mapping
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Map distance to ring level for H3-like ring queries.</summary>
    private static int DistanceToRing(double distanceMeters) => distanceMeters switch
    {
        < 250 => 0,
        < 500 => 1,
        < 1000 => 2,
        < 2000 => 3,
        < 5000 => 4,
        _ => 5
    };

    /// <summary>Get min/max distance for a ring level.</summary>
    private static (double min, double max) RingBounds(int ring) => ring switch
    {
        0 => (0, 250),
        1 => (250, 500),
        2 => (500, 1000),
        3 => (1000, 2000),
        4 => (2000, 5000),
        _ => (5000, double.MaxValue)
    };

    // ═══════════════════════════════════════════════════════════════
    // Internal Entity Record
    // ═══════════════════════════════════════════════════════════════

    private record SpatialEntity(
        string EntityId,
        string EntityType,
        double Latitude,
        double Longitude,
        Dictionary<string, string> Metadata);
}
