using TheWatch.Shared.Enums;

namespace TheWatch.Maui.Models;

public class SimulatedSensor
{
    public string SensorId { get; set; } = Guid.NewGuid().ToString();
    public SimulationEventType EventType { get; set; } = SimulationEventType.SensorReading;
    public int HeartRate { get; set; } = 75;
    public int BloodOxygen { get; set; } = 98;
    public double BodyTemperature { get; set; } = 37.0;
    public int StressLevel { get; set; } = 2;
    public bool IsFallDetected { get; set; } = false;
    public bool HasEcgAnomaly { get; set; } = false;
    public string Description { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.Now;

    public Dictionary<string, object> ToPayload()
    {
        return new Dictionary<string, object>
        {
            { "sensorId", SensorId },
            { "heartRate", HeartRate },
            { "bloodOxygen", BloodOxygen },
            { "bodyTemperature", BodyTemperature },
            { "stressLevel", StressLevel },
            { "isFallDetected", IsFallDetected },
            { "hasEcgAnomaly", HasEcgAnomaly },
            { "description", Description },
            { "timestamp", RecordedAt }
        };
    }
}
