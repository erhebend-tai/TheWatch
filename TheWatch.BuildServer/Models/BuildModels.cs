// =============================================================================
// Build Models — Orchestration data structures
// =============================================================================
// Tracks multi-project builds, agent branch merges, and validation results.
//
// The build orchestrator manages:
//   - Solution-wide builds (dotnet build/test)
//   - Per-agent branch compilation checks
//   - Merge conflict detection
//   - Test suite execution and result aggregation
//
// WAL: BuildRun is immutable once completed. In-flight state is tracked via
//      BuildRunStatus enum. SignalR broadcasts on every state transition.
// =============================================================================

namespace TheWatch.BuildServer.Models;

public enum BuildRunStatus
{
    Queued,
    Restoring,
    Building,
    Testing,
    Indexing,     // LSIF re-index after successful build
    Succeeded,
    Failed,
    Cancelled
}

public enum BuildTrigger
{
    Manual,       // CLI or API triggered
    AgentMerge,   // Triggered by agent branch merge
    FileWatch,    // File system watcher detected changes
    Scheduled,    // Periodic rebuild
    PullRequest   // PR validation build
}

public record BuildRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public BuildRunStatus Status { get; set; } = BuildRunStatus.Queued;
    public BuildTrigger Trigger { get; init; } = BuildTrigger.Manual;
    public string? TriggerSource { get; init; } // branch name, file path, etc.

    // Per-project results
    public List<ProjectBuildResult> ProjectResults { get; set; } = [];

    // Test results (if testing phase reached)
    public TestRunSummary? TestSummary { get; set; }

    // LSIF indexing (if indexing phase reached)
    public LsifIndexSummary? IndexSummary { get; set; }

    // Timing
    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.UtcNow - StartedAt;

    // Logs
    public List<BuildLogEntry> Logs { get; set; } = [];
}

public record ProjectBuildResult(
    string ProjectName,
    string ProjectPath,
    bool Succeeded,
    int WarningCount,
    int ErrorCount,
    TimeSpan Duration,
    List<BuildDiagnostic> Diagnostics);

public record BuildDiagnostic(
    string Severity,    // "Error", "Warning", "Info"
    string Code,        // e.g. "CS0246"
    string Message,
    string FilePath,
    int Line,
    int Column);

public record TestRunSummary(
    int TotalTests,
    int Passed,
    int Failed,
    int Skipped,
    TimeSpan Duration,
    List<TestResult> FailedTests);

public record TestResult(
    string FullName,
    string Outcome,     // "Passed", "Failed", "Skipped"
    string? ErrorMessage,
    string? StackTrace,
    TimeSpan Duration);

public record LsifIndexSummary(
    int DocumentsIndexed,
    int SymbolsIndexed,
    int ReferencesFound,
    int PortAdapterLinksFound,
    TimeSpan Duration);

public record BuildLogEntry(
    DateTime Timestamp,
    string Level,       // "Info", "Warning", "Error"
    string Message,
    string? ProjectName);

// ── Agent Branch Tracking ────────────────────────────────────────────────────

public enum AgentBranchStatus
{
    Running,
    Completed,
    MergePending,
    MergeConflict,
    Merged,
    Failed
}

public record AgentBranch
{
    public string AgentName { get; init; } = "";
    public string BranchName { get; init; } = "";
    public string Scope { get; init; } = "";
    public AgentBranchStatus Status { get; set; } = AgentBranchStatus.Running;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<string> FilesModified { get; set; } = [];
    public List<string> ConflictFiles { get; set; } = []; // populated on merge attempt
    public BuildRun? ValidationBuild { get; set; }
}

// ── Merge Plan ───────────────────────────────────────────────────────────────

public record MergePlan
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<MergeStep> Steps { get; set; } = [];
    public string? BaseBranch { get; init; } = "main";
}

public record MergeStep(
    int Order,
    string BranchName,
    string AgentName,
    List<string> DependsOn,     // branch names that must merge first
    List<string> HighRiskFiles, // files likely to conflict
    string? Notes);
