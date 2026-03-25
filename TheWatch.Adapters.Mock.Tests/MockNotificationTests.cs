// MockNotificationTests — verifies all notification mock adapters fulfill their
// port contracts: push send/cancel, SMS send/parse, device registration, and
// delivery/response tracking.
//
// Example — running these tests:
//   dotnet test --filter "FullyQualifiedName~MockNotificationTests"
//
// WAL: Every port interface method has at least one test. If a new method is added
//      to any INotificationXxxPort or ISmsPort, a corresponding test MUST be added here.

using Microsoft.Extensions.Logging.Abstractions;
using TheWatch.Adapters.Mock;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock.Tests;

// ═══════════════════════════════════════════════════════════════
// MockNotificationSendAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockNotificationSendAdapterTests
{
    private readonly MockNotificationSendAdapter _sut = new(NullLogger<MockNotificationSendAdapter>.Instance);

    private static NotificationPayload CreateTestPayload(string id = "notif-001", string userId = "user-001") =>
        new(
            NotificationId: id, RecipientUserId: userId,
            RecipientDeviceToken: "mock-token-123", RecipientPhoneNumber: null,
            Category: NotificationCategory.SosDispatch, Priority: NotificationPriority.High,
            PreferredChannel: NotificationChannel.Push,
            Title: "Emergency Alert", Body: "Someone nearby needs help",
            Subtitle: null, DeepLink: $"thewatch://response/req-001",
            RequestId: "req-001", RequestorName: "Test User",
            Scope: ResponseScope.CheckIn,
            IncidentLatitude: 30.2672, IncidentLongitude: -97.7431,
            DistanceMeters: 500,
            SmsReplyInstructions: null,
            CreatedAt: DateTime.UtcNow, ExpiresAfter: TimeSpan.FromMinutes(10));

    [Fact]
    public async Task SendPushAsync_StoresNotificationAndReturnsDelivered()
    {
        var payload = CreateTestPayload();

        var result = await _sut.SendPushAsync(payload);

        Assert.Equal(NotificationDeliveryStatus.Delivered, result.Status);
        Assert.Equal(NotificationChannel.Push, result.Channel);
        Assert.Equal("user-001", result.RecipientUserId);
        Assert.Equal("notif-001", result.NotificationId);

        // Verify it was stored internally
        var sent = _sut.GetSentNotifications();
        Assert.True(sent.ContainsKey("notif-001"));
    }

    [Fact]
    public async Task SendPushBatchAsync_SendsAllAndReturnsDeliveredResults()
    {
        var payloads = new[]
        {
            CreateTestPayload(id: "batch-1", userId: "user-A"),
            CreateTestPayload(id: "batch-2", userId: "user-B"),
            CreateTestPayload(id: "batch-3", userId: "user-C"),
        };

        var results = await _sut.SendPushBatchAsync(payloads);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(NotificationDeliveryStatus.Delivered, r.Status));
    }

    [Fact]
    public async Task CancelNotificationAsync_RemovesSentNotification()
    {
        var payload = CreateTestPayload(id: "cancel-me");
        await _sut.SendPushAsync(payload);

        var removed = await _sut.CancelNotificationAsync("cancel-me", "user-001");

        Assert.True(removed);
        Assert.False(_sut.GetSentNotifications().ContainsKey("cancel-me"));
    }

    [Fact]
    public async Task CancelNotificationAsync_ReturnsFalse_ForUnknownNotification()
    {
        var result = await _sut.CancelNotificationAsync("does-not-exist", "user-001");

        Assert.False(result);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockSmsAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockSmsAdapterTests
{
    private readonly MockSmsAdapter _sut = new(NullLogger<MockSmsAdapter>.Instance);

    [Fact]
    public async Task SendSmsAsync_ReturnsDeliveredResult()
    {
        var result = await _sut.SendSmsAsync("+15551234001", "Test message", "req-001");

        Assert.Equal(NotificationDeliveryStatus.Delivered, result.Status);
        Assert.Equal(NotificationChannel.Sms, result.Channel);
    }

    [Fact]
    public async Task SendSmsBatchAsync_SendsAllMessages()
    {
        var messages = new List<(string PhoneNumber, string Message, string? RequestId)>
        {
            ("+15551234001", "Message 1", "req-001"),
            ("+15551234002", "Message 2", "req-001"),
            ("+15551234003", "Message 3", "req-002"),
        };

        var results = await _sut.SendSmsBatchAsync(messages);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(NotificationDeliveryStatus.Delivered, r.Status));
    }

    [Theory]
    [InlineData("Y", NotificationResponseAction.Accept)]
    [InlineData("YES", NotificationResponseAction.Accept)]
    [InlineData("OMW", NotificationResponseAction.Accept)]
    public async Task ProcessInboundSmsAsync_ParsesAcceptReplies(string body, NotificationResponseAction expected)
    {
        var response = await _sut.ProcessInboundSmsAsync("+15551234001", body);

        Assert.NotNull(response);
        Assert.Equal(expected, response!.Action);
    }

    [Theory]
    [InlineData("N", NotificationResponseAction.Decline)]
    [InlineData("NO", NotificationResponseAction.Decline)]
    [InlineData("BUSY", NotificationResponseAction.Decline)]
    public async Task ProcessInboundSmsAsync_ParsesDeclineReplies(string body, NotificationResponseAction expected)
    {
        var response = await _sut.ProcessInboundSmsAsync("+15551234001", body);

        Assert.NotNull(response);
        Assert.Equal(expected, response!.Action);
    }

    [Fact]
    public async Task ProcessInboundSmsAsync_ParsesHelpAsNeedHelp()
    {
        var response = await _sut.ProcessInboundSmsAsync("+15551234001", "HELP");

        Assert.NotNull(response);
        Assert.Equal(NotificationResponseAction.NeedHelp, response!.Action);
    }

    [Fact]
    public async Task ProcessInboundSmsAsync_Parses911AsCall911()
    {
        var response = await _sut.ProcessInboundSmsAsync("+15551234001", "911");

        Assert.NotNull(response);
        Assert.Equal(NotificationResponseAction.Call911, response!.Action);
    }

    [Fact]
    public async Task ProcessInboundSmsAsync_ReturnsNull_ForUnrecognizedText()
    {
        var response = await _sut.ProcessInboundSmsAsync("+15551234001", "random gibberish");

        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterPhoneMapping_MapsPhoneToUser()
    {
        _sut.RegisterPhoneMapping("+15559990000", "custom-user-99");

        // Verify the mapping works by sending an SMS and checking the recipient
        var result = await _sut.SendSmsAsync("+15559990000", "test", null);
        Assert.Equal("custom-user-99", result.RecipientUserId);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockNotificationRegistrationAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockNotificationRegistrationAdapterTests
{
    private readonly MockNotificationRegistrationAdapter _sut =
        new(NullLogger<MockNotificationRegistrationAdapter>.Instance);

    [Fact]
    public async Task RegisterDeviceAsync_AddsDeviceToProfile()
    {
        var device = await _sut.RegisterDeviceAsync(
            "new-user", "fcm-token-abc", DevicePlatform.Android, "Pixel 9");

        Assert.Equal(DevicePlatform.Android, device.Platform);
        Assert.Equal("Pixel 9", device.DeviceName);
        Assert.True(device.IsActive);

        var devices = await _sut.GetDevicesAsync("new-user");
        Assert.Contains(devices, d => d.DeviceToken == "fcm-token-abc");
    }

    [Fact]
    public async Task UnregisterDeviceAsync_RemovesDevice()
    {
        var device = await _sut.RegisterDeviceAsync(
            "unreg-user", "token-to-remove", DevicePlatform.iOS);

        var removed = await _sut.UnregisterDeviceAsync("unreg-user", device.DeviceId);

        Assert.True(removed);
        var devices = await _sut.GetDevicesAsync("unreg-user");
        Assert.DoesNotContain(devices, d => d.DeviceId == device.DeviceId);
    }

    [Fact]
    public async Task GetDevicesAsync_ReturnsEmpty_ForUnknownUser()
    {
        var devices = await _sut.GetDevicesAsync("totally-unknown-user");

        Assert.Empty(devices);
    }

    [Fact]
    public async Task GetProfileAsync_CreatesDefaultProfile_ForNewUser()
    {
        var profile = await _sut.GetProfileAsync("brand-new-user");

        Assert.Equal("brand-new-user", profile.UserId);
        Assert.True(profile.PushEnabled);
        Assert.False(profile.SmsEnabled);
        Assert.Empty(profile.Devices);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesLastUpdated()
    {
        var original = await _sut.GetProfileAsync("mock-user-001");
        var before = original.LastUpdated;

        await Task.Delay(10);
        var updated = await _sut.UpdateProfileAsync(original with { SmsEnabled = false });

        Assert.True(updated.LastUpdated >= before);
        Assert.False(updated.SmsEnabled);
    }

    [Theory]
    [InlineData("mock-user-001")]
    [InlineData("mock-user-002")]
    [InlineData("mock-user-003")]
    [InlineData("mock-user-004")]
    [InlineData("mock-user-005")]
    [InlineData("mock-user-006")]
    [InlineData("mock-user-007")]
    [InlineData("mock-user-008")]
    public async Task SeededProfiles_ExistForMockUsers(string userId)
    {
        var profile = await _sut.GetProfileAsync(userId);

        Assert.Equal(userId, profile.UserId);
        Assert.NotNull(profile.DisplayName);
        Assert.NotNull(profile.PhoneNumber);
        Assert.NotEmpty(profile.Devices);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockNotificationTrackingAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockNotificationTrackingAdapterTests
{
    private readonly MockNotificationTrackingAdapter _sut =
        new(NullLogger<MockNotificationTrackingAdapter>.Instance);

    private static NotificationResult CreateTestResult(string notifId = "notif-001") =>
        new(
            NotificationId: notifId, RecipientUserId: "user-001",
            Channel: NotificationChannel.Push,
            Status: NotificationDeliveryStatus.Delivered,
            ExternalMessageId: "mock-ext-001", ErrorMessage: null,
            SentAt: DateTime.UtcNow);

    private static NotificationResponse CreateTestResponse(
        string requestId = "req-001", string responderId = "resp-001") =>
        new(
            ResponseId: Guid.NewGuid().ToString("N")[..12],
            NotificationId: "notif-001", RequestId: requestId,
            ResponderId: responderId,
            Action: NotificationResponseAction.Accept,
            SourceChannel: NotificationChannel.Push,
            RawSmsBody: null,
            ResponderLatitude: 30.27, ResponderLongitude: -97.74,
            RespondedAt: DateTime.UtcNow);

    [Fact]
    public async Task RecordDeliveryAsync_StoresResult()
    {
        var result = CreateTestResult("track-delivery-001");

        await _sut.RecordDeliveryAsync(result);

        var results = await _sut.GetDeliveryResultsAsync("track-delivery-001");
        Assert.Single(results);
        Assert.Equal(NotificationDeliveryStatus.Delivered, results[0].Status);
    }

    [Fact]
    public async Task RecordResponseAsync_StoresResponse()
    {
        var response = CreateTestResponse("track-req-001", "resp-A");

        await _sut.RecordResponseAsync(response);

        var responses = await _sut.GetResponsesAsync("track-req-001");
        Assert.Single(responses);
        Assert.Equal(NotificationResponseAction.Accept, responses[0].Action);
    }

    [Fact]
    public async Task GetDeliveryResultsAsync_ReturnsEmpty_ForUnknown()
    {
        var results = await _sut.GetDeliveryResultsAsync("nonexistent-request");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetResponsesAsync_ReturnsStoredResponses()
    {
        await _sut.RecordResponseAsync(CreateTestResponse("multi-req", "resp-1"));
        await _sut.RecordResponseAsync(CreateTestResponse("multi-req", "resp-2"));
        await _sut.RecordResponseAsync(CreateTestResponse("other-req", "resp-3"));

        var responses = await _sut.GetResponsesAsync("multi-req");

        Assert.Equal(2, responses.Count);
    }
}
