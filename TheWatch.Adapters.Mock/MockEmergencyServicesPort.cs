// =============================================================================
// MockEmergencyServicesPort — Mock adapter for IEmergencyServicesPort
// =============================================================================
// Simulates 911 call initiation, RapidSOS location pushes, E911 address
// registration, and consent management. Logs all actions but does NOT place
// real calls. Returns realistic mock results for development and testing.
//
// Example:
//   var result = await port.Initiate911CallAsync(request, ct);
//   // result.Status == Emergency911CallStatus.Completed
//   // result.RapidSosLocationPushed == true
//   // result.ExternalCallId == "mock-call-abc123"
//
// In production, replace with:
//   - TwilioEmergencyServicesAdapter (Twilio Programmable Voice + PSTN 911)
//   - RapidSosAdapter (RapidSOS Emergency Data Platform for NG911)
//   - BandwidthEmergencyAdapter (Bandwidth.com 911 API)
//   - CompositeEmergencyServicesAdapter (Twilio + RapidSOS in parallel)
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

public class MockEmergencyServicesPort : IEmergencyServicesPort
{
    private readonly ConcurrentDictionary<string, Emergency911Result> _calls = new();
    private readonly ConcurrentDictionary<string, Emergency911Consent> _consents = new();
    private readonly ConcurrentDictionary<string, bool> _e911Addresses = new();
    private readonly ILogger<MockEmergencyServicesPort> _logger;

    public MockEmergencyServicesPort(ILogger<MockEmergencyServicesPort> logger)
    {
        _logger = logger;
    }

    // Parameterless constructor for scenarios where DI doesn't provide ILogger
    public MockEmergencyServicesPort()
    {
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MockEmergencyServicesPort>.Instance;
    }

    /// <summary>
    /// Mock 911 call initiation. Logs the call details, simulates a ~3s call duration,
    /// and returns a successful result with a mock external call ID.
    /// CRITICAL: In production, this places a REAL 911 call via Twilio/Bandwidth.
    /// </summary>
    public Task<Emergency911Result> Initiate911CallAsync(Emergency911Request request, CancellationToken ct)
    {
        _logger.LogWarning(
            "[Mock911] SIMULATED 911 CALL for {UserId}: " +
            "service={ServiceType}, trigger={TriggerSource}, " +
            "lat={Lat}, lng={Lng}, volunteersEnRoute={Volunteers}, " +
            "context=\"{Context}\"",
            request.UserId, request.ServiceType, request.TriggerSource,
            request.Latitude, request.Longitude, request.VolunteerRespondersEnRoute,
            request.ContextSummary);

        var result = new Emergency911Result(
            RequestId: request.RequestId,
            UserId: request.UserId,
            Status: Emergency911CallStatus.Completed,
            ExternalCallId: $"mock-call-{Guid.NewGuid().ToString("N")[..8]}",
            RapidSosLocationPushed: true,
            CallDuration: TimeSpan.FromSeconds(3),
            ConfirmationRequired: false,
            ConfirmationGiven: null,
            ErrorMessage: null,
            AuditEntryId: Guid.NewGuid().ToString("N")[..12],
            CompletedAt: DateTime.UtcNow
        );

        _calls[request.RequestId] = result;

        _logger.LogWarning(
            "[Mock911] Call completed: requestId={RequestId}, externalCallId={CallId}, " +
            "rapidSosPushed={RapidSos}",
            request.RequestId, result.ExternalCallId, result.RapidSosLocationPushed);

        return Task.FromResult(result);
    }

    /// <summary>Mock cancel — cancels a pending 911 call (during confirmation countdown).</summary>
    public Task<bool> Cancel911CallAsync(string requestId, string cancelledBy, CancellationToken ct)
    {
        _logger.LogInformation("[Mock911] Cancelled call {RequestId} by {CancelledBy}", requestId, cancelledBy);

        if (_calls.TryGetValue(requestId, out var existing))
        {
            _calls[requestId] = existing with { Status = Emergency911CallStatus.CancelledByUser };
        }
        return Task.FromResult(true);
    }

    /// <summary>Mock confirm — confirms a pending 911 call.</summary>
    public Task<Emergency911Result> Confirm911CallAsync(string requestId, CancellationToken ct)
    {
        _logger.LogInformation("[Mock911] Confirmed call {RequestId}", requestId);

        if (_calls.TryGetValue(requestId, out var existing))
        {
            var confirmed = existing with
            {
                Status = Emergency911CallStatus.Completed,
                ConfirmationGiven = true
            };
            _calls[requestId] = confirmed;
            return Task.FromResult(confirmed);
        }

        // If no pending call found, create a new completed result
        var result = new Emergency911Result(
            RequestId: requestId,
            UserId: "unknown",
            Status: Emergency911CallStatus.Completed,
            ExternalCallId: $"mock-call-{Guid.NewGuid().ToString("N")[..8]}",
            RapidSosLocationPushed: true,
            CallDuration: TimeSpan.FromSeconds(2),
            ConfirmationRequired: true,
            ConfirmationGiven: true,
            ErrorMessage: null,
            AuditEntryId: Guid.NewGuid().ToString("N")[..12],
            CompletedAt: DateTime.UtcNow
        );
        _calls[requestId] = result;
        return Task.FromResult(result);
    }

    /// <summary>Mock get result — returns the result for a 911 call if it exists.</summary>
    public Task<Emergency911Result?> Get911CallResultAsync(string requestId, CancellationToken ct)
    {
        _calls.TryGetValue(requestId, out var result);
        return Task.FromResult(result);
    }

    // ── RapidSOS Location Push ──────────────────────────────────────

    /// <summary>Mock RapidSOS push — logs the location data, returns true.</summary>
    public Task<bool> PushLocationToRapidSosAsync(RapidSosLocationPush locationData, CancellationToken ct)
    {
        _logger.LogInformation(
            "[Mock911] RapidSOS location pushed: user={UserId}, lat={Lat}, lng={Lng}, accuracy={Acc}m",
            locationData.UserId, locationData.Latitude, locationData.Longitude, locationData.AccuracyMeters);
        return Task.FromResult(true);
    }

    /// <summary>Mock RapidSOS update — logs the update, returns true.</summary>
    public Task<bool> UpdateRapidSosLocationAsync(string requestId, double latitude, double longitude,
        double? accuracyMeters, CancellationToken ct)
    {
        _logger.LogInformation(
            "[Mock911] RapidSOS location updated: request={RequestId}, lat={Lat}, lng={Lng}",
            requestId, latitude, longitude);
        return Task.FromResult(true);
    }

    /// <summary>Mock RapidSOS close — logs the session close, returns true.</summary>
    public Task<bool> CloseRapidSosSessionAsync(string requestId, CancellationToken ct)
    {
        _logger.LogInformation("[Mock911] RapidSOS session closed: request={RequestId}", requestId);
        return Task.FromResult(true);
    }

    // ── E911 Address Registration ───────────────────────────────────

    /// <summary>Mock E911 address registration — stores in memory, returns true.</summary>
    public Task<bool> RegisterE911AddressAsync(string userId, string address, string city,
        string state, string zip, string country, string callbackNumber, CancellationToken ct)
    {
        _e911Addresses[userId] = true;
        _logger.LogInformation(
            "[Mock911] E911 address registered: user={UserId}, address=\"{Address}, {City}, {State} {Zip}\"",
            userId, address, city, state, zip);
        return Task.FromResult(true);
    }

    /// <summary>Mock E911 address check — returns true if registered.</summary>
    public Task<bool> HasRegisteredE911AddressAsync(string userId, CancellationToken ct)
    {
        var hasAddress = _e911Addresses.ContainsKey(userId);
        return Task.FromResult(hasAddress);
    }

    // ── Consent Management ──────────────────────────────────────────

    /// <summary>Mock consent get — returns stored consent or a default opt-in.</summary>
    public Task<Emergency911Consent?> GetConsentAsync(string userId, CancellationToken ct)
    {
        if (_consents.TryGetValue(userId, out var consent))
            return Task.FromResult<Emergency911Consent?>(consent);

        // Default: user opted in (default YES per the app design)
        var defaultConsent = new Emergency911Consent(
            UserId: userId,
            AutoNotify911Enabled: true,
            RequireConfirmation: false,
            E911Address: null,
            E911City: null,
            E911State: null,
            E911Zip: null,
            E911Country: null,
            ConsentedAt: DateTime.UtcNow
        );
        _consents[userId] = defaultConsent;
        return Task.FromResult<Emergency911Consent?>(defaultConsent);
    }

    /// <summary>Mock consent update — stores the consent record.</summary>
    public Task<Emergency911Consent> UpdateConsentAsync(Emergency911Consent consent, CancellationToken ct)
    {
        _consents[consent.UserId] = consent;
        _logger.LogInformation(
            "[Mock911] Consent updated: user={UserId}, autoNotify={Auto}, requireConfirm={Confirm}",
            consent.UserId, consent.AutoNotify911Enabled, consent.RequireConfirmation);
        return Task.FromResult(consent);
    }

    // ── Audit & History ─────────────────────────────────────────────

    /// <summary>Mock call history — returns all stored call results for the user.</summary>
    public Task<IReadOnlyList<Emergency911Result>> GetCallHistoryAsync(string userId, int maxResults,
        CancellationToken ct)
    {
        var history = _calls.Values
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CompletedAt)
            .Take(maxResults)
            .ToList();
        return Task.FromResult<IReadOnlyList<Emergency911Result>>(history);
    }
}
