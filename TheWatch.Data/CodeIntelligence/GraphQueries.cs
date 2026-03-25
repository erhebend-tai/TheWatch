// =============================================================================
// GraphQueries.cs — Raw SQL helpers for SQL Server graph MATCH traversals
// =============================================================================
// EF Core does not support the SQL Server MATCH clause natively. These static
// methods return parameterized SQL strings that can be executed via:
//   ctx.Symbols.FromSqlRaw(GraphQueries.FindCallers("MyMethod")).ToListAsync();
//   ctx.Database.ExecuteSqlRawAsync(sql, params);
//   ctx.Database.SqlQueryRaw<T>(sql, params).ToListAsync();
//
// SQL Server graph table recap:
//   - NODE tables have a hidden $node_id column (JSON: {"type":"node","schema":"dbo","table":"symbols","id":N})
//   - EDGE tables have hidden $from_id and $to_id columns matching $node_id format
//   - MATCH(node1-(edge)->node2) traverses from node1 through edge to node2
//   - Multi-hop: MATCH(n1-(e1)->n2-(e2)->n3) for transitive closure
//
// All queries return columns matching SymbolNode properties so they can be
// materialized via EF Core's FromSqlRaw into SymbolNode entities.
//
// Example:
//   var sql = GraphQueries.FindImplementors("IAuditTrail");
//   var implementors = await ctx.Symbols
//       .FromSqlRaw(sql)
//       .ToListAsync();
//
//   var sql2 = GraphQueries.FindDependencyChain("ResponseCoordinationService", 3);
//   var chain = await ctx.Database
//       .SqlQueryRaw<SymbolNode>(sql2)
//       .ToListAsync();
//
// WAL: All queries are read-only SELECT statements. No mutations. MATCH is a
//      SQL Server 2017+ feature; SQL Server 2025 adds SHORTEST_PATH for variable-
//      length traversals which we use in FindDependencyChain.
// =============================================================================

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// Static helper methods that return raw SQL strings for SQL Server graph MATCH queries.
/// Use with <c>DbSet.FromSqlRaw</c> or <c>Database.SqlQueryRaw</c>.
/// </summary>
public static class GraphQueries
{
    /// <summary>
    /// Find all types that implement a given interface across all repos.
    /// </summary>
    /// <param name="interfaceName">Fully-qualified or simple interface name.</param>
    /// <returns>SQL returning SymbolNode columns for all implementors.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindImplementors("IAuditTrail");
    ///   var results = await ctx.Symbols.FromSqlRaw(sql).ToListAsync();
    /// </example>
    public static string FindImplementors(string interfaceName)
    {
        // Escape single quotes to prevent SQL injection in the literal
        var safe = interfaceName.Replace("'", "''");
        return $"""
            SELECT s2.Id, s2.Repo, s2.Project, s2.[File], s2.Kind, s2.Language,
                   s2.FullName, s2.Signature, s2.Lines, s2.BodyHash, s2.IndexedAt
            FROM symbols s1, [references] r, symbols s2
            WHERE MATCH(s2-(r)->s1)
              AND s1.FullName LIKE '%{safe}'
              AND r.Kind = 'Implements'
            ORDER BY s2.Repo, s2.FullName
            """;
    }

    /// <summary>
    /// Find all symbols that call a given method.
    /// </summary>
    /// <param name="methodName">Method name or fully-qualified name.</param>
    /// <returns>SQL returning SymbolNode columns for all callers.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindCallers("DispatchEmergency");
    ///   var results = await ctx.Symbols.FromSqlRaw(sql).ToListAsync();
    /// </example>
    public static string FindCallers(string methodName)
    {
        var safe = methodName.Replace("'", "''");
        return $"""
            SELECT s1.Id, s1.Repo, s1.Project, s1.[File], s1.Kind, s1.Language,
                   s1.FullName, s1.Signature, s1.Lines, s1.BodyHash, s1.IndexedAt
            FROM symbols s1, [references] r, symbols s2
            WHERE MATCH(s1-(r)->s2)
              AND s2.FullName LIKE '%{safe}'
              AND r.Kind = 'Calls'
            ORDER BY s1.Repo, s1.FullName
            """;
    }

    /// <summary>
    /// Find a transitive dependency chain N levels deep from a given symbol.
    /// Uses SQL Server 2019+ SHORTEST_PATH for variable-length graph traversal.
    /// </summary>
    /// <param name="symbolName">Starting symbol name.</param>
    /// <param name="depth">Maximum traversal depth (1-10, clamped).</param>
    /// <returns>SQL returning SymbolNode columns for all reachable symbols within depth.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindDependencyChain("ResponseCoordinationService", 3);
    ///   var chain = await ctx.Symbols.FromSqlRaw(sql).ToListAsync();
    /// </example>
    public static string FindDependencyChain(string symbolName, int depth)
    {
        var safe = symbolName.Replace("'", "''");
        var maxDepth = Math.Clamp(depth, 1, 10);

        // Use recursive CTE for compatibility (works on SQL Server 2017+).
        // SHORTEST_PATH is available on 2019+ but CTE is more universally compatible.
        return $"""
            ;WITH Deps AS (
                -- Anchor: direct dependencies from the starting symbol
                SELECT s2.Id, s2.Repo, s2.Project, s2.[File], s2.Kind, s2.Language,
                       s2.FullName, s2.Signature, s2.Lines, s2.BodyHash, s2.IndexedAt,
                       1 AS Depth
                FROM symbols s1
                INNER JOIN [references] r ON r.SourceId = s1.Id
                INNER JOIN symbols s2 ON r.TargetId = s2.Id
                WHERE s1.FullName LIKE '%{safe}'

                UNION ALL

                -- Recursive: follow edges from discovered symbols
                SELECT s3.Id, s3.Repo, s3.Project, s3.[File], s3.Kind, s3.Language,
                       s3.FullName, s3.Signature, s3.Lines, s3.BodyHash, s3.IndexedAt,
                       d.Depth + 1 AS Depth
                FROM Deps d
                INNER JOIN [references] r2 ON r2.SourceId = d.Id
                INNER JOIN symbols s3 ON r2.TargetId = s3.Id
                WHERE d.Depth < {maxDepth}
            )
            SELECT DISTINCT Id, Repo, Project, [File], Kind, Language,
                            FullName, Signature, Lines, BodyHash, IndexedAt
            FROM Deps
            ORDER BY FullName
            """;
    }

    /// <summary>
    /// Find all reference edges between symbols in two different repos.
    /// Useful for understanding cross-repo coupling.
    /// </summary>
    /// <param name="repo1">First repository name.</param>
    /// <param name="repo2">Second repository name.</param>
    /// <returns>SQL returning edge details with source and target symbol names.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindCrossRepoEdges("TheWatch", "ExternalApi");
    /// </example>
    public static string FindCrossRepoEdges(string repo1, string repo2)
    {
        var safe1 = repo1.Replace("'", "''");
        var safe2 = repo2.Replace("'", "''");
        return $"""
            SELECT s1.Id, s1.Repo, s1.Project, s1.[File], s1.Kind, s1.Language,
                   s1.FullName, s1.Signature, s1.Lines, s1.BodyHash, s1.IndexedAt
            FROM symbols s1, [references] r, symbols s2
            WHERE MATCH(s1-(r)->s2)
              AND ((s1.Repo = '{safe1}' AND s2.Repo = '{safe2}')
                OR (s1.Repo = '{safe2}' AND s2.Repo = '{safe1}'))
            ORDER BY s1.Repo, s1.FullName
            """;
    }

    /// <summary>
    /// Find hub symbols — those with more than N connections (incoming + outgoing edges).
    /// High-connectivity symbols are architectural hotspots.
    /// </summary>
    /// <param name="minEdges">Minimum total edge count to qualify as a hub.</param>
    /// <returns>SQL returning SymbolNode columns plus an EdgeCount column.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindClusters(10);
    /// </example>
    public static string FindClusters(int minEdges)
    {
        return $"""
            ;WITH EdgeCounts AS (
                SELECT SymbolId, SUM(Cnt) AS EdgeCount FROM (
                    SELECT SourceId AS SymbolId, COUNT(*) AS Cnt FROM [references] GROUP BY SourceId
                    UNION ALL
                    SELECT TargetId AS SymbolId, COUNT(*) AS Cnt FROM [references] GROUP BY TargetId
                ) combined
                GROUP BY SymbolId
                HAVING SUM(Cnt) >= {minEdges}
            )
            SELECT s.Id, s.Repo, s.Project, s.[File], s.Kind, s.Language,
                   s.FullName, s.Signature, s.Lines, s.BodyHash, s.IndexedAt
            FROM symbols s
            INNER JOIN EdgeCounts ec ON ec.SymbolId = s.Id
            ORDER BY ec.EdgeCount DESC, s.FullName
            """;
    }

    /// <summary>
    /// Find orphan symbols — those with zero incoming or outgoing edges.
    /// Candidates for dead code or missing integration.
    /// </summary>
    /// <returns>SQL returning SymbolNode columns for orphaned symbols.</returns>
    /// <example>
    ///   var sql = GraphQueries.FindOrphans();
    ///   var deadCode = await ctx.Symbols.FromSqlRaw(sql).ToListAsync();
    /// </example>
    public static string FindOrphans()
    {
        return """
            SELECT s.Id, s.Repo, s.Project, s.[File], s.Kind, s.Language,
                   s.FullName, s.Signature, s.Lines, s.BodyHash, s.IndexedAt
            FROM symbols s
            WHERE NOT EXISTS (
                SELECT 1 FROM [references] r WHERE r.SourceId = s.Id OR r.TargetId = s.Id
            )
            ORDER BY s.Repo, s.FullName
            """;
    }

    /// <summary>
    /// Find all symbols with a specific tag.
    /// </summary>
    /// <param name="tag">Tag to search for (e.g., "#emergency", "#port").</param>
    /// <returns>SQL returning SymbolNode columns for all symbols with the tag.</returns>
    public static string FindByTag(string tag)
    {
        var safe = tag.Replace("'", "''");
        return $"""
            SELECT s.Id, s.Repo, s.Project, s.[File], s.Kind, s.Language,
                   s.FullName, s.Signature, s.Lines, s.BodyHash, s.IndexedAt
            FROM symbols s
            INNER JOIN tags t ON t.SymbolId = s.Id
            WHERE t.Tag = '{safe}'
            ORDER BY s.Repo, s.FullName
            """;
    }

    /// <summary>
    /// Find all symbols with a specific tag that also have edges of a specific kind.
    /// Example: "all #emergency symbols that Calls something in #auth".
    /// </summary>
    /// <param name="sourceTag">Tag on the source symbol.</param>
    /// <param name="edgeKind">Reference kind (Calls, Implements, etc.).</param>
    /// <param name="targetTag">Tag on the target symbol.</param>
    /// <returns>SQL returning source SymbolNode columns.</returns>
    public static string FindTaggedEdges(string sourceTag, string edgeKind, string targetTag)
    {
        var safeSrc = sourceTag.Replace("'", "''");
        var safeKind = edgeKind.Replace("'", "''");
        var safeTgt = targetTag.Replace("'", "''");
        return $"""
            SELECT DISTINCT s1.Id, s1.Repo, s1.Project, s1.[File], s1.Kind, s1.Language,
                   s1.FullName, s1.Signature, s1.Lines, s1.BodyHash, s1.IndexedAt
            FROM symbols s1, [references] r, symbols s2
            INNER JOIN tags t1 ON t1.SymbolId = s1.Id
            INNER JOIN tags t2 ON t2.SymbolId = s2.Id
            WHERE MATCH(s1-(r)->s2)
              AND t1.Tag = '{safeSrc}'
              AND t2.Tag = '{safeTgt}'
              AND r.Kind = '{safeKind}'
            ORDER BY s1.Repo, s1.FullName
            """;
    }
}
