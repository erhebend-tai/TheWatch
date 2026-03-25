// =============================================================================
// CecilAssemblyScanner — IL-level assembly scanner using Mono.Cecil
// =============================================================================
// Scans compiled TheWatch.*.dll assemblies to resolve the full port-to-adapter
// mapping at the IL level — no source code needed, works across assembly
// boundaries. This is the foundation of the codegen pipeline:
//
//   1. FindPortInterfaces()       — Walk TheWatch.Shared.dll for I*Port interfaces
//   2. FindPortImplementations()  — Walk all TheWatch.Adapters.*.dll for classes
//                                    implementing those interfaces
//   3. FindPortGaps()             — Diff ports vs implementations to find missing adapters
//   4. GenerateServiceCollectionExtensions() — Emit DI registration code
//   5. ScanAssembliesAsync()      — Orchestrates all of the above
//
// Why Cecil instead of Roslyn?
//   - Roslyn requires source code and MSBuild resolution (slow, fragile with MAUI/iOS)
//   - Cecil works on compiled DLLs — fast, reliable, cross-platform
//   - Constructor params are available in IL metadata (no need for semantic model)
//   - Can scan assemblies from any build configuration (Debug/Release)
//
// Cross-Assembly Resolution Strategy:
//   - Port interfaces live in TheWatch.Shared.dll under TheWatch.Shared.Domain.Ports
//   - Implementations live in TheWatch.Adapters.Mock.dll, TheWatch.Adapters.Azure.dll, etc.
//   - Cecil resolves interface references by FullName across assembly boundaries
//   - We also check base class hierarchies (abstract adapter base → concrete impl)
//
// Example:
//   var scanner = new CecilAssemblyScanner();
//   var result = await scanner.ScanAssembliesAsync("bin/Debug/net10.0");
//   Console.WriteLine($"Found {result.AllPorts.Count} ports, {result.AllImplementations.Count} impls");
//   Console.WriteLine($"Gaps: {result.Gaps.Count}");
//   var diCode = scanner.GenerateServiceCollectionExtensions(result.AllImplementations, "TheWatch.Adapters.Mock");
//   File.WriteAllText("ServiceCollectionExtensions.g.cs", diCode);
//
// WAL: Some assemblies may fail to load (MAUI platform-specific, iOS stubs, etc.).
//      Each assembly is loaded in its own try/catch — failures are logged and skipped.
//      ReaderParameters { ReadSymbols = false } avoids PDB/MDB dependency issues.
//
// Potential enhancements (not yet implemented):
//   - Support for open generic port interfaces (IRepository<T>)
//   - Detection of decorator/proxy patterns wrapping adapters
//   - NuGet package scanning for third-party adapter implementations
//   - Assembly version mismatch detection between shared and adapter DLLs
//   - Support for IServiceCollection.AddScoped / AddTransient lifetime heuristics
//   - Write-ahead log file for incremental re-scan (hash DLLs, skip unchanged)
// =============================================================================

using Mono.Cecil;

namespace TheWatch.Cli.Services.Cecil;

/// <summary>
/// Scans compiled assemblies using Mono.Cecil to discover port interfaces,
/// their implementations across adapter assemblies, constructor dependencies,
/// and gaps where no implementation exists.
/// </summary>
public class CecilAssemblyScanner
{
    // ── Port namespace convention ────────────────────────────────────
    // All port interfaces live under this namespace in TheWatch.Shared.dll
    private const string PortNamespace = "TheWatch.Shared.Domain.Ports";

    // Port interfaces end with "Port" (IResponseCoordinationPort, IEvidencePort, etc.)
    // Some also follow other naming: IAuditTrail, ISpatialIndex, IStorageService
    // We scan ALL interfaces in the port namespace, not just *Port suffix
    private const string PortSuffix = "Port";

    // ── Primary entry point ──────────────────────────────────────────

    /// <summary>
    /// Full scan: discover all ports, implementations, and gaps from compiled binaries.
    /// </summary>
    /// <param name="binDirectory">Path to bin/Debug/net10.0 (or similar) containing TheWatch.*.dll files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete scan result with ports, implementations, gaps, and assembly stats.</returns>
    public Task<AssemblyScanResult> ScanAssembliesAsync(string binDirectory, CancellationToken ct = default)
    {
        var result = new AssemblyScanResult();

        if (!Directory.Exists(binDirectory))
        {
            result.Errors.Add($"Directory not found: {binDirectory}");
            return Task.FromResult(result);
        }

        // Find all TheWatch.*.dll files
        var dllFiles = Directory.GetFiles(binDirectory, "TheWatch.*.dll", SearchOption.TopDirectoryOnly);
        result.AssembliesScanned = dllFiles.Length;

        if (dllFiles.Length == 0)
        {
            result.Errors.Add($"No TheWatch.*.dll files found in {binDirectory}");
            return Task.FromResult(result);
        }

        // Step 1: Find port interfaces from TheWatch.Shared.dll
        var sharedDll = dllFiles.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("TheWatch.Shared.dll", StringComparison.OrdinalIgnoreCase));

        if (sharedDll is not null)
        {
            result.AllPorts = FindPortInterfaces(sharedDll);
        }
        else
        {
            result.Errors.Add("TheWatch.Shared.dll not found — cannot discover port interfaces.");
        }

        // Step 2: Find implementations across all assemblies
        result.AllImplementations = FindPortImplementations(binDirectory);

        // Step 3: Count total types scanned
        result.TotalTypes = CountTotalTypes(dllFiles);

        // Step 4: Find gaps
        result.Gaps = FindPortGaps(result.AllPorts, result.AllImplementations);

        return Task.FromResult(result);
    }

    // ── Port Interface Discovery ─────────────────────────────────────

    /// <summary>
    /// Find all port interfaces defined in the shared assembly.
    /// Scans the TheWatch.Shared.Domain.Ports namespace for public interfaces.
    /// </summary>
    /// <param name="sharedAssemblyPath">Full path to TheWatch.Shared.dll.</param>
    /// <returns>List of port interface metadata.</returns>
    public List<PortInterfaceInfo> FindPortInterfaces(string sharedAssemblyPath)
    {
        var ports = new List<PortInterfaceInfo>();

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(sharedAssemblyPath,
                new ReaderParameters { ReadSymbols = false });

            var assemblyName = assembly.Name.Name;

            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    // Must be a public interface in the ports namespace
                    if (!type.IsInterface || !type.IsPublic)
                        continue;

                    if (!type.Namespace?.StartsWith(PortNamespace) ?? true)
                        continue;

                    var portInfo = new PortInterfaceInfo
                    {
                        InterfaceName = type.Name,
                        FullName = type.FullName,
                        AssemblyName = assemblyName,
                        Methods = ExtractMethodSignatures(type)
                    };

                    ports.Add(portInfo);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw — caller handles empty list
            Console.Error.WriteLine($"[Cecil] Failed to read {sharedAssemblyPath}: {ex.Message}");
        }

        return ports;
    }

    // ── Implementation Discovery ─────────────────────────────────────

    /// <summary>
    /// Find all classes implementing port interfaces across all TheWatch.*.dll assemblies.
    /// Resolves interface implementation across assembly boundaries by matching FullName.
    /// Also checks base class hierarchies for indirect implementation.
    /// </summary>
    /// <param name="binDirectory">Directory containing compiled DLLs.</param>
    /// <returns>List of implementation metadata including constructor parameters.</returns>
    public List<PortImplementationInfo> FindPortImplementations(string binDirectory)
    {
        var implementations = new List<PortImplementationInfo>();
        var dllFiles = Directory.GetFiles(binDirectory, "TheWatch.*.dll", SearchOption.TopDirectoryOnly);

        // First pass: collect all port interface full names for matching
        var portFullNames = new HashSet<string>();
        var sharedDll = dllFiles.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("TheWatch.Shared.dll", StringComparison.OrdinalIgnoreCase));

        if (sharedDll is not null)
        {
            var ports = FindPortInterfaces(sharedDll);
            foreach (var p in ports)
                portFullNames.Add(p.FullName);
        }

        if (portFullNames.Count == 0)
            return implementations;

        // Second pass: scan all assemblies for implementations
        foreach (var dllPath in dllFiles)
        {
            var fileName = Path.GetFileName(dllPath);

            // Skip the shared assembly itself — it defines ports, not implementations
            if (fileName.Equals("TheWatch.Shared.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(dllPath,
                    new ReaderParameters { ReadSymbols = false });

                var assemblyName = assembly.Name.Name;

                foreach (var module in assembly.Modules)
                {
                    foreach (var type in module.Types)
                    {
                        // Skip non-class types, abstract types, and non-public types
                        if (!type.IsClass || type.IsAbstract || !type.IsPublic)
                            continue;

                        // Check direct interface implementations
                        var implementedPorts = GetImplementedPortInterfaces(type, portFullNames);

                        foreach (var portFullName in implementedPorts)
                        {
                            var implInfo = new PortImplementationInfo
                            {
                                ClassName = type.Name,
                                FullName = type.FullName,
                                AssemblyName = assemblyName,
                                ImplementsInterface = portFullName,
                                ConstructorParams = ExtractConstructorParams(type)
                            };

                            implementations.Add(implInfo);
                        }
                    }

                    // Also check nested types (some adapters are nested classes)
                    foreach (var type in module.Types)
                    {
                        if (!type.HasNestedTypes) continue;
                        foreach (var nested in type.NestedTypes)
                        {
                            if (!nested.IsClass || nested.IsAbstract || !nested.IsNestedPublic)
                                continue;

                            var implementedPorts = GetImplementedPortInterfaces(nested, portFullNames);
                            foreach (var portFullName in implementedPorts)
                            {
                                implementations.Add(new PortImplementationInfo
                                {
                                    ClassName = nested.Name,
                                    FullName = nested.FullName,
                                    AssemblyName = assemblyName,
                                    ImplementsInterface = portFullName,
                                    ConstructorParams = ExtractConstructorParams(nested)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // MAUI, iOS, and platform-specific DLLs may fail to load — skip gracefully
                Console.Error.WriteLine($"[Cecil] Skipping {fileName}: {ex.Message}");
            }
        }

        return implementations;
    }

    // ── Gap Analysis ─────────────────────────────────────────────────

    /// <summary>
    /// Compare discovered ports against implementations to find missing adapters.
    /// A "gap" is a port interface with zero concrete implementations.
    /// </summary>
    public List<PortGapInfo> FindPortGaps(
        List<PortInterfaceInfo> ports,
        List<PortImplementationInfo> implementations)
    {
        var gaps = new List<PortGapInfo>();

        // Build a set of implemented port full names
        var implementedPorts = new HashSet<string>(
            implementations.Select(i => i.ImplementsInterface));

        // Collect adapter assembly names for the gap report
        var adapterAssemblies = implementations
            .Select(i => i.AssemblyName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        foreach (var port in ports)
        {
            if (!implementedPorts.Contains(port.FullName))
            {
                gaps.Add(new PortGapInfo
                {
                    PortInterfaceName = port.InterfaceName,
                    PortFullName = port.FullName,
                    DefinedInAssembly = port.AssemblyName,
                    AdapterAssembliesChecked = adapterAssemblies,
                    MethodCount = port.Methods.Count
                });
            }
        }

        return gaps;
    }

    // ── Code Generation: ServiceCollectionExtensions ─────────────────

    /// <summary>
    /// Generate a ServiceCollectionExtensions.cs file with AddSingleton registrations
    /// for all discovered port implementations. Groups by adapter assembly.
    /// </summary>
    /// <param name="implementations">All discovered port implementations.</param>
    /// <param name="namespaceName">Target namespace (e.g., "TheWatch.Adapters.Mock").</param>
    /// <returns>Complete C# source code for ServiceCollectionExtensions.cs.</returns>
    public string GenerateServiceCollectionExtensions(
        List<PortImplementationInfo> implementations,
        string namespaceName)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// =============================================================================");
        sb.AppendLine("// ServiceCollectionExtensions — Auto-generated DI registrations");
        sb.AppendLine("// =============================================================================");
        sb.AppendLine($"// Generated by TheWatch.Cli CecilAssemblyScanner at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("// DO NOT EDIT — regenerate with: dotnet run --project TheWatch.Cli -- codegen di-wire");
        sb.AppendLine("//");
        sb.AppendLine("// Example:");
        sb.AppendLine("//   services.AddTheWatchAdapters();  // registers all port→adapter bindings");
        sb.AppendLine("//");
        sb.AppendLine("// WAL: This file is regenerated from Cecil assembly scan results.");
        sb.AppendLine("//      Any manual edits will be overwritten on next codegen run.");
        sb.AppendLine("//      To add custom registrations, create a separate extension method.");
        sb.AppendLine("// =============================================================================");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");

        // Collect all unique namespaces needed for using directives
        var namespaces = new HashSet<string>();
        foreach (var impl in implementations)
        {
            var ns = GetNamespaceFromFullName(impl.FullName);
            if (!string.IsNullOrEmpty(ns))
                namespaces.Add(ns);

            var ifaceNs = GetNamespaceFromFullName(impl.ImplementsInterface);
            if (!string.IsNullOrEmpty(ifaceNs))
                namespaces.Add(ifaceNs);
        }

        foreach (var ns in namespaces.OrderBy(n => n))
        {
            sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine("public static class ServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Register all discovered port→adapter bindings as singletons.");
        sb.AppendLine($"    /// Auto-generated from Cecil scan of {implementations.Count} implementations.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddTheWatchAdapters(this IServiceCollection services)");
        sb.AppendLine("    {");

        // Group implementations by the adapter assembly for readability
        var grouped = implementations
            .GroupBy(i => i.AssemblyName)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"        // ── {group.Key} ──");

            foreach (var impl in group.OrderBy(i => i.ImplementsInterface))
            {
                var ifaceName = GetTypeNameFromFullName(impl.ImplementsInterface);
                var implName = GetTypeNameFromFullName(impl.FullName);

                // Include constructor param comment for discoverability
                if (impl.ConstructorParams.Count > 0)
                {
                    var paramList = string.Join(", ", impl.ConstructorParams.Select(p => $"{p.TypeName} {p.Name}"));
                    sb.AppendLine($"        // ctor({paramList})");
                }

                sb.AppendLine($"        services.AddSingleton<{ifaceName}, {implName}>();");
            }

            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Internal Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Get all port interfaces implemented by a type, including via base class hierarchy.
    /// Walks type.Interfaces and recursively checks base types.
    /// </summary>
    private static List<string> GetImplementedPortInterfaces(TypeDefinition type, HashSet<string> portFullNames)
    {
        var result = new List<string>();
        var checkedTypes = new HashSet<string>();

        CollectPortInterfaces(type, portFullNames, result, checkedTypes);

        return result;
    }

    private static void CollectPortInterfaces(
        TypeDefinition? type,
        HashSet<string> portFullNames,
        List<string> result,
        HashSet<string> checkedTypes)
    {
        if (type is null) return;
        if (!checkedTypes.Add(type.FullName)) return;

        // Check direct interface implementations
        if (type.HasInterfaces)
        {
            foreach (var iface in type.Interfaces)
            {
                var ifaceFullName = iface.InterfaceType.FullName;
                if (portFullNames.Contains(ifaceFullName))
                {
                    result.Add(ifaceFullName);
                }
            }
        }

        // Recursively check base class — handles abstract adapter base patterns
        // e.g., class MockFooPort : FooPortBase where FooPortBase : IFooPort
        if (type.BaseType is not null && type.BaseType.FullName != "System.Object")
        {
            try
            {
                var baseType = type.BaseType.Resolve();
                CollectPortInterfaces(baseType, portFullNames, result, checkedTypes);
            }
            catch
            {
                // Base type may be in an unresolvable assembly — skip
            }
        }
    }

    /// <summary>
    /// Extract constructor parameters from the primary (non-static) constructor.
    /// Uses the first public instance constructor, or the first instance constructor if none are public.
    /// </summary>
    private static List<ConstructorParamInfo> ExtractConstructorParams(TypeDefinition type)
    {
        var ctor = type.Methods
            .Where(m => m.IsConstructor && !m.IsStatic)
            .OrderByDescending(m => m.IsPublic)      // prefer public ctor
            .ThenByDescending(m => m.Parameters.Count) // prefer most-params ctor (DI convention)
            .FirstOrDefault();

        if (ctor is null || !ctor.HasParameters)
            return new List<ConstructorParamInfo>();

        return ctor.Parameters.Select(p => new ConstructorParamInfo
        {
            Name = p.Name,
            TypeName = SimplifyTypeName(p.ParameterType.Name),
            FullTypeName = p.ParameterType.FullName
        }).ToList();
    }

    /// <summary>
    /// Extract method signatures from an interface type definition.
    /// Returns human-readable signatures like "Task{StorageResult} CreateAsync(string id, CancellationToken ct)"
    /// </summary>
    private static List<string> ExtractMethodSignatures(TypeDefinition type)
    {
        var methods = new List<string>();

        foreach (var method in type.Methods)
        {
            if (method.IsConstructor || method.IsSpecialName) continue;

            var returnType = SimplifyTypeName(method.ReturnType.Name);
            var parameters = string.Join(", ",
                method.Parameters.Select(p => $"{SimplifyTypeName(p.ParameterType.Name)} {p.Name}"));

            methods.Add($"{returnType} {method.Name}({parameters})");
        }

        return methods;
    }

    /// <summary>
    /// Count total types across all assemblies for the scan summary.
    /// </summary>
    private static int CountTotalTypes(string[] dllFiles)
    {
        int total = 0;
        foreach (var dll in dllFiles)
        {
            try
            {
                using var asm = AssemblyDefinition.ReadAssembly(dll,
                    new ReaderParameters { ReadSymbols = false });
                foreach (var module in asm.Modules)
                    total += module.Types.Count;
            }
            catch { }
        }
        return total;
    }

    /// <summary>
    /// Simplify a Cecil type name (remove backtick generics notation, common namespace prefixes).
    /// e.g., "Task`1" → "Task", "ILogger`1" → "ILogger{T}"
    /// </summary>
    private static string SimplifyTypeName(string name)
    {
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex > 0)
            return name[..backtickIndex];
        return name;
    }

    /// <summary>Extract namespace portion from a FullName like "TheWatch.Shared.Domain.Ports.IEvidencePort".</summary>
    private static string GetNamespaceFromFullName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot > 0 ? fullName[..lastDot] : "";
    }

    /// <summary>Extract type name portion from a FullName like "TheWatch.Adapters.Mock.MockEvidencePort".</summary>
    private static string GetTypeNameFromFullName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot > 0 ? fullName[(lastDot + 1)..] : fullName;
    }
}

// ── Result Types ─────────────────────────────────────────────────────

/// <summary>Complete result of a Cecil assembly scan.</summary>
public class AssemblyScanResult
{
    public List<PortInterfaceInfo> AllPorts { get; set; } = new();
    public List<PortImplementationInfo> AllImplementations { get; set; } = new();
    public List<PortGapInfo> Gaps { get; set; } = new();
    public int AssembliesScanned { get; set; }
    public int TotalTypes { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>Metadata for a port interface discovered in TheWatch.Shared.</summary>
public class PortInterfaceInfo
{
    public string InterfaceName { get; set; } = "";
    public string FullName { get; set; } = "";    // e.g., TheWatch.Shared.Domain.Ports.IEvidencePort
    public string AssemblyName { get; set; } = ""; // e.g., TheWatch.Shared
    public List<string> Methods { get; set; } = new();
}

/// <summary>Metadata for a concrete class implementing a port interface.</summary>
public class PortImplementationInfo
{
    public string ClassName { get; set; } = "";
    public string FullName { get; set; } = "";           // e.g., TheWatch.Adapters.Mock.MockEvidencePort
    public string AssemblyName { get; set; } = "";       // e.g., TheWatch.Adapters.Mock
    public string ImplementsInterface { get; set; } = ""; // e.g., TheWatch.Shared.Domain.Ports.IEvidencePort
    public List<ConstructorParamInfo> ConstructorParams { get; set; } = new();
}

/// <summary>A constructor parameter of an adapter implementation.</summary>
public class ConstructorParamInfo
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";     // Short name, e.g., "ILogger"
    public string FullTypeName { get; set; } = ""; // Full name, e.g., "Microsoft.Extensions.Logging.ILogger`1<MockEvidencePort>"
}

/// <summary>A port interface with no discovered implementation — a gap in the adapter layer.</summary>
public class PortGapInfo
{
    public string PortInterfaceName { get; set; } = "";
    public string PortFullName { get; set; } = "";
    public string DefinedInAssembly { get; set; } = "";
    public List<string> AdapterAssembliesChecked { get; set; } = new();
    public int MethodCount { get; set; }
}
