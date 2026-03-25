// BuildOutputParserService — parses MSBuild/dotnet CLI stdout/stderr into structured
// BuildDiagnostic entries. Detects error/warning patterns from dotnet build, dotnet test,
// dotnet publish, and NuGet restore output.
//
// MSBuild output patterns:
//   {file}({line},{col}): error {code}: {message} [{project}]
//   {file}({line},{col}): warning {code}: {message} [{project}]
//   MSBUILD : error {code}: {message}
//
// Example:
//   var diagnostics = BuildOutputParserService.ParseOutput(stdout + "\n" + stderr);

using System.Text.RegularExpressions;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

public static class BuildOutputParserService
{
    // Pattern: Controllers/EvidenceController.cs(42,13): error CS1002: ; expected [TheWatch.Dashboard.Api.csproj]
    private static readonly Regex MsBuildDiagnosticPattern = new(
        @"^(?<file>[^(\r\n]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<severity>error|warning)\s+(?<code>\w+):\s+(?<message>.+?)(?:\s+\[(?<project>[^\]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Pattern: MSBUILD : error MSB1009: Project file does not exist.
    private static readonly Regex MsBuildGlobalPattern = new(
        @"^(?:MSBUILD|CSC)\s*:\s+(?<severity>error|warning)\s+(?<code>\w+):\s+(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Pattern: error NU1903: Package 'Foo' 1.0.0 has a known high severity vulnerability
    private static readonly Regex NuGetPattern = new(
        @"^(?<severity>error|warning)\s+(?<code>NU\d+):\s+(?<message>.+?)(?:\s+\[(?<project>[^\]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Parse all diagnostic entries from combined build output.
    /// Returns a list of BuildDiagnostic with file, line, column, code, and message.
    /// </summary>
    public static List<BuildDiagnostic> ParseOutput(string output)
    {
        var diagnostics = new List<BuildDiagnostic>();
        if (string.IsNullOrEmpty(output)) return diagnostics;

        // MSBuild file-level diagnostics
        foreach (Match match in MsBuildDiagnosticPattern.Matches(output))
        {
            diagnostics.Add(new BuildDiagnostic
            {
                Severity = match.Groups["severity"].Value == "error" ? BuildOutputSeverity.Error : BuildOutputSeverity.Warning,
                Code = match.Groups["code"].Value,
                Message = match.Groups["message"].Value.Trim(),
                FilePath = match.Groups["file"].Value.Trim(),
                Line = int.TryParse(match.Groups["line"].Value, out var l) ? l : null,
                Column = int.TryParse(match.Groups["col"].Value, out var c) ? c : null,
                ProjectName = match.Groups["project"].Success ? System.IO.Path.GetFileNameWithoutExtension(match.Groups["project"].Value) : null
            });
        }

        // MSBuild global diagnostics
        foreach (Match match in MsBuildGlobalPattern.Matches(output))
        {
            diagnostics.Add(new BuildDiagnostic
            {
                Severity = match.Groups["severity"].Value == "error" ? BuildOutputSeverity.Error : BuildOutputSeverity.Warning,
                Code = match.Groups["code"].Value,
                Message = match.Groups["message"].Value.Trim()
            });
        }

        // NuGet diagnostics
        foreach (Match match in NuGetPattern.Matches(output))
        {
            // Avoid duplicates with MSBuild pattern
            var code = match.Groups["code"].Value;
            if (diagnostics.Any(d => d.Code == code && d.Message == match.Groups["message"].Value.Trim()))
                continue;

            diagnostics.Add(new BuildDiagnostic
            {
                Severity = match.Groups["severity"].Value == "error" ? BuildOutputSeverity.Error : BuildOutputSeverity.Warning,
                Code = code,
                Message = match.Groups["message"].Value.Trim(),
                ProjectName = match.Groups["project"].Success ? System.IO.Path.GetFileNameWithoutExtension(match.Groups["project"].Value) : null
            });
        }

        return diagnostics;
    }

    /// <summary>
    /// Determine overall build result from exit code and parsed diagnostics.
    /// </summary>
    public static BuildResult DetermineResult(int exitCode, List<BuildDiagnostic> diagnostics)
    {
        if (exitCode == 0 && !diagnostics.Any(d => d.Severity >= BuildOutputSeverity.Error))
            return BuildResult.Success;
        if (diagnostics.Any(d => d.Severity >= BuildOutputSeverity.Error))
            return BuildResult.Failure;
        return exitCode == 0 ? BuildResult.Success : BuildResult.Failure;
    }
}
