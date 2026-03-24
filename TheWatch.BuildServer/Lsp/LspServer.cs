// =============================================================================
// LspServer — Language Server Protocol implementation over JSON-RPC
// =============================================================================
// Exposes the LSIF index via standard LSP methods + custom TheWatch extensions.
// Agents (Claude Code, Gemini) and the CLI dashboard can connect and query
// symbol information, definitions, references, and port→adapter mappings.
//
// Transport modes:
//   - stdio: JSON-RPC over stdin/stdout (for CLI integration)
//   - WebSocket: JSON-RPC over WS (for dashboard/agent HTTP connections)
//
// Supported methods:
//   initialize                     → capabilities handshake
//   textDocument/definition        → go-to-definition
//   textDocument/references        → find-all-references
//   textDocument/hover             → hover documentation
//   workspace/symbol               → global symbol search
//   thewatch/portAdapterMap        → port→adapter cross-reference
//   thewatch/buildStatus           → current build orchestrator state
//   thewatch/agentBranches         → agent branch tracking
//
// WAL: StreamJsonRpc handles JSON-RPC framing and method dispatch via attributes.
//      All handler methods must be public and decorated with [JsonRpcMethod].
// =============================================================================

using StreamJsonRpc;
using TheWatch.BuildServer.Lsif;
using TheWatch.BuildServer.Models;
using TheWatch.BuildServer.Services;

namespace TheWatch.BuildServer.Lsp;

public class LspServer
{
    private readonly LsifIndexer _indexer;
    private readonly BuildOrchestrator _orchestrator;
    private readonly ILogger<LspServer> _logger;

    public LspServer(LsifIndexer indexer, BuildOrchestrator orchestrator, ILogger<LspServer> logger)
    {
        _indexer = indexer;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    // ── LSP Lifecycle ────────────────────────────────────────────────────────

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize()
    {
        _logger.LogInformation("LSP client connected");
        return new InitializeResult(
            new ServerCapabilities(
                DefinitionProvider: true,
                ReferencesProvider: true,
                HoverProvider: true,
                WorkspaceSymbolProvider: true,
                Experimental: new CustomCapabilities()),
            new ServerInfo());
    }

    [JsonRpcMethod("initialized")]
    public void Initialized()
    {
        _logger.LogInformation("LSP client initialized");
    }

    [JsonRpcMethod("shutdown")]
    public object? Shutdown()
    {
        _logger.LogInformation("LSP shutdown requested");
        return null;
    }

    // ── Standard LSP Methods ─────────────────────────────────────────────────

    [JsonRpcMethod("textDocument/definition")]
    public LspLocation? GoToDefinition(TextDocumentPositionParams @params)
    {
        var index = _indexer.CurrentIndex;
        var symbol = FindSymbolAtPosition(index, @params.TextDocument.Uri, @params.Position);
        if (symbol is null) return null;

        var doc = index.Documents.Find(d => d.Id == symbol.DefinitionRange.DocumentId);
        if (doc is null) return null;

        return new LspLocation(doc.Uri, new LspRange(
            new LspPosition(symbol.DefinitionRange.StartLine, symbol.DefinitionRange.StartCharacter),
            new LspPosition(symbol.DefinitionRange.EndLine, symbol.DefinitionRange.EndCharacter)));
    }

    [JsonRpcMethod("textDocument/references")]
    public List<LspLocation> FindReferences(ReferenceParams @params)
    {
        var index = _indexer.CurrentIndex;
        var symbol = FindSymbolAtPosition(index, @params.TextDocument.Uri, @params.Position);
        if (symbol is null) return [];

        return index.References
            .Where(r => r.SymbolId == symbol.Id)
            .Where(r => @params.IncludeDeclaration || !r.IsDefinition)
            .Select(r => new LspLocation(r.DocumentUri, new LspRange(
                new LspPosition(r.Range.StartLine, r.Range.StartCharacter),
                new LspPosition(r.Range.EndLine, r.Range.EndCharacter))))
            .ToList();
    }

    [JsonRpcMethod("textDocument/hover")]
    public LspHover? Hover(TextDocumentPositionParams @params)
    {
        var index = _indexer.CurrentIndex;
        var symbol = FindSymbolAtPosition(index, @params.TextDocument.Uri, @params.Position);
        if (symbol is null) return null;

        var hover = index.HoverResults.Find(h => h.SymbolId == symbol.Id);
        if (hover is null) return null;

        return new LspHover(
            new LspMarkupContent("markdown", hover.MarkdownContent),
            new LspRange(
                new LspPosition(symbol.DefinitionRange.StartLine, symbol.DefinitionRange.StartCharacter),
                new LspPosition(symbol.DefinitionRange.EndLine, symbol.DefinitionRange.EndCharacter)));
    }

    [JsonRpcMethod("workspace/symbol")]
    public List<LspSymbolInformation> WorkspaceSymbol(WorkspaceSymbolParams @params)
    {
        var index = _indexer.CurrentIndex;
        var query = @params.Query.ToLowerInvariant();

        return index.Symbols
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.FullyQualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(100) // cap results for performance
            .Select(s =>
            {
                var doc = index.Documents.Find(d => d.Id == s.DefinitionRange.DocumentId);
                return new LspSymbolInformation(
                    s.Name,
                    (int)s.Kind + 1, // LSP SymbolKind is 1-indexed
                    new LspLocation(
                        doc?.Uri ?? "",
                        new LspRange(
                            new LspPosition(s.DefinitionRange.StartLine, s.DefinitionRange.StartCharacter),
                            new LspPosition(s.DefinitionRange.EndLine, s.DefinitionRange.EndCharacter))),
                    s.ContainingType);
            })
            .ToList();
    }

    // ── Custom TheWatch Extensions ───────────────────────────────────────────

    [JsonRpcMethod("thewatch/portAdapterMap")]
    public List<PortAdapterMapResponse> GetPortAdapterMap(PortAdapterMapRequest? request = null)
    {
        var index = _indexer.CurrentIndex;
        var links = index.PortAdapterLinks.AsEnumerable();

        if (request?.PortInterfaceName is not null)
            links = links.Where(l => l.PortInterfaceName.Contains(
                request.PortInterfaceName, StringComparison.OrdinalIgnoreCase));

        return links.Select(link =>
        {
            var portDoc = index.Documents.Find(d =>
            {
                var symbol = index.Symbols.Find(s => s.Id == link.PortSymbolId);
                return symbol is not null && d.Id == symbol.DefinitionRange.DocumentId;
            });

            return new PortAdapterMapResponse(
                link.PortInterfaceName,
                link.PortProject,
                portDoc?.Uri ?? "",
                link.Implementations.Select(impl =>
                {
                    var implSymbol = index.Symbols.Find(s => s.Id == impl.SymbolId);
                    var implDoc = implSymbol is not null
                        ? index.Documents.Find(d => d.Id == implSymbol.DefinitionRange.DocumentId)
                        : null;

                    return new AdapterMapEntry(
                        impl.ClassName,
                        impl.ProjectName,
                        impl.AdapterTier,
                        implDoc?.Uri ?? "",
                        implSymbol?.DefinitionRange.StartLine ?? 0);
                }).ToList());
        }).ToList();
    }

    [JsonRpcMethod("thewatch/buildStatus")]
    public BuildStatusResponse GetBuildStatus()
    {
        return new BuildStatusResponse(
            _orchestrator.CurrentBuild?.Status.ToString() ?? "Idle",
            _orchestrator.CurrentBuild,
            _orchestrator.LastCompletedBuild,
            _orchestrator.QueueDepth);
    }

    [JsonRpcMethod("thewatch/agentBranches")]
    public AgentBranchesResponse GetAgentBranches()
    {
        return new AgentBranchesResponse(
            _orchestrator.AgentBranches.ToList(),
            _orchestrator.ActiveMergePlan);
    }

    [JsonRpcMethod("thewatch/reindex")]
    public async Task<LsifIndexSummary> TriggerReindex(string? projectName = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        LsifIndex index;
        if (projectName is not null)
            index = await _indexer.IncrementalIndexAsync(projectName);
        else
            index = await _indexer.BuildFullIndexAsync();

        sw.Stop();

        return new LsifIndexSummary(
            index.TotalFiles,
            index.TotalSymbols,
            index.TotalReferences,
            index.TotalPortAdapterLinks,
            sw.Elapsed);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SymbolInfo? FindSymbolAtPosition(LsifIndex index, string uri, LspPosition position)
    {
        // Find document
        var doc = index.Documents.Find(d =>
            d.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));
        if (doc is null) return null;

        // Find symbol whose range contains the position
        return index.Symbols
            .Where(s => s.DefinitionRange.DocumentId == doc.Id)
            .FirstOrDefault(s =>
                (s.DefinitionRange.StartLine < position.Line ||
                 (s.DefinitionRange.StartLine == position.Line && s.DefinitionRange.StartCharacter <= position.Character)) &&
                (s.DefinitionRange.EndLine > position.Line ||
                 (s.DefinitionRange.EndLine == position.Line && s.DefinitionRange.EndCharacter >= position.Character)));
    }
}
