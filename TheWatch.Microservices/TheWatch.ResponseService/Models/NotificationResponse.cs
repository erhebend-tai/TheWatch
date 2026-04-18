namespace TheWatch.ResponseService.Models;

public record NotificationResponse(
    string UserId,
    string IncidentId,
    string ResponseType // "Accept", "Decline", "ImOk", "NeedHelp"
);
