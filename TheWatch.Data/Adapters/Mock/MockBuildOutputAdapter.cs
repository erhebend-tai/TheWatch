// MockBuildOutputAdapter — ConcurrentDictionary-backed IBuildOutputPort for testing.
// Example: services.AddSingleton<IBuildOutputPort, MockBuildOutputAdapter>();

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockBuildOutputAdapter : IBuildOutputPort
{
    public BuildOutputStore Store => BuildOutputStore.Mock;
    private readonly ConcurrentDictionary<string, BuildOutput> _builds = new();

    public Task<StorageResult<BuildOutput>> SaveAsync(BuildOutput output, CancellationToken ct = default)
    {
        _builds[output.Id] = output;
        return Task.FromResult(StorageResult<BuildOutput>.Ok(output));
    }

    public Task<StorageResult<BuildOutput>> GetByIdAsync(string buildId, CancellationToken ct = default)
    {
        if (_builds.TryGetValue(buildId, out var b))
            return Task.FromResult(StorageResult<BuildOutput>.Ok(b));
        return Task.FromResult(StorageResult<BuildOutput>.Fail($"Build '{buildId}' not found"));
    }

    public Task<StorageResult<List<BuildOutput>>> GetRecentAsync(int limit = 50, string? projectName = null, CancellationToken ct = default)
    {
        var q = _builds.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(projectName)) q = q.Where(b => b.ProjectName == projectName);
        return Task.FromResult(StorageResult<List<BuildOutput>>.Ok(q.OrderByDescending(b => b.StartedAt).Take(limit).ToList()));
    }

    public Task<StorageResult<List<BuildOutput>>> GetFailedAsync(DateTime since, string? projectName = null, CancellationToken ct = default)
    {
        var q = _builds.Values.Where(b => !b.Succeeded && b.StartedAt >= since);
        if (!string.IsNullOrEmpty(projectName)) q = q.Where(b => b.ProjectName == projectName);
        return Task.FromResult(StorageResult<List<BuildOutput>>.Ok(q.OrderByDescending(b => b.StartedAt).ToList()));
    }

    public Task<StorageResult<List<BuildDiagnostic>>> GetDiagnosticsAsync(DateTime since, BuildOutputSeverity? severity = null, string? projectName = null, CancellationToken ct = default)
    {
        var q = _builds.Values.Where(b => b.StartedAt >= since).SelectMany(b => b.Diagnostics);
        if (severity.HasValue) q = q.Where(d => d.Severity == severity.Value);
        if (!string.IsNullOrEmpty(projectName)) q = q.Where(d => d.ProjectName == projectName);
        return Task.FromResult(StorageResult<List<BuildDiagnostic>>.Ok(q.ToList()));
    }

    public Task<StorageResult<BuildOutputStats>> GetStatsAsync(DateTime since, CancellationToken ct = default)
    {
        var builds = _builds.Values.Where(b => b.StartedAt >= since).ToList();
        var diags = builds.SelectMany(b => b.Diagnostics).ToList();
        return Task.FromResult(StorageResult<BuildOutputStats>.Ok(new BuildOutputStats
        {
            TotalBuilds = builds.Count,
            SuccessCount = builds.Count(b => b.Succeeded),
            FailureCount = builds.Count(b => !b.Succeeded),
            SuccessRate = builds.Count > 0 ? (double)builds.Count(b => b.Succeeded) / builds.Count * 100 : 0,
            AverageDurationMs = builds.Count > 0 ? builds.Average(b => b.DurationMs) : 0,
            TotalErrors = diags.Count(d => d.Severity >= BuildOutputSeverity.Error),
            TotalWarnings = diags.Count(d => d.Severity == BuildOutputSeverity.Warning),
            ErrorsByProject = builds.Where(b => !b.Succeeded).GroupBy(b => b.ProjectName).ToDictionary(g => g.Key, g => g.Count()),
            TopErrorCodes = diags.Where(d => d.Code != null).GroupBy(d => d.Code!).OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count())
        }));
    }

    public Task<StorageResult<int>> PurgeAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var toRemove = _builds.Values.Where(b => b.StartedAt < olderThan).Select(b => b.Id).ToList();
        foreach (var id in toRemove) _builds.TryRemove(id, out _);
        return Task.FromResult(StorageResult<int>.Ok(toRemove.Count));
    }
}
