// MockEvidenceAdapterTests — standalone xUnit tests for MockEvidenceAdapter.
// Covers the IEvidencePort contract: submit, retrieve, filter, status update, integrity.
//
// WAL: Evidence integrity is life-safety critical. In a real incident, tampered evidence
// could compromise legal proceedings. VerifyIntegrityAsync must return true for any
// submission that has not been externally modified.
//
// Example (evidence submission flow):
//   1. User captures photo during SOS → SubmitAsync
//   2. Server processes (thumbnail, moderation) → UpdateStatusAsync
//   3. Responder retrieves evidence → GetByRequestIdAsync
//   4. Legal review → VerifyIntegrityAsync confirms no tampering

using TheWatch.Data.Adapters.Mock;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Tests;

public class MockEvidenceAdapterTests
{
    private MockEvidenceAdapter CreateAdapter() => new();

    [Fact]
    public async Task SubmitAsync_StoresAndReturnsSubmission()
    {
        var adapter = CreateAdapter();
        var submission = new EvidenceSubmission
        {
            Id = "ev-1",
            RequestId = "req-1",
            UserId = "user-1",
            SubmitterId = "user-1",
            Phase = SubmissionPhase.Active,
            SubmissionType = SubmissionType.Image,
            Title = "Kitchen fire photo"
        };

        var result = await adapter.SubmitAsync(submission);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("ev-1", result.Data!.Id);
        Assert.Equal("Kitchen fire photo", result.Data.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsStoredSubmission()
    {
        var adapter = CreateAdapter();
        var submission = new EvidenceSubmission
        {
            Id = "ev-2",
            RequestId = "req-1",
            UserId = "user-1",
            SubmitterId = "user-1",
            Phase = SubmissionPhase.Active,
            SubmissionType = SubmissionType.Text,
            Title = "Witness statement"
        };
        await adapter.SubmitAsync(submission);

        var result = await adapter.GetByIdAsync("ev-2");

        Assert.True(result.Success);
        Assert.Equal("ev-2", result.Data!.Id);
        Assert.Equal("Witness statement", result.Data.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsFailForMissing()
    {
        var adapter = CreateAdapter();

        var result = await adapter.GetByIdAsync("nonexistent");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetByRequestIdAsync_FiltersCorrectly()
    {
        var adapter = CreateAdapter();
        await adapter.SubmitAsync(new EvidenceSubmission { Id = "ev-a", RequestId = "req-100", UserId = "u1", SubmitterId = "u1" });
        await adapter.SubmitAsync(new EvidenceSubmission { Id = "ev-b", RequestId = "req-100", UserId = "u1", SubmitterId = "u1" });
        await adapter.SubmitAsync(new EvidenceSubmission { Id = "ev-c", RequestId = "req-200", UserId = "u1", SubmitterId = "u1" });

        var result = await adapter.GetByRequestIdAsync("req-100");

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, s => Assert.Equal("req-100", s.RequestId));
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        var adapter = CreateAdapter();
        await adapter.SubmitAsync(new EvidenceSubmission { Id = "ev-status", RequestId = "req-1", UserId = "u1", SubmitterId = "u1", Status = SubmissionStatus.Pending });

        var result = await adapter.UpdateStatusAsync("ev-status", SubmissionStatus.Available);

        Assert.True(result.Success);

        var retrieved = await adapter.GetByIdAsync("ev-status");
        Assert.Equal(SubmissionStatus.Available, retrieved.Data!.Status);
        Assert.NotNull(retrieved.Data.ProcessedAt);
    }

    [Fact]
    public async Task UpdateStatusAsync_FailsForMissing()
    {
        var adapter = CreateAdapter();

        var result = await adapter.UpdateStatusAsync("nonexistent", SubmissionStatus.Available);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ReturnsTrueForValidSubmission()
    {
        var adapter = CreateAdapter();
        await adapter.SubmitAsync(new EvidenceSubmission { Id = "ev-integrity", RequestId = "req-1", UserId = "u1", SubmitterId = "u1" });

        var intact = await adapter.VerifyIntegrityAsync("ev-integrity");

        Assert.True(intact);
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ReturnsFalseForMissing()
    {
        var adapter = CreateAdapter();

        var intact = await adapter.VerifyIntegrityAsync("nonexistent");

        Assert.False(intact);
    }
}
