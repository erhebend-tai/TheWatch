// MockStorageAdapterContractTests — concrete xUnit test class that runs the full
// StorageAdapterContractTests contract against MockStorageAdapter.
//
// WAL: Every adapter (SqlServer, CosmosDb, Firebase, Firestore, Oracle) must have
// an equivalent class in its own test project. If Mock passes but a real adapter fails,
// the adapter has a conformance bug.
//
// Example (adding another adapter):
//   public class FirestoreStorageContractTests : StorageAdapterContractTests
//   {
//       protected override IStorageService CreateAdapter() => new FirestoreStorageAdapter(config);
//   }

using TheWatch.Data.Adapters.Mock;
using TheWatch.Data.Testing;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Tests;

public class MockStorageAdapterContractTests : StorageAdapterContractTests
{
    protected override IStorageService CreateAdapter() => new MockStorageAdapter();

    [Fact]
    public override Task StoreAndRetrieve_RoundTrips_Successfully()
        => base.StoreAndRetrieve_RoundTrips_Successfully();

    [Fact]
    public override Task Delete_RemovesEntity()
        => base.Delete_RemovesEntity();

    [Fact]
    public override Task Query_ReturnsFilteredResults()
        => base.Query_ReturnsFilteredResults();

    [Fact]
    public override Task SOSTrigger_StoreRetrieve_Under500ms()
        => base.SOSTrigger_StoreRetrieve_Under500ms();

    [Fact]
    public override Task OfflineQueue_MaintainsFIFO_ZeroLoss()
        => base.OfflineQueue_MaintainsFIFO_ZeroLoss();

    [Fact]
    public override Task OfflineQueue_MarkSynced_RemovesFromPending()
        => base.OfflineQueue_MarkSynced_RemovesFromPending();
}
