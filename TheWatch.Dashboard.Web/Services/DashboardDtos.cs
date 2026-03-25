// =============================================================================
// DashboardDtos — Local DTOs for the Dashboard.Web Blazor frontend.
// =============================================================================
// These mirror the API-side models (ResponseSituation, etc.) but live in the
// Web project so we don't need a project reference to Dashboard.Api.
// They are deserialized from API JSON responses via DashboardApiClient.
//
// Example — API returns:
//   { "request": {...}, "acknowledgments": [...], "escalationHistory": [...],
//     "totalDispatched": 10, "totalAcknowledged": 5, "totalEnRoute": 3, "totalOnScene": 2 }
// DashboardApiClient deserializes into ResponseSituation automatically.
//
// WAL: Keep these in sync with the API-side definitions in
//      TheWatch.Dashboard.Api.Services.IResponseCoordinationService.
// =============================================================================

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Web.Services;

/// <summary>
/// Snapshot of a response situation — the request plus all responder acknowledgments
/// and escalation history. Mirrors the API-side ResponseSituation record.
/// </summary>
public record ResponseSituation(
    ResponseRequest Request,
    IReadOnlyList<ResponderAcknowledgment> Acknowledgments,
    IReadOnlyList<EscalationEvent> EscalationHistory,
    int TotalDispatched,
    int TotalAcknowledged,
    int TotalEnRoute,
    int TotalOnScene
);
