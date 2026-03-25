// WAL: Tests for all domain model records and classes in TheWatch.Shared.Domain.Models
// and TheWatch.Shared.Domain.Ports (record types defined alongside port interfaces).
// Covers construction, default values, factory methods, and computed properties.
//
// Example:
//   var result = StorageResult<string>.Ok("hello", "etag-1");
//   Assert.True(result.Success);
//   Assert.Equal("hello", result.Data);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Tests;

public class DomainModelTests
{
    // ═══════════════════════════════════════════════════════════════
    // StorageResult<T> — envelope with Ok/Fail factory methods
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StorageResult_Ok_SetsSuccessTrue()
    {
        var result = StorageResult<string>.Ok("data");
        Assert.True(result.Success);
    }

    [Fact]
    public void StorageResult_Ok_SetsData()
    {
        var result = StorageResult<int>.Ok(42);
        Assert.Equal(42, result.Data);
    }

    [Fact]
    public void StorageResult_Ok_WithETag_SetsETag()
    {
        var result = StorageResult<string>.Ok("data", "etag-abc");
        Assert.Equal("etag-abc", result.ETag);
    }

    [Fact]
    public void StorageResult_Ok_WithoutETag_ETagIsNull()
    {
        var result = StorageResult<string>.Ok("data");
        Assert.Null(result.ETag);
    }

    [Fact]
    public void StorageResult_Fail_SetsSuccessFalse()
    {
        var result = StorageResult<string>.Fail("not found");
        Assert.False(result.Success);
    }

    [Fact]
    public void StorageResult_Fail_SetsErrorMessage()
    {
        var result = StorageResult<string>.Fail("timeout");
        Assert.Equal("timeout", result.ErrorMessage);
    }

    [Fact]
    public void StorageResult_Fail_DataIsDefault()
    {
        var result = StorageResult<int>.Fail("error");
        Assert.Equal(default, result.Data);
    }

    [Fact]
    public void StorageResult_DefaultConstruction_SuccessIsFalse()
    {
        var result = new StorageResult<string>();
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ETag);
    }

    // ═══════════════════════════════════════════════════════════════
    // OfflineQueueEntry — offline-first queue item
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void OfflineQueueEntry_DefaultConstruction_GeneratesId()
    {
        var entry = new OfflineQueueEntry();
        Assert.False(string.IsNullOrWhiteSpace(entry.Id));
    }

    [Fact]
    public void OfflineQueueEntry_DefaultConstruction_SetsDefaults()
    {
        var entry = new OfflineQueueEntry();
        Assert.Equal(string.Empty, entry.OperationType);
        Assert.Equal(string.Empty, entry.EntityType);
        Assert.Equal(string.Empty, entry.SerializedPayload);
        Assert.Equal(0, entry.RetryCount);
        Assert.False(entry.IsSynced);
    }

    [Fact]
    public void OfflineQueueEntry_DefaultConstruction_QueuedAtIsReasonable()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var entry = new OfflineQueueEntry();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(entry.QueuedAt, before, after);
    }

    [Fact]
    public void OfflineQueueEntry_SetProperties_RetainsValues()
    {
        var entry = new OfflineQueueEntry
        {
            OperationType = "Create",
            EntityType = "Alert",
            SerializedPayload = "{\"key\":\"value\"}",
            RetryCount = 3,
            IsSynced = true
        };

        Assert.Equal("Create", entry.OperationType);
        Assert.Equal("Alert", entry.EntityType);
        Assert.Equal("{\"key\":\"value\"}", entry.SerializedPayload);
        Assert.Equal(3, entry.RetryCount);
        Assert.True(entry.IsSynced);
    }

    // ═══════════════════════════════════════════════════════════════
    // EvidenceSubmission — core evidence entity
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EvidenceSubmission_DefaultConstruction_GeneratesId()
    {
        var submission = new EvidenceSubmission();
        Assert.False(string.IsNullOrWhiteSpace(submission.Id));
    }

    [Fact]
    public void EvidenceSubmission_DefaultConstruction_StatusIsPending()
    {
        var submission = new EvidenceSubmission();
        Assert.Equal(SubmissionStatus.Pending, submission.Status);
    }

    [Fact]
    public void EvidenceSubmission_AllFieldsPopulated_RetainsValues()
    {
        var now = DateTime.UtcNow;
        var submission = new EvidenceSubmission
        {
            Id = "ev-001",
            RequestId = "req-123",
            UserId = "user-456",
            SubmitterId = "user-789",
            Phase = SubmissionPhase.Active,
            SubmissionType = SubmissionType.Image,
            Title = "Kitchen fire",
            Description = "Smoke visible from kitchen",
            Latitude = 30.2672,
            Longitude = -97.7431,
            Accuracy = 5.0,
            ContentHash = "sha256-abc123",
            MimeType = "image/jpeg",
            FileSizeBytes = 2_048_000,
            BlobReference = "evidence/req-123/img-001.jpg",
            ThumbnailBlobReference = "evidence/req-123/thumb-001.jpg",
            Status = SubmissionStatus.Available,
            SubmittedAt = now,
            ProcessedAt = now.AddSeconds(30),
            ExpiresAt = now.AddDays(90),
            IsOfflineSubmission = true,
            OfflineCapturedAt = now.AddMinutes(-5),
            DeviceId = "device-789",
            DeviceModel = "Pixel 8",
            AppVersion = "1.0.0"
        };

        Assert.Equal("ev-001", submission.Id);
        Assert.Equal("req-123", submission.RequestId);
        Assert.Equal("user-456", submission.UserId);
        Assert.Equal("user-789", submission.SubmitterId);
        Assert.Equal(SubmissionPhase.Active, submission.Phase);
        Assert.Equal(SubmissionType.Image, submission.SubmissionType);
        Assert.Equal("Kitchen fire", submission.Title);
        Assert.Equal("Smoke visible from kitchen", submission.Description);
        Assert.Equal(30.2672, submission.Latitude);
        Assert.Equal(-97.7431, submission.Longitude);
        Assert.Equal(5.0, submission.Accuracy);
        Assert.Equal("sha256-abc123", submission.ContentHash);
        Assert.Equal("image/jpeg", submission.MimeType);
        Assert.Equal(2_048_000, submission.FileSizeBytes);
        Assert.Equal("evidence/req-123/img-001.jpg", submission.BlobReference);
        Assert.Equal("evidence/req-123/thumb-001.jpg", submission.ThumbnailBlobReference);
        Assert.Equal(SubmissionStatus.Available, submission.Status);
        Assert.True(submission.IsOfflineSubmission);
        Assert.NotNull(submission.OfflineCapturedAt);
        Assert.Equal("device-789", submission.DeviceId);
        Assert.Equal("Pixel 8", submission.DeviceModel);
        Assert.Equal("1.0.0", submission.AppVersion);
    }

    [Fact]
    public void EvidenceSubmission_NullableFields_DefaultToNull()
    {
        var submission = new EvidenceSubmission();
        Assert.Null(submission.RequestId);
        Assert.Null(submission.Title);
        Assert.Null(submission.Description);
        Assert.Null(submission.Accuracy);
        Assert.Null(submission.ContentHash);
        Assert.Null(submission.MimeType);
        Assert.Null(submission.BlobReference);
        Assert.Null(submission.ThumbnailBlobReference);
        Assert.Null(submission.ProcessedAt);
        Assert.Null(submission.ExpiresAt);
        Assert.Null(submission.OfflineCapturedAt);
        Assert.Null(submission.DeviceId);
        Assert.Null(submission.DeviceModel);
        Assert.Null(submission.AppVersion);
    }

    // ═══════════════════════════════════════════════════════════════
    // AuditEntry — Merkle-chained audit record
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AuditEntry_DefaultConstruction_GeneratesIdAndCorrelationId()
    {
        var entry = new AuditEntry();
        Assert.False(string.IsNullOrWhiteSpace(entry.Id));
        Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
        Assert.NotEqual(entry.Id, entry.CorrelationId);
    }

    [Fact]
    public void AuditEntry_DefaultConstruction_HashIsEmpty()
    {
        var entry = new AuditEntry();
        Assert.Equal(string.Empty, entry.Hash);
    }

    [Fact]
    public void AuditEntry_SetProperties_RetainsValues()
    {
        var entry = new AuditEntry
        {
            UserId = "user-1",
            Action = AuditAction.SOSTrigger,
            EntityType = "Alert",
            EntityId = "alert-42",
            OldValue = null,
            NewValue = "{\"status\":\"active\"}",
            IpAddress = "192.168.1.1",
            Hash = "abc123",
            PreviousHash = "xyz789"
        };

        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(AuditAction.SOSTrigger, entry.Action);
        Assert.Equal("Alert", entry.EntityType);
        Assert.Equal("alert-42", entry.EntityId);
        Assert.Null(entry.OldValue);
        Assert.Equal("{\"status\":\"active\"}", entry.NewValue);
        Assert.Equal("192.168.1.1", entry.IpAddress);
        Assert.Equal("abc123", entry.Hash);
        Assert.Equal("xyz789", entry.PreviousHash);
    }

    // ═══════════════════════════════════════════════════════════════
    // SpatialQuery / SpatialResult
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SpatialQuery_DefaultConstruction_HasSensibleDefaults()
    {
        var query = new SpatialQuery();
        Assert.Equal(1000, query.RadiusMeters);
        Assert.Equal(50, query.MaxResults);
        Assert.Null(query.RingLevel);
    }

    [Fact]
    public void SpatialQuery_SetProperties_RetainsValues()
    {
        var query = new SpatialQuery
        {
            Latitude = 30.2672,
            Longitude = -97.7431,
            RadiusMeters = 500,
            MaxResults = 25,
            RingLevel = 2
        };

        Assert.Equal(30.2672, query.Latitude);
        Assert.Equal(-97.7431, query.Longitude);
        Assert.Equal(500, query.RadiusMeters);
        Assert.Equal(25, query.MaxResults);
        Assert.Equal(2, query.RingLevel);
    }

    [Fact]
    public void SpatialResult_DefaultConstruction_HasEmptyDefaults()
    {
        var result = new SpatialResult();
        Assert.Equal(string.Empty, result.EntityId);
        Assert.Equal(string.Empty, result.EntityType);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void SpatialResult_SetProperties_RetainsValues()
    {
        var result = new SpatialResult
        {
            EntityId = "vol-42",
            EntityType = "Volunteer",
            Latitude = 30.27,
            Longitude = -97.74,
            DistanceMeters = 312.5,
            RingLevel = 1,
            Metadata = new Dictionary<string, string> { ["cert"] = "EMT" }
        };

        Assert.Equal("vol-42", result.EntityId);
        Assert.Equal("Volunteer", result.EntityType);
        Assert.Equal(312.5, result.DistanceMeters);
        Assert.Equal(1, result.RingLevel);
        Assert.NotNull(result.Metadata);
        Assert.Equal("EMT", result.Metadata["cert"]);
    }

    // ═══════════════════════════════════════════════════════════════
    // WorkItem
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void WorkItem_DefaultConstruction_HasEmptyStrings()
    {
        var item = new WorkItem();
        Assert.Equal(string.Empty, item.Id);
        Assert.Equal(string.Empty, item.Title);
        Assert.Equal(string.Empty, item.Description);
        Assert.Equal(string.Empty, item.Milestone);
    }

    [Fact]
    public void WorkItem_SetProperties_RetainsValues()
    {
        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            Id = "wi-1",
            Title = "Implement SOS button",
            Description = "Add panic button to main screen",
            Milestone = "M1",
            Platform = Platform.Android,
            AssignedAgent = "Claude Code",
            Status = WorkItemStatus.InProgress,
            Priority = WorkItemPriority.Critical,
            Type = WorkItemType.Feature,
            BranchName = "feature/sos-button",
            PrUrl = "https://github.com/example/pr/1",
            CreatedAt = now,
            UpdatedAt = now
        };

        Assert.Equal("wi-1", item.Id);
        Assert.Equal("Implement SOS button", item.Title);
        Assert.Equal(Platform.Android, item.Platform);
        Assert.Equal("Claude Code", item.AssignedAgent);
        Assert.Equal(WorkItemStatus.InProgress, item.Status);
        Assert.Equal(WorkItemPriority.Critical, item.Priority);
        Assert.Equal(WorkItemType.Feature, item.Type);
    }

    // ═══════════════════════════════════════════════════════════════
    // Milestone — includes computed PercentComplete
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Milestone_DefaultConstruction_HasEmptyStrings()
    {
        var ms = new Milestone();
        Assert.Equal(string.Empty, ms.Id);
        Assert.Equal(string.Empty, ms.Name);
        Assert.Equal(string.Empty, ms.Description);
    }

    [Fact]
    public void Milestone_PercentComplete_ZeroIssues_ReturnsZero()
    {
        var ms = new Milestone { TotalIssues = 0, ClosedIssues = 0 };
        Assert.Equal(0, ms.PercentComplete);
    }

    [Fact]
    public void Milestone_PercentComplete_AllClosed_Returns100()
    {
        var ms = new Milestone { TotalIssues = 10, ClosedIssues = 10 };
        Assert.Equal(100, ms.PercentComplete);
    }

    [Fact]
    public void Milestone_PercentComplete_PartialProgress_CalculatesCorrectly()
    {
        var ms = new Milestone { TotalIssues = 4, ClosedIssues = 1 };
        Assert.Equal(25, ms.PercentComplete);
    }

    [Fact]
    public void Milestone_PercentComplete_IntegerDivision_Truncates()
    {
        // 1/3 = 33.33... should truncate to 33
        var ms = new Milestone { TotalIssues = 3, ClosedIssues = 1 };
        Assert.Equal(33, ms.PercentComplete);
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildStatus
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildStatus_SetProperties_RetainsValues()
    {
        var now = DateTime.UtcNow;
        var build = new BuildStatus
        {
            WorkflowName = "CI",
            RunId = "run-99",
            Status = BuildResult.Success,
            Platform = Platform.iOS,
            DurationSeconds = 120,
            TriggeredBy = "push",
            Url = "https://github.com/runs/99",
            StartedAt = now
        };

        Assert.Equal("CI", build.WorkflowName);
        Assert.Equal("run-99", build.RunId);
        Assert.Equal(BuildResult.Success, build.Status);
        Assert.Equal(Platform.iOS, build.Platform);
        Assert.Equal(120, build.DurationSeconds);
        Assert.Equal("push", build.TriggeredBy);
    }

    // ═══════════════════════════════════════════════════════════════
    // QuickTapConfiguration — EffectiveWindowDuration computed property
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void QuickTapConfiguration_EffectiveWindowDuration_NullWindow_ReturnsFiveSeconds()
    {
        var config = new QuickTapConfiguration("user-1", WindowDuration: null);
        Assert.Equal(TimeSpan.FromSeconds(5), config.EffectiveWindowDuration);
    }

    [Fact]
    public void QuickTapConfiguration_EffectiveWindowDuration_CustomWindow_ReturnsCustom()
    {
        var custom = TimeSpan.FromSeconds(3);
        var config = new QuickTapConfiguration("user-1", WindowDuration: custom);
        Assert.Equal(custom, config.EffectiveWindowDuration);
    }

    [Fact]
    public void QuickTapConfiguration_DefaultValues_AreCorrect()
    {
        var config = new QuickTapConfiguration("user-1");
        Assert.Equal(4, config.RequiredTaps);
        Assert.True(config.IsEnabled);
        Assert.True(config.VolumeButtonEnabled);
        Assert.True(config.ScreenTapEnabled);
        Assert.False(config.DeviceShakeEnabled);
        Assert.Null(config.WindowDuration);
    }

    // ═══════════════════════════════════════════════════════════════
    // QuickTapEvent
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void QuickTapEvent_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var evt = new QuickTapEvent(
            UserId: "user-1",
            DeviceId: "dev-1",
            TapCount: 5,
            WindowDuration: TimeSpan.FromSeconds(3),
            TriggerType: TapTriggerType.VolumeButton,
            DetectedAt: now);

        Assert.Equal("user-1", evt.UserId);
        Assert.Equal("dev-1", evt.DeviceId);
        Assert.Equal(5, evt.TapCount);
        Assert.Equal(TimeSpan.FromSeconds(3), evt.WindowDuration);
        Assert.Equal(TapTriggerType.VolumeButton, evt.TriggerType);
        Assert.Equal(now, evt.DetectedAt);
    }

    // ═══════════════════════════════════════════════════════════════
    // NotificationPayload
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationPayload_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var payload = new NotificationPayload(
            NotificationId: "notif-1",
            RecipientUserId: "user-2",
            RecipientDeviceToken: "fcm-token-abc",
            RecipientPhoneNumber: "+15125551234",
            Category: NotificationCategory.SosDispatch,
            Priority: NotificationPriority.Critical,
            PreferredChannel: NotificationChannel.Push,
            Title: "Emergency Alert",
            Body: "Jane D. needs help at 123 Main St",
            Subtitle: "Neighborhood SOS",
            DeepLink: "thewatch://response/req-123",
            RequestId: "req-123",
            RequestorName: "Jane D.",
            Scope: ResponseScope.Neighborhood,
            IncidentLatitude: 30.2672,
            IncidentLongitude: -97.7431,
            DistanceMeters: 450.0,
            SmsReplyInstructions: "Reply Y to accept, N to decline",
            CreatedAt: now,
            ExpiresAfter: TimeSpan.FromMinutes(10));

        Assert.Equal("notif-1", payload.NotificationId);
        Assert.Equal("user-2", payload.RecipientUserId);
        Assert.Equal("fcm-token-abc", payload.RecipientDeviceToken);
        Assert.Equal("+15125551234", payload.RecipientPhoneNumber);
        Assert.Equal(NotificationCategory.SosDispatch, payload.Category);
        Assert.Equal(NotificationPriority.Critical, payload.Priority);
        Assert.Equal(NotificationChannel.Push, payload.PreferredChannel);
        Assert.Equal("Emergency Alert", payload.Title);
        Assert.Equal("Jane D. needs help at 123 Main St", payload.Body);
        Assert.Equal("thewatch://response/req-123", payload.DeepLink);
        Assert.Equal("req-123", payload.RequestId);
        Assert.Equal(ResponseScope.Neighborhood, payload.Scope);
        Assert.Equal(30.2672, payload.IncidentLatitude);
    }

    [Fact]
    public void NotificationPayload_DeepLink_FollowsExpectedFormat()
    {
        var payload = new NotificationPayload(
            NotificationId: "n-1", RecipientUserId: "u-1",
            RecipientDeviceToken: null, RecipientPhoneNumber: null,
            Category: NotificationCategory.SosDispatch,
            Priority: NotificationPriority.High,
            PreferredChannel: NotificationChannel.Push,
            Title: "Alert", Body: "Body", Subtitle: null,
            DeepLink: "thewatch://response/req-456",
            RequestId: "req-456", RequestorName: null,
            Scope: null, IncidentLatitude: null, IncidentLongitude: null,
            DistanceMeters: null, SmsReplyInstructions: null,
            CreatedAt: DateTime.UtcNow, ExpiresAfter: null);

        Assert.StartsWith("thewatch://response/", payload.DeepLink);
        Assert.Contains("req-456", payload.DeepLink);
    }

    // ═══════════════════════════════════════════════════════════════
    // NotificationResult
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationResult_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var result = new NotificationResult(
            NotificationId: "notif-1",
            RecipientUserId: "user-1",
            Channel: NotificationChannel.Sms,
            Status: NotificationDeliveryStatus.Delivered,
            ExternalMessageId: "twilio-sid-123",
            ErrorMessage: null,
            SentAt: now);

        Assert.Equal("notif-1", result.NotificationId);
        Assert.Equal(NotificationChannel.Sms, result.Channel);
        Assert.Equal(NotificationDeliveryStatus.Delivered, result.Status);
        Assert.Equal("twilio-sid-123", result.ExternalMessageId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void NotificationResult_FailedDelivery_HasErrorMessage()
    {
        var result = new NotificationResult(
            NotificationId: "notif-2",
            RecipientUserId: "user-1",
            Channel: NotificationChannel.Push,
            Status: NotificationDeliveryStatus.Failed,
            ExternalMessageId: null,
            ErrorMessage: "Invalid device token",
            SentAt: DateTime.UtcNow);

        Assert.Equal(NotificationDeliveryStatus.Failed, result.Status);
        Assert.Equal("Invalid device token", result.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // NotificationResponse
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotificationResponse_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var response = new NotificationResponse(
            ResponseId: "resp-1",
            NotificationId: "notif-1",
            RequestId: "req-1",
            ResponderId: "user-3",
            Action: NotificationResponseAction.Accept,
            SourceChannel: NotificationChannel.Push,
            RawSmsBody: null,
            ResponderLatitude: 30.27,
            ResponderLongitude: -97.74,
            RespondedAt: now);

        Assert.Equal("resp-1", response.ResponseId);
        Assert.Equal(NotificationResponseAction.Accept, response.Action);
        Assert.Equal(NotificationChannel.Push, response.SourceChannel);
        Assert.Null(response.RawSmsBody);
        Assert.Equal(30.27, response.ResponderLatitude);
    }

    [Fact]
    public void NotificationResponse_SmsSource_HasRawBody()
    {
        var response = new NotificationResponse(
            ResponseId: "resp-2",
            NotificationId: "notif-2",
            RequestId: "req-2",
            ResponderId: "user-4",
            Action: NotificationResponseAction.Accept,
            SourceChannel: NotificationChannel.Sms,
            RawSmsBody: "Y",
            ResponderLatitude: null,
            ResponderLongitude: null,
            RespondedAt: DateTime.UtcNow);

        Assert.Equal(NotificationChannel.Sms, response.SourceChannel);
        Assert.Equal("Y", response.RawSmsBody);
    }

    // ═══════════════════════════════════════════════════════════════
    // ResponseRequest
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ResponseRequest_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var request = new ResponseRequest(
            RequestId: "req-1",
            UserId: "user-1",
            DeviceId: "dev-1",
            Scope: ResponseScope.CheckIn,
            Escalation: EscalationPolicy.Manual,
            Strategy: DispatchStrategy.NearestN,
            Latitude: 30.2672,
            Longitude: -97.7431,
            AccuracyMeters: 10.0,
            RadiusMeters: 1000,
            DesiredResponderCount: 8,
            EscalationTimeout: TimeSpan.FromMinutes(5),
            Description: "Feeling unsafe",
            TriggerSource: "PHRASE",
            TriggerConfidence: 0.95f,
            CreatedAt: now);

        Assert.Equal("req-1", request.RequestId);
        Assert.Equal("user-1", request.UserId);
        Assert.Equal(ResponseScope.CheckIn, request.Scope);
        Assert.Equal(30.2672, request.Latitude);
        Assert.Equal(1000, request.RadiusMeters);
        Assert.Equal(0.95f, request.TriggerConfidence);
        Assert.Equal(ResponseStatus.Pending, request.Status); // default
    }

    [Fact]
    public void ResponseRequest_DefaultStatus_IsPending()
    {
        var request = new ResponseRequest(
            "r-1", "u-1", null, ResponseScope.CheckIn,
            EscalationPolicy.Manual, DispatchStrategy.NearestN,
            0, 0, null, 1000, 8, TimeSpan.FromMinutes(5),
            null, null, null, DateTime.UtcNow);

        Assert.Equal(ResponseStatus.Pending, request.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // ResponderAcknowledgment
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ResponderAcknowledgment_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var ack = new ResponderAcknowledgment(
            AckId: "ack-1",
            RequestId: "req-1",
            ResponderId: "user-2",
            ResponderName: "John Doe",
            ResponderRole: "EMT",
            ResponderLatitude: 30.28,
            ResponderLongitude: -97.73,
            DistanceMeters: 450.0,
            EstimatedArrival: TimeSpan.FromMinutes(3),
            Status: AckStatus.EnRoute,
            AcknowledgedAt: now);

        Assert.Equal("ack-1", ack.AckId);
        Assert.Equal("John Doe", ack.ResponderName);
        Assert.Equal("EMT", ack.ResponderRole);
        Assert.Equal(450.0, ack.DistanceMeters);
        Assert.Equal(AckStatus.EnRoute, ack.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // EscalationEvent
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EscalationEvent_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var evt = new EscalationEvent(
            EventId: "esc-1",
            RequestId: "req-1",
            PolicyTriggered: EscalationPolicy.TimedEscalation,
            Reason: "Timeout: 0/5 responders acknowledged after 120s",
            NewScope: ResponseScope.Community,
            NewRadiusMeters: 10000,
            TriggeredAt: now);

        Assert.Equal("esc-1", evt.EventId);
        Assert.Equal(EscalationPolicy.TimedEscalation, evt.PolicyTriggered);
        Assert.Contains("120s", evt.Reason);
        Assert.Equal(ResponseScope.Community, evt.NewScope);
        Assert.Equal(10000, evt.NewRadiusMeters);
    }

    // ═══════════════════════════════════════════════════════════════
    // ParticipationPreferences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ParticipationPreferences_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var prefs = new ParticipationPreferences(
            UserId: "user-1",
            IsResponderEnabled: true,
            OptedInCheckIn: true,
            OptedInNeighborhood: true,
            OptedInCommunity: false,
            OptedInEvacuation: true,
            IsCurrentlyAvailable: true,
            AvailableFrom: new TimeOnly(8, 0),
            AvailableTo: new TimeOnly(22, 0),
            AvailableDays: [DayOfWeek.Monday, DayOfWeek.Friday],
            Certifications: ["EMT", "CPR"],
            MaxResponseRadiusMeters: 5000,
            WillingToBeFirstOnScene: true,
            HasVehicle: true,
            QuietHoursStart: new TimeOnly(23, 0),
            QuietHoursEnd: new TimeOnly(7, 0),
            LastUpdated: now);

        Assert.Equal("user-1", prefs.UserId);
        Assert.True(prefs.IsResponderEnabled);
        Assert.True(prefs.OptedInCheckIn);
        Assert.False(prefs.OptedInCommunity);
        Assert.Equal(new TimeOnly(8, 0), prefs.AvailableFrom);
        Assert.Equal(2, prefs.Certifications!.Length);
        Assert.Equal(5000, prefs.MaxResponseRadiusMeters);
        Assert.True(prefs.HasVehicle);
    }

    [Fact]
    public void ParticipationPreferences_NullableFields_CanBeNull()
    {
        var prefs = new ParticipationPreferences(
            "u-1", true, true, true, true, true, true,
            null, null, null, null, 1000, true, false, null, null,
            DateTime.UtcNow);

        Assert.Null(prefs.AvailableFrom);
        Assert.Null(prefs.AvailableTo);
        Assert.Null(prefs.AvailableDays);
        Assert.Null(prefs.Certifications);
        Assert.Null(prefs.QuietHoursStart);
        Assert.Null(prefs.QuietHoursEnd);
    }

    // ═══════════════════════════════════════════════════════════════
    // NavigationDirections
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NavigationDirections_Construction_RetainsAllFields()
    {
        var directions = new NavigationDirections(
            RequestId: "req-1",
            ResponderId: "user-2",
            IncidentLatitude: 30.2672,
            IncidentLongitude: -97.7431,
            ResponderLatitude: 30.28,
            ResponderLongitude: -97.73,
            DistanceMeters: 1500,
            TravelMode: "driving",
            GoogleMapsUrl: "https://www.google.com/maps/dir/?api=1&origin=30.28,-97.73&destination=30.2672,-97.7431&travelmode=driving",
            AppleMapsUrl: "https://maps.apple.com/?saddr=30.28,-97.73&daddr=30.2672,-97.7431&dirflg=d",
            WazeUrl: "https://waze.com/ul?ll=30.2672,-97.7431&navigate=yes",
            EstimatedTravelTime: TimeSpan.FromMinutes(5));

        Assert.Equal("req-1", directions.RequestId);
        Assert.Equal("driving", directions.TravelMode);
        Assert.Contains("google.com/maps", directions.GoogleMapsUrl);
        Assert.Contains("maps.apple.com", directions.AppleMapsUrl);
        Assert.Contains("waze.com", directions.WazeUrl);
        Assert.Equal(TimeSpan.FromMinutes(5), directions.EstimatedTravelTime);
    }

    [Fact]
    public void NavigationDirections_NullEstimatedTime_IsAllowed()
    {
        var directions = new NavigationDirections(
            "req-1", "user-2", 30.0, -97.0, 30.1, -97.1,
            2000, "walking", "gurl", "aurl", "wurl", null);

        Assert.Null(directions.EstimatedTravelTime);
    }

    // ═══════════════════════════════════════════════════════════════
    // IoT Models — IoTAlertRequest, IoTAlertResult, IoTCheckInRequest, IoTCheckInResult
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IoTAlertRequest_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var location = new IoTLocation(30.2672, -97.7431, 10.0, "123 Main St", "9q9hvb");
        var request = new IoTAlertRequest(
            Source: IoTSource.Alexa,
            ExternalUserId: "amzn1.ask.account.XXX",
            TriggerMethod: "VOICE_COMMAND",
            DeviceType: "ECHO_DOT",
            Location: location,
            EmergencyPhrase: "help me now",
            Scope: ResponseScope.Neighborhood,
            Timestamp: now,
            PlatformRequestId: "alexa-req-1",
            AccessToken: "bearer-token",
            Metadata: new Dictionary<string, string> { ["skill_version"] = "1.0" });

        Assert.Equal(IoTSource.Alexa, request.Source);
        Assert.Equal("amzn1.ask.account.XXX", request.ExternalUserId);
        Assert.Equal("VOICE_COMMAND", request.TriggerMethod);
        Assert.Equal("ECHO_DOT", request.DeviceType);
        Assert.NotNull(request.Location);
        Assert.Equal(30.2672, request.Location!.Latitude);
        Assert.Equal("help me now", request.EmergencyPhrase);
        Assert.Equal(ResponseScope.Neighborhood, request.Scope);
        Assert.Equal("alexa-req-1", request.PlatformRequestId);
    }

    [Fact]
    public void IoTAlertRequest_OptionalFields_DefaultToNull()
    {
        var request = new IoTAlertRequest(
            IoTSource.GoogleHome, "google-uid-1", "VOICE_COMMAND",
            "NEST_HUB", null, null, ResponseScope.CheckIn, DateTime.UtcNow);

        Assert.Null(request.Location);
        Assert.Null(request.EmergencyPhrase);
        Assert.Null(request.PlatformRequestId);
        Assert.Null(request.AccessToken);
        Assert.Null(request.Metadata);
    }

    [Fact]
    public void IoTAlertResult_Construction_RetainsAllFields()
    {
        var result = new IoTAlertResult(
            AlertId: "alert-1",
            RequestId: "req-1",
            Status: IoTAlertStatus.Dispatched,
            RespondersNotified: 5,
            Message: "Emergency alert sent. 5 responders have been notified.",
            ResponseRequestId: "rr-1",
            EstimatedResponseTime: TimeSpan.FromMinutes(3));

        Assert.Equal("alert-1", result.AlertId);
        Assert.Equal(IoTAlertStatus.Dispatched, result.Status);
        Assert.Equal(5, result.RespondersNotified);
        Assert.Contains("5 responders", result.Message);
        Assert.Equal("rr-1", result.ResponseRequestId);
        Assert.Equal(TimeSpan.FromMinutes(3), result.EstimatedResponseTime);
    }

    [Fact]
    public void IoTAlertResult_OptionalFields_DefaultToNull()
    {
        var result = new IoTAlertResult("a-1", "r-1", IoTAlertStatus.UserNotMapped, 0, "User not found");
        Assert.Null(result.ResponseRequestId);
        Assert.Null(result.EstimatedResponseTime);
    }

    [Fact]
    public void IoTCheckInRequest_Construction_RetainsAllFields()
    {
        var location = new IoTLocation(30.0, -97.0);
        var request = new IoTCheckInRequest(
            Source: IoTSource.Alexa,
            ExternalUserId: "amzn1.ask.account.XXX",
            Status: IoTCheckInStatus.Ok,
            Message: "I'm fine, just busy today",
            Location: location,
            VitalSigns: new Dictionary<string, string> { ["heartRate"] = "72" });

        Assert.Equal(IoTSource.Alexa, request.Source);
        Assert.Equal(IoTCheckInStatus.Ok, request.Status);
        Assert.Equal("I'm fine, just busy today", request.Message);
        Assert.NotNull(request.VitalSigns);
        Assert.Equal("72", request.VitalSigns["heartRate"]);
    }

    [Fact]
    public void IoTCheckInResult_Construction_RetainsAllFields()
    {
        var nextDue = DateTime.UtcNow.AddHours(24);
        var result = new IoTCheckInResult(
            CheckInId: "ci-1",
            Status: IoTCheckInResultStatus.Recorded,
            Message: "Check-in recorded. Next check-in due tomorrow.",
            NextCheckInDue: nextDue);

        Assert.Equal("ci-1", result.CheckInId);
        Assert.Equal(IoTCheckInResultStatus.Recorded, result.Status);
        Assert.Equal(nextDue, result.NextCheckInDue);
    }

    // ═══════════════════════════════════════════════════════════════
    // IoT Device Management — IoTDeviceRegistration, IoTUserMapping, IoTDeviceStatus
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IoTDeviceRegistration_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var reg = new IoTDeviceRegistration(
            DeviceId: "dev-1",
            UserId: "user-1",
            Source: IoTSource.SmartThings,
            DeviceName: "Front Door Button",
            Capabilities: new List<string> { "PANIC_BUTTON", "VOICE_COMMAND" },
            RegisteredAt: now,
            LastSeenAt: now,
            FirmwareVersion: "2.1.0",
            IsOnline: true,
            BatteryLevel: 85,
            InstallationZone: "front_door");

        Assert.Equal("dev-1", reg.DeviceId);
        Assert.Equal(IoTSource.SmartThings, reg.Source);
        Assert.Equal(2, reg.Capabilities.Count);
        Assert.Equal("2.1.0", reg.FirmwareVersion);
        Assert.True(reg.IsOnline);
        Assert.Equal(85, reg.BatteryLevel);
        Assert.Equal("front_door", reg.InstallationZone);
    }

    [Fact]
    public void IoTDeviceRegistration_Defaults_OnlineWithNoBattery()
    {
        var reg = new IoTDeviceRegistration(
            "dev-1", "user-1", IoTSource.Alexa, "Echo Dot",
            new List<string> { "VOICE_COMMAND" }, DateTime.UtcNow, DateTime.UtcNow);

        Assert.True(reg.IsOnline);
        Assert.Null(reg.BatteryLevel);
        Assert.Null(reg.FirmwareVersion);
        Assert.Null(reg.InstallationZone);
    }

    [Fact]
    public void IoTUserMapping_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var mapping = new IoTUserMapping(
            Source: IoTSource.Alexa,
            ExternalUserId: "amzn1.ask.account.XXX",
            TheWatchUserId: "user-1",
            AccessToken: "access-token",
            RefreshToken: "refresh-token",
            TokenExpiresAt: now.AddHours(1),
            LinkedAt: now,
            LastUsedAt: now);

        Assert.Equal(IoTSource.Alexa, mapping.Source);
        Assert.Equal("amzn1.ask.account.XXX", mapping.ExternalUserId);
        Assert.Equal("user-1", mapping.TheWatchUserId);
        Assert.Equal("access-token", mapping.AccessToken);
        Assert.Equal("refresh-token", mapping.RefreshToken);
    }

    [Fact]
    public void IoTUserMapping_OptionalFields_DefaultToNull()
    {
        var mapping = new IoTUserMapping(IoTSource.GoogleHome, "google-uid-1", "user-1");
        Assert.Null(mapping.AccessToken);
        Assert.Null(mapping.RefreshToken);
        Assert.Null(mapping.TokenExpiresAt);
        Assert.Null(mapping.LastUsedAt);
    }

    [Fact]
    public void IoTDeviceStatus_Construction_RetainsAllFields()
    {
        var devices = new List<IoTDeviceRegistration>();
        var alerts = new List<IoTActiveAlertSummary>();
        var status = new IoTDeviceStatus(
            UserId: "user-1",
            ActiveAlerts: 2,
            NearbyResponders: 5,
            LastCheckIn: DateTime.UtcNow,
            RegisteredDevices: devices,
            ActiveAlertDetails: alerts);

        Assert.Equal("user-1", status.UserId);
        Assert.Equal(2, status.ActiveAlerts);
        Assert.Equal(5, status.NearbyResponders);
        Assert.NotNull(status.RegisteredDevices);
        Assert.NotNull(status.ActiveAlertDetails);
    }

    // ═══════════════════════════════════════════════════════════════
    // DispatchDistancePolicy
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DispatchDistancePolicy_DefaultMaxWalkingDistanceMeters_Is1600()
    {
        Assert.Equal(1600, DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters);
    }

    // ═══════════════════════════════════════════════════════════════
    // EligibleResponder
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EligibleResponder_Construction_RetainsAllFields()
    {
        var now = DateTime.UtcNow;
        var responder = new EligibleResponder(
            UserId: "user-5",
            Name: "Jane EMT",
            Latitude: 30.28,
            Longitude: -97.73,
            DistanceMeters: 450.0,
            Certifications: new[] { "EMT", "CPR" },
            IsFirstOnSceneWilling: true,
            HasVehicle: true,
            LastActiveAt: now);

        Assert.Equal("user-5", responder.UserId);
        Assert.Equal("Jane EMT", responder.Name);
        Assert.Equal(450.0, responder.DistanceMeters);
        Assert.Equal(2, responder.Certifications!.Length);
        Assert.Contains("EMT", responder.Certifications);
        Assert.True(responder.IsFirstOnSceneWilling);
        Assert.True(responder.HasVehicle);
    }

    [Fact]
    public void EligibleResponder_NullCertifications_IsAllowed()
    {
        var responder = new EligibleResponder(
            "u-1", "Bob", 30.0, -97.0, 100.0, null, false, false, DateTime.UtcNow);
        Assert.Null(responder.Certifications);
    }

    // ═══════════════════════════════════════════════════════════════
    // FeatureImplementation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FeatureImplementation_DefaultConstruction_HasSensibleDefaults()
    {
        var feature = new FeatureImplementation();
        Assert.False(string.IsNullOrWhiteSpace(feature.Id));
        Assert.Equal(string.Empty, feature.Name);
        Assert.Equal(FeatureStatus.Planned, feature.Status);
        Assert.Equal(5, feature.Priority);
        Assert.Equal(0, feature.ProgressPercent);
        Assert.NotNull(feature.FilePaths);
        Assert.Empty(feature.FilePaths);
        Assert.NotNull(feature.Tags);
        Assert.Empty(feature.Tags);
        Assert.NotNull(feature.Dependencies);
        Assert.Empty(feature.Dependencies);
    }

    [Fact]
    public void FeatureImplementation_SetProperties_RetainsValues()
    {
        var feature = new FeatureImplementation
        {
            Name = "Evidence Upload Controller",
            Category = FeatureCategory.EvidenceSystem,
            Status = FeatureStatus.Completed,
            Project = "TheWatch.Dashboard.Api",
            FilePaths = new List<string> { "Controllers/EvidenceController.cs" },
            AssignedTo = "Claude Code",
            Priority = 1,
            ProgressPercent = 100
        };

        Assert.Equal("Evidence Upload Controller", feature.Name);
        Assert.Equal(FeatureCategory.EvidenceSystem, feature.Category);
        Assert.Equal(FeatureStatus.Completed, feature.Status);
        Assert.Single(feature.FilePaths);
        Assert.Equal(100, feature.ProgressPercent);
    }
}
