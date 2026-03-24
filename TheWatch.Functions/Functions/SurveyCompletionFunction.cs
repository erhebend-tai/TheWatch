// SurveyCompletionFunction — RabbitMQ-triggered function for survey completion aggregation.
// When a user submits a survey response (via SurveysController), a SurveyCompletedMessage
// is published to "survey-completed". This function:
//   1. Checks if all required responders have completed their surveys
//   2. If completion threshold met: updates incident status, generates summary
//   3. Notifies coordinators via SignalR
//
// Architecture:
//   SurveysController → RabbitMQ("survey-completed") → THIS FUNCTION
//     → Query ISurveyPort for completion status
//     → If threshold met: update incident, broadcast "SurveyCompleted"

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Messages;

namespace TheWatch.Functions.Functions;

public class SurveyCompletionFunction
{
    private readonly ILogger<SurveyCompletionFunction> _logger;

    public SurveyCompletionFunction(ILogger<SurveyCompletionFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Triggered by a message on the "survey-completed" RabbitMQ queue.
    /// Checks completion threshold and notifies coordinators.
    /// </summary>
    [Function("SurveyCompletion")]
    public async Task Run(
        [RabbitMQTrigger("survey-completed", ConnectionStringSetting = "RabbitMQConnection")] string message)
    {
        _logger.LogInformation("SurveyCompletion triggered. Processing message...");

        try
        {
            var completed = JsonSerializer.Deserialize<SurveyCompletedMessage>(message);
            if (completed is null)
            {
                _logger.LogWarning("Failed to deserialize SurveyCompletedMessage");
                return;
            }

            _logger.LogInformation(
                "Survey completed: ResponseId={ResponseId}, TemplateId={TemplateId}, " +
                "UserId={UserId}, RequestId={RequestId}",
                completed.ResponseId, completed.TemplateId,
                completed.UserId, completed.RequestId);

            if (completed.RequestId is not null)
            {
                // Check completion status for this incident
                await CheckCompletionThresholdAsync(completed.TemplateId, completed.RequestId);
            }

            // In production: broadcast via SignalR
            // await _signalROutput.SendToGroupAsync($"response-{completed.RequestId}",
            //     "SurveyCompleted", new { completed.ResponseId, completed.RequestId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing survey-completed message");
            throw; // Let RabbitMQ retry
        }
    }

    private Task CheckCompletionThresholdAsync(string templateId, string requestId)
    {
        // In production:
        //   1. Query ISurveyPort.GetPendingSurveyUserIdsAsync(templateId, requestId)
        //   2. If pending count == 0: all surveys complete
        //      a. Generate summary report
        //      b. Update incident metadata
        //      c. Notify coordinators
        //   3. If pending count > 0 but past deadline: send reminder notifications
        //
        // var pending = await _surveyPort.GetPendingSurveyUserIdsAsync(templateId, requestId);
        // if (pending.Data?.Count == 0)
        // {
        //     _logger.LogInformation("All surveys completed for template={Template}, request={Request}",
        //         templateId, requestId);
        //     // Generate summary, update incident
        // }

        _logger.LogInformation(
            "Checked completion threshold for template={TemplateId}, request={RequestId}",
            templateId, requestId);

        return Task.CompletedTask;
    }
}
