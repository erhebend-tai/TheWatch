using TheWatch.Shared.Enums;

namespace TheWatch.Maui.Models;

public class SimulatedAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString();
    public SimulationEventType EventType { get; set; } = SimulationEventType.SOSTrigger;
    public string AlertSeverity { get; set; } = "Critical";
    public string TriggerType { get; set; } = "Manual";
    public double Latitude { get; set; } = 40.7128;
    public double Longitude { get; set; } = -74.0060;
    public int ConfidenceLevel { get; set; } = 85;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Dictionary<string, object> ToPayload()
    {
        return new Dictionary<string, object>
        {
            { "alertId", AlertId },
            { "severity", AlertSeverity },
            { "triggerType", TriggerType },
            { "latitude", Latitude },
            { "longitude", Longitude },
            { "confidenceLevel", ConfidenceLevel },
            { "description", Description },
            { "timestamp", CreatedAt }
        };
    }
}
