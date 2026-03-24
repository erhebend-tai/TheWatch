namespace TheWatch.Shared.Dtos;

public record HealthStatusDto(
    string Provider,
    bool IsHealthy,
    string Message,
    DateTime LastChecked
);
