// MockAuditTrailAdapter — in-memory audit trail with SHA-256 Merkle hash chain.
// Thread-safe, lock-based. VerifyIntegrity walks the chain and validates hashes.
//
// Example:
//   services.AddSingleton<IAuditTrail, MockAuditTrailAdapter>();
//   await audit.AppendAsync(new AuditEntry { Action = AuditAction.SOSTrigger });
//   bool ok = await audit.VerifyIntegrityAsync(); // true if chain intact
//   var chain = await audit.GetTrailByCorrelationAsync("req-123"); // full SOS flow

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockAuditTrailAdapter : AuditTrailAdapterBase
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _lock = new();
    private long _sequence;

    public override Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        lock (_lock)
        {
            entry.Timestamp = DateTime.UtcNow;
            entry.SequenceNumber = ++_sequence;
            entry.PreviousHash = _entries.Count > 0 ? _entries[^1].Hash : null;
            entry.Hash = ComputeHash(entry);
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    public override Task AppendBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var entry in entries)
            {
                entry.Timestamp = DateTime.UtcNow;
                entry.SequenceNumber = ++_sequence;
                entry.PreviousHash = _entries.Count > 0 ? _entries[^1].Hash : null;
                entry.Hash = ComputeHash(entry);
                _entries.Add(entry);
            }
        }
        return Task.CompletedTask;
    }

    public override Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList());
    }

    public override Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_entries.Where(e => e.EntityType == entityType && e.EntityId == entityId).ToList());
    }

    public override Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_entries.Where(e => e.UserId == userId).ToList());
    }

    public override Task<List<AuditEntry>> GetTrailByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_entries.Where(e => e.CorrelationId == correlationId).OrderBy(e => e.Timestamp).ToList());
    }

    public override Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var expectedPrev = i > 0 ? _entries[i - 1].Hash : null;
                if (entry.PreviousHash != expectedPrev)
                    return Task.FromResult(false);
                if (ComputeHash(entry) != entry.Hash)
                    return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }

    public override Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_entries.Count > 0 ? _entries[^1] : null);
    }
}
