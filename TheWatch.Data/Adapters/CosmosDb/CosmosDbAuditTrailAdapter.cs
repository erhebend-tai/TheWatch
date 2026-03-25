// CosmosDbAuditTrailAdapter — IAuditTrail over Cosmos DB with change feed awareness.
// Inherits query/statistics from AuditTrailAdapterBase; overrides core CRUD + integrity.
// Example:
//   services.AddScoped<IAuditTrail, CosmosDbAuditTrailAdapter>();

using Microsoft.Azure.Cosmos;
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Data.Adapters.CosmosDb;

public class CosmosDbAuditTrailAdapter : AuditTrailAdapterBase
{
    private readonly Container _container;

    public CosmosDbAuditTrailAdapter(CosmosClient client, string databaseId = "TheWatch")
    {
        _container = client.GetContainer(databaseId, "audit_trail");
    }

    public override async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var latest = await GetLatestEntryAsync(ct);
        entry.Timestamp = DateTime.UtcNow;
        entry.SequenceNumber = (latest?.SequenceNumber ?? 0) + 1;
        entry.PreviousHash = latest?.Hash;
        entry.Hash = ComputeHash(entry);
        await _container.CreateItemAsync(entry, new PartitionKey(entry.Id), cancellationToken: ct);
    }

    public override async Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.Timestamp >= '{from:O}' AND c.Timestamp <= '{to:O}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
            results.AddRange(await query.ReadNextAsync(ct));
        return results;
    }

    public override async Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.EntityType = '{entityType}' AND c.EntityId = '{entityId}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
            results.AddRange(await query.ReadNextAsync(ct));
        return results;
    }

    public override async Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.UserId = '{userId}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
            results.AddRange(await query.ReadNextAsync(ct));
        return results;
    }

    public override async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>("SELECT * FROM c ORDER BY c.Timestamp");
        var entries = new List<AuditEntry>();
        while (query.HasMoreResults)
            entries.AddRange(await query.ReadNextAsync(ct));

        for (int i = 0; i < entries.Count; i++)
        {
            var expectedPrev = i > 0 ? entries[i - 1].Hash : null;
            if (entries[i].PreviousHash != expectedPrev) return false;
            if (ComputeHash(entries[i]) != entries[i].Hash) return false;
        }
        return true;
    }

    public override async Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            "SELECT TOP 1 * FROM c ORDER BY c.Timestamp DESC");
        if (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            return response.FirstOrDefault();
        }
        return null;
    }
}
