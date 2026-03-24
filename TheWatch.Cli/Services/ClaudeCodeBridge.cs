// =============================================================================
// ClaudeCodeBridge — Programmatic interface to Claude Code CLI.
// =============================================================================
// Wraps the `claude` CLI binary for programmatic invocation from the dashboard.
// Supports:
//   1. One-shot prompts:  claude "explain this file" --output json
//   2. Interactive mode:  claude (REPL session in embedded terminal)
//   3. Print mode:        claude -p "generate code for X"
//   4. Subagent spawning: Launch multiple Claude Code instances in parallel
//
// This bridge does NOT replace the EmbeddedTerminal — it provides a higher-level
// API for automated/scripted Claude Code interactions. The terminal pane handles
// the interactive REPL experience.
//
// Architecture:
//   ClaudeCodeBridge
//     ├── RunPromptAsync()     — one-shot, returns stdout
//     ├── RunPrintAsync()      — print mode (-p), returns generated text
//     ├── SpawnSubagentAsync() — launches a background Claude Code process
//     └── GetVersionAsync()    — claude --version
//
// Example:
//   var bridge = new ClaudeCodeBridge();
//   var result = await bridge.RunPrintAsync("Generate a C# class for User model");
//   var version = await bridge.GetVersionAsync();
//
// WAL: Claude Code CLI must be installed and in PATH.
//      On Windows: npm install -g @anthropic-ai/claude-code
//      ANTHROPIC_API_KEY must be set in environment.
// =============================================================================

using System.Diagnostics;
using System.Text;

namespace TheWatch.Cli.Services;

public class ClaudeCodeBridge
{
    private readonly string _claudeBinary;
    private readonly string _workingDirectory;
    private readonly List<SubagentProcess> _activeSubagents = new();
    private readonly object _lock = new();

    public ClaudeCodeBridge(string? workingDirectory = null)
    {
        _workingDirectory = workingDirectory ?? Environment.CurrentDirectory;

        // Find claude binary — check common install locations
        _claudeBinary = FindClaudeBinary();
    }

    /// <summary>
    /// Run a one-shot prompt and capture the full response.
    /// Equivalent to: claude "your prompt here"
    /// </summary>
    public async Task<ClaudeCodeResult> RunPromptAsync(string prompt, CancellationToken ct = default)
    {
        return await RunClaudeAsync(new[] { prompt }, ct);
    }

    /// <summary>
    /// Run in print mode — no interactive REPL, just output.
    /// Equivalent to: claude -p "your prompt"
    /// </summary>
    public async Task<ClaudeCodeResult> RunPrintAsync(string prompt, CancellationToken ct = default)
    {
        return await RunClaudeAsync(new[] { "-p", prompt }, ct);
    }

    /// <summary>
    /// Run with JSON output for structured parsing.
    /// Equivalent to: claude -p "prompt" --output json
    /// </summary>
    public async Task<ClaudeCodeResult> RunJsonAsync(string prompt, CancellationToken ct = default)
    {
        return await RunClaudeAsync(new[] { "-p", prompt, "--output", "json" }, ct);
    }

    /// <summary>
    /// Spawn a subagent — a background Claude Code process working on a specific task.
    /// Returns a handle to track progress and capture output.
    /// </summary>
    public SubagentProcess SpawnSubagent(string taskDescription, string? branchName = null)
    {
        var args = new List<string> { "-p", taskDescription };

        var psi = new ProcessStartInfo
        {
            FileName = _claudeBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _workingDirectory
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var subagent = new SubagentProcess
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            TaskDescription = taskDescription,
            BranchName = branchName,
            Process = process,
            StartedAt = DateTime.UtcNow,
            OutputBuilder = new StringBuilder()
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                subagent.OutputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                subagent.ErrorBuilder.AppendLine(e.Data);
        };

        process.Exited += (_, _) =>
        {
            subagent.CompletedAt = DateTime.UtcNow;
            subagent.ExitCode = process.ExitCode;
            lock (_lock) { _activeSubagents.Remove(subagent); }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_lock) { _activeSubagents.Add(subagent); }

        return subagent;
    }

    /// <summary>Get Claude Code version string.</summary>
    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var result = await RunClaudeAsync(new[] { "--version" }, ct);
        return result.Success ? result.Output.Trim() : "Claude Code not found";
    }

    /// <summary>Get all currently running subagents.</summary>
    public IReadOnlyList<SubagentProcess> GetActiveSubagents()
    {
        lock (_lock) { return _activeSubagents.ToList(); }
    }

    // ── Internal ────────────────────────────────────────────────────

    private async Task<ClaudeCodeResult> RunClaudeAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _claudeBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            return new ClaudeCodeResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = stdout,
                Error = stderr
            };
        }
        catch (Exception ex)
        {
            return new ClaudeCodeResult
            {
                Success = false,
                ExitCode = -1,
                Output = "",
                Error = ex.Message
            };
        }
    }

    private static string FindClaudeBinary()
    {
        // Check if 'claude' is directly available in PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        foreach (var dir in pathDirs)
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { "claude.cmd", "claude.exe", "claude.ps1" }
                : new[] { "claude" };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(dir, candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        // Common npm global install locations
        var npmGlobalPaths = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "claude.cmd"),
            }
            : new[]
            {
                "/usr/local/bin/claude",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global", "bin", "claude"),
            };

        foreach (var path in npmGlobalPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Fall back to bare name — let the OS resolve it
        return "claude";
    }
}

/// <summary>Result of a one-shot Claude Code invocation.</summary>
public class ClaudeCodeResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
}

/// <summary>Handle to a running Claude Code subagent process.</summary>
public class SubagentProcess
{
    public string Id { get; init; } = "";
    public string TaskDescription { get; init; } = "";
    public string? BranchName { get; init; }
    public Process Process { get; init; } = null!;
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public int? ExitCode { get; set; }
    public StringBuilder OutputBuilder { get; init; } = new();
    public StringBuilder ErrorBuilder { get; init; } = new();

    public bool IsRunning => !CompletedAt.HasValue;
    public string Output => OutputBuilder.ToString();
    public string Error => ErrorBuilder.ToString();
    public TimeSpan Elapsed => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
}
