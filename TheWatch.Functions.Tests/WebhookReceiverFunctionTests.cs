// WebhookReceiverFunctionTests - xUnit tests for WebhookReceiverFunction.
// The function has HTTP triggers and DI dependencies (ISmsPort, IResponseTrackingPort,
// INotificationTrackingPort). Since HttpRequestData is abstract and requires FunctionContext,
// we verify construction and method signatures via reflection.
//
// WAL: WebhookReceiverFunction has five HTTP endpoints:
//   POST /api/webhooks/sos          — SOS trigger from mobile devices (SOSTriggerPayload)
//   POST /api/webhooks/ack          — Responder acknowledgment (ResponderAckPayload)
//   POST /api/webhooks/sms/inbound  — Inbound SMS from gateway (Twilio/Azure Comm Services)
//   POST /api/webhooks/sms/status   — SMS delivery status callback
//   GET  /api/health                — Health check (anonymous)
//
// Dependencies: ILogger, ISmsPort, IResponseTrackingPort, INotificationTrackingPort
//
// Payload records tested for serialization:
//   SOSTriggerPayload — UserId, DeviceId, Scope, Lat, Lng, AccuracyMeters, TriggerSource, Confidence, Desc
//   ResponderAckPayload — RequestId, ResponderId, Status, Lat, Lng, EstimatedArrivalMinutes
//
// Example:
//   var payload = new SOSTriggerPayload("user-1", "dev-1", ResponseScope.CheckIn, 33.0, -97.0, 10, "PHRASE", 0.95f, "Help");
//   var json = JsonSerializer.Serialize(payload);

namespace TheWatch.Functions.Tests;

public class WebhookReceiverFunctionTests
{
    [Fact]
    public void SOSTriggerPayload_SerializesAndDeserializes()
    {
        // Arrange
        var payload = new SOSTriggerPayload(
            UserId: "user-001",
            DeviceId: "dev-001",
            Scope: ResponseScope.CheckIn,
            Latitude: 33.0198,
            Longitude: -96.6989,
            AccuracyMeters: 10.0,
            TriggerSource: "PHRASE",
            TriggerConfidence: 0.95f,
            Description: "Help me"
        );

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<SOSTriggerPayload>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("user-001", deserialized!.UserId);
        Assert.Equal(ResponseScope.CheckIn, deserialized.Scope);
        Assert.Equal(33.0198, deserialized.Latitude);
        Assert.Equal("PHRASE", deserialized.TriggerSource);
        Assert.Equal(0.95f, deserialized.TriggerConfidence);
    }

    [Fact]
    public void ResponderAckPayload_SerializesAndDeserializes()
    {
        // Arrange
        var payload = new ResponderAckPayload(
            RequestId: "req-001",
            ResponderId: "resp-001",
            Status: AckStatus.EnRoute,
            ResponderLatitude: 33.05,
            ResponderLongitude: -96.70,
            EstimatedArrivalMinutes: 5
        );

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<ResponderAckPayload>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("req-001", deserialized!.RequestId);
        Assert.Equal(AckStatus.EnRoute, deserialized.Status);
        Assert.Equal(5, deserialized.EstimatedArrivalMinutes);
    }

    [Fact]
    public void SOSTriggerPayload_NullOptionalFields_Allowed()
    {
        var payload = new SOSTriggerPayload(
            UserId: "user-002",
            DeviceId: null,
            Scope: ResponseScope.SilentDuress,
            Latitude: 0,
            Longitude: 0,
            AccuracyMeters: null,
            TriggerSource: null,
            TriggerConfidence: null,
            Description: null
        );

        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<SOSTriggerPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.DeviceId);
        Assert.Null(deserialized.TriggerSource);
        Assert.Null(deserialized.TriggerConfidence);
    }

    [Fact]
    public void ResponderAckPayload_NullOptionalFields_Allowed()
    {
        var payload = new ResponderAckPayload(
            RequestId: "req-002",
            ResponderId: "resp-002",
            Status: AckStatus.Acknowledged,
            ResponderLatitude: null,
            ResponderLongitude: null,
            EstimatedArrivalMinutes: null
        );

        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<ResponderAckPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.ResponderLatitude);
        Assert.Null(deserialized.EstimatedArrivalMinutes);
    }

    [Fact]
    public void WebhookReceiverFunction_HasExpectedMethods()
    {
        var expectedMethods = new[]
        {
            "HandleSOSTrigger",
            "HandleResponderAck",
            "HandleInboundSms",
            "HandleSmsStatus",
            "HealthCheck"
        };

        foreach (var name in expectedMethods)
        {
            var method = typeof(WebhookReceiverFunction).GetMethod(name);
            Assert.NotNull(method);
        }
    }

    [Fact]
    public void WebhookReceiverFunction_AllEndpoints_HaveFunctionAttribute()
    {
        var methods = new[] { "HandleSOSTrigger", "HandleResponderAck", "HandleInboundSms", "HandleSmsStatus", "HealthCheck" };
        foreach (var name in methods)
        {
            var method = typeof(WebhookReceiverFunction).GetMethod(name);
            Assert.NotNull(method);
            var attrs = method!.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute), false);
            Assert.Single(attrs);
        }
    }

    [Fact]
    public void WebhookReceiverFunction_FunctionNames_AreCorrect()
    {
        var expected = new Dictionary<string, string>
        {
            ["HandleSOSTrigger"] = "SOSTrigger",
            ["HandleResponderAck"] = "ResponderAck",
            ["HandleInboundSms"] = "SmsInbound",
            ["HandleSmsStatus"] = "SmsStatus",
            ["HealthCheck"] = "HealthCheck"
        };

        foreach (var (methodName, functionName) in expected)
        {
            var method = typeof(WebhookReceiverFunction).GetMethod(methodName);
            var attr = (Microsoft.Azure.Functions.Worker.FunctionAttribute)
                method!.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute), false).First();
            Assert.Equal(functionName, attr.Name);
        }
    }

    [Fact]
    public void SOSTriggerPayload_AllScopes_Serialize()
    {
        // Verify serialization round-trip for all ResponseScope values
        foreach (ResponseScope scope in Enum.GetValues<ResponseScope>())
        {
            var payload = new SOSTriggerPayload("user", "dev", scope, 0, 0, null, null, null, null);
            var json = JsonSerializer.Serialize(payload);
            var deser = JsonSerializer.Deserialize<SOSTriggerPayload>(json);
            Assert.NotNull(deser);
            Assert.Equal(scope, deser!.Scope);
        }
    }

    [Fact]
    public void ResponderAckPayload_AllAckStatuses_Serialize()
    {
        foreach (AckStatus status in Enum.GetValues<AckStatus>())
        {
            var payload = new ResponderAckPayload("req", "resp", status, null, null, null);
            var json = JsonSerializer.Serialize(payload);
            var deser = JsonSerializer.Deserialize<ResponderAckPayload>(json);
            Assert.NotNull(deser);
            Assert.Equal(status, deser!.Status);
        }
    }
}
