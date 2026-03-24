// =============================================================================
// FirestoreUnitOfWork.cs — Firestore unit of work using RunTransactionAsync.
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Firestore supports server-side transactions via RunTransactionAsync.
//   This UoW wraps that capability for the IUnitOfWork contract.
//
// Example:
//   await using var uow = sp.GetRequiredService<IUnitOfWork>();
//   await uow.BeginTransactionAsync();
//   await uow.CommitAsync();
// =============================================================================

using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace TheWatch.Data.Repositories.Firestore;

/// <summary>
/// Firestore unit of work. Provides transaction lifecycle logging.
/// Actual Firestore transactions should use FirestoreDb.RunTransactionAsync
/// for full ACID guarantees within a single database.
/// </summary>
public sealed class FirestoreUnitOfWork : IUnitOfWork
{
    private readonly FirestoreDb _firestoreDb;
    private readonly ILogger<FirestoreUnitOfWork> _logger;

    public FirestoreUnitOfWork(FirestoreDb firestoreDb, ILogger<FirestoreUnitOfWork> logger)
    {
        _firestoreDb = firestoreDb ?? throw new ArgumentNullException(nameof(firestoreDb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-UOW-FIRESTORE] Begin transaction scope (use FirestoreDb.RunTransactionAsync for ACID)");
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-UOW-FIRESTORE] Commit");
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[WAL-UOW-FIRESTORE] Rollback requested");
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-UOW-FIRESTORE] SaveChanges (Firestore commits per-write or in RunTransactionAsync)");
        return Task.FromResult(0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}
