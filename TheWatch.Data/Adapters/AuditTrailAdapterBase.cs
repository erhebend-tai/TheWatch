// AuditTrailAdapterBase — shared implementation of new IAuditTrail query methods.
// Concrete adapters (SqlServer, PostgreSql, CosmosDb) override the core CRUD methods
// and inherit the query/statistics implementations that operate on the base queries.
//
// The hash function is centralized here so all adapters produce identical hashes.

using System.Security.Cryptography;
using System.Text;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters;

public abstract class AuditTrailAdapterBase : IAuditTrail
{
    // ── Abstract — each adapter implements storage-specific versions ──

    public abstract Task AppendAsync(AuditEntry entry, CancellationToken ct = default);
    public abstract Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default);
    public abstract Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default);
    public abstract Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default);
    public abstract Task<bool> VerifyIntegrityAsync(CancellationToken ct = default);
    public abstract Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default);

    // ── Default implementations using GetTrailAsync ─────────────

    public virtual async Task AppendBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        foreach (var entry in entries)
            await AppendAsync(entry, ct);
    }

    public virtual async Task<List<AuditEntry>> GetTrailByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        var all = await GetTrailAsync(DateTime.MinValue, DateTime.MaxValue, ct);
        return all.Where(e => e.CorrelationId == correlationId).OrderBy(e => e.Timestamp).ToList();
    }

    public virtual async Task<List<AuditEntry>> GetTrailByActionAsync(AuditAction action, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var all = await GetTrailAsync(from ?? DateTime.MinValue, to ?? DateTime.MaxValue, ct);
        return all.Where(e => e.Action == action).ToList();
    }

    public virtual async Task<List<AuditEntry>> GetTrailBySeverityAsync(AuditSeverity minSeverity, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var all = await GetTrailAsync(from, to, ct);
        return all.Where(e => e.Severity >= minSeverity).ToList();
    }

    public virtual async Task<List<AuditEntry>> GetTrailBySourceAsync(string sourceSystem, string? sourceComponent = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var all = await GetTrailAsync(from ?? DateTime.MinValue, to ?? DateTime.MaxValue, ct);
        var query = all.Where(e => e.SourceSystem == sourceSystem);
        if (sourceComponent is not null) query = query.Where(e => e.SourceComponent == sourceComponent);
        return query.ToList();
    }

    public virtual async Task<bool> VerifyIntegrityRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var entries = await GetTrailAsync(from, to, ct);
        foreach (var entry in entries)
        {
            if (ComputeHash(entry) != entry.Hash)
                return false;
        }
        return true;
    }

    public virtual async Task<AuditStatistics> GetStatisticsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var range = await GetTrailAsync(from, to, ct);

        return new AuditStatistics(
            TotalEntries: range.Count,
            CriticalEntries: range.Count(e => e.Severity == AuditSeverity.Critical),
            FailedEntries: range.Count(e => e.Outcome == AuditOutcome.Failure),
            DeniedEntries: range.Count(e => e.Outcome == AuditOutcome.Denied),
            CountByAction: range.GroupBy(e => e.Action.ToString()).ToDictionary(g => g.Key, g => (long)g.Count()),
            CountBySourceSystem: range.GroupBy(e => e.SourceSystem).ToDictionary(g => g.Key, g => (long)g.Count()),
            CountBySeverity: range.GroupBy(e => e.Severity.ToString()).ToDictionary(g => g.Key, g => (long)g.Count()),
            CountByOutcome: range.GroupBy(e => e.Outcome.ToString()).ToDictionary(g => g.Key, g => (long)g.Count()),
            OldestEntry: range.Count > 0 ? range.Min(e => e.Timestamp) : DateTime.UtcNow,
            NewestEntry: range.Count > 0 ? range.Max(e => e.Timestamp) : DateTime.UtcNow,
            ChainIntact: range.Count == 0 || await VerifyIntegrityRangeAsync(from, to, ct)
        );
    }

    // ── Shared hash function — identical across all adapters ────

    public static string ComputeHash(AuditEntry entry)
    {
        var input = string.Join("|",
            entry.PreviousHash ?? "",
            entry.SequenceNumber,
            entry.Timestamp.ToString("O"),
            (int)entry.Action,
            entry.UserId,
            entry.ActorRole,
            entry.EntityType,
            entry.EntityId,
            entry.OldValue ?? "",
            entry.NewValue ?? "",
            entry.CorrelationId,
            entry.CausationId ?? "",
            entry.SourceSystem,
            entry.SourceComponent,
            (int)entry.Severity,
            (int)entry.DataClassification,
            (int)entry.Outcome,
            entry.Reason ?? ""
        );
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
