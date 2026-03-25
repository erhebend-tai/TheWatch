// =============================================================================
// TheWatch.Cli — Terminal UI Command Center Entry Point
// =============================================================================
// Boots the full-screen TUI dashboard with:
//   - Top row: Feature status | Agent activity | Service health panels
//   - Bottom row: Three embedded terminal panes (Terminal 1 defaults to Claude Code)
//
// Subcommands:
//   (default)     — Launch the TUI dashboard
//   swarm         — Manage Azure OpenAI multi-agent swarms
//
// Architecture:
//   Program.cs → DashboardApp (Terminal.Gui Application.Run)
//     ├── FeaturePanel        — polls GET /api/features, receives SignalR updates
//     ├── AgentPanel           — polls GET /api/agents, receives SignalR "AgentActivityRecorded"
//     ├── ServicePanel         — polls GET /api/health, receives SignalR health updates
//     ├── EmbeddedTerminal[0]  — spawns "claude" process (Claude Code CLI)
//     ├── EmbeddedTerminal[1]  — spawns user-chosen command (default: bash/powershell)
//     └── EmbeddedTerminal[2]  — spawns user-chosen command (default: bash/powershell)
//
// Example:
//   dotnet run --project TheWatch.Cli
//   dotnet run --project TheWatch.Cli -- --api-url https://localhost:5001
//   dotnet run --project TheWatch.Cli -- --no-signalr   # polling only, no real-time
//   dotnet run --project TheWatch.Cli -- swarm presets
//   dotnet run --project TheWatch.Cli -- swarm create safety-report-pipeline
//   dotnet run --project TheWatch.Cli -- swarm run safety-report-pipeline --input "SOS at 38.9, -77.0" --stream
//   dotnet run --project TheWatch.Cli -- --aoai-endpoint https://my.openai.azure.com/ --aoai-key sk-... swarm presets
//
// WAL: System.CommandLine parses args before Terminal.Gui takes over the terminal.
//      Once Application.Init() is called, stdout belongs to Terminal.Gui.
//      Swarm commands run headless (no TUI) and exit when done.
// =============================================================================

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Logging;
using TheWatch.Adapters.Azure;
using TheWatch.Adapters.Mock;
using TheWatch.Cli.App;
using TheWatch.Cli.Commands;
using TheWatch.Shared.Domain.Ports;

// ── Global Options ──────────────────────────────────────────────────

var apiUrlOption = new Option<string>("--api-url")
{
    Description = "Dashboard API base URL (default: auto-discover via Aspire or https://localhost:5001)",
    DefaultValueFactory = _ => "https://localhost:5001"
};

var noSignalROption = new Option<bool>("--no-signalr")
{
    Description = "Disable SignalR real-time updates (poll-only mode)",
    DefaultValueFactory = _ => false
};

var pollIntervalOption = new Option<int>("--poll-interval")
{
    Description = "Polling interval in seconds for dashboard data refresh",
    DefaultValueFactory = _ => 5
};

// Azure OpenAI options (used by swarm commands)
var aoaiEndpointOption = new Option<string?>("--aoai-endpoint")
{
    Description = "Azure OpenAI endpoint URL (or set AZURE_OPENAI_ENDPOINT env var)",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
};

var aoaiKeyOption = new Option<string?>("--aoai-key")
{
    Description = "Azure OpenAI API key (or set AZURE_OPENAI_API_KEY env var)",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
};

// ── Root Command ────────────────────────────────────────────────────

var rootCommand = new RootCommand("TheWatch CLI Command Center — TUI dashboard + Azure OpenAI swarm management")
{
    apiUrlOption,
    noSignalROption,
    pollIntervalOption,
    aoaiEndpointOption,
    aoaiKeyOption
};

// ── Swarm Port Factory ──────────────────────────────────────────────
// Lazily creates the swarm port when a swarm command is invoked.
// Reads endpoint/key from CLI options or environment variables.

ISwarmPort? _cachedSwarmPort = null;

ISwarmPort? CreateSwarmPort()
{
    if (_cachedSwarmPort is not null) return _cachedSwarmPort;

    var endpoint = rootCommand.Parse(args).GetValue(aoaiEndpointOption)
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    var key = rootCommand.Parse(args).GetValue(aoaiKeyOption)
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
        return null;

    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
    var logger = loggerFactory.CreateLogger<AzureOpenAISwarmAdapter>();
    _cachedSwarmPort = new AzureOpenAISwarmAdapter(endpoint, key, logger);
    return _cachedSwarmPort;
}

// ── Swarm Agent Port Factory ──────────────────────────────────────
// Creates the interactive swarm agent that prompts users for their request.
// Uses Azure OpenAI when configured, falls back to mock adapter.

ISwarmAgentPort? _cachedAgentPort = null;

ISwarmAgentPort CreateSwarmAgentPort()
{
    if (_cachedAgentPort is not null) return _cachedAgentPort;

    var endpoint = rootCommand.Parse(args).GetValue(aoaiEndpointOption)
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    var key = rootCommand.Parse(args).GetValue(aoaiKeyOption)
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

    if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(key))
    {
        var logger = loggerFactory.CreateLogger<TheWatch.Adapters.Azure.AzureOpenAISwarmAgentAdapter>();
        _cachedAgentPort = new TheWatch.Adapters.Azure.AzureOpenAISwarmAgentAdapter(endpoint, key, logger);
    }
    else
    {
        var logger = loggerFactory.CreateLogger<TheWatch.Adapters.Mock.MockSwarmAgentAdapter>();
        _cachedAgentPort = new TheWatch.Adapters.Mock.MockSwarmAgentAdapter(logger);
    }

    return _cachedAgentPort;
}

// ── Register Subcommands ────────────────────────────────────────────

rootCommand.Add(SwarmCommand.Build(CreateSwarmPort, CreateSwarmAgentPort));
rootCommand.Add(PlanCommand.Build(CreateSwarmPort));
rootCommand.Add(CodeGenCommand.Build());

// ── Default Handler (TUI Dashboard) ─────────────────────────────────

rootCommand.SetAction(async (parseResult) =>
{
    var apiUrl = parseResult.GetValue(apiUrlOption) ?? "https://localhost:5001";
    var noSignalR = parseResult.GetValue(noSignalROption);
    var pollInterval = parseResult.GetValue(pollIntervalOption);

    var config = new DashboardConfig
    {
        ApiBaseUrl = apiUrl,
        EnableSignalR = !noSignalR,
        PollIntervalSeconds = pollInterval
    };

    var app = new DashboardApp(config);
    await app.RunAsync();
});

return await rootCommand.Parse(args).InvokeAsync();
