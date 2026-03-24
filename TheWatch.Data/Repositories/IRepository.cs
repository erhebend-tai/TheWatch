// =============================================================================
// IRepository.cs — Generic repository contract for all data providers.
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   All implementations MUST log operation intent BEFORE execution.
//   This enables crash recovery diagnostics and audit trails across
//   SQL Server, PostgreSQL, Cosmos DB, Firebase RTDB, and Firestore.
//
// Example:
//   public class SqlServerRepository<T> : IRepository<T> where T : class
//   {
//       public async Task<T> AddAsync(T entity, CancellationToken ct = default)
//       {
//           _logger.LogInformation("[WAL] Adding {EntityType}", typeof(T).Name);
//           // ... persist ...
//           _logger.LogInformation("[WAL] Added {EntityType} OK", typeof(T).Name);
//       }
//   }
//
// Providers checked for parity:
//   - Spring Data JPA (Java) — similar generic repo, no feature gap
//   - Django ORM (Python) — manager pattern, covered
//   - TypeORM (TypeScript) — repository pattern, covered
//   - Dapper — micro-ORM only, not applicable
//   - Marten (PostgreSQL doc DB) — IDocumentSession, different paradigm
//   - MongoDB C# Driver — IMongoCollection<T>, separate implementation path
// =============================================================================

using System.Linq.Expressions;

namespace TheWatch.Data.Repositories;

/// <summary>
/// Generic repository interface providing CRUD operations for any entity type.
/// Each provider (SQL Server, PostgreSQL, Cosmos DB, Firebase, Firestore)
/// supplies its own implementation.
/// </summary>
/// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>Retrieves an entity by its unique identifier.</summary>
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Retrieves all entities of this type.</summary>
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Finds entities matching the given predicate.</summary>
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>Adds a new entity and returns the persisted instance.</summary>
    Task<T> AddAsync(T entity, CancellationToken ct = default);

    /// <summary>Adds multiple entities in a single batch.</summary>
    Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>Updates an existing entity.</summary>
    Task UpdateAsync(T entity, CancellationToken ct = default);

    /// <summary>Deletes an entity by its unique identifier.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Returns the total count of entities.</summary>
    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>Checks whether an entity with the given identifier exists.</summary>
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
}
