using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Dtos;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimulationController : ControllerBase
{
    private readonly ISimulationService _simulationService;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<SimulationController> _logger;

    public SimulationController(ISimulationService simulationService, IHubContext<DashboardHub> hubContext, ILogger<SimulationController> logger)
    {
        _simulationService = simulationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost("events")]
    public async Task<ActionResult<SimulationEventDto>> PublishEvent([FromBody] SimulationEventDto eventDto)
    {
        try
        {
            var simulationEvent = new SimulationEvent
            {
                Id = Guid.NewGuid().ToString(), EventType = eventDto.EventType, Payload = eventDto.Payload,
                Source = eventDto.Source, Timestamp = eventDto.Timestamp, Latitude = eventDto.Latitude, Longitude = eventDto.Longitude
            };
            await _simulationService.PublishEventAsync(simulationEvent);
            await _hubContext.Clients.All.SendAsync("SimulationEventReceived", eventDto);
            _logger.LogInformation("Published simulation event: {EventType} from {Source}", eventDto.EventType, eventDto.Source);
            return CreatedAtAction(nameof(GetEventLog), new { id = simulationEvent.Id }, eventDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing simulation event");
            return StatusCode(500, new { error = "Failed to publish simulation event" });
        }
    }

    [HttpGet("log")]
    public async Task<ActionResult<List<SimulationEventDto>>> GetEventLog([FromQuery] int limit = 100)
    {
        try
        {
            var events = await _simulationService.GetEventLogAsync(limit);
            var dtos = events.Select(e => new SimulationEventDto(e.EventType, e.Payload, e.Source, e.Timestamp, e.Latitude, e.Longitude)).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving simulation event log");
            return StatusCode(500, new { error = "Failed to retrieve simulation event log" });
        }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetSimulationStats()
    {
        try
        {
            var events = await _simulationService.GetEventLogAsync(limit: 500);
            var last24h = events.Where(e => e.Timestamp > DateTime.Now.AddHours(-24)).ToList();
            return Ok(new
            {
                TotalEvents = events.Count, Last24hEvents = last24h.Count,
                ByEventType = last24h.GroupBy(e => e.EventType.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                BySource = last24h.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Count()),
                LastEventTime = events.FirstOrDefault()?.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving simulation stats");
            return StatusCode(500, new { error = "Failed to retrieve simulation stats" });
        }
    }
}
