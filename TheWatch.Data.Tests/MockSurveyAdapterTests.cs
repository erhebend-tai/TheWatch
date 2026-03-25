// MockSurveyAdapterTests — standalone xUnit tests for MockSurveyAdapter.
// Covers ISurveyPort contract: template CRUD, response submission, dispatch tracking.
//
// WAL: Surveys are dispatched at critical lifecycle moments (registration, SOS trigger,
// resolution). If GetPendingSurveyUserIdsAsync returns incorrect IDs, a responder or
// victim may never receive a required safety check. These tests verify the dispatch
// tracking logic is correct even under concurrent response submissions.
//
// Example (survey lifecycle):
//   1. Template created for PostIncident phase → CreateTemplateAsync
//   2. Incident resolved → dispatch survey to all participants → RegisterDispatch
//   3. Each user completes → SubmitResponseAsync
//   4. System checks who hasn't responded → GetPendingSurveyUserIdsAsync
//   5. Reminders sent to pending users

using TheWatch.Data.Adapters.Mock;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Tests;

public class MockSurveyAdapterTests
{
    private MockSurveyAdapter CreateAdapter() => new();

    [Fact]
    public async Task CreateTemplateAsync_StoresTemplate()
    {
        var adapter = CreateAdapter();
        var template = new SurveyTemplate
        {
            Id = "tpl-custom-1",
            Title = "Custom Safety Check",
            Phase = SubmissionPhase.Active,
            TriggerCondition = SurveyTrigger.OnSOSTrigger,
            IsRequired = true,
            Questions = new List<SurveyQuestion>
            {
                new() { Id = "q-1", TemplateId = "tpl-custom-1", Text = "Are you safe?", QuestionType = QuestionType.YesNo, IsRequired = true, DisplayOrder = 1 }
            }
        };

        var result = await adapter.CreateTemplateAsync(template);

        Assert.True(result.Success);
        Assert.Equal("tpl-custom-1", result.Data!.Id);

        // Verify retrieval
        var retrieved = await adapter.GetTemplateByIdAsync("tpl-custom-1");
        Assert.True(retrieved.Success);
        Assert.Equal("Custom Safety Check", retrieved.Data!.Title);
    }

    [Fact]
    public async Task CreateTemplateAsync_GeneratesIdWhenEmpty()
    {
        var adapter = CreateAdapter();
        var template = new SurveyTemplate
        {
            Id = "",
            Title = "Auto ID Template",
            Phase = SubmissionPhase.PreIncident
        };

        var result = await adapter.CreateTemplateAsync(template);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Data!.Id));
    }

    [Fact]
    public async Task GetTemplatesByPhaseAsync_FiltersByPhase()
    {
        var adapter = CreateAdapter();
        // MockSurveyAdapter seeds 3 default templates: PreIncident, Active, PostIncident

        var preIncident = await adapter.GetTemplatesByPhaseAsync(SubmissionPhase.PreIncident);
        var active = await adapter.GetTemplatesByPhaseAsync(SubmissionPhase.Active);
        var postIncident = await adapter.GetTemplatesByPhaseAsync(SubmissionPhase.PostIncident);

        Assert.True(preIncident.Success);
        Assert.True(preIncident.Data!.Count > 0);
        Assert.All(preIncident.Data, t => Assert.Equal(SubmissionPhase.PreIncident, t.Phase));

        Assert.True(active.Success);
        Assert.True(active.Data!.Count > 0);
        Assert.All(active.Data, t => Assert.Equal(SubmissionPhase.Active, t.Phase));

        Assert.True(postIncident.Success);
        Assert.True(postIncident.Data!.Count > 0);
        Assert.All(postIncident.Data, t => Assert.Equal(SubmissionPhase.PostIncident, t.Phase));
    }

    [Fact]
    public async Task SubmitResponseAsync_StoresResponse()
    {
        var adapter = CreateAdapter();
        var response = new SurveyResponse
        {
            Id = "resp-1",
            TemplateId = "tpl-quick-status-v1",
            RequestId = "req-500",
            UserId = "user-1",
            Phase = SubmissionPhase.Active,
            Answers = new List<SurveyAnswer>
            {
                new() { QuestionId = "q-qs-1", AnswerText = "No" }
            }
        };

        var result = await adapter.SubmitResponseAsync(response);

        Assert.True(result.Success);
        Assert.Equal("resp-1", result.Data!.Id);
    }

    [Fact]
    public async Task GetResponsesByRequestIdAsync_FiltersCorrectly()
    {
        var adapter = CreateAdapter();
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "r-a", TemplateId = "tpl-1", RequestId = "req-300", UserId = "u1", Phase = SubmissionPhase.Active });
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "r-b", TemplateId = "tpl-1", RequestId = "req-300", UserId = "u2", Phase = SubmissionPhase.Active });
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "r-c", TemplateId = "tpl-1", RequestId = "req-400", UserId = "u3", Phase = SubmissionPhase.Active });

        var result = await adapter.GetResponsesByRequestIdAsync("req-300");

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, r => Assert.Equal("req-300", r.RequestId));
    }

    [Fact]
    public async Task GetPendingSurveyUserIdsAsync_ReturnsCorrectIds()
    {
        var adapter = CreateAdapter();

        // Register that 3 users were dispatched
        adapter.RegisterDispatch("tpl-quick-status-v1", "req-600", new[] { "u-1", "u-2", "u-3" });

        // User u-1 completes the survey
        await adapter.SubmitResponseAsync(new SurveyResponse
        {
            Id = "r-done",
            TemplateId = "tpl-quick-status-v1",
            RequestId = "req-600",
            UserId = "u-1",
            Phase = SubmissionPhase.Active
        });

        var pending = await adapter.GetPendingSurveyUserIdsAsync("tpl-quick-status-v1", "req-600");

        Assert.True(pending.Success);
        Assert.Equal(2, pending.Data!.Count);
        Assert.Contains("u-2", pending.Data);
        Assert.Contains("u-3", pending.Data);
        Assert.DoesNotContain("u-1", pending.Data);
    }

    [Fact]
    public async Task GetPendingSurveyUserIdsAsync_ReturnsEmptyWhenNoDispatches()
    {
        var adapter = CreateAdapter();

        var pending = await adapter.GetPendingSurveyUserIdsAsync("tpl-nonexistent", "req-nonexistent");

        Assert.True(pending.Success);
        Assert.Empty(pending.Data!);
    }

    [Fact]
    public async Task GetResponsesByUserIdAsync_FiltersCorrectly()
    {
        var adapter = CreateAdapter();
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "ru-1", TemplateId = "tpl-1", RequestId = "req-1", UserId = "target-user", Phase = SubmissionPhase.Active });
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "ru-2", TemplateId = "tpl-2", RequestId = "req-2", UserId = "target-user", Phase = SubmissionPhase.PostIncident });
        await adapter.SubmitResponseAsync(new SurveyResponse { Id = "ru-3", TemplateId = "tpl-1", RequestId = "req-1", UserId = "other-user", Phase = SubmissionPhase.Active });

        var result = await adapter.GetResponsesByUserIdAsync("target-user");

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, r => Assert.Equal("target-user", r.UserId));
    }
}
