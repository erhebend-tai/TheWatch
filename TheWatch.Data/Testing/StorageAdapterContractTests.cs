// StorageAdapterContractTests — abstract xUnit base class for IStorageService contract verification.
// Every adapter (Mock, SqlServer, CosmosDb, Firebase, Firestore) creates a derived test class.
// Example:
//   public class MockStorageContractTests : StorageAdapterContractTests
//   {
//       protected override IStorageService CreateAdapter() => new MockStorageAdapter();
//   }
//
// Life-Safety Tests:
//   - SOS Trigger Reliability: StoreAsync must complete in <500ms round-trip
//   - Offline Queue Integrity: FIFO ordering, zero-loss guarantee
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Testing;

public abstract class StorageAdapterContractTests
{
    protected abstract IStorageService CreateAdapter();

    // --- Core CRUD ---

    public virtual async Task StoreAndRetrieve_RoundTrips_Successfully()
    {
        var adapter = CreateAdapter();
        var item = new WorkItem { Id = "wi-1", Title = "Test SOS Trigger", Description = "Life safety test" };

        var storeResult = await adapter.StoreAsync("workitems", "wi-1", item);
        var retrieveResult = await adapter.RetrieveAsync<WorkItem>("workitems", "wi-1");

        Assert(storeResult.Success, "Store must succeed");
        Assert(retrieveResult.Success, "Retrieve must succeed");
        Assert(retrieveResult.Data?.Title == "Test SOS Trigger", "Data must round-trip");
    }

    public virtual async Task Delete_RemovesEntity()
    {
        var adapter = CreateAdapter();
        await adapter.StoreAsync("workitems", "del-1", new WorkItem { Id = "del-1", Title = "Delete Me" });

        var deleteResult = await adapter.DeleteAsync("workitems", "del-1");
        var exists = await adapter.ExistsAsync("workitems", "del-1");

        Assert(deleteResult.Success, "Delete must succeed");
        Assert(!exists, "Entity must not exist after delete");
    }

    public virtual async Task Query_ReturnsFilteredResults()
    {
        var adapter = CreateAdapter();
        await adapter.StoreAsync("items", "q-1", new WorkItem { Id = "q-1", Title = "Alpha" });
        await adapter.StoreAsync("items", "q-2", new WorkItem { Id = "q-2", Title = "Beta" });

        var result = await adapter.QueryAsync<WorkItem>("items", w => w.Title == "Alpha");

        Assert(result.Success, "Query must succeed");
        Assert(result.Data?.Count == 1, "Must return exactly one filtered result");
    }

    // --- SOS Trigger Reliability (Life Safety) ---

    public virtual async Task SOSTrigger_StoreRetrieve_Under500ms()
    {
        var adapter = CreateAdapter();
        var alert = new WorkItem { Id = "sos-1", Title = "SOS TRIGGER", Description = "Emergency" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await adapter.StoreAsync("alerts", "sos-1", alert);
        var result = await adapter.RetrieveAsync<WorkItem>("alerts", "sos-1");
        sw.Stop();

        Assert(result.Success, "SOS store+retrieve must succeed");
        Assert(sw.ElapsedMilliseconds < 500, $"SOS round-trip must be <500ms, was {sw.ElapsedMilliseconds}ms");
    }

    // --- Offline Queue Integrity ---

    public virtual async Task OfflineQueue_MaintainsFIFO_ZeroLoss()
    {
        var adapter = CreateAdapter();
        var entries = Enumerable.Range(1, 10).Select(i => new OfflineQueueEntry
        {
            Id = $"oq-{i}", OperationType = "Create", EntityType = "Alert",
            SerializedPayload = $"{{\"index\":{i}}}", QueuedAt = DateTime.UtcNow.AddSeconds(i)
        }).ToList();

        foreach (var entry in entries)
            await adapter.EnqueueOfflineAsync(entry);

        var pending = await adapter.GetPendingQueueAsync();

        Assert(pending.Count == 10, $"Must have 10 pending entries, got {pending.Count}");
        // Verify FIFO: first queued = first in list
        Assert(pending[0].Id == "oq-1", "FIFO: first entry must be oq-1");
        Assert(pending[9].Id == "oq-10", "FIFO: last entry must be oq-10");
    }

    public virtual async Task OfflineQueue_MarkSynced_RemovesFromPending()
    {
        var adapter = CreateAdapter();
        await adapter.EnqueueOfflineAsync(new OfflineQueueEntry { Id = "sync-1", OperationType = "Create", EntityType = "Alert", SerializedPayload = "{}" });

        await adapter.MarkSyncedAsync("sync-1");
        var pending = await adapter.GetPendingQueueAsync();

        Assert(pending.All(p => p.Id != "sync-1"), "Synced entry must not appear in pending");
    }

    protected static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Contract test failed: {message}");
    }
}
