// =============================================================================
// ReferenceEdge.cs — EDGE entity for symbol-to-symbol relationships
// =============================================================================
// Stores directed relationships between two SymbolNodes in the SQL Server graph.
// Each edge has a Kind (from ReferenceKind enum) plus optional metadata about
// where the reference was found (source file, line number).
//
// SQL Server graph table:
//   CREATE TABLE [references] (
//       Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
//       SourceId     UNIQUEIDENTIFIER NOT NULL,
//       TargetId     UNIQUEIDENTIFIER NOT NULL,
//       Kind         NVARCHAR(64)     NOT NULL,
//       SourceFile   NVARCHAR(1024)   NULL,
//       SourceLine   INT              NULL
//   ) AS EDGE;
//
// MATCH query example:
//   SELECT s2.FullName FROM symbols s1, [references] r, symbols s2
//   WHERE MATCH(s1-(r)->s2) AND s1.FullName = 'MyClass' AND r.Kind = 'Calls'
//
// Example:
//   new ReferenceEdge {
//       Id = Guid.NewGuid(),
//       SourceId = callerSymbolId,
//       TargetId = calleeSymbolId,
//       Kind = ReferenceKind.Calls,
//       SourceFile = "Services/ResponseCoordinator.cs",
//       SourceLine = 42
//   };
//
// WAL: SourceId/TargetId are application-level FKs matching SymbolNode.Id.
//      The graph engine uses $from_id/$to_id (hidden columns) for MATCH traversal;
//      we populate both during bulk insert.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// A directed relationship between two symbols, stored as an EDGE in the SQL Server graph.
/// </summary>
[Table("references")]
public class ReferenceEdge
{
    /// <summary>Unique identifier for this edge.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Source symbol ID (the symbol making the reference).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Target symbol ID (the symbol being referenced).</summary>
    public Guid TargetId { get; set; }

    /// <summary>Kind of reference (Calls, Implements, Extends, etc.).</summary>
    [Required, MaxLength(64)]
    public string Kind { get; set; } = "";

    /// <summary>File where the reference was found (for navigation).</summary>
    [MaxLength(1024)]
    public string? SourceFile { get; set; }

    /// <summary>Line number where the reference was found.</summary>
    public int? SourceLine { get; set; }
}
