// SurveyCompletedMessage — published to "survey-completed" RabbitMQ queue
// when a user submits a survey response via SurveysController.
//
// Consumer: SurveyCompletionFunction (Azure Functions)
//   → Checks if all required responders have completed their surveys
//   → If completion threshold met: updates incident status, generates summary
//
// Example:
//   new SurveyCompletedMessage(
//       ResponseId: "resp-101",
//       TemplateId: "tpl-postincident-v1",
//       RequestId: "req-123",
//       UserId: "user-456"
//   );

namespace TheWatch.Shared.Domain.Messages;

public record SurveyCompletedMessage(
    string ResponseId,
    string TemplateId,
    string? RequestId,
    string UserId
);
