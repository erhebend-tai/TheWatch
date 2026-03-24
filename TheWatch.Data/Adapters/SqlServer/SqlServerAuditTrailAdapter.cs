// SqlServerAuditTrailAdapter — IAuditTrail over EF Core (TheWatchDbContext.AuditEntries).
// SHA-256 Merkle chain computed on AppendAsync.
// Example:
//   services.AddScoped<IAuditTrail, SqlServerAuditTrailAdapter>();
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.SqlServer;

public class SqlServerAuditTrailAdapter : IAuditTrail
{
    private readonly TheWatchDbContext _db;

    public SqlServerAuditTrailAdapter(TheWatchDbContext db) => _db = db;

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var latest = await _db.AuditEntries.OrderByDescending(e => e.Timestamp).FirstOrDefaultAsync(ct);
        entry.Timestamp = DateTime.UtcNow;
        entry.PreviousHash = latest?.Hash;
        entry.Hash = ComputeHash(entry);
        await _db.AuditEntries.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.Timestamp >= from && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public async Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.EntityType == entityType && e.EntityId == entityId).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public async Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default) =>
        await _db.AuditEntries.Where(e => e.UserId == userId).OrderBy(e => e.Timestamp).ToListAsync(ct);

    public async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        var entries = await _db.AuditEntries.OrderBy(e => e.Timestamp).ToListAsync(ct);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var expectedPrev = i > 0 ? entries[i - 1].Hash : null;
            if (entry.PreviousHash != expectedPrev) return false;
            if (ComputeHash(entry) != entry.Hash) return false;
        }
        return true;
    }

    public async Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default) =>
        await _db.AuditEntries.OrderByDescending(e => e.Timestamp).FirstOrDefaultAsync(ct);

    private static string ComputeHash(AuditEntry entry)
    {
        var input = $"{entry.PreviousHash}|{entry.Timestamp:O}|{entry.Action}|{entry.UserId}|{entry.EntityType}|{entry.EntityId}|{entry.OldValue}|{entry.NewValue}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
