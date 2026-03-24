// =============================================================================
// TheWatch.BuildServer — Entry Point
// =============================================================================
// Build orchestration + LSIF code intelligence + LSP protocol server.
//
// Startup sequence:
//   1. Parse CLI args (solution path, mode flags)
//   2. Register services: LsifIndexer, LspServer, BuildOrchestrator
//   3. Build initial LSIF index (background)
//   4. Start build queue processor
//   5. Start file watcher (if enabled)
//   6. Start ASP.NET Core (REST API + WebSocket LSP + SignalR)
//
// Modes:
//   default         — Full server: REST API + LSP + file watch + build queue
//   --index-only    — Build LSIF index and exit
//   --lsp-only      — Start LSP stdio server (for editor integration)
//   --no-watch      — Disable file system watcher
//
// Example:
//   dotnet run --project TheWatch.BuildServer
//   dotnet run --project TheWatch.BuildServer -- --index-only
//   dotnet run --project TheWatch.BuildServer -- --lsp-only
//   dotnet run --project TheWatch.BuildServer -- --solution ../TheWatch.sln --port 5002
//
// WAL: In --lsp-only mode, we skip the ASP.NET Core host and run JSON-RPC
//      directly over stdin/stdout for editor piping.
// =============================================================================

using Serilog;
using TheWatch.BuildServer.Lsif;
using TheWatch.BuildServer.Lsp;
using TheWatch.BuildServer.Models;
using TheWatch.BuildServer.Services;

// ── CLI Arguments ────────────────────────────────────────────────────────────

var solutionPath = args.FirstOrDefault(a => a.StartsWith("--solution"))
    ?.Split('=').LastOrDefault()
    ?? FindSolutionFile();

var indexOnly = args.Contains("--index-only");
var lspOnly = args.Contains("--lsp-only");
var noWatch = args.Contains("--no-watch");
var port = args.FirstOrDefault(a => a.StartsWith("--port"))
    ?.Split('=').LastOrDefault() ?? "5002";
var dashboardUrl = args.FirstOrDefault(a => a.StartsWith("--dashboard-url"))
    ?.Split('=').LastOrDefault() ?? "https://localhost:5001";

// ── Serilog Bootstrap ────────────────────────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    Log.Information("TheWatch.BuildServer starting");
    Log.Information("Solution: {Solution}", solutionPath);
    Log.Information("Mode: {Mode}", indexOnly ? "index-only" : lspOnly ? "lsp-only" : "full");

    // ── Index-Only Mode ──────────────────────────────────────────────────

    if (indexOnly)
    {
        using var indexer = new LsifIndexer(solutionPath, CreateLogger<LsifIndexer>());
        var index = await indexer.BuildFullIndexAsync();

        var outputPath = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".thewatch", "lsif-index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await indexer.PersistAsync(outputPath);

        Log.Information("Index complete: {Files} files, {Symbols} symbols, {Ports} port-adapter links",
            index.TotalFiles, index.TotalSymbols, index.TotalPortAdapterLinks);
        return 0;
    }

    // ── LSP-Only Mode (stdio) ────────────────────────────────────────────

    if (lspOnly)
    {
        using var indexer = new LsifIndexer(solutionPath, CreateLogger<LsifIndexer>());
        var orchestrator = new BuildOrchestrator(solutionPath, indexer, CreateLogger<BuildOrchestrator>());
        var lspServer = new LspServer(indexer, orchestrator, CreateLogger<LspServer>());

        // Build initial index
        await indexer.BuildFullIndexAsync();

        // Start JSON-RPC over stdin/stdout
        Log.Information("Starting LSP server on stdio");
        var rpc = new StreamJsonRpc.JsonRpc(
            new StreamJsonRpc.HeaderDelimitedMessageHandler(
                Console.OpenStandardOutput(),
                Console.OpenStandardInput()),
            lspServer);
        rpc.StartListening();
        await rpc.Completion;
        return 0;
    }

    // ── Full Server Mode ─────────────────────────────────────────────────

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog();

    // Core services
    var indexerInstance = new LsifIndexer(solutionPath, CreateLogger<LsifIndexer>());
    var orchestratorInstance = new BuildOrchestrator(solutionPath, indexerInstance, CreateLogger<BuildOrchestrator>());

    builder.Services.AddSingleton(indexerInstance);
    builder.Services.AddSingleton(orchestratorInstance);
    builder.Services.AddSingleton<LspServer>();

    // File watcher (optional)
    if (!noWatch)
    {
        builder.Services.AddSingleton<BuildOrchestrator>(orchestratorInstance);
        builder.Services.AddHostedService<FileWatchService>();
    }

    // ASP.NET Core
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    });

    // WebSockets for LSP
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    var app = builder.Build();

    app.UseCors();
    app.UseWebSockets();
    app.UseLspWebSocket();
    app.MapControllers();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    // ── Startup Sequence ─────────────────────────────────────────────────

    // 1. Build initial LSIF index in background
    _ = Task.Run(async () =>
    {
        try
        {
            await indexerInstance.BuildFullIndexAsync();
            Log.Information("Initial LSIF index built");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Initial LSIF index failed — will retry on first build");
        }
    });

    // 2. Start build queue processor
    await orchestratorInstance.StartAsync();

    // 3. Connect to Dashboard API for SignalR broadcasting
    _ = Task.Run(async () =>
    {
        try
        {
            await orchestratorInstance.ConnectSignalRAsync(dashboardUrl);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not connect to Dashboard SignalR hub");
        }
    });

    // 4. Seed known agent branches from TheWatch's current fleet
    SeedAgentBranches(orchestratorInstance);

    Log.Information("TheWatch.BuildServer listening on port {Port}", port);
    Log.Information("  REST API:  http://localhost:{Port}/api/build/*", port);
    Log.Information("  LSP WS:    ws://localhost:{Port}/lsp", port);
    Log.Information("  OpenAPI:   http://localhost:{Port}/openapi/v1.json", port);

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "BuildServer terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ── Helper Functions ─────────────────────────────────────────────────────────

static string FindSolutionFile()
{
    // Walk up from current directory to find TheWatch.slnx (preferred) or TheWatch.sln (fallback)
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null)
    {
        var slnx = Directory.GetFiles(dir, "TheWatch.slnx").FirstOrDefault();
        if (slnx is not null) return slnx;
        var sln = Directory.GetFiles(dir, "TheWatch.sln").FirstOrDefault();
        if (sln is not null) return sln;
        dir = Path.GetDirectoryName(dir);
    }

    // Fallback: look in common locations (.slnx first, then .sln)
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "TheWatch.slnx"),
        Path.Combine(Directory.GetCurrentDirectory(), "TheWatch.sln"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "TheWatch.slnx"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "TheWatch.sln"),
    };

    return candidates.FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException("Could not find TheWatch.slnx or TheWatch.sln. Use --solution=<path> to specify.");
}

static void SeedAgentBranches(BuildOrchestrator orchestrator)
{
    // Pre-register known agent branches from TheWatch's development fleet
    var agents = new (string Name, string Branch, string Scope)[]
    {
        ("Agent 1 - Log Viewer UI", "feature/mobile-log-viewer-ui", "Android + iOS log viewer screens"),
        ("Agent 2 - SOS Correlation", "feature/sos-lifecycle-correlation", "SOS lifecycle correlation tracking"),
        ("Agent 3 - Adapter Registry", "feature/adapter-registry-mobile", "Mobile adapter registry + runtime switching"),
        ("Agent 4 - SignalR Client", "feature/signalr-mobile-client", "Mobile SignalR hub connection"),
        ("Agent 5 - Test Runner", "feature/mobile-test-runner", "On-device test execution engine"),
        ("Agent 6 - Sync Engine", "feature/offline-first-sync-engine", "Generalized offline-first sync"),
        ("Agent 7 - Alexa Skills", "feature/alexa-skills", "Alexa Skills Lambda integration"),
        ("Agent 8 - Google Home", "feature/google-home", "Google Home Actions SDK webhook"),
        ("Agent 9 - IoT Backend", "feature/iot-backend", "IoT alert/webhook ports + controller"),
        ("Agent 10 - CLI Dashboard", "feature/cli-dashboard", "Terminal.Gui TUI command center"),
    };

    foreach (var (name, branch, scope) in agents)
    {
        orchestrator.RegisterAgentBranch(name, branch, scope);
    }

    Log.Information("Seeded {Count} agent branches", agents.Length);
}

static ILogger<T> CreateLogger<T>()
{
    return new LoggerFactory()
        .AddSerilog()
        .CreateLogger<T>();
}
