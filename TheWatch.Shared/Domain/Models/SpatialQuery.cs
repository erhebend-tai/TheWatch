// SpatialQuery — geospatial lookup parameters for nearby-entity discovery.
// Example:
//   var q = new SpatialQuery { Latitude = 30.2672, Longitude = -97.7431, RadiusMeters = 500 };
//   var results = await spatialIndex.FindNearbyAsync(q);

namespace TheWatch.Shared.Domain.Models;

public class SpatialQuery
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 1000;
    public int MaxResults { get; set; } = 50;
    public int? RingLevel { get; set; }
}
