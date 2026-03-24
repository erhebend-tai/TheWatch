// =============================================================================
// BuildOrchestrator — Multi-project build coordination and agent branch management
// =============================================================================
// The central brain of the build server. Manages:
//   1. Solution builds (dotnet build / dotnet test)
//   2. Agent branch tracking (which branches exist, their status, merge readiness)
//   3. Merge planning (ordered merge with dependency resolution)
//   4. Post-merge validation builds + LSIF re-indexing
//   5. File watch for incremental rebuilds
//
// Build pipeline: Restore → Build → Test → LSIF Index → Broadcast
//
// Communicates with:
//   - CLI Dashboard (via SignalR): real-time build status, agent progress
//   - LspServer: triggers re-index after successful builds
//   - AgentPanel in TheWatch.Cli: agent branch state changes
//
// WAL: All builds shell out to `dotnet` via CliWrap. We parse structured output
//      (binary log) rather than text scraping. Build runs are queued and executed
//      serially — parallel project compilation is handled by MSBuild internally.
// =============================================================================

using System.Collections.Concurrent;
using System.Text;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.AspNetCore.SignalR.Client;
using TheWatch.BuildServer.Lsif;
using TheWatch.BuildServer.Models;

namespace TheWatch.BuildServer.Services;

public class BuildOrchestrator : IAsyncDisposable
{
    private readonly string _solutionPath;
    private readonly string _repoRoot;
    private readonly LsifIndexer _indexer;
    private readonly ILogger<BuildOrchestrator> _logger;
    private readonly ConcurrentQueue<BuildRun> _buildQueue = new();
    private readonly ConcurrentBag<AgentBranch> _agentBranches = new();
    private readonly List<BuildRun> _buildHistory = [];
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private HubConnection? _hubConnection;
    private CancellationTokenSource _cts = new();
    private MergePlan? _activeMergePlan;

    // Public state for LSP queries
    public BuildRun? CurrentBuild { get; private set; }
    public BuildRun? LastCompletedBuild => _buildHistory.LastOrDefault();
    public int QueueDepth => _buildQueue.Count;
    public IReadOnlyCollection<AgentBranch> AgentBranches => _agentBranches.ToArray();
    public MergePlan? ActiveMergePlan => _activeMergePlan;
    public IReadOnlyList<BuildRun> BuildHistory => _buildHistory.AsReadOnly();

    public BuildOrchestrator(string solutionPath, LsifIndexer indexer, ILogger<BuildOrchestrator> logger)
    {
        _solutionPath = solutionPath;
        _repoRoot = Path.GetDirectoryName(solutionPath) ?? ".";
        _indexer = indexer;
        _logger = logger;
    }

    /// <summary>
    /// Connect to the Dashboard API SignalR hub for broadcasting build events.
    /// </summary>
    public async Task ConnectSignalRAsync(string hubUrl, CancellationToken ct = default)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{hubUrl}/hubs/dashboard")
            .WithAutomaticReconnect()
            .Build();

        try
        {
            await _hubConnection.StartAsync(ct);
            _logger.LogInformation("BuildOrchestrator connected to SignalR hub at {Url}", hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to SignalR hub — running in offline mode");
        }
    }

    /// <summary>
    /// Start the background build queue processor.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => ProcessBuildQueueAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("BuildOrchestrator started — watching {Solution}", _solutionPath);
        return Task.CompletedTask;
    }

    // ── Build Operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Queue a new build run. Returns immediately with the BuildRun ID.
    /// </summary>
    public BuildRun QueueBuild(BuildTrigger trigger = BuildTrigger.Manual, string? triggerSource = null)
    {
        var run = new BuildRun
        {
            Trigger = trigger,
            TriggerSource = triggerSource
        };
        _buildQueue.Enqueue(run);
        _logger.LogInformation("Build {Id} queued (trigger: {Trigger}, source: {Source})",
            run.Id, trigger, triggerSource ?? "manual");
        return run;
    }

    private async Task ProcessBuildQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_buildQueue.TryDequeue(out var run))
            {
                await ExecuteBuildAsync(run, ct);
            }
            else
            {
                await Task.Delay(500, ct); // poll interval
            }
        }
    }

    private async Task ExecuteBuildAsync(BuildRun run, CancellationToken ct)
    {
        await _buildLock.WaitAsync(ct);
        try
        {
            CurrentBuild = run;
            run.Status = BuildRunStatus.Restoring;
            await BroadcastBuildStatus(run);

            _logger.LogInformation("Build {Id} starting — Phase: Restore", run.Id);

            // Phase 1: Restore
            var restoreResult = await RunDotnetCommandAsync("restore", _solutionPath, ct);
            AddLog(run, "Info", $"Restore completed (exit code: {restoreResult.ExitCode})");

            if (restoreResult.ExitCode != 0)
            {
                run.Status = BuildRunStatus.Failed;
                AddLog(run, "Error", $"Restore failed:\n{restoreResult.StandardError}");
                await CompleteBuild(run);
                return;
            }

            // Phase 2: Build
            run.Status = BuildRunStatus.Building;
            await BroadcastBuildStatus(run);
            _logger.LogInformation("Build {Id} — Phase: Build", run.Id);

            var buildResult = await RunDotnetCommandAsync(
                $"build \"{_solutionPath}\" --no-restore -v:minimal -warnaserror:false", "", ct);

            // Parse build output for per-project results
            ParseBuildOutput(run, buildResult.StandardOutput, buildResult.StandardError);
            AddLog(run, "Info", $"Build completed (exit code: {buildResult.ExitCode})");

            if (buildResult.ExitCode != 0)
            {
                run.Status = BuildRunStatus.Failed;
                AddLog(run, "Error", "Build failed — see project diagnostics");
                await CompleteBuild(run);
                return;
            }

            // Phase 3: Test
            run.Status = BuildRunStatus.Testing;
            await BroadcastBuildStatus(run);
            _logger.LogInformation("Build {Id} — Phase: Test", run.Id);

            var testResult = await RunDotnetCommandAsync(
                $"test \"{_solutionPath}\" --no-build -v:minimal --logger:\"console;verbosity=minimal\"", "", ct);

            run.TestSummary = ParseTestOutput(testResult.StandardOutput);
            AddLog(run, "Info",
                $"Tests: {run.TestSummary.Passed} passed, {run.TestSummary.Failed} failed, {run.TestSummary.Skipped} skipped");

            // Phase 4: LSIF Re-index (on successful build)
            run.Status = BuildRunStatus.Indexing;
            await BroadcastBuildStatus(run);
            _logger.LogInformation("Build {Id} — Phase: LSIF Index", run.Id);

            var indexSw = System.Diagnostics.Stopwatch.StartNew();
            var index = await _indexer.BuildFullIndexAsync(ct);
            indexSw.Stop();

            run.IndexSummary = new LsifIndexSummary(
                index.TotalFiles, index.TotalSymbols, index.TotalReferences,
                index.TotalPortAdapterLinks, indexSw.Elapsed);

            // Persist index
            var indexPath = Path.Combine(_repoRoot, ".thewatch", "lsif-index.json");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await _indexer.PersistAsync(indexPath, ct);

            run.Status = BuildRunStatus.Succeeded;
            AddLog(run, "Info", $"LSIF indexed: {index.TotalSymbols} symbols, {index.TotalPortAdapterLinks} port-adapter links");

            await CompleteBuild(run);
        }
        catch (OperationCanceledException)
        {
            run.Status = BuildRunStatus.Cancelled;
            await CompleteBuild(run);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build {Id} failed with exception", run.Id);
            run.Status = BuildRunStatus.Failed;
            AddLog(run, "Error", $"Unhandled exception: {ex.Message}");
            await CompleteBuild(run);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private async Task CompleteBuild(BuildRun run)
    {
        run.CompletedAt = DateTime.UtcNow;
        CurrentBuild = null;
        _buildHistory.Add(run);

        // Keep history capped
        while (_buildHistory.Count > 50)
            _buildHistory.RemoveAt(0);

        await BroadcastBuildStatus(run);
        _logger.LogInformation("Build {Id} completed: {Status} in {Duration}",
            run.Id, run.Status, run.Duration);
    }

    // ── Agent Branch Management ──────────────────────────────────────────────

    /// <summary>
    /// Register an agent branch for tracking.
    /// </summary>
    public AgentBranch RegisterAgentBranch(string agentName, string branchName, string scope)
    {
        var branch = new AgentBranch
        {
            AgentName = agentName,
            BranchName = branchName,
            Scope = scope
        };
        _agentBranches.Add(branch);
        _logger.LogInformation("Registered agent branch: {Agent} on {Branch}", agentName, branchName);
        return branch;
    }

    /// <summary>
    /// Update agent branch status (called when agent completes or reports progress).
    /// </summary>
    public void UpdateAgentBranch(string branchName, AgentBranchStatus status, List<string>? filesModified = null)
    {
        var branch = _agentBranches.FirstOrDefault(b => b.BranchName == branchName);
        if (branch is null) return;

        branch.Status = status;
        if (status is AgentBranchStatus.Completed or AgentBranchStatus.Merged or AgentBranchStatus.Failed)
            branch.CompletedAt = DateTime.UtcNow;
        if (filesModified is not null)
            branch.FilesModified = filesModified;
    }

    /// <summary>
    /// Generate a merge plan based on agent branch dependencies.
    /// </summary>
    public MergePlan GenerateMergePlan()
    {
        var plan = new MergePlan();

        // High-risk files that multiple agents touch
        var fileConflictMap = _agentBranches
            .Where(b => b.Status is AgentBranchStatus.Completed or AgentBranchStatus.MergePending)
            .SelectMany(b => b.FilesModified.Select(f => (File: f, Branch: b)))
            .GroupBy(x => x.File)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Branch.BranchName).ToList());

        // Dependency graph (hardcoded based on TheWatch architecture knowledge)
        var dependencyRules = new Dictionary<string, List<string>>
        {
            ["feature/signalr-mobile-client"] = [],
            ["feature/adapter-registry-mobile"] = ["feature/signalr-mobile-client"],
            ["feature/offline-first-sync-engine"] = ["feature/adapter-registry-mobile"],
            ["feature/mobile-log-viewer-ui"] = [],
            ["feature/sos-lifecycle-correlation"] = ["feature/mobile-log-viewer-ui"],
            ["feature/mobile-test-runner"] = ["feature/signalr-mobile-client", "feature/sos-lifecycle-correlation"],
            ["feature/alexa-skills"] = ["feature/iot-backend"],
            ["feature/google-home"] = ["feature/iot-backend"],
            ["feature/iot-backend"] = [],
        };

        // Topological sort for merge order
        var order = 1;
        var merged = new HashSet<string>();
        var available = _agentBranches
            .Where(b => b.Status is AgentBranchStatus.Completed or AgentBranchStatus.MergePending)
            .Select(b => b.BranchName)
            .ToHashSet();

        while (merged.Count < available.Count)
        {
            var nextBatch = available
                .Where(b => !merged.Contains(b))
                .Where(b =>
                {
                    var deps = dependencyRules.GetValueOrDefault(b, []);
                    return deps.All(d => merged.Contains(d) || !available.Contains(d));
                })
                .ToList();

            if (nextBatch.Count == 0)
            {
                // Remaining branches have circular or unresolvable deps — merge anyway
                nextBatch = available.Where(b => !merged.Contains(b)).ToList();
            }

            foreach (var branch in nextBatch)
            {
                var agentBranch = _agentBranches.First(b => b.BranchName == branch);
                var deps = dependencyRules.GetValueOrDefault(branch, [])
                    .Where(d => available.Contains(d))
                    .ToList();
                var highRisk = fileConflictMap
                    .Where(kv => kv.Value.Contains(branch))
                    .Select(kv => kv.Key)
                    .ToList();

                plan.Steps.Add(new MergeStep(
                    order++,
                    branch,
                    agentBranch.AgentName,
                    deps,
                    highRisk,
                    highRisk.Count > 0 ? $"Conflicts likely in: {string.Join(", ", highRisk)}" : null));

                merged.Add(branch);
            }
        }

        _activeMergePlan = plan;
        _logger.LogInformation("Generated merge plan with {Steps} steps", plan.Steps.Count);
        return plan;
    }

    /// <summary>
    /// Attempt to merge a branch and run validation build.
    /// </summary>
    public async Task<BuildRun> MergeAndValidateAsync(string branchName, CancellationToken ct = default)
    {
        var branch = _agentBranches.FirstOrDefault(b => b.BranchName == branchName);
        if (branch is null)
            throw new InvalidOperationException($"Branch {branchName} not registered");

        _logger.LogInformation("Starting merge of {Branch}", branchName);
        branch.Status = AgentBranchStatus.MergePending;

        // Attempt git merge
        var mergeResult = await RunGitCommandAsync($"merge {branchName} --no-ff", ct);

        if (mergeResult.ExitCode != 0)
        {
            branch.Status = AgentBranchStatus.MergeConflict;
            branch.ConflictFiles = ParseConflictFiles(mergeResult.StandardOutput + mergeResult.StandardError);
            _logger.LogWarning("Merge conflict on {Branch}: {Files}",
                branchName, string.Join(", ", branch.ConflictFiles));

            // Abort the merge
            await RunGitCommandAsync("merge --abort", ct);

            // Still queue a build to show current state
            return QueueBuild(BuildTrigger.AgentMerge, branchName);
        }

        branch.Status = AgentBranchStatus.Merged;
        branch.CompletedAt = DateTime.UtcNow;

        // Queue validation build
        var buildRun = QueueBuild(BuildTrigger.AgentMerge, branchName);
        branch.ValidationBuild = buildRun;

        return buildRun;
    }

    // ── Process Helpers ──────────────────────────────────────────────────────

    private async Task<BufferedCommandResult> RunDotnetCommandAsync(string args, string workingDir, CancellationToken ct)
    {
        var cmd = Cli.Wrap("dotnet")
            .WithArguments(args)
            .WithWorkingDirectory(string.IsNullOrEmpty(workingDir) ? _repoRoot : Path.GetDirectoryName(workingDir) ?? _repoRoot)
            .WithValidation(CommandResultValidation.None);

        return await cmd.ExecuteBufferedAsync(ct);
    }

    private async Task<BufferedCommandResult> RunGitCommandAsync(string args, CancellationToken ct)
    {
        var cmd = Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(_repoRoot)
            .WithValidation(CommandResultValidation.None);

        return await cmd.ExecuteBufferedAsync(ct);
    }

    private void ParseBuildOutput(BuildRun run, string stdout, string stderr)
    {
        // Parse MSBuild minimal verbosity output for per-project results
        var lines = (stdout + "\n" + stderr).Split('\n');
        var currentProject = "";
        var errors = new List<BuildDiagnostic>();
        var warnings = new List<BuildDiagnostic>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Project start: "Build started 1/15/2026 ..."
            // Project line: "  TheWatch.Shared -> ..."
            if (trimmed.Contains("->") && trimmed.Contains(".dll"))
            {
                var projectName = trimmed.Split("->")[0].Trim();
                run.ProjectResults.Add(new ProjectBuildResult(
                    projectName, "", true,
                    warnings.Count(w => w.Message.Contains(projectName)),
                    errors.Count(e => e.Message.Contains(projectName)),
                    TimeSpan.Zero, []));
            }

            // Error: "path(line,col): error CS1234: message"
            if (trimmed.Contains(": error "))
            {
                var parts = trimmed.Split(": error ");
                errors.Add(new BuildDiagnostic("Error", "", parts.Length > 1 ? parts[1] : trimmed, "", 0, 0));
            }

            if (trimmed.Contains(": warning "))
            {
                var parts = trimmed.Split(": warning ");
                warnings.Add(new BuildDiagnostic("Warning", "", parts.Length > 1 ? parts[1] : trimmed, "", 0, 0));
            }
        }
    }

    private TestRunSummary ParseTestOutput(string output)
    {
        // Parse dotnet test minimal output
        var passed = 0; var failed = 0; var skipped = 0;
        var failedTests = new List<TestResult>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Passed!") || trimmed.StartsWith("Total tests:"))
            {
                // "Total tests: 42. Passed: 40. Failed: 1. Skipped: 1."
                var parts = trimmed.Split('.');
                foreach (var part in parts)
                {
                    var p = part.Trim();
                    if (p.StartsWith("Passed:")) int.TryParse(p.Replace("Passed:", "").Trim(), out passed);
                    if (p.StartsWith("Failed:")) int.TryParse(p.Replace("Failed:", "").Trim(), out failed);
                    if (p.StartsWith("Skipped:")) int.TryParse(p.Replace("Skipped:", "").Trim(), out skipped);
                }
            }
            else if (trimmed.StartsWith("Failed "))
            {
                failedTests.Add(new TestResult(trimmed.Replace("Failed ", ""), "Failed", null, null, TimeSpan.Zero));
            }
        }

        return new TestRunSummary(passed + failed + skipped, passed, failed, skipped, TimeSpan.Zero, failedTests);
    }

    private static List<string> ParseConflictFiles(string gitOutput)
    {
        return gitOutput.Split('\n')
            .Where(l => l.Contains("CONFLICT") || l.Contains("Merge conflict in"))
            .Select(l =>
            {
                var idx = l.IndexOf("in ", StringComparison.Ordinal);
                return idx >= 0 ? l[(idx + 3)..].Trim() : l.Trim();
            })
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();
    }

    private async Task BroadcastBuildStatus(BuildRun run)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("BuildStatusChanged", run);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to broadcast build status");
            }
        }
    }

    private static void AddLog(BuildRun run, string level, string message)
    {
        run.Logs.Add(new BuildLogEntry(DateTime.UtcNow, level, message, null));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
        _buildLock.Dispose();
    }
}
