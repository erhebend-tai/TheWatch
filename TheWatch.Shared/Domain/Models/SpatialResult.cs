// SpatialResult — returned from ISpatialIndex.FindNearbyAsync / GetRingAsync.
// Example:
//   SpatialResult { EntityId = "vol-42", DistanceMeters = 312.5, RingLevel = 1 }

namespace TheWatch.Shared.Domain.Models;

public class SpatialResult
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double DistanceMeters { get; set; }
    public int RingLevel { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
