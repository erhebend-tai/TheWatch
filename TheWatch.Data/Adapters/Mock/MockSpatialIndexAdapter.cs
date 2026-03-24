// MockSpatialIndexAdapter — Haversine-distance in-memory spatial index.
// Supports ring-level assignment based on distance bands.
// Example:
//   await spatial.IndexAsync("vol-42", "Volunteer", 30.2672, -97.7431);
//   var nearby = await spatial.FindNearbyAsync(new SpatialQuery { Latitude = 30.27, Longitude = -97.74, RadiusMeters = 500 });
using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockSpatialIndexAdapter : ISpatialIndex
{
    private readonly ConcurrentDictionary<string, SpatialPoint> _points = new();

    // Ring boundaries in meters: Ring 0 = 0-200m, Ring 1 = 200-500m, Ring 2 = 500-1000m, Ring 3 = 1000m+
    private static readonly double[] RingBoundaries = [200, 500, 1000];

    public Task IndexAsync(string entityId, string entityType, double latitude, double longitude,
        Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        _points[entityId] = new SpatialPoint(entityId, entityType, latitude, longitude, metadata);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string entityId, CancellationToken ct = default)
    {
        _points.TryRemove(entityId, out _);
        return Task.CompletedTask;
    }

    public Task<List<SpatialResult>> FindNearbyAsync(SpatialQuery query, CancellationToken ct = default)
    {
        var results = _points.Values
            .Select(p => new { Point = p, Distance = Haversine(query.Latitude, query.Longitude, p.Latitude, p.Longitude) })
            .Where(x => x.Distance <= query.RadiusMeters)
            .Where(x => query.RingLevel is null || AssignRing(x.Distance) == query.RingLevel)
            .OrderBy(x => x.Distance)
            .Take(query.MaxResults)
            .Select(x => new SpatialResult
            {
                EntityId = x.Point.EntityId,
                EntityType = x.Point.EntityType,
                Latitude = x.Point.Latitude,
                Longitude = x.Point.Longitude,
                DistanceMeters = x.Distance,
                RingLevel = AssignRing(x.Distance),
                Metadata = x.Point.Metadata
            })
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<SpatialResult>> GetRingAsync(int ringLevel, double centerLat, double centerLng, CancellationToken ct = default)
    {
        var results = _points.Values
            .Select(p => new { Point = p, Distance = Haversine(centerLat, centerLng, p.Latitude, p.Longitude) })
            .Where(x => AssignRing(x.Distance) == ringLevel)
            .OrderBy(x => x.Distance)
            .Select(x => new SpatialResult
            {
                EntityId = x.Point.EntityId,
                EntityType = x.Point.EntityType,
                Latitude = x.Point.Latitude,
                Longitude = x.Point.Longitude,
                DistanceMeters = x.Distance,
                RingLevel = ringLevel,
                Metadata = x.Point.Metadata
            })
            .ToList();
        return Task.FromResult(results);
    }

    public Task UpdatePositionAsync(string entityId, double latitude, double longitude, CancellationToken ct = default)
    {
        if (_points.TryGetValue(entityId, out var existing))
        {
            _points[entityId] = existing with { Latitude = latitude, Longitude = longitude };
        }
        return Task.CompletedTask;
    }

    private static int AssignRing(double distanceMeters)
    {
        for (int i = 0; i < RingBoundaries.Length; i++)
        {
            if (distanceMeters <= RingBoundaries[i]) return i;
        }
        return RingBoundaries.Length;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private sealed record SpatialPoint(string EntityId, string EntityType, double Latitude, double Longitude, Dictionary<string, string>? Metadata);
}
