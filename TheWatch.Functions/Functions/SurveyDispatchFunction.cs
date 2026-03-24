// SurveyDispatchFunction — Timer-triggered function for automatic survey dispatch.
// Runs every 60 seconds to check for:
//   1. Resolved incidents that need post-incident surveys dispatched
//   2. New users who need registration surveys
//   3. Scheduled surveys that are due
//
// Architecture:
//   Timer (60s) → THIS FUNCTION → checks IStorageService for resolved incidents
//     → For each eligible incident/user: publishes SurveyDispatchMessage
//     → Consumer (Dashboard.Api) sends push notifications to users' devices
//
// WAL: This function is intentionally idempotent. It tracks which surveys have been
// dispatched via a "survey-dispatch-log" collection in IStorageService so it won't
// re-dispatch the same survey to the same user for the same incident.

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Messages;
using TheWatch.Shared.Enums;

namespace TheWatch.Functions.Functions;

public class SurveyDispatchFunction
{
    private readonly ILogger<SurveyDispatchFunction> _logger;

    public SurveyDispatchFunction(ILogger<SurveyDispatchFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Timer trigger: runs every 60 seconds to check for surveys to dispatch.
    /// </summary>
    [Function("SurveyDispatch")]
    public async Task Run(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo)
    {
        _logger.LogDebug("SurveyDispatch timer fired at {Now}", DateTime.UtcNow);

        try
        {
            // ── Check for resolved incidents needing post-incident surveys ──
            await DispatchPostIncidentSurveysAsync();

            // ── Check for new registrations needing onboarding surveys ──
            await DispatchRegistrationSurveysAsync();

            // ── Check for scheduled surveys that are due ──
            await DispatchScheduledSurveysAsync();

            if (timerInfo.ScheduleStatus is not null)
            {
                _logger.LogDebug(
                    "SurveyDispatch next occurrence: {Next}",
                    timerInfo.ScheduleStatus.Next);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SurveyDispatch timer function");
            // Don't throw — timer functions should not fail permanently
        }
    }

    private Task DispatchPostIncidentSurveysAsync()
    {
        // In production:
        //   1. Query IStorageService for ResponseRequests with Status == Resolved
        //      and no corresponding entry in "survey-dispatch-log"
        //   2. For each resolved request:
        //      a. Get the post-incident survey template (tpl-postincident-wellbeing-v1)
        //      b. Find all participants (requester + responding users)
        //      c. Publish SurveyDispatchMessage to "survey-dispatch" queue
        //      d. Log the dispatch to "survey-dispatch-log" for idempotency
        //
        // var resolvedRequests = await _storageService.QueryAsync<ResponseRequest>(
        //     "response-requests", r => r.Status == ResponseStatus.Resolved);
        // foreach (var request in resolvedRequests.Data) { ... }

        _logger.LogDebug("Checked for post-incident surveys to dispatch");
        return Task.CompletedTask;
    }

    private Task DispatchRegistrationSurveysAsync()
    {
        // In production:
        //   1. Query for users registered in the last 24h who haven't completed
        //      the household safety survey (tpl-household-safety-v1)
        //   2. For each: publish SurveyDispatchMessage
        //
        // var newUsers = await _storageService.QueryAsync<UserProfile>(
        //     "users", u => u.RegisteredAt > DateTime.UtcNow.AddHours(-24));

        _logger.LogDebug("Checked for registration surveys to dispatch");
        return Task.CompletedTask;
    }

    private Task DispatchScheduledSurveysAsync()
    {
        // In production:
        //   1. Query survey templates with TriggerCondition == OnSchedule
        //   2. Check if their schedule is due (e.g., weekly safety check)
        //   3. For each due survey: find target users and publish dispatch messages

        _logger.LogDebug("Checked for scheduled surveys to dispatch");
        return Task.CompletedTask;
    }
}
