// CosmosDbAuditTrailAdapter — IAuditTrail over Cosmos DB with change feed awareness.
// Merkle hash chain maintained on AppendAsync.
// Example:
//   services.AddScoped<IAuditTrail, CosmosDbAuditTrailAdapter>();
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.CosmosDb;

public class CosmosDbAuditTrailAdapter : IAuditTrail
{
    private readonly Container _container;

    public CosmosDbAuditTrailAdapter(CosmosClient client, string databaseId = "TheWatch")
    {
        _container = client.GetContainer(databaseId, "audit_trail");
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var latest = await GetLatestEntryAsync(ct);
        entry.Timestamp = DateTime.UtcNow;
        entry.PreviousHash = latest?.Hash;
        entry.Hash = ComputeHash(entry);
        await _container.CreateItemAsync(entry, new PartitionKey(entry.Id), cancellationToken: ct);
    }

    public async Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.Timestamp >= '{from:O}' AND c.Timestamp <= '{to:O}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(ct);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.EntityType = '{entityType}' AND c.EntityId = '{entityId}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
            results.AddRange(await query.ReadNextAsync(ct));
        return results;
    }

    public async Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            $"SELECT * FROM c WHERE c.UserId = '{userId}' ORDER BY c.Timestamp");
        var results = new List<AuditEntry>();
        while (query.HasMoreResults)
            results.AddRange(await query.ReadNextAsync(ct));
        return results;
    }

    public async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
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

    public async Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default)
    {
        var query = _container.GetItemQueryIterator<AuditEntry>(
            "SELECT TOP 1 * FROM c ORDER BY c.Timestamp DESC");
        if (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(ct);
            return response.FirstOrDefault();
        }
        return null;
    }

    private static string ComputeHash(AuditEntry entry)
    {
        var input = $"{entry.PreviousHash}|{entry.Timestamp:O}|{entry.Action}|{entry.UserId}|{entry.EntityType}|{entry.EntityId}|{entry.OldValue}|{entry.NewValue}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
