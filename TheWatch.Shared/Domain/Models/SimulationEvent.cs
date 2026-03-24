using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SimulationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public SimulationEventType EventType { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
