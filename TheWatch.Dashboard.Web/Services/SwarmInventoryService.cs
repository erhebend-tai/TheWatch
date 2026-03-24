// SwarmInventoryService — provides the file inventory, agent assignments,
// and goal lists that power the Swarm Dashboard datagrid.
//
// Data flow:
//   1. On first load, tries to fetch from Firestore via Dashboard.Api
//   2. If Firestore is empty or unreachable, falls back to static seed data
//   3. Seeds Firestore with static data on first successful connection
//   4. Subsequent loads always come from Firestore (emulator or production)
//
// Phase 2: BuildServer's LSIF indexer and DocGen's XML doc analyzer will
// push updates to Firestore via RabbitMQ, keeping the inventory live.

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Web.Services;

/// <summary>
/// A single file in the solution inventory, tracked by the swarm.
/// View model for the Razor datagrid — mapped from SwarmFileRecord.
/// </summary>
public record FileInventoryItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Project { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileType { get; init; } = "";

    /// <summary>2-word purpose shown in the collapsed cell.</summary>
    public string PurposeShort { get; init; } = "";

    /// <summary>Full description shown on expand.</summary>
    public string PurposeFull { get; init; } = "";

    /// <summary>Agent assigned to this file (maps to SwarmAgentDefinition.Name).</summary>
    public string AgentName { get; init; } = "Unassigned";

    /// <summary>Agent's role tag.</summary>
    public string AgentRole { get; init; } = "";

    /// <summary>Agent status: Active, Idle, Queued, Error.</summary>
    public string AgentStatus { get; init; } = "Idle";

    /// <summary>Goal list for the agent on this specific file.</summary>
    public List<AgentGoal> Goals { get; init; } = [];
}

public record AgentGoal
{
    public string Description { get; init; } = "";
    public string Status { get; init; } = "Pending"; // Pending, InProgress, Done, Blocked
}

/// <summary>
/// Supervisor: a higher-level agent overseeing a group of file agents.
/// View model for the Razor supervisor cards — mapped from SwarmSupervisorRecord.
/// </summary>
public record SupervisorAssignment
{
    public string Name { get; init; } = "";
    public string Domain { get; init; } = "";
    public int FileCount { get; init; }
    public int ActiveAgents { get; init; }
    public int GoalsCompleted { get; init; }
    public int GoalsTotal { get; init; }
    public string Status { get; init; } = "Active";
}

public class SwarmInventoryService
{
    private readonly DashboardApiClient _apiClient;
    private readonly ILogger<SwarmInventoryService> _logger;
    private bool _seeded;

    public SwarmInventoryService(DashboardApiClient apiClient, ILogger<SwarmInventoryService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>The mission statement shown front and center.</summary>
    public string MissionStatement => "Life-Safety Emergency Response Platform — Azure OpenAI Swarm Coordination";

    public string MissionDetail =>
        "Every code file in TheWatch has an assigned AI agent responsible for understanding, " +
        "maintaining, and improving it. Supervisors coordinate agent groups by domain. " +
        "Goals flow from milestones through supervisors to individual file agents via RabbitMQ, " +
        "with Hangfire scheduling periodic sweeps for documentation coverage, build health, and code quality.";

    /// <summary>
    /// Load supervisors — tries Firestore first, falls back to static data.
    /// </summary>
    public async Task<List<SupervisorAssignment>> GetSupervisorsAsync()
    {
        try
        {
            await EnsureSeededAsync();
            var records = await _apiClient.GetSwarmSupervisorsAsync();
            if (records.Count > 0)
                return records.Select(MapSupervisor).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore supervisor fetch failed, using static fallback");
        }

        return GetStaticSupervisors();
    }

    /// <summary>
    /// Load inventory — tries Firestore first, falls back to static data.
    /// </summary>
    public async Task<List<FileInventoryItem>> GetInventoryAsync()
    {
        try
        {
            await EnsureSeededAsync();
            var records = await _apiClient.GetSwarmFilesAsync();
            if (records.Count > 0)
                return records.Select(MapFile).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore inventory fetch failed, using static fallback");
        }

        return GetStaticInventory();
    }

    /// <summary>Synchronous fallback for initial render before async load completes.</summary>
    public List<SupervisorAssignment> GetSupervisors() => GetStaticSupervisors();

    /// <summary>Synchronous fallback for initial render before async load completes.</summary>
    public List<FileInventoryItem> GetInventory() => GetStaticInventory();

    // ── Firestore Seeding ────────────────────────────────────────

    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        _seeded = true;

        try
        {
            var seedFiles = GetStaticSeedRecords();
            var seedSupervisors = GetStaticSupervisorRecords();
            await _apiClient.SeedSwarmInventoryAsync(seedFiles, seedSupervisors);
            _logger.LogInformation("Swarm inventory seed request sent ({Files} files, {Sups} supervisors)",
                seedFiles.Count, seedSupervisors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed Firestore — will retry on next load");
            _seeded = false; // Retry next time
        }
    }

    // ── Mapping: Domain Records → View Models ────────────────────

    private static FileInventoryItem MapFile(SwarmFileRecord r) => new()
    {
        Id = r.Id,
        Project = r.Project,
        FilePath = r.FilePath,
        FileName = r.FileName,
        FileType = r.FileType,
        PurposeShort = r.PurposeShort,
        PurposeFull = r.PurposeFull,
        AgentName = r.AgentName,
        AgentRole = r.AgentRole,
        AgentStatus = r.AgentStatus,
        Goals = r.Goals.Select(g => new AgentGoal { Description = g.Description, Status = g.Status }).ToList()
    };

    private static SupervisorAssignment MapSupervisor(SwarmSupervisorRecord r) => new()
    {
        Name = r.Name,
        Domain = r.Domain,
        FileCount = r.FileCount,
        ActiveAgents = r.ActiveAgents,
        GoalsCompleted = r.GoalsCompleted,
        GoalsTotal = r.GoalsTotal,
        Status = r.Status
    };

    // ── Static Seed Data (used for Firestore seeding + fallback) ─

    private static List<SwarmFileRecord> GetStaticSeedRecords() => GetStaticInventory()
        .Select(f => new SwarmFileRecord
        {
            Id = f.Id,
            Project = f.Project,
            FilePath = f.FilePath,
            FileName = f.FileName,
            FileType = f.FileType,
            PurposeShort = f.PurposeShort,
            PurposeFull = f.PurposeFull,
            AgentName = f.AgentName,
            AgentRole = f.AgentRole,
            AgentStatus = f.AgentStatus,
            Goals = f.Goals.Select(g => new SwarmGoalRecord { Description = g.Description, Status = g.Status }).ToList()
        }).ToList();

    private static List<SwarmSupervisorRecord> GetStaticSupervisorRecords() => GetStaticSupervisors()
        .Select((s, i) => new SwarmSupervisorRecord
        {
            Id = $"sup-{i:D2}",
            Name = s.Name,
            Domain = s.Domain,
            FileCount = s.FileCount,
            ActiveAgents = s.ActiveAgents,
            GoalsCompleted = s.GoalsCompleted,
            GoalsTotal = s.GoalsTotal,
            Status = s.Status
        }).ToList();

    private static List<SupervisorAssignment> GetStaticSupervisors() =>
    [
        new() { Name = "Triage Supervisor", Domain = "Shared + Enums + DTOs", FileCount = 94, ActiveAgents = 12, GoalsCompleted = 38, GoalsTotal = 94, Status = "Active" },
        new() { Name = "Data Supervisor", Domain = "Data + Repositories", FileCount = 42, ActiveAgents = 8, GoalsCompleted = 20, GoalsTotal = 42, Status = "Active" },
        new() { Name = "API Supervisor", Domain = "Dashboard.Api + Controllers", FileCount = 38, ActiveAgents = 6, GoalsCompleted = 15, GoalsTotal = 38, Status = "Active" },
        new() { Name = "UI Supervisor", Domain = "Dashboard.Web + Razor", FileCount = 35, ActiveAgents = 5, GoalsCompleted = 12, GoalsTotal = 35, Status = "Active" },
        new() { Name = "Build Supervisor", Domain = "BuildServer + DocGen", FileCount = 24, ActiveAgents = 4, GoalsCompleted = 10, GoalsTotal = 24, Status = "Active" },
        new() { Name = "Adapter Supervisor", Domain = "Mock + Azure + Cloud", FileCount = 18, ActiveAgents = 3, GoalsCompleted = 8, GoalsTotal = 18, Status = "Idle" },
        new() { Name = "Functions Supervisor", Domain = "Azure Functions", FileCount = 14, ActiveAgents = 2, GoalsCompleted = 6, GoalsTotal = 14, Status = "Active" },
        new() { Name = "Infra Supervisor", Domain = "AppHost + ServiceDefaults", FileCount = 6, ActiveAgents = 1, GoalsCompleted = 4, GoalsTotal = 6, Status = "Idle" },
    ];

    private static List<FileInventoryItem> GetStaticInventory() =>
    [
        // ── TheWatch.Shared — Domain Models ──────────────────────
        F("Shared", "Domain/Models/SwarmAgentDefinition.cs", "Agent Definition", "Defines single agent within swarm topology with tools, handoffs, and config.", "Triage Agent", "Specialist", "Active",
            [G("Validate schema", "Done"), G("Add versioning", "Pending")]),
        F("Shared", "Domain/Models/SwarmDefinition.cs", "Swarm Topology", "Complete multi-agent swarm topology with validation and lifecycle.", "Triage Agent", "Specialist", "Active",
            [G("Cycle detection", "Done"), G("Hot reload", "Pending")]),
        F("Shared", "Domain/Models/SwarmPresets.cs", "Preset Swarms", "Pre-built swarm topologies for safety, code review, dispatch, and audit.", "Triage Agent", "Specialist", "Active",
            [G("Add file-agent preset", "InProgress"), G("Parameterize models", "Pending")]),
        F("Shared", "Domain/Models/SwarmTask.cs", "Task Unit", "Unit of work flowing through swarm with handoff tracking and metrics.", "Triage Agent", "Specialist", "Active",
            [G("Token budgeting", "Pending"), G("Retry logic", "Pending")]),
        F("Shared", "Domain/Models/SwarmRunSummary.cs", "Run Metrics", "Aggregated metrics from completed swarm task execution.", "Metrics Agent", "Aggregator", "Idle",
            [G("Cost rollup", "Pending")]),
        F("Shared", "Domain/Models/SwarmToolDefinition.cs", "Tool Schema", "Function/tool declaration mapping to Azure OpenAI tool definitions.", "Triage Agent", "Specialist", "Done",
            [G("Schema validation", "Done")]),

        // ── Ports ────────────────────────────────────────────────
        F("Shared", "Domain/Ports/ISwarmPort.cs", "Swarm Port", "Port interface for Azure OpenAI multi-agent swarm orchestration.", "Port Agent", "Specialist", "Active",
            [G("Streaming callbacks", "InProgress"), G("Cancellation tokens", "Done")]),
        F("Shared", "Domain/Ports/IStorageService.cs", "Storage Port", "Core persistence abstraction for all entity types.", "Port Agent", "Specialist", "Done",
            [G("Contract tests", "Done")]),
        F("Shared", "Domain/Ports/IResponseCoordinationPort.cs", "Response Port", "Emergency response coordination with dispatch and escalation.", "Port Agent", "Specialist", "Active",
            [G("Validate records", "Done"), G("Add GeoJSON", "Pending")]),
        F("Shared", "Domain/Ports/IEmbeddingPort.cs", "Embedding Port", "Vector embedding generation for RAG.", "RAG Agent", "Specialist", "Active",
            [G("Batch embed", "Pending")]),
        F("Shared", "Domain/Ports/IVectorSearchPort.cs", "Vector Port", "Vector similarity search for context retrieval.", "RAG Agent", "Specialist", "Active",
            [G("Hybrid search", "Pending")]),

        // ── Enums ────────────────────────────────────────────────
        F("Shared", "Enums/SwarmRole.cs", "Swarm Roles", "Agent roles: Triage, Specialist, Reviewer, Orchestrator, etc.", "Schema Agent", "CodeGen", "Done",
            [G("Role docs", "Done")]),
        F("Shared", "Enums/SwarmStatus.cs", "Swarm States", "Lifecycle states: Created through Running to Completed/Failed.", "Schema Agent", "CodeGen", "Done",
            [G("State docs", "Done")]),
        F("Shared", "Enums/AgentType.cs", "Agent Types", "Known agent types: GPT4, Claude, GitHubActions, etc.", "Schema Agent", "CodeGen", "Done",
            [G("Add Gemini", "Pending")]),
        F("Shared", "Enums/Platform.cs", "Platforms", "Target platforms: iOS, Android, Alexa, GitHub, etc.", "Schema Agent", "CodeGen", "Done",
            [G("Platform docs", "Done")]),

        // ── DTOs ─────────────────────────────────────────────────
        F("Shared", "Dtos/AgentActivityDto.cs", "Agent DTO", "Positional record for agent activity telemetry.", "DTO Agent", "CodeGen", "Done",
            [G("Serialize test", "Done")]),
        F("Shared", "Dtos/BuildStatusDto.cs", "Build DTO", "Build status for dashboard display.", "DTO Agent", "CodeGen", "Done",
            [G("Add warnings", "Pending")]),

        // ── Data ─────────────────────────────────────────────────
        F("Data", "Configuration/ServiceCollectionExtensions.cs", "DI Wiring", "Registers data-layer adapters based on environment config.", "Config Agent", "Specialist", "Active",
            [G("Env validation", "Done"), G("Hot swap", "Pending")]),
        F("Data", "Context/TheWatchDbContext.cs", "EF Context", "Entity Framework Core database context for TheWatch.", "Data Agent", "Specialist", "Active",
            [G("Migration audit", "InProgress"), G("Seed data", "Pending")]),
        F("Data", "Repositories/SqlServer/SqlServerRepository.cs", "SQL Repo", "SQL Server repository via Entity Framework Core.", "SQL Agent", "Specialist", "Active",
            [G("Batch ops", "Pending"), G("Query logging", "Pending")]),
        F("Data", "Repositories/CosmosDb/CosmosDbRepository.cs", "Cosmos Repo", "Cosmos DB repository using native SDK.", "Cosmos Agent", "Specialist", "Active",
            [G("Partition keys", "InProgress"), G("RU budgeting", "Pending")]),
        F("Data", "Adapters/Firestore/FirestoreSwarmInventoryAdapter.cs", "Swarm Store", "Firestore adapter for swarm file inventory persistence.", "Firestore Agent", "Specialist", "Active",
            [G("Batch writes", "Done"), G("Query index", "Pending")]),

        // ── BuildServer ──────────────────────────────────────────
        F("BuildServer", "Services/BuildOrchestrator.cs", "Build Orch", "Orchestrates builds and broadcasts via SignalR.", "Build Agent", "Orchestrator", "Active",
            [G("Parallel builds", "Pending"), G("Agent dispatch", "InProgress")]),
        F("BuildServer", "Lsif/LsifIndexer.cs", "LSIF Index", "Full solution Roslyn index for code intelligence.", "Index Agent", "Specialist", "Active",
            [G("Incremental index", "Pending"), G("Symbol cache", "InProgress")]),
        F("BuildServer", "Controllers/BuildController.cs", "Build API", "REST endpoints for build triggering and status.", "Build Agent", "Specialist", "Active",
            [G("Swarm trigger", "Pending")]),

        // ── DocGen ───────────────────────────────────────────────
        F("DocGen", "Services/RoslynDocumentationAnalyzer.cs", "Doc Analyzer", "Parses C# source for missing XML doc members.", "Doc Agent", "Reviewer", "Active",
            [G("Coverage report", "InProgress"), G("Auto-generate", "Pending")]),
        F("DocGen", "Services/RabbitMqConsumerService.cs", "MQ Consumer", "Consumes file-change messages, dispatches Hangfire jobs.", "Queue Agent", "Specialist", "Active",
            [G("Dead letter", "Pending"), G("Retry policy", "InProgress")]),

        // ── Dashboard.Api ────────────────────────────────────────
        F("Dashboard.Api", "Program.cs", "API Entry", "ASP.NET Core host with Aspire, Hangfire, SignalR.", "API Agent", "Orchestrator", "Active",
            [G("OpenAPI spec", "InProgress"), G("Redis backplane", "Pending")]),
        F("Dashboard.Api", "Hubs/DashboardHub.cs", "SignalR Hub", "Real-time dashboard updates broadcast to clients.", "Hub Agent", "Specialist", "Active",
            [G("Swarm events", "InProgress"), G("Auth groups", "Pending")]),
        F("Dashboard.Api", "Services/SwarmCoordinationService.cs", "Swarm Coord", "RabbitMQ + Hangfire + SignalR swarm orchestration.", "Swarm Agent", "Orchestrator", "Active",
            [G("Result consumer", "Pending"), G("Redis state", "Pending")]),
        F("Dashboard.Api", "Controllers/SwarmInventoryController.cs", "Inventory API", "REST CRUD for swarm file inventory in Firestore.", "API Agent", "Specialist", "Active",
            [G("Pagination", "Pending"), G("Auth", "Pending")]),

        // ── Dashboard.Web ────────────────────────────────────────
        F("Dashboard.Web", "Program.cs", "Web Entry", "Blazor SSR host with MudBlazor and Aspire.", "UI Agent", "Orchestrator", "Active",
            [G("Theme config", "Done"), G("Error boundary", "Pending")]),
        F("Dashboard.Web", "Components/Pages/SwarmDashboard.razor", "Swarm Page", "MudDataGrid swarm dashboard with mission and supervisors.", "UI Agent", "Specialist", "Active",
            [G("Live refresh", "Pending"), G("SignalR updates", "Pending")]),
        F("Dashboard.Web", "Services/SwarmInventoryService.cs", "Inventory Svc", "Firestore-backed inventory with static fallback.", "UI Agent", "Specialist", "Active",
            [G("Caching", "Pending"), G("Error handling", "Done")]),

        // ── AppHost ──────────────────────────────────────────────
        F("AppHost", "AppHost.cs", "Aspire Host", "Aspire orchestration: SQL, PG, Redis, Cosmos, RabbitMQ, Qdrant, Firestore.", "Infra Agent", "Orchestrator", "Active",
            [G("Resource health", "InProgress"), G("Firestore emulator", "Done")]),

        // ── Functions ────────────────────────────────────────────
        F("Functions", "Functions/EscalationCheckFunction.cs", "Escalation Fn", "Periodic escalation sweep every 30 seconds.", "Escalation Agent", "Specialist", "Active",
            [G("Backoff policy", "Pending")]),
        F("Functions", "Functions/ResponseDispatchFunction.cs", "Dispatch Fn", "RabbitMQ-triggered response dispatch.", "Dispatch Agent", "Specialist", "Active",
            [G("Fan-out tuning", "Pending")]),
        F("Functions", "Functions/WebhookReceiverFunction.cs", "Webhook Fn", "Receives SOS triggers from mobile devices.", "Webhook Agent", "Specialist", "Active",
            [G("Signature verify", "InProgress")]),
    ];

    // ── Helpers ──────────────────────────────────────────────────

    private static FileInventoryItem F(
        string project, string path, string purposeShort, string purposeFull,
        string agentName, string agentRole, string agentStatus, List<AgentGoal> goals) => new()
    {
        Project = project,
        FilePath = path,
        FileName = Path.GetFileName(path),
        FileType = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
        PurposeShort = purposeShort,
        PurposeFull = purposeFull,
        AgentName = agentName,
        AgentRole = agentRole,
        AgentStatus = agentStatus,
        Goals = goals
    };

    private static AgentGoal G(string desc, string status) => new() { Description = desc, Status = status };
}
