// IFeatureTrackingPort — domain port for feature implementation tracking.
// NO database SDK imports allowed. Backed by Firestore (live) or ConcurrentDictionary (mock).
// The MudBlazor DataGrid queries this port via the FeaturesController.
//
// Example:
//   var features = await port.GetAllAsync(ct);
//   var inProgress = await port.GetByCategoryAsync(FeatureCategory.MobileApp, ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IFeatureTrackingPort
{
    Task<StorageResult<List<FeatureImplementation>>> GetAllAsync(CancellationToken ct = default);
    Task<StorageResult<FeatureImplementation>> GetByIdAsync(string featureId, CancellationToken ct = default);
    Task<StorageResult<List<FeatureImplementation>>> GetByCategoryAsync(FeatureCategory category, CancellationToken ct = default);
    Task<StorageResult<List<FeatureImplementation>>> GetByStatusAsync(FeatureStatus status, CancellationToken ct = default);
    Task<StorageResult<FeatureImplementation>> UpsertAsync(FeatureImplementation feature, CancellationToken ct = default);
    Task<StorageResult<bool>> UpdateStatusAsync(string featureId, FeatureStatus status, int progressPercent, CancellationToken ct = default);
    Task<StorageResult<bool>> DeleteAsync(string featureId, CancellationToken ct = default);
    Task<StorageResult<Dictionary<FeatureCategory, int>>> GetCategoryCountsAsync(CancellationToken ct = default);
    Task<StorageResult<Dictionary<FeatureStatus, int>>> GetStatusCountsAsync(CancellationToken ct = default);
}
