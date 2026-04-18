using Microsoft.AspNetCore.Mvc;
using TheWatch.ResponseService.Models;

namespace TheWatch.ResponseService.Controllers;

[ApiController]
[Route("api/responses")]
public class ResponseController : ControllerBase
{
    private readonly ILogger<ResponseController> _logger;

    public ResponseController(ILogger<ResponseController> logger)
    {
        _logger = logger;
    }

    [HttpPost("notification")]
    public IActionResult HandleNotificationResponse([FromBody] NotificationResponse response)
    {
        _logger.LogInformation(
            "Received notification response from user {UserId} for incident {IncidentId}: {ResponseType}",
            response.UserId,
            response.IncidentId,
            response.ResponseType);

        // TODO:
        // 1. Validate the response
        // 2. Update the Incident status in the IncidentService
        // 3. Notify other responders via SignalR

        return Ok(new { Status = "Received" });
    }
}
