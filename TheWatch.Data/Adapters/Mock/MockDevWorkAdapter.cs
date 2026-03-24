// MockDevWorkAdapter — ConcurrentDictionary-backed IDevWorkPort for dev/testing.
//
// Example: services.AddSingleton<IDevWorkPort, MockDevWorkAdapter>();

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockDevWorkAdapter : IDevWorkPort
{
    private readonly ConcurrentDictionary<string, DevWorkLog> _logs = new();

    public Task<StorageResult<DevWorkLog>> LogWorkAsync(DevWorkLog log, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(log.Id)) log.Id = Guid.NewGuid().ToString();
        _logs[log.Id] = log;
        return Task.FromResult(StorageResult<DevWorkLog>.Ok(log));
    }

    public Task<StorageResult<DevWorkLog>> GetByIdAsync(string logId, CancellationToken ct = default)
    {
        if (_logs.TryGetValue(logId, out var log))
            return Task.FromResult(StorageResult<DevWorkLog>.Ok(log));
        return Task.FromResult(StorageResult<DevWorkLog>.Fail($"DevWorkLog '{logId}' not found"));
    }

    public Task<StorageResult<List<DevWorkLog>>> GetRecentLogsAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<DevWorkLog>>.Ok(
            _logs.Values.OrderByDescending(l => l.Timestamp).Take(limit).ToList()));

    public Task<StorageResult<List<DevWorkLog>>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<DevWorkLog>>.Ok(
            _logs.Values.Where(l => l.SessionId == sessionId).OrderByDescending(l => l.Timestamp).ToList()));

    public Task<StorageResult<List<DevWorkLog>>> GetByFeatureIdAsync(string featureId, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<DevWorkLog>>.Ok(
            _logs.Values.Where(l => l.FeatureIds.Contains(featureId)).OrderByDescending(l => l.Timestamp).ToList()));

    public Task<StorageResult<bool>> UpdateStatusAsync(string logId, string status, string? errorMessage = null, CancellationToken ct = default)
    {
        if (_logs.TryGetValue(logId, out var log))
        {
            log.Status = status;
            if (errorMessage is not null) log.ErrorMessage = errorMessage;
            return Task.FromResult(StorageResult<bool>.Ok(true));
        }
        return Task.FromResult(StorageResult<bool>.Fail($"DevWorkLog '{logId}' not found"));
    }
}
