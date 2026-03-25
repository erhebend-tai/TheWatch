// =============================================================================
// SymbolNode.cs — NODE entity for SQL Server graph code-intelligence store
// =============================================================================
// Represents a single code symbol (class, interface, method, enum, struct, etc.)
// extracted from any supported language across all indexed repositories.
//
// SQL Server graph table:
//   CREATE TABLE symbols (
//       Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
//       Repo         NVARCHAR(256)    NOT NULL,
//       Project      NVARCHAR(256)    NOT NULL,
//       [File]       NVARCHAR(1024)   NOT NULL,
//       Kind         NVARCHAR(64)     NOT NULL,
//       Language     NVARCHAR(64)     NOT NULL,
//       FullName     NVARCHAR(1024)   NOT NULL,
//       Signature    NVARCHAR(2048)   NOT NULL,
//       Lines        INT              NOT NULL,
//       BodyHash     NVARCHAR(64)     NULL,
//       IndexedAt    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
//   ) AS NODE;
//
// Example:
//   new SymbolNode {
//       Id = Guid.NewGuid(),
//       Repo = "TheWatch",
//       Project = "TheWatch.Shared",
//       File = "Domain/Ports/IAuditTrail.cs",
//       Kind = "interface",
//       Language = "csharp",
//       FullName = "TheWatch.Shared.Domain.Ports.IAuditTrail",
//       Signature = "interface IAuditTrail",
//       Lines = 42,
//       BodyHash = "a1b2c3d4...",
//       IndexedAt = DateTime.UtcNow
//   };
//
// WAL: EF Core maps this as a normal table; the InitialCodeIntelligence.sql
//      migration ALTERs it to AS NODE for SQL Server graph MATCH queries.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// A code symbol stored as a NODE in the SQL Server graph database.
/// </summary>
[Table("symbols")]
public class SymbolNode
{
    /// <summary>Unique identifier for this symbol.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Repository name (e.g., "TheWatch", "ExternalApi").</summary>
    [Required, MaxLength(256)]
    public string Repo { get; set; } = "";

    /// <summary>Project within the repo (e.g., "TheWatch.Shared", "TheWatch.Cli").</summary>
    [Required, MaxLength(256)]
    public string Project { get; set; } = "";

    /// <summary>Relative file path within the repo.</summary>
    [Required, MaxLength(1024)]
    [Column("File")]
    public string File { get; set; } = "";

    /// <summary>Symbol kind: class, interface, method, enum, struct, record, function, etc.</summary>
    [Required, MaxLength(64)]
    public string Kind { get; set; } = "";

    /// <summary>Source language: csharp, kotlin, swift, python, typescript, etc.</summary>
    [Required, MaxLength(64)]
    public string Language { get; set; } = "";

    /// <summary>Fully-qualified name (namespace.ClassName or just ClassName).</summary>
    [Required, MaxLength(1024)]
    public string FullName { get; set; } = "";

    /// <summary>Human-readable signature string.</summary>
    [Required, MaxLength(2048)]
    public string Signature { get; set; } = "";

    /// <summary>Number of lines spanned by this symbol.</summary>
    public int Lines { get; set; }

    /// <summary>SHA-256 hash of the symbol body (for duplicate/drift detection). Null if not computed.</summary>
    [MaxLength(64)]
    public string? BodyHash { get; set; }

    /// <summary>UTC timestamp when this symbol was indexed.</summary>
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
}
