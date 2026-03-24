// MockSurveyAdapter — ConcurrentDictionary-backed ISurveyPort for dev/testing.
// Seeds default survey templates for each phase on construction.
// Tracks dispatched surveys per (templateId, requestId) for pending survey queries.
//
// Example:
//   services.AddSingleton<ISurveyPort, MockSurveyAdapter>();
//   var templates = await surveyPort.GetTemplatesByPhaseAsync(SubmissionPhase.PostIncident, ct);

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockSurveyAdapter : ISurveyPort
{
    private readonly ConcurrentDictionary<string, SurveyTemplate> _templates = new();
    private readonly ConcurrentDictionary<string, SurveyResponse> _responses = new();

    // Tracks which users were dispatched a survey: Key = "{templateId}:{requestId}" → user IDs
    private readonly ConcurrentDictionary<string, HashSet<string>> _dispatchedUsers = new();

    public MockSurveyAdapter()
    {
        SeedDefaultTemplates();
    }

    // ── Template management ────────────────────────

    public Task<StorageResult<SurveyTemplate>> CreateTemplateAsync(SurveyTemplate template, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(template.Id))
            template.Id = Guid.NewGuid().ToString();
        _templates[template.Id] = template;
        return Task.FromResult(StorageResult<SurveyTemplate>.Ok(template));
    }

    public Task<StorageResult<List<SurveyTemplate>>> GetTemplatesByPhaseAsync(SubmissionPhase phase, CancellationToken ct = default)
    {
        var results = _templates.Values.Where(t => t.Phase == phase).ToList();
        return Task.FromResult(StorageResult<List<SurveyTemplate>>.Ok(results));
    }

    public Task<StorageResult<SurveyTemplate>> GetTemplateByIdAsync(string templateId, CancellationToken ct = default)
    {
        if (_templates.TryGetValue(templateId, out var tpl))
            return Task.FromResult(StorageResult<SurveyTemplate>.Ok(tpl));
        return Task.FromResult(StorageResult<SurveyTemplate>.Fail($"Template '{templateId}' not found"));
    }

    // ── Response collection ────────────────────────

    public Task<StorageResult<SurveyResponse>> SubmitResponseAsync(SurveyResponse response, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(response.Id))
            response.Id = Guid.NewGuid().ToString();
        _responses[response.Id] = response;
        return Task.FromResult(StorageResult<SurveyResponse>.Ok(response));
    }

    public Task<StorageResult<List<SurveyResponse>>> GetResponsesByRequestIdAsync(string requestId, CancellationToken ct = default)
    {
        var results = _responses.Values
            .Where(r => r.RequestId == requestId)
            .OrderByDescending(r => r.CompletedAt)
            .ToList();
        return Task.FromResult(StorageResult<List<SurveyResponse>>.Ok(results));
    }

    public Task<StorageResult<List<SurveyResponse>>> GetResponsesByUserIdAsync(string userId, CancellationToken ct = default)
    {
        var results = _responses.Values
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CompletedAt)
            .ToList();
        return Task.FromResult(StorageResult<List<SurveyResponse>>.Ok(results));
    }

    // ── Dispatch tracking ──────────────────────────

    public Task<StorageResult<List<string>>> GetPendingSurveyUserIdsAsync(string templateId, string requestId, CancellationToken ct = default)
    {
        var key = $"{templateId}:{requestId}";
        if (!_dispatchedUsers.TryGetValue(key, out var dispatched))
            return Task.FromResult(StorageResult<List<string>>.Ok(new List<string>()));

        var completedUserIds = _responses.Values
            .Where(r => r.TemplateId == templateId && r.RequestId == requestId)
            .Select(r => r.UserId)
            .ToHashSet();

        var pending = dispatched.Where(uid => !completedUserIds.Contains(uid)).ToList();
        return Task.FromResult(StorageResult<List<string>>.Ok(pending));
    }

    /// <summary>
    /// Mock helper: register that a survey was dispatched to specific users.
    /// Called by test code or the mock dispatch pipeline.
    /// </summary>
    public void RegisterDispatch(string templateId, string requestId, IEnumerable<string> userIds)
    {
        var key = $"{templateId}:{requestId}";
        _dispatchedUsers.AddOrUpdate(key,
            _ => new HashSet<string>(userIds),
            (_, existing) => { foreach (var uid in userIds) existing.Add(uid); return existing; });
    }

    // ── Seed data ──────────────────────────────────

    private void SeedDefaultTemplates()
    {
        // Pre-incident: household safety survey (dispatched on registration)
        var preIncident = new SurveyTemplate
        {
            Id = "tpl-household-safety-v1",
            Title = "Household Safety Survey",
            Description = "Help us prepare for emergencies by documenting your household details.",
            Phase = SubmissionPhase.PreIncident,
            TriggerCondition = SurveyTrigger.OnRegistration,
            IsRequired = false,
            TimeoutMinutes = null,
            Version = 1,
            Questions = new List<SurveyQuestion>
            {
                new() { Id = "q-hs-1", TemplateId = "tpl-household-safety-v1", Text = "How many people live in your household?", QuestionType = QuestionType.FreeText, IsRequired = true, DisplayOrder = 1 },
                new() { Id = "q-hs-2", TemplateId = "tpl-household-safety-v1", Text = "Are there any mobility-impaired individuals?", QuestionType = QuestionType.YesNo, IsRequired = true, DisplayOrder = 2 },
                new() { Id = "q-hs-3", TemplateId = "tpl-household-safety-v1", Text = "Describe any mobility limitations", QuestionType = QuestionType.FreeText, IsRequired = false, DisplayOrder = 3, ConditionalOnQuestionId = "q-hs-2", ConditionalOnAnswer = "Yes" },
                new() { Id = "q-hs-4", TemplateId = "tpl-household-safety-v1", Text = "Do you have pets?", QuestionType = QuestionType.YesNo, IsRequired = false, DisplayOrder = 4 },
                new() { Id = "q-hs-5", TemplateId = "tpl-household-safety-v1", Text = "Take a photo of your main entrance", QuestionType = QuestionType.PhotoCapture, IsRequired = false, DisplayOrder = 5 },
            }
        };

        // Active: quick status check (dispatched on SOS trigger)
        var active = new SurveyTemplate
        {
            Id = "tpl-quick-status-v1",
            Title = "Quick Status Check",
            Description = "Rapid assessment during an active incident.",
            Phase = SubmissionPhase.Active,
            TriggerCondition = SurveyTrigger.OnSOSTrigger,
            IsRequired = true,
            TimeoutMinutes = 5,
            Version = 1,
            Questions = new List<SurveyQuestion>
            {
                new() { Id = "q-qs-1", TemplateId = "tpl-quick-status-v1", Text = "Are you injured?", QuestionType = QuestionType.YesNo, IsRequired = true, DisplayOrder = 1 },
                new() { Id = "q-qs-2", TemplateId = "tpl-quick-status-v1", Text = "Rate the severity (1=minor, 5=critical)", QuestionType = QuestionType.Scale, IsRequired = false, DisplayOrder = 2, ConditionalOnQuestionId = "q-qs-1", ConditionalOnAnswer = "Yes" },
                new() { Id = "q-qs-3", TemplateId = "tpl-quick-status-v1", Text = "Can you move to a safe location?", QuestionType = QuestionType.YesNo, IsRequired = true, DisplayOrder = 3 },
            }
        };

        // Post-incident: wellbeing check (dispatched on resolution)
        var postIncident = new SurveyTemplate
        {
            Id = "tpl-postincident-wellbeing-v1",
            Title = "Post-Incident Wellbeing Check",
            Description = "Follow-up after incident resolution to assess wellbeing and responder performance.",
            Phase = SubmissionPhase.PostIncident,
            TriggerCondition = SurveyTrigger.OnResolution,
            IsRequired = true,
            TimeoutMinutes = 1440, // 24 hours
            Version = 1,
            Questions = new List<SurveyQuestion>
            {
                new() { Id = "q-pw-1", TemplateId = "tpl-postincident-wellbeing-v1", Text = "Are you safe now?", QuestionType = QuestionType.YesNo, IsRequired = true, DisplayOrder = 1 },
                new() { Id = "q-pw-2", TemplateId = "tpl-postincident-wellbeing-v1", Text = "Rate the responder(s) helpfulness (1-5)", QuestionType = QuestionType.Scale, IsRequired = false, DisplayOrder = 2 },
                new() { Id = "q-pw-3", TemplateId = "tpl-postincident-wellbeing-v1", Text = "Any property damage?", QuestionType = QuestionType.YesNo, IsRequired = false, DisplayOrder = 3 },
                new() { Id = "q-pw-4", TemplateId = "tpl-postincident-wellbeing-v1", Text = "Document any damage (photo)", QuestionType = QuestionType.PhotoCapture, IsRequired = false, DisplayOrder = 4, ConditionalOnQuestionId = "q-pw-3", ConditionalOnAnswer = "Yes" },
                new() { Id = "q-pw-5", TemplateId = "tpl-postincident-wellbeing-v1", Text = "Additional comments or concerns", QuestionType = QuestionType.FreeText, IsRequired = false, DisplayOrder = 5 },
            }
        };

        _templates[preIncident.Id] = preIncident;
        _templates[active.Id] = active;
        _templates[postIncident.Id] = postIncident;
    }
}
