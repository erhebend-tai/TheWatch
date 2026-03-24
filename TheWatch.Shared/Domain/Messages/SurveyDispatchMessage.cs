// SurveyDispatchMessage — published to "survey-dispatch" RabbitMQ queue
// by SurveyDispatchFunction when surveys need to be sent to users' devices.
//
// Consumer: Dashboard.Api (push notification sender)
//   → Sends push notification to each target user with survey deep link
//
// Example:
//   new SurveyDispatchMessage(
//       TemplateId: "tpl-postincident-v1",
//       RequestId: "req-123",
//       TargetUserIds: new[] { "user-456", "user-789" },
//       Phase: SubmissionPhase.PostIncident,
//       Deadline: DateTime.UtcNow.AddDays(1)
//   );

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Messages;

public record SurveyDispatchMessage(
    string TemplateId,
    string? RequestId,
    string[] TargetUserIds,
    SubmissionPhase Phase,
    DateTime? Deadline
);
