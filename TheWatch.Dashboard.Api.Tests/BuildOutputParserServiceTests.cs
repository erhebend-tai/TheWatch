// BuildOutputParserServiceTests — tests for MSBuild/dotnet CLI output parsing.
//
// BuildOutputParserService is a static utility that parses raw build output into
// structured BuildDiagnostic entries. Tests cover three regex patterns:
//   1. MsBuildDiagnosticPattern — file-level errors/warnings with line/col info
//   2. MsBuildGlobalPattern — MSBUILD/CSC global errors
//   3. NuGetPattern — NU-prefixed restore errors/warnings
//
// Also tests DetermineResult() which maps (exitCode, diagnostics) -> BuildResult.
//
// Example — running parser tests:
//   dotnet test --filter "FullyQualifiedName~BuildOutputParserServiceTests"

namespace TheWatch.Dashboard.Api.Tests;

public class BuildOutputParserServiceTests
{
    // ────────────────────────────────────────────────────────────
    // ParseOutput — MSBuild file-level diagnostics
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Standard MSBuild error line: file(line,col): error CODE: message [project]
    /// </summary>
    [Fact]
    public void ParseOutput_MsBuildError_ParsesCorrectly()
    {
        var output = "Controllers/EvidenceController.cs(42,13): error CS1002: ; expected [TheWatch.Dashboard.Api.csproj]";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        var d = diagnostics[0];
        Assert.Equal(BuildOutputSeverity.Error, d.Severity);
        Assert.Equal("CS1002", d.Code);
        Assert.Equal("; expected", d.Message);
        Assert.Equal("Controllers/EvidenceController.cs", d.FilePath);
        Assert.Equal(42, d.Line);
        Assert.Equal(13, d.Column);
        Assert.Equal("TheWatch.Dashboard.Api", d.ProjectName);
    }

    /// <summary>
    /// Standard MSBuild warning line: file(line,col): warning CODE: message [project]
    /// </summary>
    [Fact]
    public void ParseOutput_MsBuildWarning_ParsesCorrectly()
    {
        var output = "Services/SimulationService.cs(10,5): warning CS8618: Non-nullable property 'Name' must contain a non-null value [TheWatch.Shared.csproj]";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        var d = diagnostics[0];
        Assert.Equal(BuildOutputSeverity.Warning, d.Severity);
        Assert.Equal("CS8618", d.Code);
        Assert.Contains("Non-nullable property", d.Message);
        Assert.Equal("Services/SimulationService.cs", d.FilePath);
        Assert.Equal(10, d.Line);
        Assert.Equal(5, d.Column);
        Assert.Equal("TheWatch.Shared", d.ProjectName);
    }

    /// <summary>
    /// Multiple diagnostics in a single output block should all be parsed.
    /// </summary>
    [Fact]
    public void ParseOutput_MultipleDiagnostics_ParsesAll()
    {
        var output = """
            Controllers/ResponseController.cs(15,1): error CS0246: The type or namespace name 'Foo' could not be found [TheWatch.Dashboard.Api.csproj]
            Controllers/ResponseController.cs(20,10): warning CS0168: The variable 'x' is declared but never used [TheWatch.Dashboard.Api.csproj]
            Services/BuildService.cs(5,1): error CS1061: 'string' does not contain a definition for 'DoStuff' [TheWatch.Dashboard.Api.csproj]
            """;

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Equal(3, diagnostics.Count);
        Assert.Equal(2, diagnostics.Count(d => d.Severity == BuildOutputSeverity.Error));
        Assert.Single(diagnostics, d => d.Severity == BuildOutputSeverity.Warning);
    }

    // ────────────────────────────────────────────────────────────
    // ParseOutput — MSBuild global diagnostics
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// MSBUILD global error: MSBUILD : error MSB1009: Project file does not exist.
    /// </summary>
    [Fact]
    public void ParseOutput_MsBuildGlobalError_ParsesCorrectly()
    {
        var output = "MSBUILD : error MSB1009: Project file does not exist.";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        var d = diagnostics[0];
        Assert.Equal(BuildOutputSeverity.Error, d.Severity);
        Assert.Equal("MSB1009", d.Code);
        Assert.Contains("Project file does not exist", d.Message);
        Assert.Null(d.FilePath);
        Assert.Null(d.Line);
    }

    /// <summary>
    /// CSC global error: CSC : error CS5001: Program does not contain a static 'Main' method
    /// </summary>
    [Fact]
    public void ParseOutput_CscGlobalError_ParsesCorrectly()
    {
        var output = "CSC : error CS5001: Program does not contain a static 'Main' method suitable for an entry point";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        Assert.Equal("CS5001", diagnostics[0].Code);
        Assert.Equal(BuildOutputSeverity.Error, diagnostics[0].Severity);
    }

    // ────────────────────────────────────────────────────────────
    // ParseOutput — NuGet diagnostics
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// NuGet vulnerability warning: warning NU1903: Package 'X' has a known vulnerability
    /// </summary>
    [Fact]
    public void ParseOutput_NuGetWarning_ParsesCorrectly()
    {
        var output = "warning NU1903: Package 'System.Text.Json' 6.0.0 has a known high severity vulnerability [TheWatch.Dashboard.Api.csproj]";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        var d = diagnostics[0];
        Assert.Equal(BuildOutputSeverity.Warning, d.Severity);
        Assert.Equal("NU1903", d.Code);
        Assert.Contains("known high severity vulnerability", d.Message);
        Assert.Equal("TheWatch.Dashboard.Api", d.ProjectName);
    }

    /// <summary>
    /// NuGet error: error NU1101: Unable to find package 'Foo'
    /// </summary>
    [Fact]
    public void ParseOutput_NuGetError_ParsesCorrectly()
    {
        var output = "error NU1101: Unable to find package 'Foo.Bar'. No packages exist with this id in source(s): nuget.org [TheWatch.Shared.csproj]";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        Assert.Equal(BuildOutputSeverity.Error, diagnostics[0].Severity);
        Assert.Equal("NU1101", diagnostics[0].Code);
        Assert.Equal("TheWatch.Shared", diagnostics[0].ProjectName);
    }

    /// <summary>
    /// NuGet duplicates with MSBuild should be deduplicated. If the same code+message
    /// appears in both MSBuild and NuGet patterns, only one entry should be produced.
    /// </summary>
    [Fact]
    public void ParseOutput_NuGetDuplicate_IsDeduped()
    {
        // This line matches NuGet pattern but not MSBuild file-level pattern
        var output = "warning NU1903: Package 'Foo' 1.0.0 has a known vulnerability";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        // Should produce exactly 1 entry, not 2
        Assert.Single(diagnostics);
    }

    // ────────────────────────────────────────────────────────────
    // ParseOutput — edge cases
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Empty or null output should return empty list, not throw.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOutput_EmptyInput_ReturnsEmptyList(string? output)
    {
        var diagnostics = BuildOutputParserService.ParseOutput(output!);

        Assert.NotNull(diagnostics);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Non-diagnostic output lines should be ignored.
    /// </summary>
    [Fact]
    public void ParseOutput_NonDiagnosticLines_ReturnsEmpty()
    {
        var output = """
            Microsoft (R) Build Engine version 17.12.0+deadbeef
            Copyright (C) Microsoft Corporation. All rights reserved.

              Determining projects to restore...
              All projects are up-to-date for restore.
              TheWatch.Shared -> C:\Users\erheb\source\repos\TheWatch\TheWatch.Shared\bin\Debug\net10.0\TheWatch.Shared.dll

            Build succeeded.
                0 Warning(s)
                0 Error(s)
            """;

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// MSBuild error without project suffix should still parse (file/line/col/code/message).
    /// </summary>
    [Fact]
    public void ParseOutput_MsBuildErrorWithoutProject_ParsesCorrectly()
    {
        var output = "Program.cs(1,1): error CS0103: The name 'xyz' does not exist in the current context";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        Assert.Single(diagnostics);
        Assert.Equal("CS0103", diagnostics[0].Code);
        Assert.Null(diagnostics[0].ProjectName);
    }

    // ────────────────────────────────────────────────────────────
    // DetermineResult
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Exit code 0 with no errors => Success.
    /// </summary>
    [Fact]
    public void DetermineResult_ExitZeroNoErrors_ReturnsSuccess()
    {
        var result = BuildOutputParserService.DetermineResult(0, new List<BuildDiagnostic>());
        Assert.Equal(BuildResult.Success, result);
    }

    /// <summary>
    /// Exit code 0 with warnings only => Success (warnings don't fail the build).
    /// </summary>
    [Fact]
    public void DetermineResult_ExitZeroWarningsOnly_ReturnsSuccess()
    {
        var diags = new List<BuildDiagnostic>
        {
            new() { Severity = BuildOutputSeverity.Warning, Code = "CS0168", Message = "unused variable" }
        };

        var result = BuildOutputParserService.DetermineResult(0, diags);
        Assert.Equal(BuildResult.Success, result);
    }

    /// <summary>
    /// Exit code 1 with errors => Failure.
    /// </summary>
    [Fact]
    public void DetermineResult_ExitOneWithErrors_ReturnsFailure()
    {
        var diags = new List<BuildDiagnostic>
        {
            new() { Severity = BuildOutputSeverity.Error, Code = "CS1002", Message = "; expected" }
        };

        var result = BuildOutputParserService.DetermineResult(1, diags);
        Assert.Equal(BuildResult.Failure, result);
    }

    /// <summary>
    /// Exit code 0 but diagnostics contain an error => Failure.
    /// (This can happen if exit code is misreported but MSBuild emitted error lines.)
    /// </summary>
    [Fact]
    public void DetermineResult_ExitZeroWithErrors_ReturnsFailure()
    {
        var diags = new List<BuildDiagnostic>
        {
            new() { Severity = BuildOutputSeverity.Error, Code = "CS0246", Message = "type not found" }
        };

        var result = BuildOutputParserService.DetermineResult(0, diags);
        Assert.Equal(BuildResult.Failure, result);
    }

    /// <summary>
    /// Non-zero exit code with no diagnostics => Failure.
    /// (Build crashed before producing parseable output.)
    /// </summary>
    [Fact]
    public void DetermineResult_NonZeroNoDiagnostics_ReturnsFailure()
    {
        var result = BuildOutputParserService.DetermineResult(1, new List<BuildDiagnostic>());
        Assert.Equal(BuildResult.Failure, result);
    }

    // ────────────────────────────────────────────────────────────
    // Mixed output — full integration parse
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Realistic build output with mixed error types should parse all diagnostics correctly.
    /// </summary>
    [Fact]
    public void ParseOutput_MixedOutput_ParsesAllTypes()
    {
        var output =
            "Microsoft (R) Build Engine version 17.12.0+deadbeef\n" +
            "\n" +
            "  Restoring packages for TheWatch.Dashboard.Api.csproj...\n" +
            "warning NU1903: Package 'System.Text.Json' 6.0.0 has a known high severity vulnerability [TheWatch.Dashboard.Api.csproj]\n" +
            "  Determining projects to restore...\n" +
            "Controllers/ResponseController.cs(42,13): error CS1002: ; expected [TheWatch.Dashboard.Api.csproj]\n" +
            "Services/BuildService.cs(10,5): warning CS8618: Non-nullable field must contain a non-null value [TheWatch.Dashboard.Api.csproj]\n" +
            "MSBUILD : error MSB4025: The project file could not be loaded.\n" +
            "\n" +
            "Build FAILED.\n" +
            "\n" +
            "    2 Error(s)\n" +
            "    2 Warning(s)";

        var diagnostics = BuildOutputParserService.ParseOutput(output);

        // Expect: 1 NuGet warning + 1 CS error + 1 CS warning + 1 MSBuild global error = 4
        Assert.Equal(4, diagnostics.Count);
        Assert.Equal(2, diagnostics.Count(d => d.Severity == BuildOutputSeverity.Error));
        Assert.Equal(2, diagnostics.Count(d => d.Severity == BuildOutputSeverity.Warning));

        // Verify the MSBuild file-level error has full metadata
        var csError = diagnostics.First(d => d.Code == "CS1002");
        Assert.Equal("Controllers/ResponseController.cs", csError.FilePath);
        Assert.Equal(42, csError.Line);
        Assert.Equal(13, csError.Column);

        // Verify the NuGet warning
        var nuget = diagnostics.First(d => d.Code == "NU1903");
        Assert.Equal(BuildOutputSeverity.Warning, nuget.Severity);
        Assert.Equal("TheWatch.Dashboard.Api", nuget.ProjectName);

        // Verify the MSBuild global error
        var msbuild = diagnostics.First(d => d.Code == "MSB4025");
        Assert.Equal(BuildOutputSeverity.Error, msbuild.Severity);
        Assert.Null(msbuild.FilePath);
    }
}
