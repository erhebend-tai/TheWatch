// SpatialIndexContractTests — abstract xUnit base class for ISpatialIndex contract verification.
// Example:
//   public class MockSpatialContractTests : SpatialIndexContractTests
//   {
//       protected override ISpatialIndex CreateAdapter() => new MockSpatialIndexAdapter();
//   }
//
// Life-Safety Tests:
//   - DSL Determinism: identical queries produce identical results
//   - Ring Assignment: entities at known distances land in correct rings
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Testing;

public abstract class SpatialIndexContractTests
{
    protected abstract ISpatialIndex CreateAdapter();

    public virtual async Task IndexAndFind_ReturnsNearbyEntities()
    {
        var adapter = CreateAdapter();
        // Index a volunteer at Austin, TX
        await adapter.IndexAsync("vol-1", "Volunteer", 30.2672, -97.7431);

        // Search from a point ~100m away
        var results = await adapter.FindNearbyAsync(new SpatialQuery
        {
            Latitude = 30.2680, Longitude = -97.7431, RadiusMeters = 500
        });

        Assert(results.Count > 0, "Must find at least one nearby entity");
        Assert(results[0].EntityId == "vol-1", "Must find the indexed volunteer");
    }

    public virtual async Task Remove_EntityNotFoundAfter()
    {
        var adapter = CreateAdapter();
        await adapter.IndexAsync("vol-rm", "Volunteer", 30.2672, -97.7431);
        await adapter.RemoveAsync("vol-rm");

        var results = await adapter.FindNearbyAsync(new SpatialQuery
        {
            Latitude = 30.2672, Longitude = -97.7431, RadiusMeters = 100
        });

        Assert(results.All(r => r.EntityId != "vol-rm"), "Removed entity must not appear in results");
    }

    // --- DSL Determinism (identical queries → identical results) ---

    public virtual async Task DeterministicQueries_ProduceIdenticalResults()
    {
        var adapter = CreateAdapter();
        await adapter.IndexAsync("det-1", "Volunteer", 30.2672, -97.7431);
        await adapter.IndexAsync("det-2", "Volunteer", 30.2680, -97.7425);

        var query = new SpatialQuery { Latitude = 30.2675, Longitude = -97.7428, RadiusMeters = 1000, MaxResults = 10 };

        var results1 = await adapter.FindNearbyAsync(query);
        var results2 = await adapter.FindNearbyAsync(query);

        Assert(results1.Count == results2.Count, "Identical queries must return same count");
        for (int i = 0; i < results1.Count; i++)
        {
            Assert(results1[i].EntityId == results2[i].EntityId, $"Result {i} must be identical across runs");
            Assert(Math.Abs(results1[i].DistanceMeters - results2[i].DistanceMeters) < 0.01, $"Distance {i} must be deterministic");
        }
    }

    // --- Ring Assignment Correctness ---

    public virtual async Task RingAssignment_CorrectForKnownDistances()
    {
        var adapter = CreateAdapter();
        // Ring 0: 0-200m, Ring 1: 200-500m, Ring 2: 500-1000m, Ring 3: 1000m+
        // Place entities at known distances from center (30.2672, -97.7431)
        await adapter.IndexAsync("ring0", "Volunteer", 30.2673, -97.7431); // ~11m → Ring 0
        await adapter.IndexAsync("ring1", "Volunteer", 30.2700, -97.7431); // ~311m → Ring 1
        await adapter.IndexAsync("ring2", "Volunteer", 30.2740, -97.7431); // ~756m → Ring 2

        var results = await adapter.FindNearbyAsync(new SpatialQuery
        {
            Latitude = 30.2672, Longitude = -97.7431, RadiusMeters = 2000
        });

        var ring0 = results.FirstOrDefault(r => r.EntityId == "ring0");
        var ring1 = results.FirstOrDefault(r => r.EntityId == "ring1");
        var ring2 = results.FirstOrDefault(r => r.EntityId == "ring2");

        Assert(ring0 is not null && ring0.RingLevel == 0, "~11m entity must be in Ring 0");
        Assert(ring1 is not null && ring1.RingLevel == 1, "~311m entity must be in Ring 1");
        Assert(ring2 is not null && ring2.RingLevel == 2, "~756m entity must be in Ring 2");
    }

    public virtual async Task GetRing_ReturnsOnlyEntitiesInSpecifiedRing()
    {
        var adapter = CreateAdapter();
        await adapter.IndexAsync("gr-0", "Volunteer", 30.2673, -97.7431); // Ring 0
        await adapter.IndexAsync("gr-1", "Volunteer", 30.2700, -97.7431); // Ring 1

        var ring0Results = await adapter.GetRingAsync(0, 30.2672, -97.7431);
        var ring1Results = await adapter.GetRingAsync(1, 30.2672, -97.7431);

        Assert(ring0Results.All(r => r.RingLevel == 0), "GetRing(0) must only return Ring 0 entities");
        Assert(ring1Results.All(r => r.RingLevel == 1), "GetRing(1) must only return Ring 1 entities");
    }

    public virtual async Task UpdatePosition_MovesEntity()
    {
        var adapter = CreateAdapter();
        await adapter.IndexAsync("move-1", "Volunteer", 30.2672, -97.7431);
        await adapter.UpdatePositionAsync("move-1", 30.3000, -97.7431);

        var results = await adapter.FindNearbyAsync(new SpatialQuery
        {
            Latitude = 30.3000, Longitude = -97.7431, RadiusMeters = 100
        });

        Assert(results.Any(r => r.EntityId == "move-1"), "Moved entity must be findable at new position");
    }

    protected static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Contract test failed: {message}");
    }
}
