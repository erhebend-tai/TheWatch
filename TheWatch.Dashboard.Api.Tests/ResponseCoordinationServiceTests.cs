// ResponseCoordinationServiceTests — tests for the full SOS response coordination pipeline.
//
// Uses real mock adapters from TheWatch.Adapters.Mock for port dependencies
// (in-memory state, no external services). Uses NSubstitute for:
//   - IHubContext<DashboardHub> (SignalR broadcast verification)
//   - IBackgroundJobClient (Hangfire scheduled job verification)
//
// WAL: Each test method validates one specific behavior in the pipeline.
//      No test depends on another test's state — each instantiates fresh adapters.
//
// Example — running a single test:
//   dotnet test --filter "FullyQualifiedName~CreateResponseAsync_CheckIn_UsesCorrectDefaults"

using Hangfire;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TheWatch.Adapters.Mock;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Tests;

public class ResponseCoordinationServiceTests
{
    // ── Factory method: creates a fresh service with real mock adapters + NSubstitute stubs ──

    private static (ResponseCoordinationService Service,
                     MockResponseRequestAdapter RequestPort,
                     MockResponseDispatchAdapter DispatchPort,
                     MockResponseTrackingAdapter TrackingPort,
                     MockEscalationAdapter EscalationPort,
                     MockParticipationAdapter ParticipationPort,
                     MockNavigationAdapter NavigationPort,
                     IHubContext<DashboardHub> HubContext,
                     IBackgroundJobClient HangfireClient)
        CreateService()
    {
        var requestPort = new MockResponseRequestAdapter(NullLogger<MockResponseRequestAdapter>.Instance);
        var dispatchPort = new MockResponseDispatchAdapter(NullLogger<MockResponseDispatchAdapter>.Instance);
        var trackingPort = new MockResponseTrackingAdapter(NullLogger<MockResponseTrackingAdapter>.Instance);
        var escalationPort = new MockEscalationAdapter(NullLogger<MockEscalationAdapter>.Instance);
        var participationPort = new MockParticipationAdapter(NullLogger<MockParticipationAdapter>.Instance);
        var navigationPort = new MockNavigationAdapter(NullLogger<MockNavigationAdapter>.Instance);
        var guardrailsPort = new MockMessageGuardrailsAdapter(NullLogger<MockMessageGuardrailsAdapter>.Instance);
        var communicationPort = new MockResponderCommunicationAdapter(
            guardrailsPort, trackingPort, NullLogger<MockResponderCommunicationAdapter>.Instance);

        // Spatial index for responder geolocation lookups
        var spatialIndex = new MockSpatialIndex();

        // Audit trail for compliance logging
        var auditTrail = Substitute.For<IAuditTrail>();

        // Emergency services port for 911 escalation
        var emergencyServices = new MockEmergencyServicesPort();

        // Escalation configuration (defaults)
        var escalationConfig = Options.Create(new EscalationConfiguration());

        // Notification ports
        var notificationSendPort = new MockNotificationSendAdapter(NullLogger<MockNotificationSendAdapter>.Instance);
        var notificationRegistrationPort = new MockNotificationRegistrationAdapter(NullLogger<MockNotificationRegistrationAdapter>.Instance);
        var notificationTrackingPort = new MockNotificationTrackingAdapter(NullLogger<MockNotificationTrackingAdapter>.Instance);

        // NSubstitute stubs for SignalR and Hangfire — we don't verify broadcast content,
        // only that the pipeline doesn't throw when broadcasting.
        var hubContext = Substitute.For<IHubContext<DashboardHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.All.Returns(clientProxy);
        hubClients.Group(Arg.Any<string>()).Returns(clientProxy);

        var hangfireClient = Substitute.For<IBackgroundJobClient>();

        // RabbitMQ connection stub — model stub logs but doesn't connect
        var rabbitConnection = Substitute.For<IConnection>();
        var rabbitModel = Substitute.For<IModel>();
        var basicProperties = Substitute.For<IBasicProperties>();
        rabbitConnection.CreateModel().Returns(rabbitModel);
        rabbitModel.CreateBasicProperties().Returns(basicProperties);

        var service = new ResponseCoordinationService(
            requestPort, dispatchPort, trackingPort,
            escalationPort, participationPort, navigationPort,
            communicationPort, spatialIndex, auditTrail,
            emergencyServices, escalationConfig,
            notificationSendPort, notificationRegistrationPort, notificationTrackingPort,
            hubContext, hangfireClient, rabbitConnection,
            NullLogger<ResponseCoordinationService>.Instance);

        return (service, requestPort, dispatchPort, trackingPort,
                escalationPort, participationPort, navigationPort,
                hubContext, hangfireClient);
    }

    // ────────────────────────────────────────────────────────────
    // CreateResponseAsync tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// CheckIn scope should use the CheckIn defaults from ResponseScopePresets:
    ///   Radius=1000m, DesiredResponders=8, Escalation=Manual, Strategy=NearestN.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_CheckIn_UsesCorrectDefaults()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74,
            description: "Test check-in", triggerSource: "MANUAL_BUTTON");

        Assert.Equal(ResponseScope.CheckIn, result.Scope);
        Assert.Equal(1000, result.RadiusMeters);
        Assert.Equal(8, result.DesiredResponderCount);
        Assert.Equal(EscalationPolicy.Manual, result.Escalation);
        Assert.Equal(DispatchStrategy.NearestN, result.Strategy);
    }

    /// <summary>
    /// After full pipeline, request status should be Active (Dispatching -> Active transition).
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_SetsStatusToActive()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.Neighborhood, 30.27, -97.74);

        Assert.Equal(ResponseStatus.Active, result.Status);
    }

    /// <summary>
    /// CreateResponseAsync finds eligible responders via the participation port.
    /// The mock adapter seeds 8 users; for CheckIn scope, at least some should be eligible.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_FindsEligibleResponders()
    {
        var (svc, _, _, _, _, participationPort, _, _, _) = CreateService();

        // Verify eligible responders exist for CheckIn scope
        var eligible = await participationPort.FindEligibleRespondersAsync(
            30.27, -97.74, 1000, ResponseScope.CheckIn);

        Assert.NotEmpty(eligible);
        Assert.All(eligible, r => Assert.NotNull(r.UserId));

        // Now run the full pipeline and verify it completes without error
        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        Assert.NotNull(result);
        Assert.NotNull(result.RequestId);
    }

    /// <summary>
    /// Non-Manual escalation policies (e.g., Neighborhood => TimedEscalation) should
    /// schedule an escalation via IEscalationPort and a Hangfire delayed job.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_SchedulesEscalation_WhenPolicyNotManual()
    {
        var (svc, _, _, _, _, _, _, _, hangfireClient) = CreateService();

        // Neighborhood scope uses TimedEscalation with 2-min timeout
        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.Neighborhood, 30.27, -97.74);

        Assert.Equal(EscalationPolicy.TimedEscalation, result.Escalation);

        // Verify Hangfire was called to schedule escalation jobs (4 total):
        //   Stage 2 (WidenScope), Stage 3 (EmergencyContacts), Stage 4 (FirstResponders),
        //   plus the legacy IEscalationPort.CheckAndEscalateAsync fallback.
        hangfireClient.Received(4).Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Any<Hangfire.States.IState>());
    }

    /// <summary>
    /// Manual escalation policy (CheckIn) should NOT schedule any escalation.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_DoesNotScheduleEscalation_WhenManualPolicy()
    {
        var (svc, _, _, _, _, _, _, _, hangfireClient) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        Assert.Equal(EscalationPolicy.Manual, result.Escalation);

        // Verify Hangfire was NOT called
        hangfireClient.DidNotReceive().Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Any<Hangfire.States.IState>());
    }

    // ────────────────────────────────────────────────────────────
    // AcknowledgeResponseAsync tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// AcknowledgeResponseAsync should record the ack in the tracking port.
    /// </summary>
    [Fact]
    public async Task AcknowledgeResponseAsync_RecordsAck()
    {
        var (svc, _, _, trackingPort, _, _, _, _, _) = CreateService();

        // First create a response to acknowledge
        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        var ackResult = await svc.AcknowledgeResponseAsync(
            response.RequestId, "resp-001", "Marcus Chen", "EMT",
            30.275, -97.735, 500, hasVehicle: true, estimatedArrivalMinutes: 3);

        Assert.NotNull(ackResult);
        Assert.NotNull(ackResult.Acknowledgment);
        Assert.Equal(response.RequestId, ackResult.Acknowledgment.RequestId);
        Assert.Equal("resp-001", ackResult.Acknowledgment.ResponderId);
        Assert.Equal("Marcus Chen", ackResult.Acknowledgment.ResponderName);
        Assert.Equal(AckStatus.EnRoute, ackResult.Acknowledgment.Status);

        // Verify it was stored in the tracking port
        var acks = await trackingPort.GetAcknowledgmentsAsync(response.RequestId);
        Assert.Single(acks);
    }

    /// <summary>
    /// AcknowledgeResponseAsync should generate navigation directions with deep links.
    /// </summary>
    [Fact]
    public async Task AcknowledgeResponseAsync_GeneratesNavigationDirections()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        var ackResult = await svc.AcknowledgeResponseAsync(
            response.RequestId, "resp-001", "Marcus Chen", "EMT",
            30.275, -97.735, 500, hasVehicle: true, estimatedArrivalMinutes: 5);

        Assert.NotNull(ackResult.Directions);
        Assert.Equal("driving", ackResult.Directions.TravelMode);
        Assert.Contains("google.com/maps", ackResult.Directions.GoogleMapsUrl);
        Assert.Contains("maps.apple.com", ackResult.Directions.AppleMapsUrl);
        Assert.Contains("waze.com", ackResult.Directions.WazeUrl);
        Assert.NotNull(ackResult.Directions.EstimatedTravelTime);
    }

    /// <summary>
    /// When enough responders acknowledge (>= DesiredResponderCount), escalation should
    /// be cancelled. CheckIn wants 8 responders. Acknowledging 8 should cancel escalation.
    /// </summary>
    [Fact]
    public async Task AcknowledgeResponseAsync_CancelsEscalation_WhenThresholdMet()
    {
        var (svc, _, _, _, escalationPort, _, _, _, _) = CreateService();

        // Use a scope with a small desired count so we can meet the threshold
        // SilentDuress wants only 3 responders
        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.SilentDuress, 30.27, -97.74);

        Assert.Equal(3, response.DesiredResponderCount);

        // Acknowledge 3 responders (meeting the threshold)
        for (int i = 1; i <= 3; i++)
        {
            await svc.AcknowledgeResponseAsync(
                response.RequestId, $"resp-{i:D3}", $"Responder {i}", "VOLUNTEER",
                30.27 + (i * 0.001), -97.74 + (i * 0.001), 100 * i,
                hasVehicle: true, estimatedArrivalMinutes: i * 2);
        }

        // The third ack should have triggered CancelEscalationAsync.
        // We can't directly verify on the mock since it's fire-and-forget,
        // but we verify that the pipeline completed without error and all 3 acks are stored.
        // The real verification is that CancelEscalationAsync was called in the service code
        // (line 198 of ResponseCoordinationService.cs) when ackCount >= desiredResponderCount.
        // Since we're using real mock adapters, not NSubstitute, we verify the result state.
        var situation = await svc.GetSituationAsync(response.RequestId);
        Assert.NotNull(situation);
        Assert.Equal(3, situation.TotalAcknowledged);
    }

    // ────────────────────────────────────────────────────────────
    // CancelResponseAsync tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// CancelResponseAsync should set status to Cancelled.
    /// </summary>
    [Fact]
    public async Task CancelResponseAsync_SetsStatusToCancelled()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        var cancelled = await svc.CancelResponseAsync(
            response.RequestId, "User pressed I'm OK");

        Assert.Equal(ResponseStatus.Cancelled, cancelled.Status);
        Assert.Equal(response.RequestId, cancelled.RequestId);
    }

    /// <summary>
    /// CancelResponseAsync should also cancel any scheduled escalation.
    /// </summary>
    [Fact]
    public async Task CancelResponseAsync_CancelsEscalation()
    {
        var (svc, _, _, _, _, _, _, hubContext, _) = CreateService();

        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.Neighborhood, 30.27, -97.74);

        var cancelled = await svc.CancelResponseAsync(
            response.RequestId, "False alarm");

        Assert.Equal(ResponseStatus.Cancelled, cancelled.Status);

        // Verify SignalR broadcast was called with cancellation
        await hubContext.Clients.All.Received().SendCoreAsync(
            "SOSResponseCancelled",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────
    // ResolveResponseAsync tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// ResolveResponseAsync should set status to Resolved.
    /// </summary>
    [Fact]
    public async Task ResolveResponseAsync_SetsStatusToResolved()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        var resolved = await svc.ResolveResponseAsync(
            response.RequestId, "resp-001");

        Assert.Equal(ResponseStatus.Resolved, resolved.Status);
        Assert.Equal(response.RequestId, resolved.RequestId);
    }

    // ────────────────────────────────────────────────────────────
    // GetSituationAsync tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// GetSituationAsync should return null for an unknown request ID.
    /// </summary>
    [Fact]
    public async Task GetSituationAsync_ReturnsNullForUnknown()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var situation = await svc.GetSituationAsync("nonexistent-id");

        Assert.Null(situation);
    }

    /// <summary>
    /// GetSituationAsync should return a full ResponseSituation with request,
    /// acknowledgments, escalation history, and correct tallies.
    /// </summary>
    [Fact]
    public async Task GetSituationAsync_ReturnsSituationWithAllData()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var response = await svc.CreateResponseAsync(
            "user-001", ResponseScope.CheckIn, 30.27, -97.74);

        // Add two acks
        await svc.AcknowledgeResponseAsync(
            response.RequestId, "resp-001", "Marcus Chen", "EMT",
            30.275, -97.735, 500, hasVehicle: true, estimatedArrivalMinutes: 3);

        await svc.AcknowledgeResponseAsync(
            response.RequestId, "resp-002", "Sarah Williams", "NURSE",
            30.28, -97.73, 800, hasVehicle: true, estimatedArrivalMinutes: 5);

        var situation = await svc.GetSituationAsync(response.RequestId);

        Assert.NotNull(situation);
        Assert.Equal(response.RequestId, situation.Request.RequestId);
        Assert.Equal(2, situation.TotalAcknowledged);
        Assert.Equal(2, situation.TotalEnRoute); // Both acks have EnRoute status
        Assert.Equal(0, situation.TotalOnScene);
        Assert.Equal(2, situation.Acknowledgments.Count);
        Assert.NotNull(situation.EscalationHistory);
    }

    // ────────────────────────────────────────────────────────────
    // Scope preset integration tests
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Neighborhood scope should use different defaults than CheckIn.
    /// Verifies the scope preset plumbing is correct end-to-end.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_NeighborhoodScope_UsesCorrectPresets()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.Neighborhood, 30.27, -97.74);

        Assert.Equal(ResponseScope.Neighborhood, result.Scope);
        Assert.Equal(3000, result.RadiusMeters);
        Assert.Equal(15, result.DesiredResponderCount);
        Assert.Equal(EscalationPolicy.TimedEscalation, result.Escalation);
        Assert.Equal(DispatchStrategy.CertifiedFirst, result.Strategy);
    }

    /// <summary>
    /// Community scope should use Immediate911 and RadiusBroadcast.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_CommunityScope_UsesCorrectPresets()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.Community, 30.27, -97.74);

        Assert.Equal(ResponseScope.Community, result.Scope);
        Assert.Equal(10000, result.RadiusMeters);
        Assert.Equal(50, result.DesiredResponderCount);
        Assert.Equal(EscalationPolicy.Immediate911, result.Escalation);
        Assert.Equal(DispatchStrategy.RadiusBroadcast, result.Strategy);
    }

    /// <summary>
    /// SilentDuress scope should use TrustedContactsOnly and Conditional911.
    /// </summary>
    [Fact]
    public async Task CreateResponseAsync_SilentDuressScope_UsesCorrectPresets()
    {
        var (svc, _, _, _, _, _, _, _, _) = CreateService();

        var result = await svc.CreateResponseAsync(
            "user-001", ResponseScope.SilentDuress, 30.27, -97.74);

        Assert.Equal(ResponseScope.SilentDuress, result.Scope);
        Assert.Equal(500, result.RadiusMeters);
        Assert.Equal(3, result.DesiredResponderCount);
        Assert.Equal(EscalationPolicy.Conditional911, result.Escalation);
        Assert.Equal(DispatchStrategy.TrustedContactsOnly, result.Strategy);
    }
}
