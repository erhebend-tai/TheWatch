using TheWatch.Shared.Domain.Models;

namespace TheWatch.Dashboard.Api.Services;

public interface ISimulationService
{
    Task PublishEventAsync(SimulationEvent simulationEvent);
    Task<List<SimulationEvent>> GetEventLogAsync(int limit = 100);
    IAsyncEnumerable<SimulationEvent> SubscribeToEventsAsync(CancellationToken cancellationToken = default);
}
