// =============================================================================
// IUnitOfWork.cs — Transactional unit-of-work contract.
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Implementations log transaction boundaries (begin, commit, rollback) to
//   enable replay and crash recovery diagnostics.
//
// Example:
//   await using var uow = serviceProvider.GetRequiredService<IUnitOfWork>();
//   await uow.BeginTransactionAsync();
//   try
//   {
//       // ... repository operations ...
//       await uow.CommitAsync();
//   }
//   catch
//   {
//       await uow.RollbackAsync();
//       throw;
//   }
//
// Providers checked:
//   - EF Core DbContext.Database.BeginTransactionAsync — used for SQL/Postgres
//   - Cosmos DB TransactionalBatch — used for CosmosDbUnitOfWork
//   - Firestore RunTransactionAsync — used for FirestoreUnitOfWork
//   - Firebase RTDB — no true transactions; uses multi-path updates
// =============================================================================

namespace TheWatch.Data.Repositories;

/// <summary>
/// Abstracts transactional boundaries across all supported data providers.
/// Implementations coordinate SaveChanges/Commit semantics for their backend.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    /// <summary>Begins a new transaction (or transaction scope).</summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Commits all pending changes within the current transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Rolls back all pending changes within the current transaction.</summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>Saves all pending changes without an explicit transaction boundary.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
