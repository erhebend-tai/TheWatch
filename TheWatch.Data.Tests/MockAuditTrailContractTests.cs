// MockAuditTrailContractTests — concrete xUnit test class that runs the full
// AuditTrailContractTests contract against MockAuditTrailAdapter.
//
// WAL: The hash-chain integrity test (VerifyIntegrity_ReturnsTrueForUntamperedChain)
// is the most critical test here. If any adapter silently drops or reorders entries,
// the tamper-evidence chain breaks and the audit trail is untrustworthy.
//
// Example (adding another adapter):
//   public class CosmosAuditTrailContractTests : AuditTrailContractTests
//   {
//       protected override IAuditTrail CreateAdapter() => new CosmosAuditTrailAdapter(client);
//   }

using TheWatch.Data.Adapters.Mock;
using TheWatch.Data.Testing;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Tests;

public class MockAuditTrailContractTests : AuditTrailContractTests
{
    protected override IAuditTrail CreateAdapter() => new MockAuditTrailAdapter();

    [Fact]
    public override Task Append_CreatesEntry_WithHashChain()
        => base.Append_CreatesEntry_WithHashChain();

    [Fact]
    public override Task SOSTrigger_AlwaysRecorded()
        => base.SOSTrigger_AlwaysRecorded();

    [Fact]
    public override Task EscalationChain_ChronologicalOrdering()
        => base.EscalationChain_ChronologicalOrdering();

    [Fact]
    public override Task VerifyIntegrity_ReturnsTrueForUntamperedChain()
        => base.VerifyIntegrity_ReturnsTrueForUntamperedChain();

    [Fact]
    public override Task GetTrailByDateRange_ReturnsCorrectSubset()
        => base.GetTrailByDateRange_ReturnsCorrectSubset();
}
