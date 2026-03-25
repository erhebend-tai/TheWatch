// WAL: Tests for all enum types in TheWatch.Shared — verifies member count, presence of
// expected values, and explicit integer assignments where applicable. Enum correctness is
// critical for serialization stability (JSON, Firestore, SQL) and wire compatibility
// between the mobile apps, dashboard, and cloud functions.
//
// Example:
//   var values = Enum.GetValues<ResponseScope>();
//   Assert.Equal(6, values.Length);
//   Assert.Contains(ResponseScope.SilentDuress, values);

using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Tests;

public class EnumTests
{
    // ═══════════════════════════════════════════════════════════════
    // Response Coordination Enums (from IResponseCoordinationPort.cs)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ResponseScope_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<ResponseScope>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(nameof(ResponseScope.CheckIn))]
    [InlineData(nameof(ResponseScope.Neighborhood))]
    [InlineData(nameof(ResponseScope.Community))]
    [InlineData(nameof(ResponseScope.Evacuation))]
    [InlineData(nameof(ResponseScope.SilentDuress))]
    [InlineData(nameof(ResponseScope.Custom))]
    public void ResponseScope_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(ResponseScope), Enum.Parse<ResponseScope>(memberName)));
    }

    [Fact]
    public void EscalationPolicy_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<EscalationPolicy>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(EscalationPolicy.Manual))]
    [InlineData(nameof(EscalationPolicy.TimedEscalation))]
    [InlineData(nameof(EscalationPolicy.Immediate911))]
    [InlineData(nameof(EscalationPolicy.Conditional911))]
    [InlineData(nameof(EscalationPolicy.FullCascade))]
    public void EscalationPolicy_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(EscalationPolicy), Enum.Parse<EscalationPolicy>(memberName)));
    }

    [Fact]
    public void DispatchStrategy_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<DispatchStrategy>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(DispatchStrategy.NearestN))]
    [InlineData(nameof(DispatchStrategy.RadiusBroadcast))]
    [InlineData(nameof(DispatchStrategy.TrustedContactsOnly))]
    [InlineData(nameof(DispatchStrategy.CertifiedFirst))]
    [InlineData(nameof(DispatchStrategy.EmergencyBroadcast))]
    public void DispatchStrategy_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(DispatchStrategy), Enum.Parse<DispatchStrategy>(memberName)));
    }

    [Fact]
    public void ResponseStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<ResponseStatus>();
        Assert.Equal(7, values.Length);
    }

    [Theory]
    [InlineData(nameof(ResponseStatus.Pending))]
    [InlineData(nameof(ResponseStatus.Dispatching))]
    [InlineData(nameof(ResponseStatus.Active))]
    [InlineData(nameof(ResponseStatus.Escalated))]
    [InlineData(nameof(ResponseStatus.Resolved))]
    [InlineData(nameof(ResponseStatus.Cancelled))]
    [InlineData(nameof(ResponseStatus.Expired))]
    public void ResponseStatus_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(ResponseStatus), Enum.Parse<ResponseStatus>(memberName)));
    }

    [Fact]
    public void AckStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<AckStatus>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(AckStatus.Acknowledged))]
    [InlineData(nameof(AckStatus.EnRoute))]
    [InlineData(nameof(AckStatus.OnScene))]
    [InlineData(nameof(AckStatus.Declined))]
    [InlineData(nameof(AckStatus.TimedOut))]
    public void AckStatus_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(AckStatus), Enum.Parse<AckStatus>(memberName)));
    }

    // ═══════════════════════════════════════════════════════════════
    // Notification Enums (from INotificationPort.cs)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationChannel_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<NotificationChannel>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationChannel.Push))]
    [InlineData(nameof(NotificationChannel.Sms))]
    [InlineData(nameof(NotificationChannel.InApp))]
    [InlineData(nameof(NotificationChannel.Email))]
    [InlineData(nameof(NotificationChannel.VoiceCall))]
    [InlineData(nameof(NotificationChannel.AlarmPanel))]
    public void NotificationChannel_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationChannel), Enum.Parse<NotificationChannel>(memberName)));
    }

    [Fact]
    public void NotificationPriority_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<NotificationPriority>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationPriority.Low))]
    [InlineData(nameof(NotificationPriority.Normal))]
    [InlineData(nameof(NotificationPriority.High))]
    [InlineData(nameof(NotificationPriority.Critical))]
    public void NotificationPriority_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationPriority), Enum.Parse<NotificationPriority>(memberName)));
    }

    [Fact]
    public void NotificationCategory_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<NotificationCategory>();
        Assert.Equal(8, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationCategory.SosDispatch))]
    [InlineData(nameof(NotificationCategory.SosUpdate))]
    [InlineData(nameof(NotificationCategory.SosCancelled))]
    [InlineData(nameof(NotificationCategory.SosResolved))]
    [InlineData(nameof(NotificationCategory.EscalationAlert))]
    [InlineData(nameof(NotificationCategory.CheckInRequest))]
    [InlineData(nameof(NotificationCategory.EvacuationNotice))]
    [InlineData(nameof(NotificationCategory.ResponderLocationUpdate))]
    public void NotificationCategory_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationCategory), Enum.Parse<NotificationCategory>(memberName)));
    }

    [Fact]
    public void NotificationDeliveryStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<NotificationDeliveryStatus>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationDeliveryStatus.Queued))]
    [InlineData(nameof(NotificationDeliveryStatus.Sent))]
    [InlineData(nameof(NotificationDeliveryStatus.Delivered))]
    [InlineData(nameof(NotificationDeliveryStatus.Read))]
    [InlineData(nameof(NotificationDeliveryStatus.Failed))]
    [InlineData(nameof(NotificationDeliveryStatus.Expired))]
    public void NotificationDeliveryStatus_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationDeliveryStatus), Enum.Parse<NotificationDeliveryStatus>(memberName)));
    }

    [Fact]
    public void NotificationResponseAction_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<NotificationResponseAction>();
        Assert.Equal(7, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationResponseAction.Accept))]
    [InlineData(nameof(NotificationResponseAction.Decline))]
    [InlineData(nameof(NotificationResponseAction.Acknowledge))]
    [InlineData(nameof(NotificationResponseAction.NeedHelp))]
    [InlineData(nameof(NotificationResponseAction.ImOk))]
    [InlineData(nameof(NotificationResponseAction.Call911))]
    [InlineData(nameof(NotificationResponseAction.ViewDetails))]
    public void NotificationResponseAction_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationResponseAction), Enum.Parse<NotificationResponseAction>(memberName)));
    }

    [Fact]
    public void DevicePlatform_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<DevicePlatform>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(DevicePlatform.Android))]
    [InlineData(nameof(DevicePlatform.iOS))]
    [InlineData(nameof(DevicePlatform.LandlinePhone))]
    [InlineData(nameof(DevicePlatform.AlarmPanel))]
    public void DevicePlatform_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(DevicePlatform), Enum.Parse<DevicePlatform>(memberName)));
    }

    // ═══════════════════════════════════════════════════════════════
    // IoT Enums (from IIoTAlertPort.cs)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IoTSource_HasExpected14Members()
    {
        var values = Enum.GetValues<IoTSource>();
        Assert.Equal(14, values.Length);
    }

    [Theory]
    [InlineData(nameof(IoTSource.Alexa))]
    [InlineData(nameof(IoTSource.GoogleHome))]
    [InlineData(nameof(IoTSource.SmartThings))]
    [InlineData(nameof(IoTSource.HomeKit))]
    [InlineData(nameof(IoTSource.IFTTT))]
    [InlineData(nameof(IoTSource.CustomWebhook))]
    [InlineData(nameof(IoTSource.Ring))]
    [InlineData(nameof(IoTSource.Wyze))]
    [InlineData(nameof(IoTSource.Tuya))]
    [InlineData(nameof(IoTSource.ZigbeeDirect))]
    [InlineData(nameof(IoTSource.ZWaveDirect))]
    [InlineData(nameof(IoTSource.Matter))]
    [InlineData(nameof(IoTSource.LandlinePhone))]
    [InlineData(nameof(IoTSource.AlarmSystem))]
    public void IoTSource_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(IoTSource), Enum.Parse<IoTSource>(memberName)));
    }

    [Fact]
    public void IoTAlertStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<IoTAlertStatus>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(nameof(IoTAlertStatus.Dispatched))]
    [InlineData(nameof(IoTAlertStatus.PendingConfirmation))]
    [InlineData(nameof(IoTAlertStatus.UserNotMapped))]
    [InlineData(nameof(IoTAlertStatus.Throttled))]
    [InlineData(nameof(IoTAlertStatus.Cancelled))]
    [InlineData(nameof(IoTAlertStatus.Error))]
    public void IoTAlertStatus_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(IoTAlertStatus), Enum.Parse<IoTAlertStatus>(memberName)));
    }

    [Fact]
    public void IoTCheckInStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<IoTCheckInStatus>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(IoTCheckInStatus.Ok))]
    [InlineData(nameof(IoTCheckInStatus.NeedHelp))]
    [InlineData(nameof(IoTCheckInStatus.FeelingUnwell))]
    [InlineData(nameof(IoTCheckInStatus.Missed))]
    public void IoTCheckInStatus_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(IoTCheckInStatus), Enum.Parse<IoTCheckInStatus>(memberName)));
    }

    [Fact]
    public void IoTCheckInResultStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<IoTCheckInResultStatus>();
        Assert.Equal(4, values.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phrase Detection Enums (from IPhraseDetectionPort.cs)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PhraseType_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<PhraseType>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(PhraseType.Duress))]
    [InlineData(nameof(PhraseType.ClearWord))]
    [InlineData(nameof(PhraseType.Custom))]
    public void PhraseType_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(PhraseType), Enum.Parse<PhraseType>(memberName)));
    }

    [Fact]
    public void PhraseMatchStrategy_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<PhraseMatchStrategy>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(PhraseMatchStrategy.Exact))]
    [InlineData(nameof(PhraseMatchStrategy.Fuzzy))]
    [InlineData(nameof(PhraseMatchStrategy.Substring))]
    [InlineData(nameof(PhraseMatchStrategy.Phonetic))]
    public void PhraseMatchStrategy_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(PhraseMatchStrategy), Enum.Parse<PhraseMatchStrategy>(memberName)));
    }

    // ═══════════════════════════════════════════════════════════════
    // Quick Tap Enum (from IQuickTapDetectionPort.cs)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TapTriggerType_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<TapTriggerType>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(TapTriggerType.VolumeButton))]
    [InlineData(nameof(TapTriggerType.PowerButton))]
    [InlineData(nameof(TapTriggerType.ScreenTap))]
    [InlineData(nameof(TapTriggerType.DeviceShake))]
    public void TapTriggerType_ContainsExpectedValue(string memberName)
    {
        Assert.True(Enum.IsDefined(typeof(TapTriggerType), Enum.Parse<TapTriggerType>(memberName)));
    }

    // ═══════════════════════════════════════════════════════════════
    // Evidence / Submission Enums (from TheWatch.Shared.Enums)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SubmissionStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<SubmissionStatus>();
        Assert.Equal(7, values.Length);
    }

    [Theory]
    [InlineData(SubmissionStatus.Pending, 0)]
    [InlineData(SubmissionStatus.Uploading, 1)]
    [InlineData(SubmissionStatus.Processing, 2)]
    [InlineData(SubmissionStatus.Available, 3)]
    [InlineData(SubmissionStatus.Rejected, 4)]
    [InlineData(SubmissionStatus.Archived, 5)]
    [InlineData(SubmissionStatus.Expired, 6)]
    public void SubmissionStatus_HasCorrectIntegerValues(SubmissionStatus status, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)status);
    }

    [Fact]
    public void SubmissionPhase_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<SubmissionPhase>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(SubmissionPhase.PreIncident, 0)]
    [InlineData(SubmissionPhase.Active, 1)]
    [InlineData(SubmissionPhase.PostIncident, 2)]
    public void SubmissionPhase_HasCorrectIntegerValues(SubmissionPhase phase, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)phase);
    }

    [Fact]
    public void SubmissionType_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<SubmissionType>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(SubmissionType.Image, 0)]
    [InlineData(SubmissionType.Audio, 1)]
    [InlineData(SubmissionType.Video, 2)]
    [InlineData(SubmissionType.Text, 3)]
    [InlineData(SubmissionType.Document, 4)]
    [InlineData(SubmissionType.Survey, 5)]
    public void SubmissionType_HasCorrectIntegerValues(SubmissionType type, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)type);
    }

    // ═══════════════════════════════════════════════════════════════
    // Storage / Audit / Feature Enums
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StorageScope_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<StorageScope>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(StorageScope.Local, 0)]
    [InlineData(StorageScope.Remote, 1)]
    [InlineData(StorageScope.Cached, 2)]
    [InlineData(StorageScope.Replicated, 3)]
    public void StorageScope_HasCorrectIntegerValues(StorageScope scope, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)scope);
    }

    [Fact]
    public void AuditAction_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<AuditAction>();
        Assert.Equal(160, values.Length);
    }

    [Theory]
    [InlineData(AuditAction.Create, 0)]
    [InlineData(AuditAction.Read, 1)]
    [InlineData(AuditAction.Update, 2)]
    [InlineData(AuditAction.Delete, 3)]
    [InlineData(AuditAction.Login, 4)]
    [InlineData(AuditAction.Logout, 5)]
    [InlineData(AuditAction.SOSTrigger, 6)]
    [InlineData(AuditAction.SOSCancel, 7)]
    [InlineData(AuditAction.AlertAcknowledge, 8)]
    [InlineData(AuditAction.AlertEscalate, 9)]
    [InlineData(AuditAction.EvidenceCapture, 10)]
    [InlineData(AuditAction.LocationUpdate, 11)]
    [InlineData(AuditAction.ConfigChange, 12)]
    [InlineData(AuditAction.PermissionGrant, 13)]
    [InlineData(AuditAction.PermissionRevoke, 14)]
    public void AuditAction_HasCorrectIntegerValues(AuditAction action, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)action);
    }

    [Fact]
    public void FeatureCategory_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<FeatureCategory>();
        Assert.Equal(18, values.Length);
    }

    [Theory]
    [InlineData(FeatureCategory.CoreInfrastructure, 0)]
    [InlineData(FeatureCategory.MobileApp, 1)]
    [InlineData(FeatureCategory.DashboardWeb, 2)]
    [InlineData(FeatureCategory.Standards, 17)]
    public void FeatureCategory_HasCorrectIntegerValues(FeatureCategory category, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)category);
    }

    [Fact]
    public void FeatureStatus_HasExpectedMemberCount()
    {
        var values = Enum.GetValues<FeatureStatus>();
        Assert.Equal(7, values.Length);
    }

    [Theory]
    [InlineData(FeatureStatus.Planned, 0)]
    [InlineData(FeatureStatus.InProgress, 1)]
    [InlineData(FeatureStatus.InReview, 2)]
    [InlineData(FeatureStatus.Testing, 3)]
    [InlineData(FeatureStatus.Completed, 4)]
    [InlineData(FeatureStatus.Blocked, 5)]
    [InlineData(FeatureStatus.Deferred, 6)]
    public void FeatureStatus_HasCorrectIntegerValues(FeatureStatus status, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)status);
    }
}
