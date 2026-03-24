// =============================================================================
// LSIF Models — Language Server Index Format data structures
// =============================================================================
// These mirror the LSIF 0.6 spec (https://lsif.dev/) with TheWatch extensions
// for multi-project Aspire solutions.
//
// The index stores:
//   - Documents (source files) with their URI and language
//   - Ranges (symbol locations within documents)
//   - ResultSets linking ranges to definition/reference/hover results
//   - Cross-project edges (e.g., Shared port → Mock adapter implementation)
//
// WAL: IDs are monotonically increasing integers, not GUIDs, for compactness.
//      The full index is held in memory and serialized to .lsif.json for persistence.
// =============================================================================

namespace TheWatch.BuildServer.Models;

// ── Core LSIF Vertex Types ───────────────────────────────────────────────────

public record LsifDocument(
    int Id,
    string Uri,
    string LanguageId,
    string ProjectName);

public record LsifRange(
    int Id,
    int DocumentId,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter);

public record LsifResultSet(
    int Id,
    int RangeId);

// ── Symbol Information ───────────────────────────────────────────────────────

public enum SymbolKind
{
    Namespace, Class, Interface, Struct, Enum, Method, Property, Field,
    Event, Constructor, EnumMember, Delegate, Record
}

public record SymbolInfo(
    int Id,
    string Name,
    string FullyQualifiedName,
    SymbolKind Kind,
    string ContainingType,
    string ContainingNamespace,
    string ProjectName,
    LsifRange DefinitionRange,
    string? Documentation = null);

// ── Reference & Definition Results ───────────────────────────────────────────

public record DefinitionResult(
    int SymbolId,
    LsifRange Range,
    string DocumentUri);

public record ReferenceResult(
    int SymbolId,
    LsifRange Range,
    string DocumentUri,
    bool IsDefinition);

public record HoverResult(
    int SymbolId,
    string MarkdownContent);

// ── Cross-Project Relationships (TheWatch Extension) ─────────────────────────

/// <summary>
/// Links a port interface to its adapter implementations across projects.
/// E.g., IStorageService (TheWatch.Shared) → MockStorageService (TheWatch.Adapters.Mock)
///                                         → SqlStorageService (TheWatch.Data)
/// </summary>
public record PortAdapterLink(
    int PortSymbolId,
    string PortInterfaceName,
    string PortProject,
    List<AdapterImplementation> Implementations);

public record AdapterImplementation(
    int SymbolId,
    string ClassName,
    string ProjectName,
    string AdapterTier); // "Mock", "Native", "Live"

// ── Index Container ──────────────────────────────────────────────────────────

public class LsifIndex
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string SolutionPath { get; set; } = "";
    public int Version { get; set; } = 1;

    // Core LSIF data
    public List<LsifDocument> Documents { get; set; } = [];
    public List<SymbolInfo> Symbols { get; set; } = [];
    public List<ReferenceResult> References { get; set; } = [];
    public List<HoverResult> HoverResults { get; set; } = [];

    // TheWatch extensions
    public List<PortAdapterLink> PortAdapterLinks { get; set; } = [];

    // Stats
    public int TotalFiles => Documents.Count;
    public int TotalSymbols => Symbols.Count;
    public int TotalReferences => References.Count;
    public int TotalPortAdapterLinks => PortAdapterLinks.Count;

    private int _nextId;
    public int NextId() => Interlocked.Increment(ref _nextId);
}
