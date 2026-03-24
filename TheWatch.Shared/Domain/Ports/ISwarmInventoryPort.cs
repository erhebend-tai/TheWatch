// ISwarmInventoryPort — domain port for swarm file inventory persistence.
// Stores file-agent assignments, goals, and supervisor rollups in Firestore.
// NO database SDK imports allowed in this file.
//
// Collections:
//   swarm_files        — one doc per tracked file (keyed by Project+FilePath hash)
//   swarm_supervisors  — one doc per supervisor domain
//   swarm_goals        — subcollection under each file doc
//
// Example:
//   await port.UpsertFileAsync(file);
//   var inventory = await port.GetAllFilesAsync();
//   var supervisors = await port.GetSupervisorsAsync();

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Domain port for swarm inventory CRUD — file tracking, agent assignments, goals.
/// Backed by Firestore (emulator or production).
/// </summary>
public interface ISwarmInventoryPort
{
    // ── File Inventory ───────────────────────────────────────────

    /// <summary>Upsert a single file inventory record.</summary>
    Task UpsertFileAsync(SwarmFileRecord file, CancellationToken ct = default);

    /// <summary>Upsert a batch of file inventory records.</summary>
    Task UpsertFilesAsync(IEnumerable<SwarmFileRecord> files, CancellationToken ct = default);

    /// <summary>Get all tracked files.</summary>
    Task<List<SwarmFileRecord>> GetAllFilesAsync(CancellationToken ct = default);

    /// <summary>Get files filtered by project name.</summary>
    Task<List<SwarmFileRecord>> GetFilesByProjectAsync(string project, CancellationToken ct = default);

    /// <summary>Get a single file by its document ID.</summary>
    Task<SwarmFileRecord?> GetFileAsync(string fileId, CancellationToken ct = default);

    /// <summary>Delete a file record.</summary>
    Task DeleteFileAsync(string fileId, CancellationToken ct = default);

    // ── Supervisors ──────────────────────────────────────────────

    /// <summary>Upsert a supervisor assignment.</summary>
    Task UpsertSupervisorAsync(SwarmSupervisorRecord supervisor, CancellationToken ct = default);

    /// <summary>Get all supervisor assignments.</summary>
    Task<List<SwarmSupervisorRecord>> GetSupervisorsAsync(CancellationToken ct = default);

    // ── Goals ────────────────────────────────────────────────────

    /// <summary>Update goals for a specific file.</summary>
    Task UpdateGoalsAsync(string fileId, List<SwarmGoalRecord> goals, CancellationToken ct = default);

    /// <summary>Get goals for a specific file.</summary>
    Task<List<SwarmGoalRecord>> GetGoalsAsync(string fileId, CancellationToken ct = default);

    // ── Seeding ──────────────────────────────────────────────────

    /// <summary>Seed the inventory from static data if the collection is empty.</summary>
    Task SeedIfEmptyAsync(List<SwarmFileRecord> files, List<SwarmSupervisorRecord> supervisors, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════
// Domain Records — flat, serializable, no SDK dependencies
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A file tracked by the swarm. Stored in Firestore "swarm_files" collection.
/// </summary>
public record SwarmFileRecord
{
    public string Id { get; init; } = "";
    public string Project { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileType { get; init; } = "";
    public string PurposeShort { get; init; } = "";
    public string PurposeFull { get; init; } = "";
    public string AgentName { get; init; } = "Unassigned";
    public string AgentRole { get; init; } = "";
    public string AgentStatus { get; init; } = "Idle";
    public List<SwarmGoalRecord> Goals { get; init; } = [];
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A supervisor overseeing a domain of files. Stored in "swarm_supervisors" collection.
/// </summary>
public record SwarmSupervisorRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Domain { get; init; } = "";
    public int FileCount { get; init; }
    public int ActiveAgents { get; init; }
    public int GoalsCompleted { get; init; }
    public int GoalsTotal { get; init; }
    public string Status { get; init; } = "Active";
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A goal assigned to an agent for a specific file. Embedded in SwarmFileRecord.
/// </summary>
public record SwarmGoalRecord
{
    public string Description { get; init; } = "";
    public string Status { get; init; } = "Pending"; // Pending, InProgress, Done, Blocked
}
