// FirestoreSwarmInventoryAdapter — Firestore implementation of ISwarmInventoryPort.
//
// Collections:
//   swarm_files        — one doc per tracked file, keyed by deterministic ID
//   swarm_supervisors  — one doc per supervisor domain
//
// Goals are stored as an embedded array within each swarm_files document,
// not as a subcollection, because they're always read with the file record.
//
// Works with both:
//   - Firestore Emulator (local dev via FIRESTORE_EMULATOR_HOST env var)
//   - Production Firestore (via service account credentials)
//
// WAL: All mutations logged at Information level with [WAL-SWARM-FIRESTORE] prefix.

using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Firestore;

public class FirestoreSwarmInventoryAdapter : ISwarmInventoryPort
{
    private const string FilesCollection = "swarm_files";
    private const string SupervisorsCollection = "swarm_supervisors";

    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreSwarmInventoryAdapter> _logger;

    public FirestoreSwarmInventoryAdapter(FirestoreDb db, ILogger<FirestoreSwarmInventoryAdapter> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── File Inventory ───────────────────────────────────────────

    public async Task UpsertFileAsync(SwarmFileRecord file, CancellationToken ct = default)
    {
        var docRef = _db.Collection(FilesCollection).Document(file.Id);
        await docRef.SetAsync(ToFirestoreDict(file), SetOptions.MergeAll, ct);

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Upserted file {FileId}: {Project}/{FilePath}",
            file.Id, file.Project, file.FilePath);
    }

    public async Task UpsertFilesAsync(IEnumerable<SwarmFileRecord> files, CancellationToken ct = default)
    {
        var batch = _db.StartBatch();
        var count = 0;

        foreach (var file in files)
        {
            var docRef = _db.Collection(FilesCollection).Document(file.Id);
            batch.Set(docRef, ToFirestoreDict(file), SetOptions.MergeAll);
            count++;

            // Firestore batch limit is 500 writes
            if (count >= 490)
            {
                await batch.CommitAsync(ct);
                batch = _db.StartBatch();
                count = 0;
            }
        }

        if (count > 0)
            await batch.CommitAsync(ct);

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Batch upserted {Count} files", count);
    }

    public async Task<List<SwarmFileRecord>> GetAllFilesAsync(CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(FilesCollection)
            .OrderBy("Project")
            .GetSnapshotAsync(ct);

        return snapshot.Documents.Select(FromFirestoreDoc).ToList();
    }

    public async Task<List<SwarmFileRecord>> GetFilesByProjectAsync(string project, CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(FilesCollection)
            .WhereEqualTo("Project", project)
            .GetSnapshotAsync(ct);

        return snapshot.Documents.Select(FromFirestoreDoc).ToList();
    }

    public async Task<SwarmFileRecord?> GetFileAsync(string fileId, CancellationToken ct = default)
    {
        var docRef = _db.Collection(FilesCollection).Document(fileId);
        var snapshot = await docRef.GetSnapshotAsync(ct);

        return snapshot.Exists ? FromFirestoreDoc(snapshot) : null;
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken ct = default)
    {
        await _db.Collection(FilesCollection).Document(fileId).DeleteAsync(cancellationToken: ct);
        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Deleted file {FileId}", fileId);
    }

    // ── Supervisors ──────────────────────────────────────────────

    public async Task UpsertSupervisorAsync(SwarmSupervisorRecord supervisor, CancellationToken ct = default)
    {
        var docRef = _db.Collection(SupervisorsCollection).Document(supervisor.Id);
        await docRef.SetAsync(ToSupervisorDict(supervisor), SetOptions.MergeAll, ct);

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Upserted supervisor {Name}: {Domain}",
            supervisor.Name, supervisor.Domain);
    }

    public async Task<List<SwarmSupervisorRecord>> GetSupervisorsAsync(CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(SupervisorsCollection).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(FromSupervisorDoc).ToList();
    }

    // ── Goals ────────────────────────────────────────────────────

    public async Task UpdateGoalsAsync(string fileId, List<SwarmGoalRecord> goals, CancellationToken ct = default)
    {
        var docRef = _db.Collection(FilesCollection).Document(fileId);
        var goalsData = goals.Select(g => new Dictionary<string, object>
        {
            ["Description"] = g.Description,
            ["Status"] = g.Status
        }).ToList();

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            ["Goals"] = goalsData,
            ["LastUpdated"] = Timestamp.FromDateTime(DateTime.UtcNow)
        }, cancellationToken: ct);

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Updated {GoalCount} goals for file {FileId}",
            goals.Count, fileId);
    }

    public async Task<List<SwarmGoalRecord>> GetGoalsAsync(string fileId, CancellationToken ct = default)
    {
        var file = await GetFileAsync(fileId, ct);
        return file?.Goals ?? [];
    }

    // ── Seeding ──────────────────────────────────────────────────

    public async Task SeedIfEmptyAsync(
        List<SwarmFileRecord> files,
        List<SwarmSupervisorRecord> supervisors,
        CancellationToken ct = default)
    {
        // Check if collection already has data
        var existingFiles = await _db.Collection(FilesCollection).Limit(1).GetSnapshotAsync(ct);
        if (existingFiles.Count > 0)
        {
            _logger.LogInformation("[WAL-SWARM-FIRESTORE] Skipping seed — {Collection} already has data",
                FilesCollection);
            return;
        }

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Seeding {FileCount} files and {SupervisorCount} supervisors",
            files.Count, supervisors.Count);

        await UpsertFilesAsync(files, ct);

        foreach (var sup in supervisors)
            await UpsertSupervisorAsync(sup, ct);

        _logger.LogInformation("[WAL-SWARM-FIRESTORE] Seed complete");
    }

    // ── Serialization Helpers ────────────────────────────────────

    private static Dictionary<string, object> ToFirestoreDict(SwarmFileRecord file) => new()
    {
        ["Project"] = file.Project,
        ["FilePath"] = file.FilePath,
        ["FileName"] = file.FileName,
        ["FileType"] = file.FileType,
        ["PurposeShort"] = file.PurposeShort,
        ["PurposeFull"] = file.PurposeFull,
        ["AgentName"] = file.AgentName,
        ["AgentRole"] = file.AgentRole,
        ["AgentStatus"] = file.AgentStatus,
        ["Goals"] = file.Goals.Select(g => new Dictionary<string, object>
        {
            ["Description"] = g.Description,
            ["Status"] = g.Status
        }).ToList(),
        ["LastUpdated"] = Timestamp.FromDateTime(
            file.LastUpdated.Kind == DateTimeKind.Utc
                ? file.LastUpdated
                : DateTime.SpecifyKind(file.LastUpdated, DateTimeKind.Utc))
    };

    private static SwarmFileRecord FromFirestoreDoc(DocumentSnapshot doc)
    {
        var dict = doc.ToDictionary();

        var goals = new List<SwarmGoalRecord>();
        if (dict.TryGetValue("Goals", out var goalsObj) && goalsObj is List<object> goalsList)
        {
            foreach (var item in goalsList)
            {
                if (item is Dictionary<string, object> goalDict)
                {
                    goals.Add(new SwarmGoalRecord
                    {
                        Description = goalDict.GetValueOrDefault("Description")?.ToString() ?? "",
                        Status = goalDict.GetValueOrDefault("Status")?.ToString() ?? "Pending"
                    });
                }
            }
        }

        return new SwarmFileRecord
        {
            Id = doc.Id,
            Project = dict.GetValueOrDefault("Project")?.ToString() ?? "",
            FilePath = dict.GetValueOrDefault("FilePath")?.ToString() ?? "",
            FileName = dict.GetValueOrDefault("FileName")?.ToString() ?? "",
            FileType = dict.GetValueOrDefault("FileType")?.ToString() ?? "",
            PurposeShort = dict.GetValueOrDefault("PurposeShort")?.ToString() ?? "",
            PurposeFull = dict.GetValueOrDefault("PurposeFull")?.ToString() ?? "",
            AgentName = dict.GetValueOrDefault("AgentName")?.ToString() ?? "Unassigned",
            AgentRole = dict.GetValueOrDefault("AgentRole")?.ToString() ?? "",
            AgentStatus = dict.GetValueOrDefault("AgentStatus")?.ToString() ?? "Idle",
            Goals = goals,
            LastUpdated = dict.TryGetValue("LastUpdated", out var ts) && ts is Timestamp timestamp
                ? timestamp.ToDateTime()
                : DateTime.UtcNow
        };
    }

    private static Dictionary<string, object> ToSupervisorDict(SwarmSupervisorRecord sup) => new()
    {
        ["Name"] = sup.Name,
        ["Domain"] = sup.Domain,
        ["FileCount"] = sup.FileCount,
        ["ActiveAgents"] = sup.ActiveAgents,
        ["GoalsCompleted"] = sup.GoalsCompleted,
        ["GoalsTotal"] = sup.GoalsTotal,
        ["Status"] = sup.Status,
        ["LastUpdated"] = Timestamp.FromDateTime(
            sup.LastUpdated.Kind == DateTimeKind.Utc
                ? sup.LastUpdated
                : DateTime.SpecifyKind(sup.LastUpdated, DateTimeKind.Utc))
    };

    private static SwarmSupervisorRecord FromSupervisorDoc(DocumentSnapshot doc)
    {
        var dict = doc.ToDictionary();
        return new SwarmSupervisorRecord
        {
            Id = doc.Id,
            Name = dict.GetValueOrDefault("Name")?.ToString() ?? "",
            Domain = dict.GetValueOrDefault("Domain")?.ToString() ?? "",
            FileCount = dict.TryGetValue("FileCount", out var fc) && fc is long fcl ? (int)fcl : 0,
            ActiveAgents = dict.TryGetValue("ActiveAgents", out var aa) && aa is long aal ? (int)aal : 0,
            GoalsCompleted = dict.TryGetValue("GoalsCompleted", out var gc) && gc is long gcl ? (int)gcl : 0,
            GoalsTotal = dict.TryGetValue("GoalsTotal", out var gt) && gt is long gtl ? (int)gtl : 0,
            Status = dict.GetValueOrDefault("Status")?.ToString() ?? "Active",
            LastUpdated = dict.TryGetValue("LastUpdated", out var ts) && ts is Timestamp timestamp
                ? timestamp.ToDateTime()
                : DateTime.UtcNow
        };
    }
}
