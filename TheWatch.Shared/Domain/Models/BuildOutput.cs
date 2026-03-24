// BuildOutput — a complete record of a single build execution.
// Captures everything: stdout, stderr, parsed errors/warnings, exit code,
// affected project, artifacts produced, and the environment it ran in.
//
// Serilog-enriched build processes write these records through IBuildOutputPort.
// The Feature Tracker DataGrid links BuildOutputs to FeatureImplementations via ProjectName.
//
// Example:
//   new BuildOutput
//   {
//       Id = "build-20260324-001",
//       ProjectName = "TheWatch.Dashboard.Api",
//       Configuration = "Debug",
//       TargetFramework = "net10.0",
//       Command = "dotnet build TheWatch.Dashboard.Api.csproj",
//       ExitCode = 1,
//       Succeeded = false,
//       Stdout = "Microsoft (R) Build Engine version 17.12...\n...",
//       Stderr = "error CS1002: ; expected\n...",
//       Errors = new() { new BuildDiagnostic { ... } },
//       WarningCount = 3,
//       ErrorCount = 1
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class BuildOutput
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // ── Build identity ─────────────────────────────
    /// <summary>Solution or project name (e.g., "TheWatch.Dashboard.Api").</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Build configuration (Debug, Release, etc.).</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Target framework (net10.0, net9.0, etc.).</summary>
    public string? TargetFramework { get; set; }

    /// <summary>The exact command that was executed.</summary>
    public string? Command { get; set; }

    /// <summary>What triggered this build (Manual, CI, Webhook, Hangfire, ClaudeCode).</summary>
    public string? TriggerSource { get; set; }

    /// <summary>Git branch at build time.</summary>
    public string? Branch { get; set; }

    /// <summary>Git commit SHA at build time.</summary>
    public string? CommitSha { get; set; }

    // ── Result ─────────────────────────────────────
    public int ExitCode { get; set; }
    public bool Succeeded { get; set; }

    /// <summary>Overall build result status.</summary>
    public BuildResult Result { get; set; }

    // ── Output streams ─────────────────────────────
    /// <summary>Full stdout text from the build process.</summary>
    public string? Stdout { get; set; }

    /// <summary>Full stderr text from the build process.</summary>
    public string? Stderr { get; set; }

    // ── Parsed diagnostics ─────────────────────────
    /// <summary>Individual errors and warnings parsed from build output.</summary>
    public List<BuildDiagnostic> Diagnostics { get; set; } = new();

    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    // ── Artifacts ──────────────────────────────────
    /// <summary>Metadata about produced artifacts (DLLs, NuGet packages, etc.).</summary>
    public List<BuildArtifact> Artifacts { get; set; } = new();

    // ── Timing ─────────────────────────────────────
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long DurationMs { get; set; }

    // ── Environment ────────────────────────────────
    public string? MachineName { get; set; }
    public string? OsVersion { get; set; }
    public string? DotNetVersion { get; set; }

    /// <summary>Which store this record is persisted to.</summary>
    public BuildOutputStore Store { get; set; } = BuildOutputStore.Sqlite;
}

/// <summary>
/// A single diagnostic (error or warning) parsed from MSBuild output.
/// Pattern: {file}({line},{col}): {severity} {code}: {message}
/// </summary>
public class BuildDiagnostic
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public BuildOutputSeverity Severity { get; set; }

    /// <summary>Compiler/analyzer error code (e.g., "CS1002", "CA1822", "NU1903").</summary>
    public string? Code { get; set; }

    /// <summary>Human-readable error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Source file path (relative to project).</summary>
    public string? FilePath { get; set; }

    /// <summary>Line number in the source file.</summary>
    public int? Line { get; set; }

    /// <summary>Column number in the source file.</summary>
    public int? Column { get; set; }

    /// <summary>Which project produced this diagnostic.</summary>
    public string? ProjectName { get; set; }
}

/// <summary>
/// Metadata about a build artifact (DLL, NuGet package, APK, IPA, etc.).
/// Not the binary itself — just the metadata for tracking.
/// </summary>
public class BuildArtifact
{
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Type of artifact: "dll", "nupkg", "apk", "ipa", "exe", "zip".</summary>
    public string? ArtifactType { get; set; }

    /// <summary>SHA-256 hash of the artifact for integrity verification.</summary>
    public string? ContentHash { get; set; }
}
