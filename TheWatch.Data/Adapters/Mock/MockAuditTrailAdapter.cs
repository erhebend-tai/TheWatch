// MockAuditTrailAdapter — in-memory audit trail with SHA-256 Merkle hash chain.
// VerifyIntegrity actually walks the chain and validates hashes.
// Example:
//   services.AddSingleton<IAuditTrail, MockAuditTrailAdapter>();
//   await audit.AppendAsync(new AuditEntry { Action = AuditAction.SOSTrigger });
//   bool ok = await audit.VerifyIntegrityAsync(); // true if chain intact
using System.Security.Cryptography;
using System.Text;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockAuditTrailAdapter : IAuditTrail
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _lock = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        lock (_lock)
        {
            entry.Timestamp = DateTime.UtcNow;
            entry.PreviousHash = _entries.Count > 0 ? _entries[^1].Hash : null;
            entry.Hash = ComputeHash(entry);
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList());
        }
    }

    public Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries
                .Where(e => e.EntityType == entityType && e.EntityId == entityId).ToList());
        }
    }

    public Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries.Where(e => e.UserId == userId).ToList());
        }
    }

    public Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var expectedPrev = i > 0 ? _entries[i - 1].Hash : null;
                if (entry.PreviousHash != expectedPrev)
                    return Task.FromResult(false);

                var recomputed = ComputeHash(entry);
                if (recomputed != entry.Hash)
                    return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }

    public Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries.Count > 0 ? _entries[^1] : null);
        }
    }

    private static string ComputeHash(AuditEntry entry)
    {
        var input = $"{entry.PreviousHash}|{entry.Timestamp:O}|{entry.Action}|{entry.UserId}|{entry.EntityType}|{entry.EntityId}|{entry.OldValue}|{entry.NewValue}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
