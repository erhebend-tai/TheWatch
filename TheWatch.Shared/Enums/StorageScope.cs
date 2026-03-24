// StorageScope — indicates where data lives in the replication topology.
// Example: StorageScope.Cached for data served from local device cache.

namespace TheWatch.Shared.Enums;

public enum StorageScope
{
    Local = 0,
    Remote = 1,
    Cached = 2,
    Replicated = 3
}
