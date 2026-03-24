// =============================================================================
// LSP Models — Language Server Protocol message types
// =============================================================================
// Subset of LSP 3.17 spec messages that the build server implements.
// We don't need the full editor protocol — just the code intelligence queries
// that agents and the CLI dashboard need:
//
//   textDocument/definition    — "Where is this symbol defined?"
//   textDocument/references    — "Where is this symbol used?"
//   textDocument/hover         — "What is this symbol? Show docs."
//   workspace/symbol           — "Find all symbols matching this query"
//   custom/portAdapterMap      — "Show me all adapter implementations for this port"
//   custom/buildStatus         — "What's the current build state?"
//   custom/agentBranches       — "What branches are agents working on?"
//
// Transport: JSON-RPC over stdin/stdout (for CLI) or WebSocket (for dashboard).
// WAL: StreamJsonRpc handles the JSON-RPC framing. We just define the method signatures.
// =============================================================================

namespace TheWatch.BuildServer.Models;

// ── LSP Position & Location ──────────────────────────────────────────────────

public record LspPosition(int Line, int Character);

public record LspRange(LspPosition Start, LspPosition End);

public record LspLocation(string Uri, LspRange Range);

// ── LSP Request Parameters ───────────────────────────────────────────────────

public record TextDocumentIdentifier(string Uri);

public record TextDocumentPositionParams(
    TextDocumentIdentifier TextDocument,
    LspPosition Position);

public record WorkspaceSymbolParams(string Query);

public record ReferenceParams(
    TextDocumentIdentifier TextDocument,
    LspPosition Position,
    bool IncludeDeclaration = true);

// ── LSP Response Types ───────────────────────────────────────────────────────

public record LspSymbolInformation(
    string Name,
    int Kind,       // maps to SymbolKind enum values (1-indexed per LSP spec)
    LspLocation Location,
    string? ContainerName);

public record LspHover(
    LspMarkupContent Contents,
    LspRange? Range = null);

public record LspMarkupContent(
    string Kind,    // "markdown" or "plaintext"
    string Value);

// ── Custom TheWatch Extensions ───────────────────────────────────────────────

public record PortAdapterMapRequest(string? PortInterfaceName = null);

public record PortAdapterMapResponse(
    string PortInterfaceName,
    string PortProject,
    string DocumentUri,
    List<AdapterMapEntry> Adapters);

public record AdapterMapEntry(
    string ClassName,
    string ProjectName,
    string Tier,
    string DocumentUri,
    int DefinitionLine);

public record BuildStatusResponse(
    string Status,
    BuildRun? CurrentBuild,
    BuildRun? LastBuild,
    int QueueDepth);

public record AgentBranchesResponse(
    List<AgentBranch> Branches,
    MergePlan? ActiveMergePlan);

// ── LSP Server Capabilities ──────────────────────────────────────────────────

public record ServerCapabilities(
    bool DefinitionProvider = true,
    bool ReferencesProvider = true,
    bool HoverProvider = true,
    bool WorkspaceSymbolProvider = true,
    CustomCapabilities? Experimental = null);

public record CustomCapabilities(
    bool PortAdapterMap = true,
    bool BuildStatus = true,
    bool AgentBranches = true,
    bool MergePlanner = true);

public record InitializeResult(
    ServerCapabilities Capabilities,
    ServerInfo? ServerInfo = null);

public record ServerInfo(
    string Name = "TheWatch.BuildServer",
    string Version = "1.0.0");
