// =============================================================================
// CodeIndexDbCommand.cs — Index code symbols into SQL Server graph database
// =============================================================================
// Extends the existing CodeIndexCommand CSV workflow by persisting symbols,
// documents, reference edges, and tags directly into SQL Server graph tables
// via the CodeIntelligenceDbContext and SqlBulkCopy.
//
// For C# projects: uses Roslyn SemanticModel via LsifEmitter for precise edges.
// For other languages: falls back to MultiLanguageParser DependsOn field.
//
// Subcommands:
//   thewatch codeindex-db                                — Index into default SQL Server
//   thewatch codeindex-db --connection "Server=..."      — Use specific connection string
//   thewatch codeindex-db --solution TheWatch.slnx       — Index a specific solution
//   thewatch codeindex-db --external path/               — Also scan external directories
//   thewatch codeindex-db --reset                        — Drop and recreate graph tables
//
// Output: prints node counts, edge counts, and top connected symbols.
//
// Performance: SqlBulkCopy inserts 137K+ rows in seconds. Edge emission for C#
//   projects uses Roslyn SemanticModel (10-30 seconds for full solution).
//
// Example:
//   dotnet run --project TheWatch.Cli -- codeindex-db
//   dotnet run --project TheWatch.Cli -- codeindex-db --reset --solution TheWatch.slnx
//   dotnet run --project TheWatch.Cli -- codeindex-db --connection "Server=localhost;Database=TheWatch;Trusted_Connection=true;TrustServerCertificate=true"
//
// WAL: Uses SqlBulkCopy for bulk insert (faster than EF Core SaveChanges for 137K rows).
//      Graph table creation uses raw SQL migration (InitialCodeIntelligence.sql).
//      LsifEmitter uses Roslyn SemanticModel for precise cross-reference data.
// =============================================================================

using System.CommandLine;
using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TheWatch.Cli.Services;
using TheWatch.Data.CodeIntelligence;

namespace TheWatch.Cli.Commands;

public static class CodeIndexDbCommand
{
    public static Command Build()
    {
        var connectionOption = new Option<string>("--connection")
        {
            Description = "SQL Server connection string (default: from Aspire 'thewatch-sqlserver')",
            DefaultValueFactory = _ =>
                Environment.GetEnvironmentVariable("ConnectionStrings__thewatch-sqlserver")
                ?? "Server=localhost;Database=TheWatch;Trusted_Connection=true;TrustServerCertificate=true"
        };

        var solutionOption = new Option<string>("--solution")
        {
            Description = "Path to .slnx/.sln file",
            DefaultValueFactory = _ => "TheWatch.slnx"
        };

        var externalOption = new Option<string[]>("--external")
        {
            Description = "Scan files from external directories (can specify multiple)",
            AllowMultipleArgumentsPerToken = true
        };

        var resetOption = new Option<bool>("--reset")
        {
            Description = "Drop and recreate all graph tables before indexing",
            DefaultValueFactory = _ => false
        };

        var cmd = new Command("codeindex-db", "Index code symbols into SQL Server graph database")
        {
            connectionOption,
            solutionOption,
            externalOption,
            resetOption
        };

        cmd.SetAction(async (parseResult) =>
        {
            var connection = parseResult.GetValue(connectionOption)!;
            var solution = parseResult.GetValue(solutionOption)!;
            var externalDirs = parseResult.GetValue(externalOption) ?? Array.Empty<string>();
            var reset = parseResult.GetValue(resetOption);

            await RunAsync(connection, solution, externalDirs, reset);
        });

        return cmd;
    }

    private static async Task RunAsync(string connectionString, string solutionPath, string[] externalDirs, bool reset)
    {
        Console.WriteLine("[CODEINDEX-DB] SQL Server Graph Code Intelligence Indexer");
        Console.WriteLine($"[CODEINDEX-DB] Solution:   {solutionPath}");
        Console.WriteLine($"[CODEINDEX-DB] Connection: {MaskConnectionString(connectionString)}");

        // ── Step 1: Create/migrate the database schema ───────────────
        Console.WriteLine("[CODEINDEX-DB] Initializing database schema...");
        await InitializeDatabaseAsync(connectionString, reset);

        // Register MSBuild before using Roslyn workspaces
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();

        // ── Step 2: Scan all projects ────────────────────────────────
        var symbols = new List<SymbolRecord>();
        var documents = new List<DocumentNode>();
        var edges = new List<EmittedEdge>();
        var tagRecords = new List<(string SymbolFullName, string Tag)>();

        // Try Roslyn workspace for .sln, fall back to file-system for .slnx
        var usedWorkspace = false;
        if (File.Exists(solutionPath) && solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var workspace = MSBuildWorkspace.Create();
                workspace.WorkspaceFailed += (_, e) =>
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                        Console.Error.WriteLine($"[CODEINDEX-DB] Workspace: {e.Diagnostic.Message}");
                };

                Console.WriteLine("[CODEINDEX-DB] Loading solution via Roslyn...");
                var solution = await workspace.OpenSolutionAsync(Path.GetFullPath(solutionPath));
                Console.WriteLine($"[CODEINDEX-DB] Found {solution.Projects.Count()} projects");

                foreach (var project in solution.Projects)
                {
                    if (project.FilePath?.Contains("/obj/") == true || project.FilePath?.Contains("\\obj\\") == true)
                        continue;

                    Console.Write($"  {project.Name}... ");
                    var (projSymbols, projDocs) = await ScanRoslynProjectAsync(project);
                    symbols.AddRange(projSymbols);
                    documents.AddRange(projDocs);

                    // Tags
                    foreach (var sym in projSymbols)
                    {
                        foreach (var tag in sym.Tags)
                            tagRecords.Add((sym.FullName, tag));
                    }

                    Console.WriteLine($"{projSymbols.Count} symbols, {projDocs.Count} docs");

                    // ── Emit Roslyn-precise edges ─────────────────────
                    try
                    {
                        var compilation = await project.GetCompilationAsync();
                        if (compilation != null)
                        {
                            var emitter = new LsifEmitter();
                            var projEdges = await emitter.EmitEdgesAsync(compilation, GetRelativePath);
                            edges.AddRange(projEdges);
                            Console.WriteLine($"    → {projEdges.Count} edges ({emitter.SkippedNullSymbol} unresolved)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"    → Edge emission failed: {ex.Message}");
                    }
                }
                usedWorkspace = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CODEINDEX-DB] Roslyn workspace failed: {ex.Message}");
            }
        }

        if (!usedWorkspace)
        {
            // File-system scan
            Console.WriteLine("[CODEINDEX-DB] Scanning from file system...");
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
                var (dirSymbols, dirDocs, dirEdges) = await ScanDirectoryAsync(dir, dirName);
                symbols.AddRange(dirSymbols);
                documents.AddRange(dirDocs);
                edges.AddRange(dirEdges);

                foreach (var sym in dirSymbols)
                    foreach (var tag in sym.Tags)
                        tagRecords.Add((sym.FullName, tag));

                Console.WriteLine($"{dirSymbols.Count} symbols");
            }
        }

        // ── Scan external directories ────────────────────────────────
        foreach (var externalDir in externalDirs)
        {
            if (string.IsNullOrEmpty(externalDir) || !Directory.Exists(externalDir)) continue;
            Console.WriteLine($"[CODEINDEX-DB] Scanning external: {externalDir}");
            var extName = Path.GetFileName(Path.GetFullPath(externalDir));
            var (extSymbols, extDocs, extEdges) = await ScanDirectoryAsync(externalDir, extName);
            symbols.AddRange(extSymbols);
            documents.AddRange(extDocs);
            edges.AddRange(extEdges);

            foreach (var sym in extSymbols)
                foreach (var tag in sym.Tags)
                    tagRecords.Add((sym.FullName, tag));
        }

        // ── Also scan Kotlin and Swift directories ───────────────────
        ScanMobileDirectory("TheWatch-Android", "android", "kotlin", symbols, documents, edges, tagRecords);
        ScanMobileDirectory("TheWatch-iOS", "ios", "swift", symbols, documents, edges, tagRecords);

        // ── Step 3: Bulk insert into SQL Server ──────────────────────
        Console.WriteLine();
        Console.WriteLine($"[CODEINDEX-DB] Total: {symbols.Count} symbols, {documents.Count} documents, {edges.Count} edges, {tagRecords.Count} tags");
        Console.WriteLine("[CODEINDEX-DB] Bulk inserting into SQL Server...");

        var (symbolLookup, insertedSymbols, insertedDocs) = await BulkInsertNodesAsync(connectionString, symbols, documents);
        var insertedEdges = await BulkInsertEdgesAsync(connectionString, edges, symbolLookup);
        var insertedTags = await BulkInsertTagsAsync(connectionString, tagRecords, symbolLookup);

        // ── Step 7: Print summary ────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("[CODEINDEX-DB] ══════════════════════════════════════════");
        Console.WriteLine($"[CODEINDEX-DB] Symbols inserted:   {insertedSymbols:N0}");
        Console.WriteLine($"[CODEINDEX-DB] Documents inserted: {insertedDocs:N0}");
        Console.WriteLine($"[CODEINDEX-DB] Edges inserted:     {insertedEdges:N0}");
        Console.WriteLine($"[CODEINDEX-DB] Tags inserted:      {insertedTags:N0}");

        // Top connected symbols
        await PrintTopConnectedAsync(connectionString);

        Console.WriteLine("[CODEINDEX-DB] Done.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Database initialization
    // ═══════════════════════════════════════════════════════════════════

    private static async Task InitializeDatabaseAsync(string connectionString, bool reset)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        if (reset)
        {
            Console.WriteLine("[CODEINDEX-DB] Dropping existing graph tables...");
            var dropSql = """
                IF OBJECT_ID('dbo.tags', 'U') IS NOT NULL DROP TABLE [dbo].[tags];
                IF OBJECT_ID('dbo.references', 'U') IS NOT NULL DROP TABLE [dbo].[references];
                IF OBJECT_ID('dbo.documents', 'U') IS NOT NULL DROP TABLE [dbo].[documents];
                IF OBJECT_ID('dbo.symbols', 'U') IS NOT NULL DROP TABLE [dbo].[symbols];
                """;
            await using var dropCmd = new SqlCommand(dropSql, conn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        // Execute the migration SQL
        var migrationSql = LoadMigrationSql();

        // SQL Server requires splitting on GO statements for batch execution
        var batches = migrationSql.Split(new[] { "\nGO\n", "\nGO\r\n", "\nGO" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            try
            {
                await using var sqlCmd = new SqlCommand(batch, conn);
                sqlCmd.CommandTimeout = 60;
                await sqlCmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (ex.Number == 2714) // Object already exists
            {
                // Idempotent — ignore "already exists" errors
            }
        }

        Console.WriteLine("[CODEINDEX-DB] Schema initialized.");
    }

    private static string LoadMigrationSql()
    {
        // Try loading from the embedded file path relative to the CLI project
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TheWatch.Data", "CodeIntelligence", "Migrations", "InitialCodeIntelligence.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "TheWatch.Data", "CodeIntelligence", "Migrations", "InitialCodeIntelligence.sql"),
            Path.Combine(AppContext.BaseDirectory, "InitialCodeIntelligence.sql"),
        };

        foreach (var path in candidatePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                Console.WriteLine($"[CODEINDEX-DB] Loading migration from: {fullPath}");
                return File.ReadAllText(fullPath);
            }
        }

        // Fallback: inline minimal schema creation
        Console.WriteLine("[CODEINDEX-DB] Migration SQL file not found, using inline schema.");
        return GetInlineMigrationSql();
    }

    private static string GetInlineMigrationSql()
    {
        return """
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'symbols' AND is_node = 1)
            BEGIN
                CREATE TABLE [dbo].[symbols] (
                    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
                    [Repo] NVARCHAR(256) NOT NULL, [Project] NVARCHAR(256) NOT NULL,
                    [File] NVARCHAR(1024) NOT NULL, [Kind] NVARCHAR(64) NOT NULL,
                    [Language] NVARCHAR(64) NOT NULL, [FullName] NVARCHAR(1024) NOT NULL,
                    [Signature] NVARCHAR(2048) NOT NULL, [Lines] INT NOT NULL DEFAULT 0,
                    [BodyHash] NVARCHAR(64) NULL, [IndexedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT [PK_symbols] PRIMARY KEY ([Id])
                ) AS NODE;
            END

            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'documents' AND is_node = 1)
            BEGIN
                CREATE TABLE [dbo].[documents] (
                    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
                    [Repo] NVARCHAR(256) NOT NULL, [Project] NVARCHAR(256) NOT NULL,
                    [FilePath] NVARCHAR(1024) NOT NULL, [Language] NVARCHAR(64) NOT NULL,
                    [Lines] INT NOT NULL DEFAULT 0, [IndexedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT [PK_documents] PRIMARY KEY ([Id])
                ) AS NODE;
            END

            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'references' AND is_edge = 1)
            BEGIN
                CREATE TABLE [dbo].[references] (
                    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
                    [SourceId] UNIQUEIDENTIFIER NOT NULL, [TargetId] UNIQUEIDENTIFIER NOT NULL,
                    [Kind] NVARCHAR(64) NOT NULL, [SourceFile] NVARCHAR(1024) NULL,
                    [SourceLine] INT NULL,
                    CONSTRAINT [PK_references] PRIMARY KEY ([Id])
                ) AS EDGE;
            END

            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tags' AND is_edge = 1)
            BEGIN
                CREATE TABLE [dbo].[tags] (
                    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
                    [SymbolId] UNIQUEIDENTIFIER NOT NULL, [Tag] NVARCHAR(128) NOT NULL,
                    CONSTRAINT [PK_tags] PRIMARY KEY ([Id])
                ) AS EDGE;
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_FullName')
                CREATE NONCLUSTERED INDEX [IX_symbols_FullName] ON [dbo].[symbols] ([FullName]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Repo')
                CREATE NONCLUSTERED INDEX [IX_symbols_Repo] ON [dbo].[symbols] ([Repo]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Kind')
                CREATE NONCLUSTERED INDEX [IX_symbols_Kind] ON [dbo].[symbols] ([Kind]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Language')
                CREATE NONCLUSTERED INDEX [IX_symbols_Language] ON [dbo].[symbols] ([Language]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_references_SourceId')
                CREATE NONCLUSTERED INDEX [IX_references_SourceId] ON [dbo].[references] ([SourceId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_references_TargetId')
                CREATE NONCLUSTERED INDEX [IX_references_TargetId] ON [dbo].[references] ([TargetId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tags_Tag')
                CREATE NONCLUSTERED INDEX [IX_tags_Tag] ON [dbo].[tags] ([Tag]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tags_SymbolId')
                CREATE NONCLUSTERED INDEX [IX_tags_SymbolId] ON [dbo].[tags] ([SymbolId]);
            """;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Scanning — Roslyn projects
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<(List<SymbolRecord> Symbols, List<DocumentNode> Documents)> ScanRoslynProjectAsync(Project project)
    {
        var symbols = new List<SymbolRecord>();
        var documents = new List<DocumentNode>();
        var repoName = "TheWatch";
        var projectName = project.Name;

        foreach (var doc in project.Documents)
        {
            if (doc.FilePath == null) continue;
            var relPath = GetRelativePath(doc.FilePath);
            if (relPath.Contains("/obj/") || relPath.Contains("\\obj\\") ||
                relPath.Contains("/bin/") || relPath.Contains("\\bin\\")) continue;

            var tree = await doc.GetSyntaxTreeAsync();
            if (tree == null) continue;
            var root = await tree.GetRootAsync();
            var lineCount = tree.GetText().Lines.Count;

            // Document node
            documents.Add(new DocumentNode
            {
                Id = Guid.NewGuid(),
                Repo = repoName,
                Project = projectName,
                FilePath = relPath,
                Language = "csharp",
                Lines = lineCount,
                IndexedAt = DateTime.UtcNow
            });

            // Type declarations
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
                var fullName = ns != null ? $"{ns}.{name}" : name;
                var tags = DeriveTags(relPath, projectName, kind, name, ns);
                var lineSpan = typeDecl.GetLocation().GetLineSpan();
                var lines = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

                symbols.Add(new SymbolRecord
                {
                    Id = Guid.NewGuid(),
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = kind,
                    Language = "csharp",
                    FullName = fullName,
                    Signature = BuildTypeSignature(typeDecl),
                    Lines = lines,
                    BodyHash = HashBody(typeDecl.ToFullString()),
                    Tags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    DependsOn = ExtractDependencies(typeDecl)
                });
            }

            // Enum declarations
            foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                var name = enumDecl.Identifier.Text;
                var ns = GetNamespace(enumDecl);
                var fullName = ns != null ? $"{ns}.{name}" : name;
                var tags = DeriveTags(relPath, projectName, "enum", name, ns);
                var lineSpan = enumDecl.GetLocation().GetLineSpan();
                var lines = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

                symbols.Add(new SymbolRecord
                {
                    Id = Guid.NewGuid(),
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = "enum",
                    Language = "csharp",
                    FullName = fullName,
                    Signature = $"enum {name} ({enumDecl.Members.Count} members)",
                    Lines = lines,
                    BodyHash = HashBody(enumDecl.ToFullString()),
                    Tags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    DependsOn = ""
                });
            }
        }

        return (symbols, documents);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Scanning — File system (fallback for .slnx and non-C# files)
    // ═══════════════════════════════════════════════════════════════════

    private static readonly string[] MultiLangExtensions = [
        ".kt", ".kts", ".swift", ".py", ".pyi",
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp",
        ".java", ".rs", ".go", ".rb", ".dart",
        ".sql", ".proto", ".tf", ".bicep",
        ".ps1", ".psm1", ".sh", ".bash",
        ".razor", ".xaml", ".css", ".scss", ".html"
    ];

    private static async Task<(List<SymbolRecord> Symbols, List<DocumentNode> Documents, List<EmittedEdge> Edges)>
        ScanDirectoryAsync(string dir, string repoName)
    {
        var symbols = new List<SymbolRecord>();
        var documents = new List<DocumentNode>();
        var edges = new List<EmittedEdge>();

        // ── C# files via Roslyn ──────────────────────────────────────
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
            var lineCount = tree.GetText().Lines.Count;

            documents.Add(new DocumentNode
            {
                Id = Guid.NewGuid(),
                Repo = repoName,
                Project = projectName,
                FilePath = relPath,
                Language = "csharp",
                Lines = lineCount,
                IndexedAt = DateTime.UtcNow
            });

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
                var fullName = ns != null ? $"{ns}.{name}" : name;
                var tags = DeriveTags(relPath, projectName, kind, name, ns);

                symbols.Add(new SymbolRecord
                {
                    Id = Guid.NewGuid(),
                    Repo = repoName,
                    Project = projectName,
                    File = relPath,
                    Kind = kind,
                    Language = "csharp",
                    FullName = fullName,
                    Signature = BuildTypeSignature(typeDecl),
                    Lines = typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line
                          - typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    BodyHash = HashBody(typeDecl.ToFullString()),
                    Tags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    DependsOn = ExtractDependencies(typeDecl)
                });
            }
        }

        // ── Non-C# files via MultiLanguageParser ─────────────────────
        var multiLangFiles = MultiLangExtensions
            .SelectMany(ext =>
            {
                try { return Directory.GetFiles(dir, $"*{ext}", SearchOption.AllDirectories); }
                catch { return Array.Empty<string>(); }
            })
            .Where(f => !f.Contains("/obj/") && !f.Contains("\\obj\\")
                     && !f.Contains("/bin/") && !f.Contains("\\bin\\")
                     && !f.Contains("/node_modules/") && !f.Contains("\\node_modules\\")
                     && !f.Contains("/.gradle/") && !f.Contains("\\.gradle\\"))
            .Distinct();

        foreach (var file in multiLangFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var relPath = GetRelativePath(file);
                var projectName = GuessProjectName(relPath);
                var language = MultiLanguageParser.GetLanguage(file) ?? "unknown";
                var parsed = MultiLanguageParser.TryParse(file, content);
                var lineCount = content.Split('\n').Length;

                documents.Add(new DocumentNode
                {
                    Id = Guid.NewGuid(),
                    Repo = repoName,
                    Project = projectName,
                    FilePath = relPath,
                    Language = language,
                    Lines = lineCount,
                    IndexedAt = DateTime.UtcNow
                });

                foreach (var sym in parsed)
                {
                    var fullName = sym.Namespace != null ? $"{sym.Namespace}.{sym.Name}" : sym.Name;
                    var tags = DeriveTags(relPath, projectName, sym.Kind, sym.Name, sym.Namespace);
                    tags += $" #{language}";
                    tags = tags.Trim();

                    symbols.Add(new SymbolRecord
                    {
                        Id = Guid.NewGuid(),
                        Repo = repoName,
                        Project = projectName,
                        File = relPath,
                        Kind = sym.Kind,
                        Language = language,
                        FullName = fullName,
                        Signature = sym.Signature,
                        Lines = sym.EndLine - sym.StartLine + 1,
                        BodyHash = "",
                        Tags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                        DependsOn = sym.DependsOn
                    });

                    // Text-based edges from DependsOn field
                    var depEdges = LsifEmitter.EmitFromDependsOn(fullName, sym.DependsOn, relPath, sym.StartLine);
                    edges.AddRange(depEdges);
                }
            }
            catch
            {
                // Skip files that can't be read/parsed
            }
        }

        return (symbols, documents, edges);
    }

    private static void ScanMobileDirectory(
        string dirName, string platform, string language,
        List<SymbolRecord> symbols, List<DocumentNode> documents,
        List<EmittedEdge> edges, List<(string, string)> tagRecords)
    {
        if (!Directory.Exists(dirName)) return;
        Console.Write($"  {dirName} ({platform})... ");

        var ext = language == "kotlin" ? "*.kt" : "*.swift";
        string[] files;
        try { files = Directory.GetFiles(dirName, ext, SearchOption.AllDirectories); }
        catch { Console.WriteLine("0 symbols"); return; }

        var count = 0;
        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relPath = GetRelativePath(file);
                var projectName = dirName;
                var parsed = MultiLanguageParser.TryParse(file, content);
                var lineCount = content.Split('\n').Length;

                documents.Add(new DocumentNode
                {
                    Id = Guid.NewGuid(),
                    Repo = "TheWatch",
                    Project = projectName,
                    FilePath = relPath,
                    Language = language,
                    Lines = lineCount,
                    IndexedAt = DateTime.UtcNow
                });

                foreach (var sym in parsed)
                {
                    var fullName = sym.Namespace != null ? $"{sym.Namespace}.{sym.Name}" : sym.Name;
                    var tags = DeriveTags(relPath, projectName, sym.Kind, sym.Name, sym.Namespace);
                    tags += $" #{language} #{platform}";
                    tags = tags.Trim();
                    var tagList = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

                    symbols.Add(new SymbolRecord
                    {
                        Id = Guid.NewGuid(),
                        Repo = "TheWatch",
                        Project = projectName,
                        File = relPath,
                        Kind = sym.Kind,
                        Language = language,
                        FullName = fullName,
                        Signature = sym.Signature,
                        Lines = sym.EndLine - sym.StartLine + 1,
                        BodyHash = "",
                        Tags = tagList,
                        DependsOn = sym.DependsOn
                    });

                    foreach (var tag in tagList)
                        tagRecords.Add((fullName, tag));

                    var depEdges = LsifEmitter.EmitFromDependsOn(fullName, sym.DependsOn, relPath, sym.StartLine);
                    edges.AddRange(depEdges);
                    count++;
                }
            }
            catch { /* skip unreadable files */ }
        }

        Console.WriteLine($"{count} symbols");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Bulk insert via SqlBulkCopy
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<(Dictionary<string, Guid> SymbolLookup, int SymbolCount, int DocCount)>
        BulkInsertNodesAsync(string connectionString, List<SymbolRecord> symbols, List<DocumentNode> documents)
    {
        var symbolLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        // ── Clear existing data (for re-index) ──────────────────────
        // Use MERGE or just truncate-and-reload for simplicity
        await using (var truncCmd = new SqlCommand(
            "DELETE FROM [dbo].[tags]; DELETE FROM [dbo].[references]; DELETE FROM [dbo].[symbols]; DELETE FROM [dbo].[documents];", conn))
        {
            truncCmd.CommandTimeout = 120;
            await truncCmd.ExecuteNonQueryAsync();
        }

        // ── Bulk insert symbols ──────────────────────────────────────
        var symbolTable = new DataTable();
        symbolTable.Columns.Add("Id", typeof(Guid));
        symbolTable.Columns.Add("Repo", typeof(string));
        symbolTable.Columns.Add("Project", typeof(string));
        symbolTable.Columns.Add("File", typeof(string));
        symbolTable.Columns.Add("Kind", typeof(string));
        symbolTable.Columns.Add("Language", typeof(string));
        symbolTable.Columns.Add("FullName", typeof(string));
        symbolTable.Columns.Add("Signature", typeof(string));
        symbolTable.Columns.Add("Lines", typeof(int));
        symbolTable.Columns.Add("BodyHash", typeof(string));
        symbolTable.Columns.Add("IndexedAt", typeof(DateTime));

        // Deduplicate symbols by (Repo, File, FullName)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            var key = $"{sym.Repo}|{sym.File}|{sym.FullName}";
            if (!seen.Add(key)) continue;

            symbolLookup[sym.FullName] = sym.Id;
            var row = symbolTable.NewRow();
            row["Id"] = sym.Id;
            row["Repo"] = Truncate(sym.Repo, 256);
            row["Project"] = Truncate(sym.Project, 256);
            row["File"] = Truncate(sym.File, 1024);
            row["Kind"] = Truncate(sym.Kind, 64);
            row["Language"] = Truncate(sym.Language, 64);
            row["FullName"] = Truncate(sym.FullName, 1024);
            row["Signature"] = Truncate(sym.Signature, 2048);
            row["Lines"] = sym.Lines;
            row["BodyHash"] = string.IsNullOrEmpty(sym.BodyHash) ? (object)DBNull.Value : Truncate(sym.BodyHash, 64);
            row["IndexedAt"] = DateTime.UtcNow;
            symbolTable.Rows.Add(row);
        }

        using (var bulkCopy = new SqlBulkCopy(conn))
        {
            bulkCopy.DestinationTableName = "symbols";
            bulkCopy.BatchSize = 10000;
            bulkCopy.BulkCopyTimeout = 120;
            bulkCopy.ColumnMappings.Add("Id", "Id");
            bulkCopy.ColumnMappings.Add("Repo", "Repo");
            bulkCopy.ColumnMappings.Add("Project", "Project");
            bulkCopy.ColumnMappings.Add("File", "File");
            bulkCopy.ColumnMappings.Add("Kind", "Kind");
            bulkCopy.ColumnMappings.Add("Language", "Language");
            bulkCopy.ColumnMappings.Add("FullName", "FullName");
            bulkCopy.ColumnMappings.Add("Signature", "Signature");
            bulkCopy.ColumnMappings.Add("Lines", "Lines");
            bulkCopy.ColumnMappings.Add("BodyHash", "BodyHash");
            bulkCopy.ColumnMappings.Add("IndexedAt", "IndexedAt");
            await bulkCopy.WriteToServerAsync(symbolTable);
        }

        Console.WriteLine($"  Symbols: {symbolTable.Rows.Count:N0} inserted");

        // ── Bulk insert documents ────────────────────────────────────
        var docTable = new DataTable();
        docTable.Columns.Add("Id", typeof(Guid));
        docTable.Columns.Add("Repo", typeof(string));
        docTable.Columns.Add("Project", typeof(string));
        docTable.Columns.Add("FilePath", typeof(string));
        docTable.Columns.Add("Language", typeof(string));
        docTable.Columns.Add("Lines", typeof(int));
        docTable.Columns.Add("IndexedAt", typeof(DateTime));

        var seenDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in documents)
        {
            var key = $"{doc.Repo}|{doc.FilePath}";
            if (!seenDocs.Add(key)) continue;

            var row = docTable.NewRow();
            row["Id"] = doc.Id;
            row["Repo"] = Truncate(doc.Repo, 256);
            row["Project"] = Truncate(doc.Project, 256);
            row["FilePath"] = Truncate(doc.FilePath, 1024);
            row["Language"] = Truncate(doc.Language, 64);
            row["Lines"] = doc.Lines;
            row["IndexedAt"] = DateTime.UtcNow;
            docTable.Rows.Add(row);
        }

        using (var bulkCopy = new SqlBulkCopy(conn))
        {
            bulkCopy.DestinationTableName = "documents";
            bulkCopy.BatchSize = 10000;
            bulkCopy.BulkCopyTimeout = 120;
            bulkCopy.ColumnMappings.Add("Id", "Id");
            bulkCopy.ColumnMappings.Add("Repo", "Repo");
            bulkCopy.ColumnMappings.Add("Project", "Project");
            bulkCopy.ColumnMappings.Add("FilePath", "FilePath");
            bulkCopy.ColumnMappings.Add("Language", "Language");
            bulkCopy.ColumnMappings.Add("Lines", "Lines");
            bulkCopy.ColumnMappings.Add("IndexedAt", "IndexedAt");
            await bulkCopy.WriteToServerAsync(docTable);
        }

        Console.WriteLine($"  Documents: {docTable.Rows.Count:N0} inserted");

        return (symbolLookup, symbolTable.Rows.Count, docTable.Rows.Count);
    }

    private static async Task<int> BulkInsertEdgesAsync(
        string connectionString, List<EmittedEdge> edges, Dictionary<string, Guid> symbolLookup)
    {
        var edgeTable = new DataTable();
        edgeTable.Columns.Add("Id", typeof(Guid));
        edgeTable.Columns.Add("SourceId", typeof(Guid));
        edgeTable.Columns.Add("TargetId", typeof(Guid));
        edgeTable.Columns.Add("Kind", typeof(string));
        edgeTable.Columns.Add("SourceFile", typeof(string));
        edgeTable.Columns.Add("SourceLine", typeof(int));

        var resolved = 0;
        var unresolved = 0;

        foreach (var edge in edges)
        {
            // Resolve source and target to symbol IDs
            // Try exact match first, then suffix match
            if (!TryResolveSymbol(edge.SourceFullName, symbolLookup, out var sourceId) ||
                !TryResolveSymbol(edge.TargetFullName, symbolLookup, out var targetId))
            {
                unresolved++;
                continue;
            }

            var row = edgeTable.NewRow();
            row["Id"] = Guid.NewGuid();
            row["SourceId"] = sourceId;
            row["TargetId"] = targetId;
            row["Kind"] = edge.Kind.ToString();
            row["SourceFile"] = string.IsNullOrEmpty(edge.SourceFile) ? (object)DBNull.Value : Truncate(edge.SourceFile, 1024);
            row["SourceLine"] = edge.SourceLine;
            edgeTable.Rows.Add(row);
            resolved++;
        }

        if (edgeTable.Rows.Count > 0)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            using var bulkCopy = new SqlBulkCopy(conn);
            bulkCopy.DestinationTableName = "references";
            bulkCopy.BatchSize = 10000;
            bulkCopy.BulkCopyTimeout = 120;
            bulkCopy.ColumnMappings.Add("Id", "Id");
            bulkCopy.ColumnMappings.Add("SourceId", "SourceId");
            bulkCopy.ColumnMappings.Add("TargetId", "TargetId");
            bulkCopy.ColumnMappings.Add("Kind", "Kind");
            bulkCopy.ColumnMappings.Add("SourceFile", "SourceFile");
            bulkCopy.ColumnMappings.Add("SourceLine", "SourceLine");
            await bulkCopy.WriteToServerAsync(edgeTable);
        }

        Console.WriteLine($"  Edges: {resolved:N0} inserted, {unresolved:N0} unresolved");
        return resolved;
    }

    private static async Task<int> BulkInsertTagsAsync(
        string connectionString, List<(string SymbolFullName, string Tag)> tagRecords,
        Dictionary<string, Guid> symbolLookup)
    {
        var tagTable = new DataTable();
        tagTable.Columns.Add("Id", typeof(Guid));
        tagTable.Columns.Add("SymbolId", typeof(Guid));
        tagTable.Columns.Add("Tag", typeof(string));

        foreach (var (fullName, tag) in tagRecords)
        {
            if (!TryResolveSymbol(fullName, symbolLookup, out var symbolId)) continue;

            var row = tagTable.NewRow();
            row["Id"] = Guid.NewGuid();
            row["SymbolId"] = symbolId;
            row["Tag"] = Truncate(tag, 128);
            tagTable.Rows.Add(row);
        }

        if (tagTable.Rows.Count > 0)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            using var bulkCopy = new SqlBulkCopy(conn);
            bulkCopy.DestinationTableName = "tags";
            bulkCopy.BatchSize = 10000;
            bulkCopy.BulkCopyTimeout = 120;
            bulkCopy.ColumnMappings.Add("Id", "Id");
            bulkCopy.ColumnMappings.Add("SymbolId", "SymbolId");
            bulkCopy.ColumnMappings.Add("Tag", "Tag");
            await bulkCopy.WriteToServerAsync(tagTable);
        }

        Console.WriteLine($"  Tags: {tagTable.Rows.Count:N0} inserted");
        return tagTable.Rows.Count;
    }

    private static bool TryResolveSymbol(string name, Dictionary<string, Guid> lookup, out Guid id)
    {
        // Exact match
        if (lookup.TryGetValue(name, out id)) return true;

        // Try without global:: prefix (Roslyn emits "global::Namespace.Type")
        var stripped = name.StartsWith("global::") ? name[8..] : name;
        if (lookup.TryGetValue(stripped, out id)) return true;

        // Suffix match: find any key ending with ".{name}" or matching just the simple name
        var simpleName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        foreach (var kv in lookup)
        {
            if (kv.Key.EndsWith($".{simpleName}", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals(simpleName, StringComparison.OrdinalIgnoreCase))
            {
                id = kv.Value;
                return true;
            }
        }

        id = Guid.Empty;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Summary output
    // ═══════════════════════════════════════════════════════════════════

    private static async Task PrintTopConnectedAsync(string connectionString)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            var sql = """
                SELECT TOP 15 s.FullName, s.Kind, s.Repo,
                    (SELECT COUNT(*) FROM [references] r WHERE r.SourceId = s.Id) +
                    (SELECT COUNT(*) FROM [references] r WHERE r.TargetId = s.Id) AS EdgeCount
                FROM symbols s
                ORDER BY EdgeCount DESC
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            await using var reader = await cmd.ExecuteReaderAsync();

            Console.WriteLine();
            Console.WriteLine("[CODEINDEX-DB] Top 15 most-connected symbols:");
            Console.WriteLine($"  {"FullName",-60} {"Kind",-12} {"Repo",-16} {"Edges",6}");
            Console.WriteLine($"  {new string('-', 60)} {new string('-', 12)} {new string('-', 16)} {new string('-', 6)}");

            while (await reader.ReadAsync())
            {
                var fullName = reader.GetString(0);
                var kind = reader.GetString(1);
                var repo = reader.GetString(2);
                var edgeCount = reader.GetInt32(3);
                if (edgeCount == 0) break;

                // Truncate long names for display
                if (fullName.Length > 58) fullName = fullName[..55] + "...";
                Console.WriteLine($"  {fullName,-60} {kind,-12} {repo,-16} {edgeCount,6}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CODEINDEX-DB] Could not query top connected: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers (mirrored from CodeIndexCommand for independence)
    // ═══════════════════════════════════════════════════════════════════

    private static string GetRelativePath(string fullPath)
    {
        var basePath = Directory.GetCurrentDirectory();
        return Path.GetRelativePath(basePath, fullPath).Replace('\\', '/');
    }

    private static string GuessProjectName(string relPath)
    {
        var parts = relPath.Split('/');
        if (parts.Length > 0 && parts[0].StartsWith("TheWatch"))
            return parts[0];
        return "Unknown";
    }

    private static string? GetNamespace(SyntaxNode node)
    {
        // Walk up to find namespace declaration
        var current = node.Parent;
        while (current != null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            current = current.Parent;
        }
        return null;
    }

    private static string BuildTypeSignature(TypeDeclarationSyntax typeDecl)
    {
        var sb = new StringBuilder();
        var keyword = typeDecl switch
        {
            InterfaceDeclarationSyntax => "interface",
            ClassDeclarationSyntax c when c.Modifiers.Any(SyntaxKind.AbstractKeyword) => "abstract class",
            ClassDeclarationSyntax c when c.Modifiers.Any(SyntaxKind.StaticKeyword) => "static class",
            ClassDeclarationSyntax => "class",
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
            StructDeclarationSyntax => "struct",
            _ => "type"
        };

        sb.Append(keyword);
        sb.Append(' ');
        sb.Append(typeDecl.Identifier.Text);

        if (typeDecl.TypeParameterList != null)
            sb.Append(typeDecl.TypeParameterList.ToString());

        if (typeDecl.BaseList != null)
            sb.Append($" : {typeDecl.BaseList.Types}");

        return sb.ToString();
    }

    private static string ExtractDependencies(TypeDeclarationSyntax typeDecl)
    {
        var deps = new List<string>();

        if (typeDecl.BaseList != null)
        {
            foreach (var baseType in typeDecl.BaseList.Types)
                deps.Add(baseType.Type.ToString());
        }

        return string.Join(";", deps);
    }

    private static string HashBody(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexStringLower(bytes)[..16]; // First 16 hex chars
    }

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
        if (projLower.Contains("shared")) tags.Add("#shared");
        if (projLower.Contains("data")) tags.Add("#data");
        if (projLower.Contains("cli")) tags.Add("#cli");
        if (projLower.Contains("dashboard")) tags.Add("#dashboard");
        if (projLower.Contains("worker")) tags.Add("#worker");
        if (projLower.Contains("functions")) tags.Add("#functions");

        // Namespace tags
        if (ns != null)
        {
            var nsLower = ns.ToLowerInvariant();
            if (nsLower.Contains("auth")) tags.Add("#auth");
            if (nsLower.Contains("audit")) tags.Add("#audit");
            if (nsLower.Contains("response") || nsLower.Contains("dispatch")) tags.Add("#emergency");
            if (nsLower.Contains("spatial") || nsLower.Contains("geo")) tags.Add("#spatial");
            if (nsLower.Contains("swarm") || nsLower.Contains("agent")) tags.Add("#swarm");
        }

        // Name-derived tags (simplified set for DB indexing)
        var lower = name.ToLowerInvariant();
        if (lower.Contains("emergency") || lower.Contains("sos") || lower.Contains("dispatch")) tags.Add("#emergency");
        if (lower.Contains("auth") || lower.Contains("login")) tags.Add("#auth");
        if (lower.Contains("audit")) tags.Add("#audit");
        if (lower.Contains("volunteer") || lower.Contains("responder")) tags.Add("#volunteering");
        if (lower.Contains("guard") || lower.Contains("report")) tags.Add("#guardreport");
        if (lower.Contains("swarm") || lower.Contains("agent")) tags.Add("#swarm");
        if (lower.Contains("cctv") || lower.Contains("sensor") || lower.Contains("camera")) tags.Add("#sensor");
        if (lower.Contains("graph") || lower.Contains("traversal")) tags.Add("#graph");
        if (lower.Contains("cache") || lower.Contains("redis")) tags.Add("#cache");
        if (lower.Contains("queue") || lower.Contains("rabbitmq") || lower.Contains("kafka")) tags.Add("#queue");

        return string.Join(" ", tags.OrderBy(t => t));
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string MaskConnectionString(string cs)
    {
        // Mask password in connection string for display
        if (cs.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            return System.Text.RegularExpressions.Regex.Replace(cs, @"Password=[^;]+", "Password=***");
        return cs;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Internal record for in-memory symbol accumulation
    // ═══════════════════════════════════════════════════════════════════

    private record SymbolRecord
    {
        public Guid Id { get; init; }
        public string Repo { get; init; } = "";
        public string Project { get; init; } = "";
        public string File { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Language { get; init; } = "";
        public string FullName { get; init; } = "";
        public string Signature { get; init; } = "";
        public int Lines { get; init; }
        public string BodyHash { get; init; } = "";
        public List<string> Tags { get; init; } = [];
        public string DependsOn { get; init; } = "";
    }
}
