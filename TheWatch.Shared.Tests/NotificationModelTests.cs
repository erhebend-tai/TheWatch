// WAL: Focused tests for notification-specific models — NotificationPayload deep link format,
// NotificationResult delivery status variants, NotificationResponse action parsing,
// UserNotificationProfile device list management, DeviceRegistration platform variants,
// and EligibleResponder certification/vehicle logic.
//
// These models are the data contracts between the mobile apps, cloud functions, and dashboard.
// Serialization correctness is critical — a wrong field mapping could prevent SOS dispatch.
//
// Example:
//   var profile = new UserNotificationProfile("u-1", "Jane", "+1555", true, true,
//       new List<DeviceRegistration> { androidDevice, iosDevice },
//       NotificationPriority.Normal, DateTime.UtcNow);
//   Assert.Equal(2, profile.Devices.Count);

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Shared.Tests;

public class NotificationModelTests
{
    // ═══════════════════════════════════════════════════════════════
    // NotificationPayload — deep link format and field coverage
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationPayload_AllNullableFields_CanBeNull()
    {
        var payload = new NotificationPayload(
            NotificationId: "n-1",
            RecipientUserId: "u-1",
            RecipientDeviceToken: null,
            RecipientPhoneNumber: null,
            Category: NotificationCategory.SosDispatch,
            Priority: NotificationPriority.High,
            PreferredChannel: NotificationChannel.Push,
            Title: "Alert",
            Body: "Help needed",
            Subtitle: null,
            DeepLink: null,
            RequestId: null,
            RequestorName: null,
            Scope: null,
            IncidentLatitude: null,
            IncidentLongitude: null,
            DistanceMeters: null,
            SmsReplyInstructions: null,
            CreatedAt: DateTime.UtcNow,
            ExpiresAfter: null);

        Assert.Null(payload.RecipientDeviceToken);
        Assert.Null(payload.RecipientPhoneNumber);
        Assert.Null(payload.Subtitle);
        Assert.Null(payload.DeepLink);
        Assert.Null(payload.RequestId);
        Assert.Null(payload.RequestorName);
        Assert.Null(payload.Scope);
        Assert.Null(payload.IncidentLatitude);
        Assert.Null(payload.IncidentLongitude);
        Assert.Null(payload.DistanceMeters);
        Assert.Null(payload.SmsReplyInstructions);
        Assert.Null(payload.ExpiresAfter);
    }

    [Fact]
    public void NotificationPayload_DeepLink_ContainsRequestId()
    {
        var payload = new NotificationPayload(
            "n-1", "u-1", "token", null,
            NotificationCategory.SosDispatch,
            NotificationPriority.Critical,
            NotificationChannel.Push,
            "Alert", "Body", null,
            "thewatch://response/req-789",
            "req-789", null, null, null, null, null, null,
            DateTime.UtcNow, null);

        Assert.NotNull(payload.DeepLink);
        Assert.Contains("req-789", payload.DeepLink);
    }

    [Theory]
    [InlineData(NotificationPriority.Low)]
    [InlineData(NotificationPriority.Normal)]
    [InlineData(NotificationPriority.High)]
    [InlineData(NotificationPriority.Critical)]
    public void NotificationPayload_AllPriorityLevels_AreValid(NotificationPriority priority)
    {
        var payload = new NotificationPayload(
            "n-1", "u-1", null, null,
            NotificationCategory.SosDispatch, priority,
            NotificationChannel.Push,
            "T", "B", null, null, null, null, null, null, null, null, null,
            DateTime.UtcNow, null);

        Assert.Equal(priority, payload.Priority);
    }

    [Theory]
    [InlineData(NotificationChannel.Push)]
    [InlineData(NotificationChannel.Sms)]
    [InlineData(NotificationChannel.InApp)]
    [InlineData(NotificationChannel.Email)]
    [InlineData(NotificationChannel.VoiceCall)]
    [InlineData(NotificationChannel.AlarmPanel)]
    public void NotificationPayload_AllChannels_AreValid(NotificationChannel channel)
    {
        var payload = new NotificationPayload(
            "n-1", "u-1", null, null,
            NotificationCategory.SosDispatch,
            NotificationPriority.Normal, channel,
            "T", "B", null, null, null, null, null, null, null, null, null,
            DateTime.UtcNow, null);

        Assert.Equal(channel, payload.PreferredChannel);
    }

    // ═══════════════════════════════════════════════════════════════
    // NotificationResult — delivery status variants
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(NotificationDeliveryStatus.Queued)]
    [InlineData(NotificationDeliveryStatus.Sent)]
    [InlineData(NotificationDeliveryStatus.Delivered)]
    [InlineData(NotificationDeliveryStatus.Read)]
    [InlineData(NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Expired)]
    public void NotificationResult_AllStatuses_CanBeConstructed(NotificationDeliveryStatus status)
    {
        var result = new NotificationResult(
            "n-1", "u-1", NotificationChannel.Push, status,
            null, null, DateTime.UtcNow);

        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void NotificationResult_QueuedStatus_NoExternalId()
    {
        var result = new NotificationResult(
            "n-1", "u-1", NotificationChannel.Push,
            NotificationDeliveryStatus.Queued,
            null, null, DateTime.UtcNow);

        Assert.Null(result.ExternalMessageId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void NotificationResult_SentViaSms_HasExternalId()
    {
        var result = new NotificationResult(
            "n-1", "u-1", NotificationChannel.Sms,
            NotificationDeliveryStatus.Sent,
            "SM1234567890abcdef", null, DateTime.UtcNow);

        Assert.Equal("SM1234567890abcdef", result.ExternalMessageId);
    }

    [Fact]
    public void NotificationResult_FailedDelivery_HasErrorAndNoExternalId()
    {
        var result = new NotificationResult(
            "n-1", "u-1", NotificationChannel.Push,
            NotificationDeliveryStatus.Failed,
            null, "NotRegistered", DateTime.UtcNow);

        Assert.Equal(NotificationDeliveryStatus.Failed, result.Status);
        Assert.Null(result.ExternalMessageId);
        Assert.Equal("NotRegistered", result.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // NotificationResponse — action variants
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(NotificationResponseAction.Accept)]
    [InlineData(NotificationResponseAction.Decline)]
    [InlineData(NotificationResponseAction.Acknowledge)]
    [InlineData(NotificationResponseAction.NeedHelp)]
    [InlineData(NotificationResponseAction.ImOk)]
    [InlineData(NotificationResponseAction.Call911)]
    [InlineData(NotificationResponseAction.ViewDetails)]
    public void NotificationResponse_AllActions_CanBeConstructed(NotificationResponseAction action)
    {
        var response = new NotificationResponse(
            "resp-1", "notif-1", "req-1", "user-1",
            action, NotificationChannel.Push,
            null, null, null, DateTime.UtcNow);

        Assert.Equal(action, response.Action);
    }

    [Fact]
    public void NotificationResponse_SmsAccept_HasRawBodyY()
    {
        var response = new NotificationResponse(
            "resp-1", "notif-1", "req-1", "user-1",
            NotificationResponseAction.Accept,
            NotificationChannel.Sms,
            "Y", null, null, DateTime.UtcNow);

        Assert.Equal(NotificationChannel.Sms, response.SourceChannel);
        Assert.Equal("Y", response.RawSmsBody);
    }

    [Fact]
    public void NotificationResponse_SmsDecline_HasRawBodyN()
    {
        var response = new NotificationResponse(
            "resp-1", "notif-1", "req-1", "user-1",
            NotificationResponseAction.Decline,
            NotificationChannel.Sms,
            "N", null, null, DateTime.UtcNow);

        Assert.Equal("N", response.RawSmsBody);
    }

    [Fact]
    public void NotificationResponse_PushSource_NoRawSmsBody()
    {
        var response = new NotificationResponse(
            "resp-1", "notif-1", "req-1", "user-1",
            NotificationResponseAction.Accept,
            NotificationChannel.Push,
            null, 30.27, -97.74, DateTime.UtcNow);

        Assert.Null(response.RawSmsBody);
        Assert.Equal(30.27, response.ResponderLatitude);
        Assert.Equal(-97.74, response.ResponderLongitude);
    }

    // ═══════════════════════════════════════════════════════════════
    // UserNotificationProfile — device list management
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UserNotificationProfile_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var devices = new List<DeviceRegistration>
        {
            new("dev-1", "fcm-token-1", DevicePlatform.Android, "Pixel 8", now, now, true),
            new("dev-2", "apns-token-1", DevicePlatform.iOS, "iPhone 15", now, now, true)
        };

        var profile = new UserNotificationProfile(
            UserId: "user-1",
            DisplayName: "Jane Doe",
            PhoneNumber: "+15125551234",
            SmsEnabled: true,
            PushEnabled: true,
            Devices: devices,
            MinimumPriority: NotificationPriority.Normal,
            LastUpdated: now);

        Assert.Equal("user-1", profile.UserId);
        Assert.Equal("Jane Doe", profile.DisplayName);
        Assert.Equal("+15125551234", profile.PhoneNumber);
        Assert.True(profile.SmsEnabled);
        Assert.True(profile.PushEnabled);
        Assert.Equal(2, profile.Devices.Count);
        Assert.Equal(NotificationPriority.Normal, profile.MinimumPriority);
    }

    [Fact]
    public void UserNotificationProfile_EmptyDeviceList_IsValid()
    {
        var profile = new UserNotificationProfile(
            "user-1", "Jane", null, false, true,
            new List<DeviceRegistration>(),
            NotificationPriority.Low, DateTime.UtcNow);

        Assert.Empty(profile.Devices);
    }

    [Fact]
    public void UserNotificationProfile_NullableFields_CanBeNull()
    {
        var profile = new UserNotificationProfile(
            "user-1", null, null, false, false,
            new List<DeviceRegistration>(),
            NotificationPriority.Low, DateTime.UtcNow);

        Assert.Null(profile.DisplayName);
        Assert.Null(profile.PhoneNumber);
    }

    // ═══════════════════════════════════════════════════════════════
    // DeviceRegistration — platform variants
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DeviceRegistration_Android_Construction()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-1", "fcm-token-abc", DevicePlatform.Android,
            "Pixel 8 Pro", now, now, true);

        Assert.Equal(DevicePlatform.Android, reg.Platform);
        Assert.Equal("fcm-token-abc", reg.DeviceToken);
        Assert.True(reg.IsActive);
    }

    [Fact]
    public void DeviceRegistration_iOS_Construction()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-2", "apns-token-xyz", DevicePlatform.iOS,
            "iPhone 15 Pro", now, now, true);

        Assert.Equal(DevicePlatform.iOS, reg.Platform);
        Assert.Equal("apns-token-xyz", reg.DeviceToken);
    }

    [Fact]
    public void DeviceRegistration_LandlinePhone_Construction()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-3", "sip:+15125559999@gateway.local", DevicePlatform.LandlinePhone,
            "Kitchen Landline", now, now, true);

        Assert.Equal(DevicePlatform.LandlinePhone, reg.Platform);
        Assert.Equal("Kitchen Landline", reg.DeviceName);
    }

    [Fact]
    public void DeviceRegistration_AlarmPanel_Construction()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-4", "alarm-panel-001", DevicePlatform.AlarmPanel,
            "Front Door Keypad", now, now, true);

        Assert.Equal(DevicePlatform.AlarmPanel, reg.Platform);
    }

    [Fact]
    public void DeviceRegistration_InactiveDevice_IsActiveFalse()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-5", "old-token", DevicePlatform.Android,
            "Old Phone", now.AddDays(-90), now.AddDays(-30), false);

        Assert.False(reg.IsActive);
    }

    [Fact]
    public void DeviceRegistration_NullDeviceName_IsAllowed()
    {
        var now = DateTime.UtcNow;
        var reg = new DeviceRegistration(
            "dev-6", "token", DevicePlatform.Android,
            null, now, now, true);

        Assert.Null(reg.DeviceName);
    }

    // ═══════════════════════════════════════════════════════════════
    // EligibleResponder — certifications and vehicle status
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EligibleResponder_WithCertifications_RetainsAll()
    {
        var responder = new EligibleResponder(
            "u-1", "Jane EMT", 30.0, -97.0, 200.0,
            new[] { "EMT", "CPR", "FIRST_AID" },
            true, true, DateTime.UtcNow);

        Assert.Equal(3, responder.Certifications!.Length);
        Assert.Contains("EMT", responder.Certifications);
        Assert.Contains("CPR", responder.Certifications);
        Assert.Contains("FIRST_AID", responder.Certifications);
    }

    [Fact]
    public void EligibleResponder_NoCertifications_IsNull()
    {
        var responder = new EligibleResponder(
            "u-2", "Bob Volunteer", 30.0, -97.0, 500.0,
            null, false, false, DateTime.UtcNow);

        Assert.Null(responder.Certifications);
        Assert.False(responder.IsFirstOnSceneWilling);
        Assert.False(responder.HasVehicle);
    }

    [Fact]
    public void EligibleResponder_WithVehicle_HasVehicleTrue()
    {
        var responder = new EligibleResponder(
            "u-3", "Carlos", 30.0, -97.0, 3000.0,
            null, true, true, DateTime.UtcNow);

        Assert.True(responder.HasVehicle);
    }

    [Fact]
    public void EligibleResponder_WithoutVehicle_HasVehicleFalse()
    {
        var responder = new EligibleResponder(
            "u-4", "Dana", 30.0, -97.0, 800.0,
            null, true, false, DateTime.UtcNow);

        Assert.False(responder.HasVehicle);
    }

    [Fact]
    public void EligibleResponder_DistanceMeters_IsPositive()
    {
        var responder = new EligibleResponder(
            "u-5", "Eve", 30.0, -97.0, 0.5,
            null, true, false, DateTime.UtcNow);

        Assert.True(responder.DistanceMeters > 0);
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(1600.0)]
    [InlineData(5000.0)]
    [InlineData(50000.0)]
    public void EligibleResponder_VariousDistances_AreStored(double distance)
    {
        var responder = new EligibleResponder(
            "u-6", "Responder", 30.0, -97.0, distance,
            null, false, false, DateTime.UtcNow);

        Assert.Equal(distance, responder.DistanceMeters);
    }

    // ═══════════════════════════════════════════════════════════════
    // IoTLocation — simple GPS record
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IoTLocation_Construction_RetainsAllFields()
    {
        var location = new IoTLocation(
            30.2672, -97.7431, 10.0, "123 Main St", "9q9hvb");

        Assert.Equal(30.2672, location.Latitude);
        Assert.Equal(-97.7431, location.Longitude);
        Assert.Equal(10.0, location.AccuracyMeters);
        Assert.Equal("123 Main St", location.Address);
        Assert.Equal("9q9hvb", location.GeoHash);
    }

    [Fact]
    public void IoTLocation_MinimalConstruction_OptionalsAreNull()
    {
        var location = new IoTLocation(30.0, -97.0);
        Assert.Null(location.AccuracyMeters);
        Assert.Null(location.Address);
        Assert.Null(location.GeoHash);
    }

    // ═══════════════════════════════════════════════════════════════
    // SpeechTranscriptionEventArgs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SpeechTranscriptionEventArgs_RequiredProperties_AreSet()
    {
        var now = DateTime.UtcNow;
        var args = new SpeechTranscriptionEventArgs
        {
            TranscribedText = "help me please",
            Confidence = 0.92f,
            IsFinal = true,
            Timestamp = now
        };

        Assert.Equal("help me please", args.TranscribedText);
        Assert.Equal(0.92f, args.Confidence);
        Assert.True(args.IsFinal);
        Assert.Equal(now, args.Timestamp);
    }

    [Fact]
    public void SpeechTranscriptionEventArgs_PartialResult_IsFinalFalse()
    {
        var args = new SpeechTranscriptionEventArgs
        {
            TranscribedText = "help",
            Confidence = 0.5f,
            IsFinal = false,
            Timestamp = DateTime.UtcNow
        };

        Assert.False(args.IsFinal);
    }
}
