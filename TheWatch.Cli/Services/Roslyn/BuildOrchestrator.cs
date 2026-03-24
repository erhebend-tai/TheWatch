// =============================================================================
// BuildOrchestrator (CLI) — Thin client over BuildServer REST API.
// =============================================================================
// Does NOT reimplement build orchestration. Routes all build, test, LSIF,
// and agent-branch operations through the BuildServer's REST API at:
//   https+http://build-server/api/build/*
//
// The BuildServer (TheWatch.BuildServer) owns the actual build pipeline:
//   Restore → Build → Test → LSIF Index → Broadcast
//
// This class provides:
//   1. HTTP client wrappers for every /api/build/* endpoint
//   2. SignalR subscription for live build status updates
//   3. Fallback to local `dotnet build` if BuildServer is unreachable
//   4. CLI-friendly result formatting for the dashboard panels
//
// BuildServer Endpoints Used:
//   POST /api/build/runs              — Queue a build (returns BuildRun immediately)
//   GET  /api/build/runs              — Build history
//   GET  /api/build/runs/{id}         — Poll specific build status
//   GET  /api/build/status            — Current orchestrator state
//   GET  /api/build/agents            — List tracked agent branches
//   POST /api/build/agents            — Register an agent branch
//   PUT  /api/build/agents/{b}/status — Update agent branch status
//   POST /api/build/merge/plan        — Generate merge plan
//   GET  /api/build/merge/plan        — Get active merge plan
//   POST /api/build/merge/{branch}    — Merge a branch + validate
//   GET  /api/build/index/symbols     — LSIF symbol search
//   GET  /api/build/index/ports       — Port→adapter mapping
//   GET  /api/build/index/stats       — LSIF index statistics
//   POST /api/build/index/reindex     — Trigger re-index
//
// Example:
//   var orch = new BuildOrchestrator("https+http://build-server", solutionRoot);
//   var run = await orch.QueueBuildAsync();            // POST /api/build/runs
//   await orch.PollUntilCompleteAsync(run.Id);         // GET  /api/build/runs/{id}
//   var symbols = await orch.SearchSymbolsAsync("IEvidencePort");
//
// WAL: The BuildServer must be running for full functionality.
//      If unreachable, QueueBuildAsync falls back to local `dotnet build`.
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TheWatch.Cli.Services.Roslyn;

public class BuildOrchestrator : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly string _buildServerUrl;
    private readonly string _solutionRoot;
    private readonly JsonSerializerOptions _json;
    private bool _serverReachable = true;

    public event Action<string>? OnOutputLine;
    public event Action<BuildServerDiagnostic>? OnDiagnostic;

    public BuildOrchestrator(string buildServerUrl, string solutionRoot)
    {
        _buildServerUrl = buildServerUrl;
        _solutionRoot = solutionRoot;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(buildServerUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    // ── Build Operations (delegate to BuildServer) ──────────────────

    /// <summary>Queue a build via BuildServer. Falls back to local if server unreachable.</summary>
    public async Task<BuildRunDto> QueueBuildAsync(string trigger = "Manual", string? source = null, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/build/runs",
                new { Trigger = trigger, TriggerSource = source }, _json, ct);

            if (response.IsSuccessStatusCode)
            {
                _serverReachable = true;
                return await response.Content.ReadFromJsonAsync<BuildRunDto>(_json, ct) ?? new();
            }
        }
        catch (HttpRequestException) { _serverReachable = false; }
        catch (TaskCanceledException) { _serverReachable = false; }

        // Fallback: local build
        return await RunLocalBuildAsync(ct);
    }

    /// <summary>Poll a build run until it completes or times out.</summary>
    public async Task<BuildRunDto> PollUntilCompleteAsync(string runId, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        BuildRunDto? run = null;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetAsync($"/api/build/runs/{runId}", ct);
                if (response.IsSuccessStatusCode)
                {
                    run = await response.Content.ReadFromJsonAsync<BuildRunDto>(_json, ct);
                    if (run is not null)
                    {
                        OnOutputLine?.Invoke($"[Build {runId}] Status: {run.Status}");
                        if (run.Status is "Succeeded" or "Failed" or "Cancelled")
                            return run;
                    }
                }
            }
            catch { /* retry */ }

            await Task.Delay(1000, ct);
        }

        return run ?? new BuildRunDto { Id = runId, Status = "Timeout" };
    }

    /// <summary>Get current build orchestrator status.</summary>
    public async Task<BuildStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/build/status", ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<BuildStatusDto>(_json, ct) ?? new();
        }
        catch { _serverReachable = false; }
        return new BuildStatusDto { State = _serverReachable ? "Idle" : "Offline" };
    }

    /// <summary>Get build history.</summary>
    public async Task<List<BuildRunDto>> GetBuildHistoryAsync(int limit = 20, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<BuildRunDto>>($"/api/build/runs?limit={limit}", _json, ct) ?? new();
        }
        catch { return new(); }
    }

    // ── Agent Branch Operations ─────────────────────────────────────

    /// <summary>List tracked agent branches from BuildServer.</summary>
    public async Task<List<AgentBranchDto>> GetAgentBranchesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AgentBranchDto>>("/api/build/agents", _json, ct) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Register a new agent branch.</summary>
    public async Task<AgentBranchDto?> RegisterAgentAsync(string agentName, string branchName, string scope, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/build/agents",
                new { AgentName = agentName, BranchName = branchName, Scope = scope }, _json, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<AgentBranchDto>(_json, ct);
        }
        catch { }
        return null;
    }

    // ── Merge Planning ──────────────────────────────────────────────

    /// <summary>Generate a merge plan from BuildServer.</summary>
    public async Task<MergePlanDto?> GenerateMergePlanAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync("/api/build/merge/plan", null, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<MergePlanDto>(_json, ct);
        }
        catch { }
        return null;
    }

    /// <summary>Get active merge plan.</summary>
    public async Task<MergePlanDto?> GetMergePlanAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/build/merge/plan", ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<MergePlanDto>(_json, ct);
        }
        catch { }
        return null;
    }

    /// <summary>Trigger merge and validation build for a branch.</summary>
    public async Task<BuildRunDto?> MergeAndValidateAsync(string branchName, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"/api/build/merge/{branchName}", null, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<BuildRunDto>(_json, ct);
        }
        catch { }
        return null;
    }

    // ── LSIF Index Queries ──────────────────────────────────────────

    /// <summary>Search symbols in the LSIF index.</summary>
    public async Task<List<SymbolInfoDto>> SearchSymbolsAsync(string query, string? kind = null, string? project = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/build/index/symbols?query={Uri.EscapeDataString(query)}";
            if (kind is not null) url += $"&kind={kind}";
            if (project is not null) url += $"&project={Uri.EscapeDataString(project)}";
            return await _http.GetFromJsonAsync<List<SymbolInfoDto>>(url, _json, ct) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Get port→adapter mapping from LSIF index.</summary>
    public async Task<List<PortAdapterLinkDto>> GetPortAdapterMapAsync(string? portFilter = null, CancellationToken ct = default)
    {
        try
        {
            var url = "/api/build/index/ports";
            if (portFilter is not null) url += $"?port={Uri.EscapeDataString(portFilter)}";
            return await _http.GetFromJsonAsync<List<PortAdapterLinkDto>>(url, _json, ct) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Get LSIF index statistics.</summary>
    public async Task<IndexStatsDto?> GetIndexStatsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<IndexStatsDto>("/api/build/index/stats", _json, ct);
        }
        catch { return null; }
    }

    /// <summary>Trigger LSIF re-index.</summary>
    public async Task<IndexSummaryDto?> TriggerReindexAsync(string? project = null, CancellationToken ct = default)
    {
        try
        {
            var url = "/api/build/index/reindex";
            if (project is not null) url += $"?project={Uri.EscapeDataString(project)}";
            var response = await _http.PostAsync(url, null, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<IndexSummaryDto>(_json, ct);
        }
        catch { }
        return null;
    }

    // ── Server Health ───────────────────────────────────────────────

    public bool IsServerReachable => _serverReachable;

    public async Task<bool> CheckServerHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/build/status", ct);
            _serverReachable = response.IsSuccessStatusCode;
        }
        catch { _serverReachable = false; }
        return _serverReachable;
    }

    // ── Local Fallback ──────────────────────────────────────────────

    private async Task<BuildRunDto> RunLocalBuildAsync(CancellationToken ct)
    {
        OnOutputLine?.Invoke("[BuildServer unreachable — falling back to local dotnet build]");

        var result = new BuildRunDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Status = "Building",
            StartedAt = DateTime.UtcNow
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", File.Exists(Path.Combine(_solutionRoot, "TheWatch.slnx"))
                ? Path.Combine(_solutionRoot, "TheWatch.slnx")
                : Path.Combine(_solutionRoot, "TheWatch.sln"), "-v:minimal" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _solutionRoot
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            OnOutputLine?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        result.Status = process.ExitCode == 0 ? "Succeeded" : "Failed";
        result.CompletedAt = DateTime.UtcNow;

        return result;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

// ── DTOs matching BuildServer Models ────────────────────────────────

public class BuildRunDto
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string? Trigger { get; set; }
    public string? TriggerSource { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    public List<ProjectBuildResultDto> ProjectResults { get; set; } = new();
    public TestSummaryDto? TestSummary { get; set; }
    public IndexSummaryDto? IndexSummary { get; set; }
    public List<BuildLogEntryDto> Logs { get; set; } = new();
}

public class BuildStatusDto
{
    public string State { get; set; } = "Idle";
    public BuildRunDto? CurrentBuild { get; set; }
    public BuildRunDto? LastCompletedBuild { get; set; }
    public int QueueDepth { get; set; }
}

public class ProjectBuildResultDto
{
    public string ProjectName { get; set; } = "";
    public bool Succeeded { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

public class TestSummaryDto
{
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}

public class IndexSummaryDto
{
    public int DocumentsIndexed { get; set; }
    public int SymbolsIndexed { get; set; }
    public int ReferencesFound { get; set; }
    public int PortAdapterLinksFound { get; set; }
    public TimeSpan Duration { get; set; }
}

public class BuildLogEntryDto
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

public class AgentBranchDto
{
    public string AgentName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Status { get; set; } = "Running";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> FilesModified { get; set; } = new();
    public List<string> ConflictFiles { get; set; } = new();
}

public class MergePlanDto
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<MergeStepDto> Steps { get; set; } = new();
}

public class MergeStepDto
{
    public int Order { get; set; }
    public string BranchName { get; set; } = "";
    public string AgentName { get; set; } = "";
    public List<string> DependsOn { get; set; } = new();
    public List<string> HighRiskFiles { get; set; } = new();
    public string? Notes { get; set; }
}

public class SymbolInfoDto
{
    public string Name { get; set; } = "";
    public string FullyQualifiedName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public class PortAdapterLinkDto
{
    public string PortInterfaceName { get; set; } = "";
    public string AdapterClassName { get; set; } = "";
    public string AdapterProject { get; set; } = "";
}

public class IndexStatsDto
{
    public DateTime GeneratedAt { get; set; }
    public int TotalFiles { get; set; }
    public int TotalSymbols { get; set; }
    public int TotalReferences { get; set; }
    public int TotalPortAdapterLinks { get; set; }
}

public class BuildServerDiagnostic
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
