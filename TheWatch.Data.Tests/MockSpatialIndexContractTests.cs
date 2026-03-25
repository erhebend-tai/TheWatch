// MockSpatialIndexContractTests — concrete xUnit test class that runs the full
// SpatialIndexContractTests contract against MockSpatialIndexAdapter (Haversine).
//
// WAL: Ring assignment correctness is life-safety critical. If a volunteer at 150m
// lands in Ring 1 instead of Ring 0, the closest responder may not be dispatched first.
// The determinism test ensures identical queries always produce identical results,
// which is required for the DSL replay/audit guarantee.
//
// Example (adding another adapter):
//   public class RedisGeoSpatialContractTests : SpatialIndexContractTests
//   {
//       protected override ISpatialIndex CreateAdapter() => new RedisSpatialAdapter(conn);
//   }

using TheWatch.Data.Adapters.Mock;
using TheWatch.Data.Testing;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Tests;

public class MockSpatialIndexContractTests : SpatialIndexContractTests
{
    protected override ISpatialIndex CreateAdapter() => new MockSpatialIndexAdapter();

    [Fact]
    public override Task IndexAndFind_ReturnsNearbyEntities()
        => base.IndexAndFind_ReturnsNearbyEntities();

    [Fact]
    public override Task Remove_EntityNotFoundAfter()
        => base.Remove_EntityNotFoundAfter();

    [Fact]
    public override Task DeterministicQueries_ProduceIdenticalResults()
        => base.DeterministicQueries_ProduceIdenticalResults();

    [Fact]
    public override Task RingAssignment_CorrectForKnownDistances()
        => base.RingAssignment_CorrectForKnownDistances();

    [Fact]
    public override Task GetRing_ReturnsOnlyEntitiesInSpecifiedRing()
        => base.GetRing_ReturnsOnlyEntitiesInSpecifiedRing();

    [Fact]
    public override Task UpdatePosition_MovesEntity()
        => base.UpdatePosition_MovesEntity();
}
