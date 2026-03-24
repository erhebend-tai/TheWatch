// =============================================================================
// DocGenOptions.cs — Configuration POCO for the documentation generator.
// =============================================================================
// Bound from appsettings.json section "DocGen".
//
// Example (appsettings.json):
//   {
//     "DocGen": {
//       "SolutionRoot": "C:\\src\\TheWatch-Aspire",
//       "WatchEnabled": true,
//       "DebounceMs": 500,
//       "GenerateOnStartup": true,
//       "IncludePrivateMembers": false,
//       "IncludeInternalMembers": true,
//       "StubMarker": "[AUTO-DOC]",
//       "OutputReportPath": "./docs/coverage-report.xml",
//       "ExcludedPaths": [ "obj", "bin", "Migrations" ],
//       "ExcludedFiles": [ "GlobalUsings.cs", "AssemblyInfo.cs" ]
//     }
//   }
//
// WAL: Configuration changes are logged at startup and on reload.
// =============================================================================

namespace TheWatch.DocGen.Configuration;

/// <summary>
/// Configuration options for the XML documentation generator worker service.
/// </summary>
public class DocGenOptions
{
    public const string SectionName = "DocGen";

    /// <summary>
    /// Root directory of the solution to scan. All .cs files under this path are monitored.
    /// Defaults to the current working directory's parent (assumes running from TheWatch.DocGen/).
    /// </summary>
    public string SolutionRoot { get; set; } = string.Empty;

    /// <summary>
    /// When true, a FileSystemWatcher monitors for .cs file changes and triggers regeneration.
    /// When false, documentation is generated once on startup and the worker exits.
    /// </summary>
    public bool WatchEnabled { get; set; } = true;

    /// <summary>
    /// Debounce interval in milliseconds. Multiple rapid saves to the same file
    /// are coalesced into a single generation pass after this delay.
    /// </summary>
    public int DebounceMs { get; set; } = 500;

    /// <summary>
    /// When true, a full documentation scan runs at startup before the watcher begins.
    /// Useful for catching undocumented members that were added while the worker was stopped.
    /// </summary>
    public bool GenerateOnStartup { get; set; } = true;

    /// <summary>
    /// When true, private members also receive doc stubs.
    /// Default false — only public and protected members are documented.
    /// </summary>
    public bool IncludePrivateMembers { get; set; }

    /// <summary>
    /// When true, internal members also receive doc stubs.
    /// Default true — internal APIs are part of the project's documented surface.
    /// </summary>
    public bool IncludeInternalMembers { get; set; } = true;

    /// <summary>
    /// Marker text inserted into auto-generated summaries so hand-written docs
    /// can be distinguished from stubs. Stubs containing this marker are eligible
    /// for regeneration; docs without it are left untouched.
    /// </summary>
    public string StubMarker { get; set; } = "[AUTO-DOC]";

    /// <summary>
    /// Path to write the consolidated XML documentation coverage report.
    /// Empty string disables report generation.
    /// </summary>
    public string OutputReportPath { get; set; } = string.Empty;

    /// <summary>
    /// Directory names to exclude from scanning (matched against path segments).
    /// </summary>
    public List<string> ExcludedPaths { get; set; } =
    [
        "obj", "bin", "Migrations", ".vs", "node_modules", "TestResults"
    ];

    /// <summary>
    /// File names to exclude from scanning.
    /// </summary>
    public List<string> ExcludedFiles { get; set; } =
    [
        "GlobalUsings.cs", "AssemblyInfo.cs"
    ];

    /// <summary>
    /// Maximum number of files to process concurrently during a full scan.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;
}
