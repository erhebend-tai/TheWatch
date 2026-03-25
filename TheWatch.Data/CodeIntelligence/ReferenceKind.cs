// =============================================================================
// ReferenceKind.cs — Edge classification for code-intelligence graph
// =============================================================================
// Enumerates every relationship type stored in the SQL Server graph [references]
// EDGE table.  Values map 1-to-1 with LSIF moniker kinds where a standard
// equivalent exists; DependsOn is a TheWatch extension for constructor-injection
// edges discovered via Roslyn semantic analysis.
//
// SQL Server graph syntax reminder:
//   INSERT INTO [references] ($from_id, $to_id, Kind)
//   VALUES ((SELECT $node_id FROM symbols WHERE Id = @src),
//           (SELECT $node_id FROM symbols WHERE Id = @tgt),
//           'Calls');
//
// Example:
//   ReferenceKind.Calls      — MethodA invokes MethodB
//   ReferenceKind.Implements — ClassA : IInterfaceB
//   ReferenceKind.DependsOn  — ClassA receives IServiceB via constructor injection
//
// WAL: Enum values are stored as strings (nvarchar) in the EDGE table so that
//      raw SQL queries remain human-readable without joins to a lookup table.
// =============================================================================

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// Classifies the relationship between two symbols in the code-intelligence graph.
/// </summary>
public enum ReferenceKind
{
    /// <summary>Source symbol calls (invokes) the target symbol.</summary>
    Calls,

    /// <summary>Source symbol implements the target interface.</summary>
    Implements,

    /// <summary>Source symbol extends (inherits from) the target type.</summary>
    Extends,

    /// <summary>Source document/symbol imports the target module or namespace.</summary>
    Imports,

    /// <summary>Source symbol overrides the target virtual/abstract member.</summary>
    Overrides,

    /// <summary>Source symbol structurally contains the target (e.g., class contains method).</summary>
    Contains,

    /// <summary>Source symbol's type is the target type (field type, return type, parameter type).</summary>
    TypeOf,

    /// <summary>Source method returns the target type.</summary>
    Returns,

    /// <summary>Source symbol depends on the target via constructor injection (TheWatch extension).</summary>
    DependsOn
}
