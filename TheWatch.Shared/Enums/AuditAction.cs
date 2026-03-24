// AuditAction — enumerates all auditable operations in TheWatch.
// Covers CRUD, auth, SOS lifecycle, evidence capture, and config changes.
// Example: AuditAction.SOSTrigger when user utters their emergency phrase.

namespace TheWatch.Shared.Enums;

public enum AuditAction
{
    Create = 0,
    Read = 1,
    Update = 2,
    Delete = 3,
    Login = 4,
    Logout = 5,
    SOSTrigger = 6,
    SOSCancel = 7,
    AlertAcknowledge = 8,
    AlertEscalate = 9,
    EvidenceCapture = 10,
    LocationUpdate = 11,
    ConfigChange = 12,
    PermissionGrant = 13,
    PermissionRevoke = 14
}
