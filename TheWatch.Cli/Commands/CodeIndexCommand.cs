// =============================================================================
// CodeIndexCommand — Scan solution code and emit a tagged CSV index
// =============================================================================
// Walks the solution via Roslyn MSBuildWorkspace, extracts every class, interface,
// enum, record, struct, and method, tags them by namespace/folder/naming convention,
// and writes a CSV for review in Excel/Sheets.
//
// Subcommands:
//   thewatch codeindex                     — Index the default solution (TheWatch.slnx)
//   thewatch codeindex --solution path.slnx — Index a specific solution
//   thewatch codeindex --output index.csv  — Output to specific file (default: code-index.csv)
//   thewatch codeindex --include-bodies    — Include method body hashes for duplicate detection
//   thewatch codeindex --external path/    — Also scan external .cs files from a directory
//
// Output CSV columns:
//   repo, project, file, kind, name, signature, #tags, depends_on, lines, body_hash
//
// Tags are auto-derived from:
//   - Namespace segments → #emergency, #auth, #audit, #spatial, etc.
//   - Naming patterns → #port (IXxxPort), #adapter (XxxAdapter), #mock (MockXxx),
//     #controller, #service, #model, #enum, #test
//   - Folder path → #android, #ios, #functions, #dashboard, etc.
//
// Example:
//   dotnet run --project TheWatch.Cli -- codeindex
//   dotnet run --project TheWatch.Cli -- codeindex --output integration-review.csv
//   dotnet run --project TheWatch.Cli -- codeindex --external ../ExternalApi/src/
//
// WAL: Uses MSBuild.Locator to find the SDK, then Roslyn to parse without building.
// =============================================================================

using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace TheWatch.Cli.Commands;

public static class CodeIndexCommand
{
    public static Command Build()
    {
        var solutionOption = new Option<string>("--solution")
        {
            Description = "Path to .slnx/.sln file",
            DefaultValueFactory = _ => "TheWatch.slnx"
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Output CSV file path",
            DefaultValueFactory = _ => "code-index.csv"
        };

        var includeBodiesOption = new Option<bool>("--include-bodies")
        {
            Description = "Include method body hashes for duplicate detection",
            DefaultValueFactory = _ => false
        };

        var externalOption = new Option<string?>("--external")
        {
            Description = "Also scan .cs files from an external directory"
        };

        var cmd = new Command("codeindex", "Scan solution code and emit a tagged CSV index")
        {
            solutionOption,
            outputOption,
            includeBodiesOption,
            externalOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var solutionPath = parseResult.GetValue(solutionOption)!;
            var outputPath = parseResult.GetValue(outputOption)!;
            var includeBodies = parseResult.GetValue(includeBodiesOption);
            var externalDir = parseResult.GetValue(externalOption);

            await RunAsync(solutionPath, outputPath, includeBodies, externalDir);
        });

        return cmd;
    }

    private static async Task RunAsync(string solutionPath, string outputPath, bool includeBodies, string? externalDir)
    {
        Console.WriteLine($"[CODEINDEX] Scanning: {solutionPath}");
        Console.WriteLine($"[CODEINDEX] Output:   {outputPath}");

        // Register MSBuild before using Roslyn workspaces
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();

        var rows = new List<CodeIndexRow>();

        // ── Scan solution via Roslyn ────────────────────────────────
        // Try MSBuildWorkspace for .sln files; fall back to file-system scan for .slnx or failures
        var usedWorkspace = false;
        if (File.Exists(solutionPath) && solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var workspace = MSBuildWorkspace.Create();
                workspace.WorkspaceFailed += (_, e) =>
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                        Console.Error.WriteLine($"[CODEINDEX] Workspace: {e.Diagnostic.Message}");
                };

                Console.WriteLine("[CODEINDEX] Loading solution via Roslyn...");
                var solution = await workspace.OpenSolutionAsync(Path.GetFullPath(solutionPath));
                Console.WriteLine($"[CODEINDEX] Found {solution.Projects.Count()} projects");

                foreach (var project in solution.Projects)
                {
                    if (project.FilePath?.Contains("/obj/") == true || project.FilePath?.Contains("\\obj\\") == true)
                        continue;

                    Console.Write($"  {project.Name}... ");
                    var projectRows = await ScanProjectAsync(project, includeBodies);
                    rows.AddRange(projectRows);
                    Console.WriteLine($"{projectRows.Count} symbols");
                }
                usedWorkspace = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CODEINDEX] Roslyn workspace failed: {ex.Message}");
            }
        }

        if (!usedWorkspace)
        {
            // File-system scan — works with .slnx, monorepos, or any directory
            Console.WriteLine("[CODEINDEX] Scanning .cs files from file system...");
            var scanDirs = Directory.GetDirectories(".")
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.StartsWith("TheWatch") && !name.Contains("obj") && !name.Contains("bin");
                })
                .ToList();

            foreach (var dir in scanDirs)
            {
                var dirName = Path.GetFileName(Path.GetFullPath(dir));
                Console.Write($"  {dirName}... ");
                var dirRows = await ScanDirectoryAsync(dir, dirName, includeBodies);
                // Fix project name: use dirName for all rows from this directory
                foreach (var row in dirRows)
                {
                    rows.Add(row with { Project = dirName });
                }
                Console.WriteLine($"{dirRows.Count} symbols");
            }
        }

        // ── Scan external directory ─────────────────────────────────
        if (!string.IsNullOrEmpty(externalDir) && Directory.Exists(externalDir))
        {
            Console.WriteLine($"[CODEINDEX] Scanning external: {externalDir}");
            var externalName = Path.GetFileName(Path.GetFullPath(externalDir));
            rows.AddRange(await ScanDirectoryAsync(externalDir, externalName, includeBodies));
        }

        // ── Also scan Kotlin and Swift files (tag-only, no Roslyn) ──
        rows.AddRange(ScanMobileFiles("TheWatch-Android", "android", "kotlin"));
        rows.AddRange(ScanMobileFiles("TheWatch-iOS", "ios", "swift"));

        // ── Write CSV ───────────────────────────────────────────────
        Console.WriteLine($"[CODEINDEX] Writing {rows.Count} rows to {outputPath}");
        await WriteCsvAsync(outputPath, rows);

        // ── Summary ─────────────────────────────────────────────────
        var tagCounts = rows
            .SelectMany(r => r.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Take(20);

        Console.WriteLine();
        Console.WriteLine("[CODEINDEX] Top 20 tags:");
        foreach (var tag in tagCounts)
            Console.WriteLine($"  {tag.Key,-30} {tag.Count(),5} symbols");

        Console.WriteLine();
        Console.WriteLine($"[CODEINDEX] Done. {rows.Count} symbols indexed across {rows.Select(r => r.Project).Distinct().Count()} projects.");
    }

    // ── Roslyn Project Scanner ──────────────────────────────────────

    private static async Task<List<CodeIndexRow>> ScanProjectAsync(Project project, bool includeBodies)
    {
        var rows = new List<CodeIndexRow>();
        var repoName = "TheWatch";
        var projectName = project.Name;

        foreach (var doc in project.Documents)
        {
            if (doc.FilePath == null) continue;

            // Skip generated/obj files
            var relPath = GetRelativePath(doc.FilePath);
            if (relPath.Contains("/obj/") || relPath.Contains("\\obj\\")) continue;
            if (relPath.Contains("/bin/") || relPath.Contains("\\bin\\")) continue;

            var tree = await doc.GetSyntaxTreeAsync();
            if (tree == null) continue;

            var root = await tree.GetRootAsync();

            // Extract type declarations
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var kind = typeDecl switch
                {
                    InterfaceDeclarationSyntax => "interface",
                    ClassDeclarationSyntax => "class",
                    RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
                    StructDeclarationSyntax => "struct",
                    _ => "type"
                };

                var name = typeDecl.Identifier.Text;
                var ns = GetNamespace(typeDecl);
                var signature = BuildTypeSignature(typeDecl);
                var tags = DeriveTags(relPath, projectName, kind, name, ns);
                var dependsOn = ExtractDependencies(typeDecl);
                var lineCount = typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line
                              - typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                rows.Add(new CodeIndexRow
                {
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = kind,
                    Name = ns != null ? $"{ns}.{name}" : name,
                    Signature = signature,
                    Tags = tags,
                    DependsOn = dependsOn,
                    Lines = lineCount,
                    BodyHash = includeBodies ? HashBody(typeDecl.ToFullString()) : ""
                });
            }

            // Extract top-level enum declarations
            foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                var name = enumDecl.Identifier.Text;
                var ns = GetNamespace(enumDecl);
                var memberCount = enumDecl.Members.Count;
                var tags = DeriveTags(relPath, projectName, "enum", name, ns);
                var lineCount = enumDecl.GetLocation().GetLineSpan().EndLinePosition.Line
                              - enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                rows.Add(new CodeIndexRow
                {
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = "enum",
                    Name = ns != null ? $"{ns}.{name}" : name,
                    Signature = $"enum {name} ({memberCount} members)",
                    Tags = tags,
                    DependsOn = "",
                    Lines = lineCount,
                    BodyHash = includeBodies ? HashBody(enumDecl.ToFullString()) : ""
                });
            }
        }

        return rows;
    }

    // ── File-system fallback scanner ────────────────────────────────

    private static async Task<List<CodeIndexRow>> ScanDirectoryAsync(string dir, string repoName, bool includeBodies)
    {
        var rows = new List<CodeIndexRow>();
        var csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("\\obj\\")
                     && !f.Contains("/bin/") && !f.Contains("\\bin\\"));

        foreach (var file in csFiles)
        {
            var code = await File.ReadAllTextAsync(file);
            var tree = CSharpSyntaxTree.ParseText(code, path: file);
            var root = tree.GetCompilationUnitRoot();
            var relPath = GetRelativePath(file);
            var projectName = GuessProjectName(relPath);

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var kind = typeDecl switch
                {
                    InterfaceDeclarationSyntax => "interface",
                    ClassDeclarationSyntax => "class",
                    RecordDeclarationSyntax => "record",
                    StructDeclarationSyntax => "struct",
                    _ => "type"
                };

                var name = typeDecl.Identifier.Text;
                var ns = GetNamespace(typeDecl);
                var tags = DeriveTags(relPath, projectName, kind, name, ns);

                rows.Add(new CodeIndexRow
                {
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = kind,
                    Name = ns != null ? $"{ns}.{name}" : name,
                    Signature = BuildTypeSignature(typeDecl),
                    Tags = tags,
                    DependsOn = ExtractDependencies(typeDecl),
                    Lines = typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line
                          - typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    BodyHash = includeBodies ? HashBody(typeDecl.ToFullString()) : ""
                });
            }
        }

        return rows;
    }

    // ── Mobile file scanner (Kotlin/Swift — tag-based, no AST) ──────

    private static List<CodeIndexRow> ScanMobileFiles(string dir, string platform, string language)
    {
        var rows = new List<CodeIndexRow>();
        if (!Directory.Exists(dir)) return rows;

        var extensions = language == "kotlin" ? new[] { "*.kt" } : new[] { "*.swift" };
        foreach (var ext in extensions)
        {
            foreach (var file in Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
            {
                if (file.Contains("/build/") || file.Contains("\\build\\")) continue;
                if (file.Contains("/.gradle/") || file.Contains("\\.gradle\\")) continue;

                var relPath = GetRelativePath(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                var lines = File.ReadLines(file).Count();

                // Derive kind from naming conventions and folder context
                var folderContext = relPath.Replace('\\', '/').ToLowerInvariant();
                var kind = fileName switch
                {
                    _ when fileName.EndsWith("ViewModel") => "viewmodel",
                    _ when fileName.EndsWith("View") || fileName.EndsWith("Screen") || fileName.EndsWith("Page") => "view",
                    _ when fileName.EndsWith("Service") || fileName.EndsWith("Port") => "service",
                    _ when fileName.EndsWith("Repository") => "repository",
                    _ when fileName.EndsWith("Adapter") => "adapter",
                    _ when fileName.EndsWith("Model") || fileName.EndsWith("Models") => "model",
                    _ when fileName.EndsWith("Controller") => "controller",
                    _ when fileName.EndsWith("Coordinator") || fileName.EndsWith("Engine") || fileName.EndsWith("Worker") || fileName.EndsWith("Dispatcher") => "service",
                    _ when fileName.StartsWith("Mock") => "mock",
                    _ when fileName.EndsWith("Tests") || fileName.EndsWith("Test") => "test",
                    _ when fileName.EndsWith("Config") || fileName.EndsWith("Configuration") || fileName.EndsWith("Constants") => "config",
                    _ when fileName.EndsWith("Entity") || fileName.EndsWith("Dto") || fileName.EndsWith("Request") || fileName.EndsWith("Response") => "model",
                    _ when folderContext.Contains("/models/") || folderContext.Contains("/model/") || folderContext.Contains("/data/model/") => "model",
                    _ when folderContext.Contains("/views/") || folderContext.Contains("/screens/") || folderContext.Contains("/ui/") => "view",
                    _ when folderContext.Contains("/viewmodels/") || folderContext.Contains("/viewmodel/") => "viewmodel",
                    _ when folderContext.Contains("/services/") || folderContext.Contains("/service/") => "service",
                    _ when folderContext.Contains("/repository/") || folderContext.Contains("/repositories/") => "repository",
                    _ when folderContext.Contains("/di/") || folderContext.Contains("/navigation/") => "config",
                    _ => "source"
                };

                var tags = $"#{platform} #{language}";

                // Folder-based tags
                var folderParts = relPath.Replace('\\', '/').Split('/');
                foreach (var part in folderParts)
                {
                    var tag = DeriveTagFromFolder(part.ToLowerInvariant());
                    if (tag != null) tags += $" {tag}";
                }

                // Name-based tags
                var nameTags = DeriveTagsFromName(fileName);
                if (nameTags.Length > 0) tags += $" {nameTags}";

                rows.Add(new CodeIndexRow
                {
                    Repo = "TheWatch",
                    Project = platform == "android" ? "TheWatch-Android" : "TheWatch-iOS",
                    File = relPath,
                    Kind = kind,
                    Name = fileName,
                    Signature = $"{language} {kind}",
                    Tags = tags.Trim(),
                    DependsOn = "",
                    Lines = lines,
                    BodyHash = ""
                });
            }
        }

        Console.WriteLine($"  {(platform == "android" ? "TheWatch-Android" : "TheWatch-iOS")}... {rows.Count} files");
        return rows;
    }

    // ── Tag derivation ──────────────────────────────────────────────

    private static string DeriveTags(string filePath, string projectName, string kind, string name, string? ns)
    {
        var tags = new HashSet<string>();

        // Kind tags
        if (kind == "interface" && name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            tags.Add("#port");
        if (name.StartsWith("Mock")) tags.Add("#mock");
        if (name.EndsWith("Adapter")) tags.Add("#adapter");
        if (name.EndsWith("Controller")) tags.Add("#controller");
        if (name.EndsWith("Service")) tags.Add("#service");
        if (name.EndsWith("ViewModel")) tags.Add("#viewmodel");
        if (kind == "enum") tags.Add("#enum");
        if (kind == "record" || kind == "record struct") tags.Add("#model");

        // Project/folder tags
        var projLower = projectName.ToLowerInvariant();
        if (projLower.Contains("test")) tags.Add("#test");
        if (projLower.Contains("mock")) tags.Add("#mock");
        if (projLower.Contains("azure")) tags.Add("#azure");
        if (projLower.Contains("aws")) tags.Add("#aws");
        if (projLower.Contains("google")) tags.Add("#google");
        if (projLower.Contains("oracle")) tags.Add("#oracle");
        if (projLower.Contains("cloudflare")) tags.Add("#cloudflare");
        if (projLower.Contains("functions")) tags.Add("#functions");
        if (projLower.Contains("dashboard")) tags.Add("#dashboard");
        if (projLower.Contains("maui")) tags.Add("#maui");
        if (projLower.Contains("shared")) tags.Add("#shared");
        if (projLower.Contains("data")) tags.Add("#data");
        if (projLower.Contains("cli")) tags.Add("#cli");
        if (projLower.Contains("worker")) tags.Add("#worker");

        // Namespace-derived domain tags
        if (ns != null)
        {
            var nsLower = ns.ToLowerInvariant();
            if (nsLower.Contains("auth")) tags.Add("#auth");
            if (nsLower.Contains("audit")) tags.Add("#audit");
            if (nsLower.Contains("response") || nsLower.Contains("dispatch")) tags.Add("#emergency");
            if (nsLower.Contains("escalation")) tags.Add("#escalation");
            if (nsLower.Contains("evidence")) tags.Add("#evidence");
            if (nsLower.Contains("spatial") || nsLower.Contains("geo")) tags.Add("#spatial");
            if (nsLower.Contains("notification")) tags.Add("#notification");
            if (nsLower.Contains("cctv") || nsLower.Contains("sensor")) tags.Add("#sensor");
            if (nsLower.Contains("swarm") || nsLower.Contains("agent")) tags.Add("#swarm");
            if (nsLower.Contains("firebase") || nsLower.Contains("firestore")) tags.Add("#firebase");
            if (nsLower.Contains("cosmos")) tags.Add("#cosmosdb");
            if (nsLower.Contains("postgres") || nsLower.Contains("npgsql")) tags.Add("#postgres");
            if (nsLower.Contains("sqlserver")) tags.Add("#sqlserver");
            if (nsLower.Contains("qdrant") || nsLower.Contains("vector")) tags.Add("#vector");
            if (nsLower.Contains("embedding")) tags.Add("#embedding");
            if (nsLower.Contains("watchcall") || nsLower.Contains("narration")) tags.Add("#watchcall");
            if (nsLower.Contains("guard")) tags.Add("#guardreport");
            if (nsLower.Contains("volunteer") || nsLower.Contains("participation")) tags.Add("#volunteering");
            if (nsLower.Contains("gdpr") || nsLower.Contains("consent") || nsLower.Contains("privacy")) tags.Add("#gdpr");
        }

        // Name-derived domain tags
        var nameTags = DeriveTagsFromName(name);
        if (nameTags.Length > 0)
            foreach (var t in nameTags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                tags.Add(t);

        return string.Join(" ", tags.OrderBy(t => t));
    }

    private static string DeriveTagsFromName(string name)
    {
        var tags = new List<string>();
        var lower = name.ToLowerInvariant();

        if (lower.Contains("sos") || lower.Contains("emergency") || lower.Contains("dispatch")) tags.Add("#emergency");
        if (lower.Contains("auth") || lower.Contains("login") || lower.Contains("signup")) tags.Add("#auth");
        if (lower.Contains("audit")) tags.Add("#audit");
        if (lower.Contains("escalat")) tags.Add("#escalation");
        if (lower.Contains("evidence")) tags.Add("#evidence");
        if (lower.Contains("spatial") || lower.Contains("geohash") || lower.Contains("h3")) tags.Add("#spatial");
        if (lower.Contains("notification") || lower.Contains("push") || lower.Contains("fcm") || lower.Contains("apns")) tags.Add("#notification");
        if (lower.Contains("cctv") || lower.Contains("sensor") || lower.Contains("camera")) tags.Add("#sensor");
        if (lower.Contains("swarm") || lower.Contains("agent")) tags.Add("#swarm");
        if (lower.Contains("watchcall") || lower.Contains("narrat")) tags.Add("#watchcall");
        if (lower.Contains("guard") || lower.Contains("report")) tags.Add("#guardreport");
        if (lower.Contains("volunteer") || lower.Contains("responder")) tags.Add("#volunteering");
        if (lower.Contains("gdpr") || lower.Contains("consent") || lower.Contains("export")) tags.Add("#gdpr");
        if (lower.Contains("history") || lower.Contains("timeline")) tags.Add("#history");
        if (lower.Contains("mfa") || lower.Contains("twofactor") || lower.Contains("2fa") || lower.Contains("totp")) tags.Add("#mfa");
        if (lower.Contains("biometric") || lower.Contains("fingerprint") || lower.Contains("faceid")) tags.Add("#biometric");
        if (lower.Contains("sync") || lower.Contains("offline")) tags.Add("#offline");
        if (lower.Contains("signalr") || lower.Contains("hub")) tags.Add("#realtime");
        if (lower.Contains("rabbit") || lower.Contains("queue") || lower.Contains("message")) tags.Add("#messaging");
        if (lower.Contains("hangfire") || lower.Contains("job") || lower.Contains("schedule")) tags.Add("#scheduling");

        return string.Join(" ", tags.Distinct());
    }

    private static string? DeriveTagFromFolder(string folder)
    {
        return folder switch
        {
            "auth" => "#auth",
            "login" => "#auth",
            "signup" => "#auth",
            "sos" => "#emergency",
            "emergency" => "#emergency",
            "evidence" => "#evidence",
            "sync" => "#offline",
            "accessibility" => "#accessibility",
            "permissions" => "#permissions",
            "home" => "#home",
            "profile" => "#profile",
            "history" => "#history",
            "volunteering" => "#volunteering",
            "settings" => "#settings",
            "navigation" => "#navigation",
            "api" => "#api",
            "services" => "#service",
            "models" => "#model",
            "screens" => "#ui",
            "views" => "#ui",
            "viewmodels" => "#viewmodel",
            "controllers" => "#controller",
            "middleware" => "#middleware",
            "hubs" => "#realtime",
            _ => null
        };
    }

    // ── Roslyn helpers ──────────────────────────────────────────────

    private static string? GetNamespace(SyntaxNode node)
    {
        var nsDecl = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return nsDecl?.Name.ToString();
    }

    private static string BuildTypeSignature(TypeDeclarationSyntax type)
    {
        var bases = type.BaseList?.Types.Select(t => t.ToString()) ?? Enumerable.Empty<string>();
        var baseStr = bases.Any() ? $" : {string.Join(", ", bases)}" : "";
        var memberCount = type.Members.Count;
        return $"{type.Keyword.Text} {type.Identifier.Text}{baseStr} ({memberCount} members)";
    }

    private static string ExtractDependencies(TypeDeclarationSyntax type)
    {
        var deps = new HashSet<string>();

        // Base types
        if (type.BaseList != null)
            foreach (var baseType in type.BaseList.Types)
                deps.Add(baseType.Type.ToString().Split('<')[0]); // Strip generics

        // Constructor parameter types (injected dependencies)
        foreach (var ctor in type.Members.OfType<ConstructorDeclarationSyntax>())
            foreach (var param in ctor.ParameterList.Parameters)
                if (param.Type != null)
                    deps.Add(param.Type.ToString().Split('<')[0]);

        // Primary constructor parameters (records, positional classes)
        if (type.ParameterList != null)
            foreach (var param in type.ParameterList.Parameters)
                if (param.Type != null)
                    deps.Add(param.Type.ToString().Split('<')[0]);

        // Filter out primitives and common framework types
        var skip = new HashSet<string>
        {
            "string", "int", "long", "bool", "double", "float", "decimal",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "CancellationToken",
            "Task", "ILogger", "IOptions", "IConfiguration", "object", "byte"
        };

        return string.Join("; ", deps.Where(d => !skip.Contains(d)).OrderBy(d => d));
    }

    private static string HashBody(string body)
    {
        var normalized = body.Replace("\r", "").Replace("\n", "").Replace(" ", "");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16]; // First 8 bytes = 16 hex chars
    }

    private static string GetRelativePath(string fullPath)
    {
        var cwd = Directory.GetCurrentDirectory();
        if (fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
            return fullPath[(cwd.Length + 1)..].Replace('\\', '/');
        return fullPath.Replace('\\', '/');
    }

    private static string GuessProjectName(string relPath)
    {
        var parts = relPath.Split('/');
        return parts.Length > 0 ? parts[0] : "unknown";
    }

    // ── CSV writer ──────────────────────────────────────────────────

    private static async Task WriteCsvAsync(string path, List<CodeIndexRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("repo,project,file,kind,name,signature,tags,depends_on,lines,body_hash");

        foreach (var row in rows.OrderBy(r => r.Project).ThenBy(r => r.File).ThenBy(r => r.Name))
        {
            sb.Append(CsvEscape(row.Repo)); sb.Append(',');
            sb.Append(CsvEscape(row.Project)); sb.Append(',');
            sb.Append(CsvEscape(row.File)); sb.Append(',');
            sb.Append(CsvEscape(row.Kind)); sb.Append(',');
            sb.Append(CsvEscape(row.Name)); sb.Append(',');
            sb.Append(CsvEscape(row.Signature)); sb.Append(',');
            sb.Append(CsvEscape(row.Tags)); sb.Append(',');
            sb.Append(CsvEscape(row.DependsOn)); sb.Append(',');
            sb.Append(row.Lines); sb.Append(',');
            sb.AppendLine(CsvEscape(row.BodyHash));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ── Row model ───────────────────────────────────────────────────

    private record CodeIndexRow
    {
        public string Repo { get; init; } = "";
        public string Project { get; init; } = "";
        public string File { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Name { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Tags { get; init; } = "";
        public string DependsOn { get; init; } = "";
        public int Lines { get; init; }
        public string BodyHash { get; init; } = "";
    }
}
