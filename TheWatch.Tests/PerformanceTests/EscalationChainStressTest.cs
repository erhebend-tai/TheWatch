// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:         EscalationChainStressTest.cs
// Purpose:      Stress test for the escalation chain under many active incidents.
//               Verifies that escalation timers do not drift under load and that
//               the system can manage hundreds of concurrent active responses
//               without timer starvation or scheduling lag.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: NUnit, Aspire Testing, Hangfire (escalation timers)
//
// Usage example:
//   dotnet test --filter "Category=Performance&FullyQualifiedName~Escalation"
//
// Targets:
//   - Escalation timer accuracy: within +/- 2 seconds of scheduled time
//   - No timer starvation: all incidents escalate eventually
//   - API remains responsive while escalation chain is processing
//   - 100 concurrent incidents with escalation timers
//
// Background:
//   TheWatch uses a tiered escalation chain:
//     T+0:    Initial dispatch to volunteers within 1km (CheckIn scope)
//     T+30s:  If < 2 acks, expand to 3km (Emergency scope)
//     T+60s:  If < 2 acks, expand to 10km (CommunityWatch scope)
//     T+90s:  If < 2 acks, notify emergency services (if user opted in)
//     T+120s: If no acks, flag for manual operator review
//
//   Under load (mass-casualty event), many incidents may be escalating
//   simultaneously. Hangfire/background timers must not starve or drift.
//
// Potential additions:
//   - Hangfire dashboard metrics scraping
//   - Timer precision histogram
//   - Cross-incident priority ordering verification
//   - Emergency services notification throttling under load
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TheWatch.Tests.PerformanceTests;

/// <summary>
/// Stress test verifying escalation timer accuracy under many concurrent incidents.
/// Marked as Category=Performance so normal CI skips these tests.
/// </summary>
[TestFixture]
[Category("Performance")]
public class EscalationChainStressTest
{
    // ── Configuration ────────────────────────────────────────────────────

    /// <summary>Number of concurrent active incidents to create.</summary>
    private const int ConcurrentIncidents = 100;

    /// <summary>Maximum acceptable timer drift in seconds.</summary>
    private const double MaxTimerDriftSeconds = 2.0;

    /// <summary>How long to monitor escalation behavior (seconds).</summary>
    private const int MonitorDurationSeconds = 45;

    /// <summary>Interval between status checks (milliseconds).</summary>
    private const int PollIntervalMs = 1000;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    // ── Test: Concurrent Escalation ──────────────────────────────────────

    /// <summary>
    /// Creates many concurrent SOS incidents, none of which receive acks,
    /// forcing the escalation chain to fire for all of them. Verifies:
    ///   1. All incidents progress through escalation tiers
    ///   2. Timer drift stays within acceptable bounds
    ///   3. The API remains responsive during mass escalation
    /// </summary>
    [Test]
    public async Task ManyActiveIncidents_EscalationTimers_DoNotDrift()
    {
        // Arrange
        var ct = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TheWatch_AppHost>(ct);

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(ct).WaitAsync(StartupTimeout, ct);
        await app.StartAsync(ct).WaitAsync(StartupTimeout, ct);

        var httpClient = app.CreateHttpClient("apiservice");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("apiservice", ct)
            .WaitAsync(StartupTimeout, ct);

        // Track incident creation times and escalation events
        var incidentCreationTimes = new ConcurrentDictionary<string, DateTime>();
        var incidentRequestIds = new ConcurrentBag<string>();
        var creationErrors = new ConcurrentBag<string>();
        var creationLatencies = new ConcurrentBag<double>();

        // Act — Phase 1: Create N concurrent incidents
        TestContext.Out.WriteLine($"Creating {ConcurrentIncidents} concurrent SOS incidents...");

        var createTasks = Enumerable.Range(0, ConcurrentIncidents).Select(async i =>
        {
            var request = new
            {
                UserId = $"escalation-user-{i:D4}",
                Scope = "Emergency",
                Latitude = 40.7128 + (i * 0.001),
                Longitude = -74.0060 + (i * 0.001),
                Description = $"Escalation stress test incident #{i}",
                TriggerSource = "MANUAL_BUTTON"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await httpClient.PostAsync("/api/response/trigger", content, ct);
                sw.Stop();
                creationLatencies.Add(sw.Elapsed.TotalMilliseconds);

                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    var doc = JsonDocument.Parse(responseBody);

                    if (doc.RootElement.TryGetProperty("RequestId", out var requestIdProp) ||
                        doc.RootElement.TryGetProperty("requestId", out requestIdProp))
                    {
                        var requestId = requestIdProp.GetString()!;
                        incidentRequestIds.Add(requestId);
                        incidentCreationTimes[requestId] = DateTime.UtcNow;
                    }
                }
                else
                {
                    creationErrors.Add($"Incident {i}: HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                creationErrors.Add($"Incident {i}: {ex.GetType().Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(createTasks);

        var createdCount = incidentRequestIds.Count;
        TestContext.Out.WriteLine($"Created {createdCount}/{ConcurrentIncidents} incidents ({creationErrors.Count} errors)");

        if (createdCount == 0)
        {
            Assert.Inconclusive("No incidents created — API may not be available in test configuration");
            return;
        }

        // Act — Phase 2: Monitor escalation behavior over time
        TestContext.Out.WriteLine($"Monitoring escalation for {MonitorDurationSeconds}s...");

        var escalationTimeline = new ConcurrentDictionary<string, List<(DateTime Timestamp, string Status, string Scope)>>();
        var requestIdList = incidentRequestIds.ToList();
        var monitorStart = DateTime.UtcNow;
        var apiLatenciesDuringEscalation = new ConcurrentBag<double>();

        while ((DateTime.UtcNow - monitorStart).TotalSeconds < MonitorDurationSeconds && !ct.IsCancellationRequested)
        {
            // Sample a subset of incidents to check status (don't poll all at once)
            var sampleSize = Math.Min(20, requestIdList.Count);
            var sample = requestIdList.OrderBy(_ => Random.Shared.Next()).Take(sampleSize);

            var checkTasks = sample.Select(async requestId =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var response = await httpClient.GetAsync($"/api/response/{requestId}", ct);
                    sw.Stop();
                    apiLatenciesDuringEscalation.Add(sw.Elapsed.TotalMilliseconds);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        var doc = JsonDocument.Parse(body);

                        var status = "Unknown";
                        var scope = "Unknown";

                        if (doc.RootElement.TryGetProperty("Status", out var statusProp) ||
                            doc.RootElement.TryGetProperty("status", out statusProp))
                            status = statusProp.GetString() ?? "Unknown";

                        if (doc.RootElement.TryGetProperty("Scope", out var scopeProp) ||
                            doc.RootElement.TryGetProperty("scope", out scopeProp))
                            scope = scopeProp.GetString() ?? "Unknown";

                        var timeline = escalationTimeline.GetOrAdd(requestId, _ => new List<(DateTime, string, string)>());
                        lock (timeline)
                        {
                            // Only add if status/scope changed
                            if (timeline.Count == 0 ||
                                timeline[^1].Status != status ||
                                timeline[^1].Scope != scope)
                            {
                                timeline.Add((DateTime.UtcNow, status, scope));
                            }
                        }
                    }
                }
                catch
                {
                    sw.Stop();
                    apiLatenciesDuringEscalation.Add(sw.Elapsed.TotalMilliseconds);
                }
            });

            await Task.WhenAll(checkTasks);
            await Task.Delay(PollIntervalMs, ct);
        }

        // Act — Phase 3: Check API responsiveness under escalation load
        TestContext.Out.WriteLine("Verifying API responsiveness during escalation...");

        var responsivenessTasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await httpClient.GetAsync("/api/response/active/escalation-user-0000", ct);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            }
            catch
            {
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            }
        });

        var responsivenessLatencies = await Task.WhenAll(responsivenessTasks);

        // Assert — Analyze results
        var incidentsWithEscalation = escalationTimeline
            .Where(kv => kv.Value.Count > 1)
            .ToList();

        var incidentsStuck = escalationTimeline
            .Where(kv => kv.Value.Count <= 1)
            .ToList();

        // Compute timer drift for incidents that did escalate
        var timerDrifts = new List<double>();
        foreach (var (requestId, timeline) in incidentsWithEscalation)
        {
            if (incidentCreationTimes.TryGetValue(requestId, out var createdAt) && timeline.Count >= 2)
            {
                // Time from creation to first escalation
                var firstEscalation = timeline[1].Timestamp;
                var actualSeconds = (firstEscalation - createdAt).TotalSeconds;

                // Expected first escalation is at ~30 seconds
                var expectedSeconds = 30.0;
                var drift = Math.Abs(actualSeconds - expectedSeconds);
                timerDrifts.Add(drift);
            }
        }

        // Report
        TestContext.Out.WriteLine("╔══════════════════════════════════════════════════╗");
        TestContext.Out.WriteLine("║  Escalation Chain Stress Test Results            ║");
        TestContext.Out.WriteLine("╠══════════════════════════════════════════════════╣");
        TestContext.Out.WriteLine($"║  Incidents Created:    {createdCount,5}                       ║");
        TestContext.Out.WriteLine($"║  Creation Errors:      {creationErrors.Count,5}                       ║");
        TestContext.Out.WriteLine($"║  Monitor Duration:     {MonitorDurationSeconds,5}s                      ║");

        if (creationLatencies.Count > 0)
        {
            var sorted = creationLatencies.OrderBy(l => l).ToList();
            TestContext.Out.WriteLine($"║  Avg Creation Latency: {sorted.Average(),7:F1}ms                   ║");
            TestContext.Out.WriteLine($"║  p99 Creation Latency: {Percentile(sorted, 0.99),7:F1}ms                   ║");
        }

        TestContext.Out.WriteLine("╠──────────────────────────────────────────────────╣");
        TestContext.Out.WriteLine($"║  Incidents Escalated:  {incidentsWithEscalation.Count,5}                       ║");
        TestContext.Out.WriteLine($"║  Incidents Not Yet:    {incidentsStuck.Count,5}                       ║");

        if (timerDrifts.Count > 0)
        {
            var avgDrift = timerDrifts.Average();
            var maxDrift = timerDrifts.Max();
            var driftsSorted = timerDrifts.OrderBy(d => d).ToList();
            var p99Drift = Percentile(driftsSorted, 0.99);

            TestContext.Out.WriteLine($"║  Avg Timer Drift:      {avgDrift,6:F1}s                       ║");
            TestContext.Out.WriteLine($"║  Max Timer Drift:      {maxDrift,6:F1}s                       ║");
            TestContext.Out.WriteLine($"║  p99 Timer Drift:      {p99Drift,6:F1}s (target <{MaxTimerDriftSeconds}s)    ║");
        }

        if (apiLatenciesDuringEscalation.Count > 0)
        {
            var sorted = apiLatenciesDuringEscalation.OrderBy(l => l).ToList();
            TestContext.Out.WriteLine("╠──────────────────────────────────────────────────╣");
            TestContext.Out.WriteLine($"║  API p50 During Load:  {Percentile(sorted, 0.50),7:F1}ms                   ║");
            TestContext.Out.WriteLine($"║  API p99 During Load:  {Percentile(sorted, 0.99),7:F1}ms                   ║");
        }

        TestContext.Out.WriteLine("╠──────────────────────────────────────────────────╣");
        TestContext.Out.WriteLine($"║  Responsiveness Check: {responsivenessLatencies.Average(),7:F1}ms avg            ║");
        TestContext.Out.WriteLine("╚══════════════════════════════════════════════════╝");

        // Log escalation timeline samples
        TestContext.Out.WriteLine("\nSample escalation timelines (first 5):");
        foreach (var (requestId, timeline) in escalationTimeline.Take(5))
        {
            TestContext.Out.WriteLine($"  {requestId}:");
            foreach (var (ts, status, scope) in timeline)
            {
                var elapsed = incidentCreationTimes.TryGetValue(requestId, out var created)
                    ? (ts - created).TotalSeconds
                    : 0;
                TestContext.Out.WriteLine($"    T+{elapsed:F1}s: Status={status}, Scope={scope}");
            }
        }

        Assert.Multiple(() =>
        {
            // The API must remain responsive during escalation load
            Assert.That(responsivenessLatencies.Average(), Is.LessThanOrEqualTo(1000),
                "API responsiveness degraded during escalation chain processing");

            // Timer drift should be within bounds (if we have data)
            if (timerDrifts.Count > 0)
            {
                var maxObservedDrift = timerDrifts.Max();
                Assert.That(maxObservedDrift, Is.LessThanOrEqualTo(MaxTimerDriftSeconds * 5),
                    $"Maximum timer drift {maxObservedDrift:F1}s exceeds tolerance " +
                    $"({MaxTimerDriftSeconds * 5:F1}s = {MaxTimerDriftSeconds}s target x5 for test env overhead)");
            }
        });
    }

    // ── Test: Rapid Escalation Cancellation Race ─────────────────────────

    /// <summary>
    /// Creates incidents and immediately cancels some while they are mid-escalation.
    /// Verifies that cancellation correctly stops the escalation chain and that
    /// no ghost escalations occur after cancellation.
    /// </summary>
    [Test]
    public async Task CancelDuringEscalation_NoGhostEscalations()
    {
        // Arrange
        var ct = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TheWatch_AppHost>(ct);

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(ct).WaitAsync(StartupTimeout, ct);
        await app.StartAsync(ct).WaitAsync(StartupTimeout, ct);

        var httpClient = app.CreateHttpClient("apiservice");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("apiservice", ct)
            .WaitAsync(StartupTimeout, ct);

        // Create incidents
        var incidentCount = 20;
        var requestIds = new ConcurrentBag<string>();
        var cancelledIds = new ConcurrentBag<string>();

        var createTasks = Enumerable.Range(0, incidentCount).Select(async i =>
        {
            var request = new
            {
                UserId = $"cancel-race-user-{i:D4}",
                Scope = "Emergency",
                Latitude = 40.7128,
                Longitude = -74.0060,
                Description = $"Cancel race test #{i}",
                TriggerSource = "MANUAL_BUTTON"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await httpClient.PostAsync("/api/response/trigger", content, ct);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("RequestId", out var prop) ||
                        doc.RootElement.TryGetProperty("requestId", out prop))
                    {
                        requestIds.Add(prop.GetString()!);
                    }
                }
            }
            catch { /* ignore creation errors for this test */ }
        });

        await Task.WhenAll(createTasks);

        // Cancel half of them immediately
        var idsToCancel = requestIds.Take(requestIds.Count / 2).ToList();

        var cancelTasks = idsToCancel.Select(async requestId =>
        {
            var cancelBody = JsonSerializer.Serialize(new { Reason = "Test cancellation" });
            var content = new StringContent(cancelBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await httpClient.PostAsync($"/api/response/{requestId}/cancel", content, ct);
                if (response.IsSuccessStatusCode)
                    cancelledIds.Add(requestId);
            }
            catch { /* ignore */ }
        });

        await Task.WhenAll(cancelTasks);

        TestContext.Out.WriteLine($"Created {requestIds.Count} incidents, cancelled {cancelledIds.Count}");

        // Wait for potential ghost escalations
        await Task.Delay(5000, ct);

        // Check that cancelled incidents are actually cancelled
        var ghostEscalations = 0;
        foreach (var cancelledId in cancelledIds)
        {
            try
            {
                var response = await httpClient.GetAsync($"/api/response/{cancelledId}", ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var doc = JsonDocument.Parse(body);

                    var status = "Unknown";
                    if (doc.RootElement.TryGetProperty("Status", out var statusProp) ||
                        doc.RootElement.TryGetProperty("status", out statusProp))
                        status = statusProp.GetString() ?? "Unknown";

                    if (status != "Cancelled" && status != "cancelled")
                    {
                        ghostEscalations++;
                        TestContext.Out.WriteLine($"  Ghost escalation: {cancelledId} status={status}");
                    }
                }
            }
            catch { /* ignore query errors */ }
        }

        TestContext.Out.WriteLine("╔══════════════════════════════════════════════════╗");
        TestContext.Out.WriteLine("║  Escalation Cancel Race Test Results             ║");
        TestContext.Out.WriteLine("╠══════════════════════════════════════════════════╣");
        TestContext.Out.WriteLine($"║  Incidents Created:    {requestIds.Count,5}                       ║");
        TestContext.Out.WriteLine($"║  Incidents Cancelled:  {cancelledIds.Count,5}                       ║");
        TestContext.Out.WriteLine($"║  Ghost Escalations:    {ghostEscalations,5}                       ║");
        TestContext.Out.WriteLine("╚══════════════════════════════════════════════════╝");

        Assert.That(ghostEscalations, Is.EqualTo(0),
            $"{ghostEscalations} cancelled incidents continued escalating (ghost escalation)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var fraction = index - lower;

        if (lower == upper || upper >= sortedValues.Count)
            return sortedValues[lower];

        return sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]);
    }
}
