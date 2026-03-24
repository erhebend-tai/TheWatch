// IDevWorkPort — domain port for Claude Code DevWork logging and interaction.
// Stores structured logs of all Claude Code requests/responses within the Aspire app.
// Serilog enriches log events with CorrelationId; this port persists the full DevWorkLog.
//
// Example:
//   await port.LogWorkAsync(new DevWorkLog { Action = "ImplementFeature", ... }, ct);
//   var recent = await port.GetRecentLogsAsync(50, ct);

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IDevWorkPort
{
    Task<StorageResult<DevWorkLog>> LogWorkAsync(DevWorkLog log, CancellationToken ct = default);
    Task<StorageResult<DevWorkLog>> GetByIdAsync(string logId, CancellationToken ct = default);
    Task<StorageResult<List<DevWorkLog>>> GetRecentLogsAsync(int limit = 50, CancellationToken ct = default);
    Task<StorageResult<List<DevWorkLog>>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task<StorageResult<List<DevWorkLog>>> GetByFeatureIdAsync(string featureId, CancellationToken ct = default);
    Task<StorageResult<bool>> UpdateStatusAsync(string logId, string status, string? errorMessage = null, CancellationToken ct = default);
}
