// ISpatialIndex — domain port for geospatial indexing (H3/geohash volunteer lookup).
// NO database SDK imports allowed in this file.
// Example:
//   await spatial.IndexAsync("vol-42", "Volunteer", 30.2672, -97.7431);
//   var nearby = await spatial.FindNearbyAsync(new SpatialQuery { Latitude = 30.27, Longitude = -97.74, RadiusMeters = 500 });
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface ISpatialIndex
{
    Task IndexAsync(string entityId, string entityType, double latitude, double longitude, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task RemoveAsync(string entityId, CancellationToken ct = default);
    Task<List<SpatialResult>> FindNearbyAsync(SpatialQuery query, CancellationToken ct = default);
    Task<List<SpatialResult>> GetRingAsync(int ringLevel, double centerLat, double centerLng, CancellationToken ct = default);
    Task UpdatePositionAsync(string entityId, double latitude, double longitude, CancellationToken ct = default);
}
