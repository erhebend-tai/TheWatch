// SqlServerAuditTrailAdapter — IAuditTrail over EF Core (TheWatchDbContext.AuditEntries).
// Inherits query/statistics from AuditTrailAdapterBase; overrides core CRUD + integrity.
// Example:
//   services.AddScoped<IAuditTrail, SqlServerAuditTrailAdapter>();

using Microsoft.EntityFrameworkCore;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Data.Adapters.SqlServer;

public class SqlServerAuditTrailAdapter : AuditTrailAdapterBase
{
    private readonly TheWatchDbContext _db;
    private long _sequence;

    public SqlServerAuditTrailAdapter(TheWatchDbContext db) => _db = db;

    public override async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var latest = await _db.AuditEntries.OrderByDescending(e => e.Timestamp).FirstOrDefaultAsync(ct);
        entry.Timestamp = DateTime.UtcNow;
        entry.SequenceNumber = (latest?.SequenceNumber ?? 0) + 1;
        entry.PreviousHash = latest?.Hash;
        entry.Hash = ComputeHash(entry);
        await _db.AuditEntries.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }

    public override async Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.Timestamp >= from && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public override async Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.EntityType == entityType && e.EntityId == entityId).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public override async Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.UserId == userId).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public override async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        var entries = await _db.AuditEntries.OrderBy(e => e.SequenceNumber).ToListAsync(ct);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var expectedPrev = i > 0 ? entries[i - 1].Hash : null;
            if (entry.PreviousHash != expectedPrev) return false;
            if (ComputeHash(entry) != entry.Hash) return false;
        }
        return true;
    }

    public override async Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default) =>
        await _db.AuditEntries.OrderByDescending(e => e.SequenceNumber).FirstOrDefaultAsync(ct);
}
