// =============================================================================
// DocumentNode.cs — NODE entity for source documents in the code-intelligence graph
// =============================================================================
// Represents a source file (document) as a NODE in the SQL Server graph.
// Documents contain symbols and can be linked via Contains edges.
//
// SQL Server graph table:
//   CREATE TABLE documents (
//       Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
//       Repo         NVARCHAR(256)    NOT NULL,
//       Project      NVARCHAR(256)    NOT NULL,
//       FilePath     NVARCHAR(1024)   NOT NULL,
//       Language     NVARCHAR(64)     NOT NULL,
//       Lines        INT              NOT NULL,
//       IndexedAt    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
//   ) AS NODE;
//
// Example:
//   new DocumentNode {
//       Id = Guid.NewGuid(),
//       Repo = "TheWatch",
//       Project = "TheWatch.Shared",
//       FilePath = "Domain/Ports/IAuditTrail.cs",
//       Language = "csharp",
//       Lines = 128,
//       IndexedAt = DateTime.UtcNow
//   };
//
// WAL: Same hybrid approach as SymbolNode — EF Core entity + raw SQL migration
//      to convert to AS NODE.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// A source document stored as a NODE in the SQL Server graph database.
/// </summary>
[Table("documents")]
public class DocumentNode
{
    /// <summary>Unique identifier for this document.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Repository name.</summary>
    [Required, MaxLength(256)]
    public string Repo { get; set; } = "";

    /// <summary>Project within the repo.</summary>
    [Required, MaxLength(256)]
    public string Project { get; set; } = "";

    /// <summary>Relative file path within the repo.</summary>
    [Required, MaxLength(1024)]
    public string FilePath { get; set; } = "";

    /// <summary>Source language: csharp, kotlin, swift, python, typescript, etc.</summary>
    [Required, MaxLength(64)]
    public string Language { get; set; } = "";

    /// <summary>Total lines in the document.</summary>
    public int Lines { get; set; }

    /// <summary>UTC timestamp when this document was indexed.</summary>
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
}
