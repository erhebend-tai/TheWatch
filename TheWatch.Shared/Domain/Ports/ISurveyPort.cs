// ISurveyPort — domain port for survey lifecycle management.
// NO database SDK imports allowed in this file. Adapters implement this.
// Handles template CRUD, response collection, and pending survey tracking.
//
// Survey dispatch flow:
//   SurveyDispatchFunction (timer) → checks triggers → ISurveyPort.GetTemplatesByPhaseAsync
//   → publishes SurveyDispatchMessage → mobile app renders survey → user submits
//   → SurveysController → ISurveyPort.SubmitResponseAsync → SurveyCompletedMessage published
//
// Example:
//   var templates = await surveyPort.GetTemplatesByPhaseAsync(SubmissionPhase.PostIncident, ct);
//   var response = await surveyPort.SubmitResponseAsync(userResponse, ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface ISurveyPort
{
    // ── Template management ────────────────────────
    /// <summary>Create a new survey template.</summary>
    Task<StorageResult<SurveyTemplate>> CreateTemplateAsync(SurveyTemplate template, CancellationToken ct = default);

    /// <summary>Get all templates for a specific phase (e.g., all post-incident surveys).</summary>
    Task<StorageResult<List<SurveyTemplate>>> GetTemplatesByPhaseAsync(SubmissionPhase phase, CancellationToken ct = default);

    /// <summary>Get a single template by ID.</summary>
    Task<StorageResult<SurveyTemplate>> GetTemplateByIdAsync(string templateId, CancellationToken ct = default);

    // ── Response collection ────────────────────────
    /// <summary>Submit a completed survey response.</summary>
    Task<StorageResult<SurveyResponse>> SubmitResponseAsync(SurveyResponse response, CancellationToken ct = default);

    /// <summary>Get all survey responses for an incident.</summary>
    Task<StorageResult<List<SurveyResponse>>> GetResponsesByRequestIdAsync(string requestId, CancellationToken ct = default);

    /// <summary>Get all survey responses by a specific user.</summary>
    Task<StorageResult<List<SurveyResponse>>> GetResponsesByUserIdAsync(string userId, CancellationToken ct = default);

    // ── Dispatch tracking ──────────────────────────
    /// <summary>
    /// Get user IDs who have NOT yet completed a specific survey for a specific incident.
    /// Used by the dispatch function to send reminders.
    /// </summary>
    Task<StorageResult<List<string>>> GetPendingSurveyUserIdsAsync(string templateId, string requestId, CancellationToken ct = default);
}
