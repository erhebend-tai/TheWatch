// =============================================================================
// ArchitectureManifest — Query facade for TheWatchArchitecture.xml
// =============================================================================
// Loads the architecture manifest (from embedded resource or file path) and
// provides strongly-typed query methods for entities, projects, jobs, services,
// capabilities, and compliance frameworks.
//
// All queries gracefully handle missing XML elements — they return empty
// collections rather than throwing.
//
// Usage:
//   // From embedded resource (default):
//   var manifest = ArchitectureManifest.LoadFromEmbeddedResource();
//
//   // From a file on disk:
//   var manifest = ArchitectureManifest.LoadFromFile("path/to/TheWatchArchitecture.xml");
//
//   // Query:
//   var entities = manifest.GetAllEntities();       // → [("Incident", "Full lifecycle..."), ...]
//   var stubs = manifest.GetProjectsByStatus("stub"); // → projects with csFiles=1
//   var domainProjects = manifest.GetProjectsByLayer("Domain");
//
// Write-ahead log:
//   - v1.0.0: Initial — Load, GetAllEntities, GetAllProjects, GetAllJobs,
//     GetAllServices, GetAllCapabilities, GetAllComplianceFrameworks,
//     GetProjectsByStatus, GetProjectsByLayer (2026-03-24)
// =============================================================================

using System.Reflection;
using System.Xml.Linq;

namespace TheWatch.XML;

/// <summary>
/// Represents a named item with a description extracted from the architecture XML.
/// </summary>
/// <param name="Name">The element name attribute.</param>
/// <param name="Description">The element description attribute, or empty string if absent.</param>
public record ManifestItem(string Name, string Description);

/// <summary>
/// Represents a project entry from the architecture XML with status and layer metadata.
/// </summary>
/// <param name="Name">Project name (e.g., "TheWatch.Security").</param>
/// <param name="Status">Implementation status: "substantial", "medium", "light", "stub", "empty", or category-derived.</param>
/// <param name="CsFileCount">Number of .cs files reported in the manifest.</param>
/// <param name="Layer">Architecture layer: "Domain", "Application", "Infrastructure", "Presentation", etc.</param>
/// <param name="Description">Optional project description.</param>
public record ManifestProject(string Name, string Status, int CsFileCount, string Layer, string Description);

/// <summary>
/// Provides read-only query access to TheWatchArchitecture.xml.
/// Uses System.Xml.Linq (XDocument) for parsing — no external dependencies.
/// </summary>
public sealed class ArchitectureManifest
{
    private readonly XDocument _doc;

    private ArchitectureManifest(XDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    // ── Factory Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Loads the architecture manifest from the embedded resource compiled into this assembly.
    /// </summary>
    public static ArchitectureManifest LoadFromEmbeddedResource()
    {
        var assembly = typeof(ArchitectureManifest).Assembly;
        using var stream = assembly.GetManifestResourceStream("TheWatch.XML.TheWatchArchitecture.xml")
            ?? throw new InvalidOperationException(
                "Embedded resource 'TheWatch.XML.TheWatchArchitecture.xml' not found. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        return new ArchitectureManifest(XDocument.Load(stream));
    }

    /// <summary>
    /// Loads the architecture manifest from a file on disk.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to TheWatchArchitecture.xml.</param>
    public static ArchitectureManifest LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Architecture manifest not found: {filePath}", filePath);
        return new ArchitectureManifest(XDocument.Load(filePath));
    }

    /// <summary>
    /// Loads the architecture manifest from an XML string.
    /// </summary>
    public static ArchitectureManifest LoadFromString(string xml)
    {
        return new ArchitectureManifest(XDocument.Parse(xml));
    }

    // ── Entity Queries ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all domain entities declared in the manifest.
    /// Path: //Entities/Entity[@name, @description]
    /// </summary>
    public IReadOnlyList<ManifestItem> GetAllEntities()
    {
        return _doc.Descendants("Entity")
            .Where(e => e.Parent?.Name.LocalName == "Entities")
            .Select(e => new ManifestItem(
                e.Attribute("name")?.Value ?? "",
                e.Attribute("description")?.Value ?? ""))
            .Where(i => !string.IsNullOrEmpty(i.Name))
            .ToList();
    }

    /// <summary>
    /// Returns all value objects declared in the manifest.
    /// Path: //ValueObjects/ValueObject[@name, @description]
    /// </summary>
    public IReadOnlyList<ManifestItem> GetAllValueObjects()
    {
        return _doc.Descendants("ValueObject")
            .Where(e => e.Parent?.Name.LocalName == "ValueObjects")
            .Select(e => new ManifestItem(
                e.Attribute("name")?.Value ?? "",
                e.Attribute("description")?.Value ?? ""))
            .Where(i => !string.IsNullOrEmpty(i.Name))
            .ToList();
    }

    /// <summary>
    /// Returns all domain events declared in the manifest.
    /// Path: //DomainEvents/Event[@name, @trigger]
    /// </summary>
    public IReadOnlyList<ManifestItem> GetAllDomainEvents()
    {
        return _doc.Descendants("Event")
            .Where(e => e.Parent?.Name.LocalName == "DomainEvents")
            .Select(e => new ManifestItem(
                e.Attribute("name")?.Value ?? "",
                e.Attribute("trigger")?.Value ?? ""))
            .Where(i => !string.IsNullOrEmpty(i.Name))
            .ToList();
    }

    // ── Project Queries ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all projects declared in the manifest across all layers and categories.
    /// Derives status from the containing Category element or Project-level status attribute.
    /// </summary>
    public IReadOnlyList<ManifestProject> GetAllProjects()
    {
        var projects = new List<ManifestProject>();

        // Projects directly under Layer elements (Application layer pattern)
        foreach (var layer in _doc.Descendants("Layer"))
        {
            var layerName = layer.Attribute("name")?.Value ?? "Unknown";

            // Direct <Project> children under <Projects> container
            foreach (var proj in layer.Descendants("Project"))
            {
                var name = proj.Attribute("name")?.Value ?? proj.Attribute("project")?.Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                var status = DeriveProjectStatus(proj);
                var csFiles = ParseCsFiles(proj.Attribute("csFiles")?.Value);
                var desc = proj.Attribute("description")?.Value ?? "";

                projects.Add(new ManifestProject(name, status, csFiles, layerName, desc));
            }

            // <Client> elements in Presentation layer
            foreach (var client in layer.Elements("Client"))
            {
                var name = client.Attribute("project")?.Value ?? client.Attribute("name")?.Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                var csFiles = ParseCsFiles(client.Attribute("csFiles")?.Value);
                var desc = client.Attribute("features")?.Value ?? "";

                projects.Add(new ManifestProject(name, "substantial", csFiles, layerName, desc));
            }

            // <Library> elements in Libraries layer
            foreach (var lib in layer.Descendants("Library"))
            {
                var name = lib.Attribute("name")?.Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                var desc = lib.Attribute("description")?.Value ?? "";
                projects.Add(new ManifestProject(name, "library", 0, layerName, desc));
            }
        }

        return projects;
    }

    /// <summary>
    /// Filters projects by their implementation status.
    /// Valid statuses: "substantial", "medium", "light", "stub", "empty", "library"
    /// </summary>
    public IReadOnlyList<ManifestProject> GetProjectsByStatus(string status)
    {
        return GetAllProjects()
            .Where(p => p.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Filters projects by their architecture layer.
    /// Valid layers: "Domain", "Application", "Infrastructure", "Presentation", "Shared",
    ///              "Workers", "Libraries", "Aspire", "TheWatch.StandaloneApp"
    /// </summary>
    public IReadOnlyList<ManifestProject> GetProjectsByLayer(string layer)
    {
        return GetAllProjects()
            .Where(p => p.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Job Queries ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns all background job names declared in the Workers layer.
    /// Path: //Jobs/Job[@name]
    /// </summary>
    public IReadOnlyList<string> GetAllJobs()
    {
        return _doc.Descendants("Job")
            .Where(j => j.Parent?.Name.LocalName == "Jobs")
            .Select(j => j.Attribute("name")?.Value ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    // ── Service Queries ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all hosted service names declared in the Workers layer.
    /// Path: //Services/Service[@name] (under Worker.Services project)
    /// Also includes StandaloneServices.
    /// </summary>
    public IReadOnlyList<string> GetAllServices()
    {
        var services = new List<string>();

        // Hosted services under Workers layer
        foreach (var svc in _doc.Descendants("Service"))
        {
            var parent = svc.Parent?.Name.LocalName;
            if (parent == "Services" || parent == "StandaloneServices")
            {
                var name = svc.Attribute("name")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    services.Add(name);
            }
        }

        return services;
    }

    // ── Capability Queries ──────────────────────────────────────────────

    /// <summary>
    /// Returns all operational capabilities declared in the manifest.
    /// Path: //OperationalCapabilities/Capability[@name, @features]
    /// </summary>
    public IReadOnlyList<ManifestItem> GetAllCapabilities()
    {
        return _doc.Descendants("Capability")
            .Where(c => c.Parent?.Name.LocalName == "OperationalCapabilities")
            .Select(c => new ManifestItem(
                c.Attribute("name")?.Value ?? "",
                c.Attribute("features")?.Value ?? ""))
            .Where(i => !string.IsNullOrEmpty(i.Name))
            .ToList();
    }

    // ── Compliance Queries ──────────────────────────────────────────────

    /// <summary>
    /// Returns all security/compliance frameworks declared in the manifest.
    /// Path: //SecurityAndCompliance/Frameworks/Framework[@name, @description]
    /// </summary>
    public IReadOnlyList<ManifestItem> GetAllComplianceFrameworks()
    {
        return _doc.Descendants("Framework")
            .Where(f => f.Parent?.Name.LocalName == "Frameworks")
            .Select(f => new ManifestItem(
                f.Attribute("name")?.Value ?? "",
                f.Attribute("description")?.Value ?? ""))
            .Where(i => !string.IsNullOrEmpty(i.Name))
            .ToList();
    }

    // ── Lookup Helpers (used by CodeIndexCommand for tag enrichment) ────

    /// <summary>
    /// Returns a set of all entity names for fast lookup.
    /// </summary>
    public HashSet<string> GetEntityNameSet()
    {
        return new HashSet<string>(
            GetAllEntities().Select(e => e.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a set of all job names for fast lookup.
    /// </summary>
    public HashSet<string> GetJobNameSet()
    {
        return new HashSet<string>(
            GetAllJobs(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a set of all hosted service names for fast lookup.
    /// </summary>
    public HashSet<string> GetServiceNameSet()
    {
        return new HashSet<string>(
            GetAllServices(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a dictionary mapping project name (case-insensitive) to its manifest entry.
    /// </summary>
    public Dictionary<string, ManifestProject> GetProjectLookup()
    {
        var lookup = new Dictionary<string, ManifestProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var proj in GetAllProjects())
        {
            lookup.TryAdd(proj.Name, proj);
        }
        return lookup;
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    private static string DeriveProjectStatus(XElement proj)
    {
        // Explicit status attribute
        var status = proj.Attribute("status")?.Value;
        if (!string.IsNullOrEmpty(status)) return status;

        // Derive from parent Category element
        var category = proj.Ancestors("Category").FirstOrDefault();
        var categoryName = category?.Attribute("name")?.Value?.ToLowerInvariant() ?? "";

        return categoryName switch
        {
            "heavyimplementation" => "substantial",
            "mediumimplementation" => "medium",
            "lightimplementation" => "light",
            "scaffoldedstubs" => "stub",
            "emptyplaceholders" => "empty",
            _ => "unknown"
        };
    }

    private static int ParseCsFiles(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        // Handle values like "~61", "11+", "1113"
        var cleaned = value.TrimStart('~').TrimEnd('+');
        return int.TryParse(cleaned, out var count) ? count : 0;
    }
}
