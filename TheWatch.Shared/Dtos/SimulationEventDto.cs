using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Dtos;

public record SimulationEventDto(
    SimulationEventType EventType,
    string Payload,
    string Source,
    DateTime Timestamp,
    double? Latitude,
    double? Longitude
);
