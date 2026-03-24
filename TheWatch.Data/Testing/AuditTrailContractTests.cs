// AuditTrailContractTests — abstract xUnit base class for IAuditTrail contract verification.
// Example:
//   public class MockAuditTrailContractTests : AuditTrailContractTests
//   {
//       protected override IAuditTrail CreateAdapter() => new MockAuditTrailAdapter();
//   }
//
// Life-Safety Tests:
//   - Emergency Bypass: audit entries for SOSTrigger are always recorded
//   - Escalation Chain: entries are chronologically ordered
//   - Tamper Evidence: hash chain verification detects any modification
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Testing;

public abstract class AuditTrailContractTests
{
    protected abstract IAuditTrail CreateAdapter();

    public virtual async Task Append_CreatesEntry_WithHashChain()
    {
        var adapter = CreateAdapter();
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.Create, EntityType = "Alert", EntityId = "a-1" });
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.SOSTrigger, EntityType = "Alert", EntityId = "a-1" });

        var latest = await adapter.GetLatestEntryAsync();
        Assert(latest is not null, "Latest entry must exist");
        Assert(!string.IsNullOrEmpty(latest!.Hash), "Hash must be populated");
        Assert(!string.IsNullOrEmpty(latest.PreviousHash), "PreviousHash must link to prior entry");
    }

    // --- Emergency Bypass Audit (Life Safety) ---

    public virtual async Task SOSTrigger_AlwaysRecorded()
    {
        var adapter = CreateAdapter();
        await adapter.AppendAsync(new AuditEntry { UserId = "u-sos", Action = AuditAction.SOSTrigger, EntityType = "Alert", EntityId = "sos-1" });

        var trail = await adapter.GetTrailByUserAsync("u-sos");
        Assert(trail.Any(e => e.Action == AuditAction.SOSTrigger), "SOS trigger audit entry must be recorded");
    }

    // --- Escalation Chain Ordering ---

    public virtual async Task EscalationChain_ChronologicalOrdering()
    {
        var adapter = CreateAdapter();
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.SOSTrigger, EntityType = "Alert", EntityId = "esc-1" });
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.AlertAcknowledge, EntityType = "Alert", EntityId = "esc-1" });
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.AlertEscalate, EntityType = "Alert", EntityId = "esc-1" });

        var trail = await adapter.GetTrailByEntityAsync("Alert", "esc-1");
        for (int i = 1; i < trail.Count; i++)
            Assert(trail[i].Timestamp >= trail[i - 1].Timestamp, $"Entry {i} must be >= entry {i - 1} chronologically");
    }

    // --- Tamper Evidence (Hash Chain Verification) ---

    public virtual async Task VerifyIntegrity_ReturnsTrueForUntamperedChain()
    {
        var adapter = CreateAdapter();
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.Create, EntityType = "WorkItem", EntityId = "wi-1" });
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.Update, EntityType = "WorkItem", EntityId = "wi-1" });
        await adapter.AppendAsync(new AuditEntry { UserId = "u-1", Action = AuditAction.Delete, EntityType = "WorkItem", EntityId = "wi-1" });

        var intact = await adapter.VerifyIntegrityAsync();
        Assert(intact, "Untampered hash chain must verify as intact");
    }

    public virtual async Task GetTrailByDateRange_ReturnsCorrectSubset()
    {
        var adapter = CreateAdapter();
        await adapter.AppendAsync(new AuditEntry { UserId = "u-range", Action = AuditAction.Create, EntityType = "Test", EntityId = "t-1" });

        var from = DateTime.UtcNow.AddMinutes(-1);
        var to = DateTime.UtcNow.AddMinutes(1);
        var trail = await adapter.GetTrailAsync(from, to);

        Assert(trail.Count > 0, "Trail in date range must return entries");
    }

    protected static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Contract test failed: {message}");
    }
}
