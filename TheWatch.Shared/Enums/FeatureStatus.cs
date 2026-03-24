// FeatureStatus — lifecycle of a feature implementation tracked in the DataGrid.
// Example: if (feature.Status == FeatureStatus.InProgress) ShowProgressBar();

namespace TheWatch.Shared.Enums;

public enum FeatureStatus
{
    Planned = 0,
    InProgress = 1,
    InReview = 2,
    Testing = 3,
    Completed = 4,
    Blocked = 5,
    Deferred = 6
}
