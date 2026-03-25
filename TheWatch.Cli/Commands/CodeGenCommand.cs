// =============================================================================
// CodeGenCommand — Full Cecil + Roslyn + CodeGen pipeline CLI command
// =============================================================================
// Chains assembly scanning, gap detection, mock generation, DI wiring, and
// test stub creation into a single CLI pipeline. This is the "build intelligence"
// layer — it understands the solution's port-adapter architecture and can
// automatically maintain the wiring between them.
//
// Subcommands:
//   thewatch codegen scan             — Cecil scan: show all ports, implementations, and gaps
//   thewatch codegen mock-adapters    — Generate mock adapters for all ports missing implementations
//   thewatch codegen di-wire          — Generate/update ServiceCollectionExtensions.cs from Cecil scan
//   thewatch codegen test-stubs       — Generate xUnit test stubs for all adapter classes
//   thewatch codegen full             — Run all of the above in sequence (build→scan→gen→build→verify)
//
// Example:
//   dotnet run --project TheWatch.Cli -- codegen scan
//   dotnet run --project TheWatch.Cli -- codegen scan --bin-dir ./TheWatch.Shared/bin/Debug/net10.0
//   dotnet run --project TheWatch.Cli -- codegen mock-adapters
//   dotnet run --project TheWatch.Cli -- codegen di-wire --namespace TheWatch.Adapters.Mock
//   dotnet run --project TheWatch.Cli -- codegen test-stubs
//   dotnet run --project TheWatch.Cli -- codegen full
//   dotnet run --project TheWatch.Cli -- codegen full --skip-tests
//
// Pipeline (codegen full):
//   1. dotnet build TheWatch.slnx
//   2. Cecil scan all TheWatch.*.dll in bin/Debug/net10.0
//   3. Print port/implementation/gap summary
//   4. For each gap, CodeGenerator.GenerateAdapterAsync() creates a mock adapter
//   5. dotnet build (recompile with new mocks)
//   6. Cecil re-scan to get constructor params from compiled mocks
//   7. Generate ServiceCollectionExtensions.cs with all AddSingleton registrations
//   8. Generate xUnit test stubs for all adapter classes
//   9. Final dotnet build to verify everything compiles cleanly
//
// WAL: The pipeline is idempotent — running it twice produces the same result.
//      Existing generated files are overwritten. Non-generated files are never touched.
//      Each step prints progress and errors to stdout/stderr for CI integration.
//
// System.CommandLine 2.0.0-beta5 API notes:
//   - Argument<T>(name) — single-arg ctor; set Description and DefaultValueFactory as properties
//   - Option<T>(name) — single-arg ctor; set Description, DefaultValueFactory, Required as properties
//   - cmd.SetAction(ParseResult => ...) replaces cmd.SetHandler(...)
//   - parseResult.GetValue<T>(option/argument) replaces InvocationContext binding
// =============================================================================

using System.CommandLine;
using System.Diagnostics;
using TheWatch.Cli.Services.Cecil;
using TheWatch.Cli.Services.Roslyn;

namespace TheWatch.Cli.Commands;

public static class CodeGenCommand
{
    /// <summary>
    /// Build the `codegen` command tree. Call from Program.cs:
    ///   rootCommand.Add(CodeGenCommand.Build());
    /// </summary>
    public static Command Build()
    {
        var codegenCmd = new Command("codegen",
            "Cecil + Roslyn code generation pipeline — scan assemblies, generate mocks, wire DI, create tests");

        // Shared options across subcommands
        var binDirOption = new Option<string?>("--bin-dir")
        {
            Description = "Override bin directory to scan (default: auto-discover from solution build output)",
            DefaultValueFactory = _ => null
        };

        var namespaceOption = new Option<string>("--namespace")
        {
            Description = "Target namespace for generated ServiceCollectionExtensions (default: TheWatch.Adapters.Mock)",
            DefaultValueFactory = _ => "TheWatch.Adapters.Mock"
        };

        var skipTestsOption = new Option<bool>("--skip-tests")
        {
            Description = "Skip test stub generation in full pipeline",
            DefaultValueFactory = _ => false
        };

        codegenCmd.Subcommands.Add(BuildScanCommand(binDirOption));
        codegenCmd.Subcommands.Add(BuildMockAdaptersCommand(binDirOption));
        codegenCmd.Subcommands.Add(BuildDiWireCommand(binDirOption, namespaceOption));
        codegenCmd.Subcommands.Add(BuildTestStubsCommand(binDirOption));
        codegenCmd.Subcommands.Add(BuildFullCommand(binDirOption, namespaceOption, skipTestsOption));

        return codegenCmd;
    }

    // ── codegen scan ─────────────────────────────────────────────────

    private static Command BuildScanCommand(Option<string?> binDirOption)
    {
        var cmd = new Command("scan", "Cecil scan: discover all ports, implementations, and gaps")
        {
            binDirOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var binDir = parseResult.GetValue(binDirOption) ?? DiscoverBinDirectory();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Cecil Assembly Scanner");
            Console.ResetColor();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine($"  Scanning: {binDir}");
            Console.WriteLine();

            var scanner = new CecilAssemblyScanner();
            var result = await scanner.ScanAssembliesAsync(binDir);

            PrintScanResult(result);
        });

        return cmd;
    }

    // ── codegen mock-adapters ────────────────────────────────────────

    private static Command BuildMockAdaptersCommand(Option<string?> binDirOption)
    {
        var cmd = new Command("mock-adapters", "Generate mock adapter classes for ports with no implementation")
        {
            binDirOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var binDir = parseResult.GetValue(binDirOption) ?? DiscoverBinDirectory();
            var solutionRoot = FindSolutionRoot();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Mock Adapter Generator");
            Console.ResetColor();
            Console.WriteLine(new string('=', 80));

            // Scan for gaps
            var scanner = new CecilAssemblyScanner();
            var result = await scanner.ScanAssembliesAsync(binDir);

            if (result.Gaps.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("No gaps found — all ports have implementations.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"Found {result.Gaps.Count} port(s) without implementations. Generating mocks...");
            Console.WriteLine();

            var codeGen = new CodeGenerator(solutionRoot);
            var generated = 0;

            foreach (var gap in result.Gaps)
            {
                Console.Write($"  Generating Mock for {gap.PortInterfaceName}... ");
                var genResult = await codeGen.GenerateAdapterAsync(gap.PortInterfaceName, "Mock");

                if (genResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"OK → {genResult.OutputPath}");
                    Console.ResetColor();
                    generated++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"SKIP — {genResult.ErrorMessage}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Generated {generated}/{result.Gaps.Count} mock adapter(s).");
            Console.ResetColor();
        });

        return cmd;
    }

    // ── codegen di-wire ──────────────────────────────────────────────

    private static Command BuildDiWireCommand(Option<string?> binDirOption, Option<string> namespaceOption)
    {
        var cmd = new Command("di-wire", "Generate ServiceCollectionExtensions.cs with AddSingleton registrations")
        {
            binDirOption, namespaceOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var binDir = parseResult.GetValue(binDirOption) ?? DiscoverBinDirectory();
            var ns = parseResult.GetValue(namespaceOption)!;
            var solutionRoot = FindSolutionRoot();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("DI Wire Generator");
            Console.ResetColor();
            Console.WriteLine(new string('=', 80));

            var scanner = new CecilAssemblyScanner();
            var result = await scanner.ScanAssembliesAsync(binDir);

            if (result.AllImplementations.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No implementations found. Build the solution first, then re-run.");
                Console.ResetColor();
                return;
            }

            // Generate the code
            var code = scanner.GenerateServiceCollectionExtensions(result.AllImplementations, ns);

            // Determine output path from namespace
            // TheWatch.Adapters.Mock → TheWatch.Adapters.Mock/ServiceCollectionExtensions.g.cs
            var projectDir = ns.Replace('.', Path.DirectorySeparatorChar);
            var outputPath = Path.Combine(solutionRoot, projectDir, "ServiceCollectionExtensions.g.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, code);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Generated {outputPath}");
            Console.ResetColor();
            Console.WriteLine($"  Registrations: {result.AllImplementations.Count}");
            Console.WriteLine($"  Namespace:     {ns}");
        });

        return cmd;
    }

    // ── codegen test-stubs ───────────────────────────────────────────

    private static Command BuildTestStubsCommand(Option<string?> binDirOption)
    {
        var cmd = new Command("test-stubs", "Generate xUnit test stubs for all discovered adapter classes")
        {
            binDirOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var binDir = parseResult.GetValue(binDirOption) ?? DiscoverBinDirectory();
            var solutionRoot = FindSolutionRoot();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Test Stub Generator");
            Console.ResetColor();
            Console.WriteLine(new string('=', 80));

            var scanner = new CecilAssemblyScanner();
            var result = await scanner.ScanAssembliesAsync(binDir);

            if (result.AllImplementations.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No implementations found. Build the solution first.");
                Console.ResetColor();
                return;
            }

            var codeGen = new CodeGenerator(solutionRoot);
            var generated = 0;

            foreach (var impl in result.AllImplementations)
            {
                Console.Write($"  Test stub for {impl.ClassName}... ");
                var genResult = await codeGen.GenerateTestStubAsync(impl.ClassName);

                if (genResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"OK → {genResult.OutputPath}");
                    Console.ResetColor();
                    generated++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"SKIP — {genResult.ErrorMessage}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Generated {generated} test stub(s).");
            Console.ResetColor();
        });

        return cmd;
    }

    // ── codegen full ─────────────────────────────────────────────────

    private static Command BuildFullCommand(
        Option<string?> binDirOption,
        Option<string> namespaceOption,
        Option<bool> skipTestsOption)
    {
        var cmd = new Command("full",
            "Full pipeline: build → Cecil scan → generate mocks → rebuild → DI wire → test stubs → final build")
        {
            binDirOption, namespaceOption, skipTestsOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var binDirOverride = parseResult.GetValue(binDirOption);
            var ns = parseResult.GetValue(namespaceOption)!;
            var skipTests = parseResult.GetValue(skipTestsOption);
            var solutionRoot = FindSolutionRoot();
            var slnxPath = Path.Combine(solutionRoot, "TheWatch.slnx");
            if (!File.Exists(slnxPath))
                slnxPath = Path.Combine(solutionRoot, "TheWatch.sln");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("TheWatch CodeGen — Full Pipeline");
            Console.ResetColor();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine($"  Solution root: {solutionRoot}");
            Console.WriteLine($"  Namespace:     {ns}");
            Console.WriteLine($"  Skip tests:    {skipTests}");
            Console.WriteLine();

            var sw = Stopwatch.StartNew();

            // ── Step 1: Initial Build ────────────────────────────────
            PrintStep(1, "Building solution...");
            var buildOk = await RunDotnetBuildAsync(slnxPath);
            if (!buildOk)
            {
                PrintError("Initial build failed. Fix compilation errors and retry.");
                return;
            }

            // ── Step 2: Cecil Scan ───────────────────────────────────
            PrintStep(2, "Cecil scanning compiled assemblies...");
            var binDir = binDirOverride ?? DiscoverBinDirectory();
            var scanner = new CecilAssemblyScanner();
            var scanResult = await scanner.ScanAssembliesAsync(binDir);

            Console.WriteLine($"  Assemblies:      {scanResult.AssembliesScanned}");
            Console.WriteLine($"  Total types:     {scanResult.TotalTypes}");
            Console.WriteLine($"  Ports:           {scanResult.AllPorts.Count}");
            Console.WriteLine($"  Implementations: {scanResult.AllImplementations.Count}");
            Console.WriteLine($"  Gaps:            {scanResult.Gaps.Count}");

            if (scanResult.Errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var err in scanResult.Errors)
                    Console.WriteLine($"  Warning: {err}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // ── Step 3: Generate Mock Adapters for Gaps ──────────────
            if (scanResult.Gaps.Count > 0)
            {
                PrintStep(3, $"Generating mock adapters for {scanResult.Gaps.Count} gap(s)...");
                var codeGen = new CodeGenerator(solutionRoot);
                var generated = 0;

                foreach (var gap in scanResult.Gaps)
                {
                    var genResult = await codeGen.GenerateAdapterAsync(gap.PortInterfaceName, "Mock");
                    if (genResult.Success)
                    {
                        Console.WriteLine($"    + {genResult.GeneratedTypeName} → {genResult.OutputPath}");
                        generated++;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    ~ {gap.PortInterfaceName}: {genResult.ErrorMessage}");
                        Console.ResetColor();
                    }
                }

                Console.WriteLine($"  Generated {generated} mock adapter(s).");
                Console.WriteLine();

                // ── Step 4: Rebuild with new mocks ───────────────────
                if (generated > 0)
                {
                    PrintStep(4, "Rebuilding with new mock adapters...");
                    buildOk = await RunDotnetBuildAsync(slnxPath);
                    if (!buildOk)
                    {
                        PrintError("Rebuild failed after generating mocks. Check generated code.");
                        return;
                    }
                }

                // ── Step 5: Re-scan to pick up constructor params ────
                PrintStep(5, "Re-scanning to resolve constructor parameters...");
                scanResult = await scanner.ScanAssembliesAsync(binDir);
                Console.WriteLine($"  Implementations now: {scanResult.AllImplementations.Count}");
                Console.WriteLine();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  No gaps — all ports have implementations. Skipping mock generation.");
                Console.ResetColor();
                Console.WriteLine();
            }

            // ── Step 6: Generate ServiceCollectionExtensions ─────────
            PrintStep(6, "Generating ServiceCollectionExtensions.cs...");
            if (scanResult.AllImplementations.Count > 0)
            {
                var code = scanner.GenerateServiceCollectionExtensions(scanResult.AllImplementations, ns);
                var projectDir = ns.Replace('.', Path.DirectorySeparatorChar);
                var outputPath = Path.Combine(solutionRoot, projectDir, "ServiceCollectionExtensions.g.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, code);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Written: {outputPath}");
                Console.ResetColor();
                Console.WriteLine($"  Registrations: {scanResult.AllImplementations.Count}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  No implementations to register.");
                Console.ResetColor();
            }
            Console.WriteLine();

            // ── Step 7: Generate Test Stubs ──────────────────────────
            if (!skipTests)
            {
                PrintStep(7, "Generating test stubs...");
                var codeGenForTests = new CodeGenerator(solutionRoot);
                var testCount = 0;

                foreach (var impl in scanResult.AllImplementations)
                {
                    var genResult = await codeGenForTests.GenerateTestStubAsync(impl.ClassName);
                    if (genResult.Success)
                    {
                        Console.WriteLine($"    + {genResult.GeneratedTypeName} → {genResult.OutputPath}");
                        testCount++;
                    }
                }

                Console.WriteLine($"  Generated {testCount} test stub(s).");
                Console.WriteLine();
            }
            else
            {
                PrintStep(7, "Test stub generation SKIPPED (--skip-tests).");
                Console.WriteLine();
            }

            // ── Step 8: Final Build ──────────────────────────────────
            PrintStep(8, "Final verification build...");
            buildOk = await RunDotnetBuildAsync(slnxPath);

            sw.Stop();
            Console.WriteLine();
            Console.WriteLine(new string('=', 80));

            if (buildOk)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PIPELINE COMPLETE — {sw.Elapsed.TotalSeconds:F1}s");
                Console.ResetColor();
                Console.WriteLine($"  Ports discovered:     {scanResult.AllPorts.Count}");
                Console.WriteLine($"  Implementations:      {scanResult.AllImplementations.Count}");
                Console.WriteLine($"  Gaps remaining:       {scanResult.Gaps.Count}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"PIPELINE FAILED at final build — {sw.Elapsed.TotalSeconds:F1}s");
                Console.ResetColor();
                Console.WriteLine("  Review generated code and fix compilation errors.");
            }
        });

        return cmd;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Discover the bin directory by looking for TheWatch.Shared/bin/Debug/net10.0.
    /// Falls back to other common patterns.
    /// </summary>
    private static string DiscoverBinDirectory()
    {
        var solutionRoot = FindSolutionRoot();

        // Primary: TheWatch.Shared/bin/Debug/net10.0
        var candidates = new[]
        {
            Path.Combine(solutionRoot, "TheWatch.Shared", "bin", "Debug", "net10.0"),
            Path.Combine(solutionRoot, "TheWatch.Shared", "bin", "Release", "net10.0"),
            Path.Combine(solutionRoot, "TheWatch.Shared", "bin", "Debug", "net9.0"),
            Path.Combine(solutionRoot, "TheWatch.Shared", "bin", "Release", "net9.0"),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "TheWatch.*.dll").Length > 0)
            {
                return candidate;
            }
        }

        // Fallback: look for any bin directory with TheWatch.*.dll
        var binDirs = Directory.GetDirectories(solutionRoot, "bin", SearchOption.AllDirectories);
        foreach (var binRoot in binDirs)
        {
            var debugDirs = Directory.GetDirectories(binRoot, "*", SearchOption.AllDirectories);
            foreach (var dir in debugDirs)
            {
                if (Directory.GetFiles(dir, "TheWatch.Shared.dll").Length > 0)
                    return dir;
            }
        }

        // Last resort: use the shared project bin/Debug
        return Path.Combine(solutionRoot, "TheWatch.Shared", "bin", "Debug", "net10.0");
    }

    /// <summary>
    /// Find the solution root directory (containing TheWatch.slnx or TheWatch.sln).
    /// Walks up from the current directory.
    /// </summary>
    private static string FindSolutionRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "TheWatch.slnx")) ||
                File.Exists(Path.Combine(dir, "TheWatch.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: current directory
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Run `dotnet build` on the solution/project and return whether it succeeded.
    /// Streams output to console in real-time.
    /// </summary>
    private static async Task<bool> RunDotnetBuildAsync(string projectOrSolutionPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectOrSolutionPath) ?? Directory.GetCurrentDirectory()
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(projectOrSolutionPath);
        psi.ArgumentList.Add("-v:minimal");
        psi.ArgumentList.Add("--no-restore");

        try
        {
            using var process = new Process { StartInfo = psi };
            var outputLines = new List<string>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                outputLines.Add(e.Data);
                // Show errors and warnings inline
                if (e.Data.Contains("error ") || e.Data.Contains("warning "))
                {
                    Console.ForegroundColor = e.Data.Contains("error ") ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.WriteLine($"    {e.Data}");
                    Console.ResetColor();
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    outputLines.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;

            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Build succeeded.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Build failed (exit code {process.ExitCode}).");
                Console.ResetColor();

                // Print last few lines for context
                foreach (var line in outputLines.TakeLast(15))
                    Console.WriteLine($"    {line}");
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Failed to run dotnet build: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    /// <summary>Print the full scan result to console with formatting.</summary>
    private static void PrintScanResult(AssemblyScanResult result)
    {
        // Summary
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("SUMMARY");
        Console.ResetColor();
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"  Assemblies scanned:  {result.AssembliesScanned}");
        Console.WriteLine($"  Total types:         {result.TotalTypes}");
        Console.WriteLine($"  Port interfaces:     {result.AllPorts.Count}");
        Console.WriteLine($"  Implementations:     {result.AllImplementations.Count}");
        Console.WriteLine($"  Gaps (no impl):      {result.Gaps.Count}");
        Console.WriteLine();

        if (result.Errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNINGS:");
            foreach (var err in result.Errors)
                Console.WriteLine($"  {err}");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Port interfaces
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"PORT INTERFACES ({result.AllPorts.Count})");
        Console.ResetColor();
        Console.WriteLine(new string('-', 80));

        foreach (var port in result.AllPorts.OrderBy(p => p.InterfaceName))
        {
            var implCount = result.AllImplementations.Count(i => i.ImplementsInterface == port.FullName);
            var statusColor = implCount > 0 ? ConsoleColor.Green : ConsoleColor.Red;
            var statusText = implCount > 0 ? $"{implCount} impl(s)" : "NO IMPL";

            Console.Write($"  {port.InterfaceName,-40} ");
            Console.ForegroundColor = statusColor;
            Console.Write($"[{statusText}]");
            Console.ResetColor();
            Console.WriteLine($"  ({port.Methods.Count} methods)");
        }
        Console.WriteLine();

        // Implementations
        if (result.AllImplementations.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"IMPLEMENTATIONS ({result.AllImplementations.Count})");
            Console.ResetColor();
            Console.WriteLine(new string('-', 80));

            foreach (var impl in result.AllImplementations.OrderBy(i => i.AssemblyName).ThenBy(i => i.ClassName))
            {
                var ifaceShort = impl.ImplementsInterface.Split('.').Last();
                var ctorParams = impl.ConstructorParams.Count > 0
                    ? $"  ctor({string.Join(", ", impl.ConstructorParams.Select(p => p.TypeName))})"
                    : "  (parameterless)";

                Console.WriteLine($"  {impl.ClassName,-40} → {ifaceShort}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Assembly: {impl.AssemblyName}{ctorParams}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // Gaps
        if (result.Gaps.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"GAPS — Ports with NO implementation ({result.Gaps.Count})");
            Console.ResetColor();
            Console.WriteLine(new string('-', 80));

            foreach (var gap in result.Gaps.OrderBy(g => g.PortInterfaceName))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"  {gap.PortInterfaceName,-40}");
                Console.ResetColor();
                Console.WriteLine($"  ({gap.MethodCount} methods, defined in {gap.DefinedInAssembly})");
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Fix with: dotnet run --project TheWatch.Cli -- codegen mock-adapters");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All ports have at least one implementation.");
            Console.ResetColor();
        }
    }

    private static void PrintStep(int step, string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[Step {step}] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }
}
