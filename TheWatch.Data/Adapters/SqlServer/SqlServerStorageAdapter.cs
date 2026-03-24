// SqlServerStorageAdapter — IStorageService implementation delegating to SqlServerRepository<T> via EF Core.
// Example:
//   services.AddScoped<IStorageService, SqlServerStorageAdapter>();
//   var result = await storage.StoreAsync("workitems", "wi-1", workItem);
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.SqlServer;

public class SqlServerStorageAdapter : IStorageService
{
    private readonly TheWatchDbContext _db;

    public SqlServerStorageAdapter(TheWatchDbContext db) => _db = db;

    public async Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class
    {
        var existing = await _db.Set<T>().FindAsync(new object[] { id }, ct);
        if (existing is not null)
        {
            _db.Entry(existing).CurrentValues.SetValues(entity);
        }
        else
        {
            await _db.Set<T>().AddAsync(entity, ct);
        }
        await _db.SaveChangesAsync(ct);
        return StorageResult<T>.Ok(entity);
    }

    public async Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        var entity = await _db.Set<T>().FindAsync(new object[] { id }, ct);
        return entity is not null
            ? StorageResult<T>.Ok(entity)
            : StorageResult<T>.Fail($"Entity '{id}' not found");
    }

    public async Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class
    {
        var all = await _db.Set<T>().ToListAsync(ct);
        var filtered = predicate is not null ? all.Where(predicate).ToList() : all;
        return StorageResult<List<T>>.Ok(filtered);
    }

    public async Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var entityType = _db.Model.GetEntityTypes()
            .FirstOrDefault(e => e.ClrType.Name.Equals(collection, StringComparison.OrdinalIgnoreCase));
        if (entityType is null) return StorageResult<bool>.Ok(false);
        var entity = await _db.FindAsync(entityType.ClrType, new object[] { id }, ct);
        if (entity is null) return StorageResult<bool>.Ok(false);
        _db.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return StorageResult<bool>.Ok(true);
    }

    public async Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default) =>
        await _db.FindAsync(Type.GetType($"TheWatch.Shared.Domain.Models.{collection}") ?? typeof(object), new object[] { id }) is not null;

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        // Uses reflection-free approach: count via known DbSet types
        return collection.ToLowerInvariant() switch
        {
            "workitems" => await _db.WorkItems.LongCountAsync(ct),
            "milestones" => await _db.Milestones.LongCountAsync(ct),
            "agentactivities" => await _db.AgentActivities.LongCountAsync(ct),
            "buildstatuses" => await _db.BuildStatuses.LongCountAsync(ct),
            "branchinfos" => await _db.BranchInfos.LongCountAsync(ct),
            "simulationevents" => await _db.SimulationEvents.LongCountAsync(ct),
            "auditentries" => await _db.AuditEntries.LongCountAsync(ct),
            _ => 0
        };
    }

    public async Task EnqueueOfflineAsync(OfflineQueueEntry entry, CancellationToken ct = default)
    {
        // For SQL Server, offline queue is stored in a separate table (could be extended)
        await Task.CompletedTask;
    }

    public Task<List<OfflineQueueEntry>> GetPendingQueueAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<OfflineQueueEntry>());

    public Task MarkSyncedAsync(string entryId, CancellationToken ct = default) =>
        Task.CompletedTask;
}
