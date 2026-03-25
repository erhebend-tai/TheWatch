// =============================================================================
// TagEdge.cs — EDGE entity for symbol-to-tag relationships
// =============================================================================
// Associates a SymbolNode with a tag string. Tags come from the CodeIndex
// tag derivation logic (#emergency, #auth, #port, #adapter, etc.) and enable
// fast filtered graph queries like "find all #emergency symbols that call #auth".
//
// SQL Server graph table:
//   CREATE TABLE tags (
//       Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
//       SymbolId     UNIQUEIDENTIFIER NOT NULL,
//       Tag          NVARCHAR(128)    NOT NULL
//   ) AS EDGE;
//
// Note: In SQL Server graph, EDGE tables have hidden $from_id and $to_id columns.
// For TagEdge we use $from_id pointing to the symbol node and $to_id is self-referential
// (or points to a virtual "tag" concept). Since tags are strings not nodes, we store
// the Tag as a column and use SymbolId as the application-level FK.
//
// Example:
//   new TagEdge {
//       Id = Guid.NewGuid(),
//       SymbolId = someSymbol.Id,
//       Tag = "#emergency"
//   };
//
// WAL: Tags are bulk-inserted after symbols. The Tag column is indexed for fast
//      filtered lookups. The SymbolId column enables joins without MATCH when needed.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// Associates a symbol with a tag, stored as an EDGE in the SQL Server graph.
/// </summary>
[Table("tags")]
public class TagEdge
{
    /// <summary>Unique identifier for this tag edge.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>The symbol this tag is attached to.</summary>
    public Guid SymbolId { get; set; }

    /// <summary>Tag value (e.g., "#emergency", "#port", "#adapter").</summary>
    [Required, MaxLength(128)]
    public string Tag { get; set; } = "";
}
