// SurveysController — REST endpoints for survey template management and response collection.
// Mobile clients render surveys and submit responses through this controller.
//
// Endpoints:
//   GET    /api/surveys/templates                      — List available templates
//   GET    /api/surveys/templates/{id}                 — Get template with questions
//   GET    /api/surveys/pending/{userId}               — Pending surveys for a user
//   POST   /api/surveys/responses                      — Submit survey response
//   GET    /api/surveys/responses/request/{requestId}  — All responses for an incident
//   GET    /api/surveys/results/{templateId}/{reqId}   — Aggregated results
//
// WAL: Response submission flow:
//   1. Client POSTs completed survey response
//   2. Controller validates answers against template
//   3. Stores response via ISurveyPort
//   4. Publishes SurveyCompletedMessage to RabbitMQ "survey-completed" queue
//   5. Returns 201 Created with response ID

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SurveysController : ControllerBase
{
    private readonly ISurveyPort _surveyPort;
    private readonly ILogger<SurveysController> _logger;

    public SurveysController(ISurveyPort surveyPort, ILogger<SurveysController> logger)
    {
        _surveyPort = surveyPort;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Templates
    // ─────────────────────────────────────────────────────────────

    /// <summary>List all survey templates, optionally filtered by phase.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromQuery] SubmissionPhase? phase, CancellationToken ct)
    {
        if (phase is not null)
        {
            var result = await _surveyPort.GetTemplatesByPhaseAsync(phase.Value, ct);
            return Ok(result.Data ?? new List<SurveyTemplate>());
        }

        // Return all phases
        var all = new List<SurveyTemplate>();
        foreach (var p in Enum.GetValues<SubmissionPhase>())
        {
            var r = await _surveyPort.GetTemplatesByPhaseAsync(p, ct);
            if (r.Success && r.Data is not null)
                all.AddRange(r.Data);
        }
        return Ok(all);
    }

    /// <summary>Get a single template by ID, including all questions.</summary>
    [HttpGet("templates/{id}")]
    public async Task<IActionResult> GetTemplate(string id, CancellationToken ct)
    {
        var result = await _surveyPort.GetTemplateByIdAsync(id, ct);
        if (!result.Success)
            return NotFound(new { error = result.ErrorMessage });
        return Ok(result.Data);
    }

    // ─────────────────────────────────────────────────────────────
    // Pending surveys for a user
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get all surveys pending completion for a specific user.
    /// Mobile app polls this to show "you have N surveys to complete" badge.
    /// </summary>
    [HttpGet("pending/{userId}")]
    public async Task<IActionResult> GetPendingSurveys(string userId, CancellationToken ct)
    {
        // Get all user's completed responses to know which templates they've done
        var completedResult = await _surveyPort.GetResponsesByUserIdAsync(userId, ct);
        var completedTemplateIds = (completedResult.Data ?? new List<SurveyResponse>())
            .Select(r => r.TemplateId)
            .ToHashSet();

        // Get all templates and filter to those not yet completed
        var allTemplates = new List<SurveyTemplate>();
        foreach (var p in Enum.GetValues<SubmissionPhase>())
        {
            var r = await _surveyPort.GetTemplatesByPhaseAsync(p, ct);
            if (r.Success && r.Data is not null)
                allTemplates.AddRange(r.Data);
        }

        var pending = allTemplates
            .Where(t => !completedTemplateIds.Contains(t.Id))
            .ToList();

        return Ok(pending);
    }

    // ─────────────────────────────────────────────────────────────
    // Response submission
    // ─────────────────────────────────────────────────────────────

    /// <summary>Submit a completed survey response.</summary>
    [HttpPost("responses")]
    public async Task<IActionResult> SubmitResponse(
        [FromBody] SurveyResponse response,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(response.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(response.TemplateId))
            return BadRequest(new { error = "TemplateId is required" });

        // Validate template exists
        var templateResult = await _surveyPort.GetTemplateByIdAsync(response.TemplateId, ct);
        if (!templateResult.Success)
            return BadRequest(new { error = $"Template '{response.TemplateId}' not found" });

        var template = templateResult.Data!;

        // Validate required questions are answered
        var requiredQuestionIds = template.Questions
            .Where(q => q.IsRequired)
            .Select(q => q.Id)
            .ToHashSet();

        var answeredQuestionIds = response.Answers
            .Select(a => a.QuestionId)
            .ToHashSet();

        var missing = requiredQuestionIds.Except(answeredQuestionIds).ToList();
        if (missing.Count > 0)
            return BadRequest(new { error = $"Missing required answers for questions: {string.Join(", ", missing)}" });

        response.CompletedAt = DateTime.UtcNow;
        response.Phase = template.Phase;

        var result = await _surveyPort.SubmitResponseAsync(response, ct);
        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        _logger.LogInformation(
            "Survey response submitted: ResponseId={Id}, TemplateId={TemplateId}, UserId={UserId}",
            result.Data!.Id, response.TemplateId, response.UserId);

        // In production: publish SurveyCompletedMessage to RabbitMQ here
        // await _rabbitPublisher.PublishAsync("survey-completed", new SurveyCompletedMessage(...));

        return CreatedAtAction(nameof(GetTemplate), new { id = response.TemplateId }, new
        {
            result.Data.Id,
            result.Data.TemplateId,
            result.Data.RequestId,
            Phase = result.Data.Phase.ToString(),
            result.Data.CompletedAt,
            AnswerCount = result.Data.Answers.Count
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Response queries
    // ─────────────────────────────────────────────────────────────

    /// <summary>Get all survey responses for an incident.</summary>
    [HttpGet("responses/request/{requestId}")]
    public async Task<IActionResult> GetResponsesByRequest(string requestId, CancellationToken ct)
    {
        var result = await _surveyPort.GetResponsesByRequestIdAsync(requestId, ct);
        return Ok(result.Data ?? new List<SurveyResponse>());
    }

    // ─────────────────────────────────────────────────────────────
    // Aggregated results
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Aggregated survey results for a specific template + incident.
    /// Returns response count, completion rate, and per-question summaries.
    /// </summary>
    [HttpGet("results/{templateId}/{requestId}")]
    public async Task<IActionResult> GetAggregatedResults(string templateId, string requestId, CancellationToken ct)
    {
        var templateResult = await _surveyPort.GetTemplateByIdAsync(templateId, ct);
        if (!templateResult.Success)
            return NotFound(new { error = $"Template '{templateId}' not found" });

        var responsesResult = await _surveyPort.GetResponsesByRequestIdAsync(requestId, ct);
        var responses = (responsesResult.Data ?? new List<SurveyResponse>())
            .Where(r => r.TemplateId == templateId)
            .ToList();

        var pendingResult = await _surveyPort.GetPendingSurveyUserIdsAsync(templateId, requestId, ct);
        var pendingCount = pendingResult.Data?.Count ?? 0;

        var template = templateResult.Data!;
        var questionSummaries = template.Questions.Select(q =>
        {
            var answers = responses
                .SelectMany(r => r.Answers)
                .Where(a => a.QuestionId == q.Id)
                .ToList();

            return new
            {
                QuestionId = q.Id,
                q.Text,
                QuestionType = q.QuestionType.ToString(),
                TotalAnswers = answers.Count,
                YesCount = answers.Count(a => a.AnswerText?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true),
                NoCount = answers.Count(a => a.AnswerText?.Equals("No", StringComparison.OrdinalIgnoreCase) == true),
                AverageScale = answers.Where(a => a.ScaleValue.HasValue).Select(a => a.ScaleValue!.Value).DefaultIfEmpty(0).Average(),
                FreeTextSample = answers.Where(a => !string.IsNullOrEmpty(a.AnswerText) && a.AnswerText != "Yes" && a.AnswerText != "No").Take(5).Select(a => a.AnswerText).ToList()
            };
        }).ToList();

        return Ok(new
        {
            TemplateId = templateId,
            RequestId = requestId,
            template.Title,
            TotalResponses = responses.Count,
            PendingResponders = pendingCount,
            CompletionRate = (responses.Count + pendingCount) > 0
                ? (double)responses.Count / (responses.Count + pendingCount) * 100
                : 0,
            Questions = questionSummaries
        });
    }
}
