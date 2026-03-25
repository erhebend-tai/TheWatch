// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:         SosDispatchLoadTest.cs
// Purpose:      Load test for the SOS dispatch endpoint (POST /api/response/trigger).
//               Simulates N concurrent SOS triggers and measures latency percentiles.
//               Life-safety systems MUST respond within strict latency budgets.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: NUnit, Microsoft.AspNetCore.Mvc.Testing, System.Diagnostics
//
// Usage example:
//   # Run only performance tests:
//   dotnet test --filter "Category=Performance"
//
//   # Run with custom concurrency:
//   dotnet test --filter "Category=Performance" -- NUnit.NumberOfTestWorkers=1
//
// Targets:
//   p50 < 100ms — median response time for SOS trigger
//   p95 < 150ms — 95th percentile
//   p99 < 200ms — 99th percentile (hard requirement for life-safety)
//
// Potential additions:
//   - NBomber or k6 integration for sustained load profiles
//   - Grafana/Prometheus metrics export
//   - Geographic distribution simulation (multi-region)
//   - Offline-queue backpressure testing
//   - Redis cache hit/miss ratio under load
// =============================================================================

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TheWatch.Tests.PerformanceTests;

/// <summary>
/// Load test simulating concurrent SOS triggers hitting POST /api/response/trigger.
/// Marked as Category=Performance so normal CI skips these tests.
///
/// These tests use the Aspire DistributedApplicationTestingBuilder to spin up
/// the full API stack in-process, then hammer it with parallel HTTP requests.
/// </summary>
[TestFixture]
[Category("Performance")]
public class SosDispatchLoadTest
{
    // ── Configuration ────────────────────────────────────────────────────

    /// <summary>Number of concurrent SOS triggers to fire simultaneously.</summary>
    private const int DefaultConcurrentUsers = 100;

    /// <summary>Total test duration for sustained load (seconds).</summary>
    private const int DefaultDurationSeconds = 30;

    /// <summary>Latency targets in milliseconds.</summary>
    private const double TargetP50Ms = 100.0;
    private const double TargetP95Ms = 150.0;
    private const double TargetP99Ms = 200.0;

    /// <summary>Maximum acceptable error rate (percentage).</summary>
    private const double MaxErrorRatePercent = 1.0;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    // ── Test: Burst Load ─────────────────────────────────────────────────

    /// <summary>
    /// Fires N concurrent SOS triggers simultaneously and measures latency distribution.
    /// This simulates a mass-casualty or natural disaster scenario where many users
    /// trigger SOS at the same time.
    ///
    /// Assertions:
    ///   - All requests return 2xx (Accepted)
    ///   - p50 latency &lt; 100ms
    ///   - p95 latency &lt; 150ms
    ///   - p99 latency &lt; 200ms
    /// </summary>
    [Test]
    public async Task BurstSosTrigger_ConcurrentUsers_MeetsLatencyTargets()
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

        var latencies = new List<double>(DefaultConcurrentUsers);
        var errors = new List<string>();
        var lockObj = new object();

        // Act — fire N concurrent SOS triggers
        var tasks = Enumerable.Range(0, DefaultConcurrentUsers).Select(async i =>
        {
            var request = new
            {
                UserId = $"loadtest-user-{i:D4}",
                Scope = "Emergency",
                Latitude = 40.7128 + (i * 0.0001),
                Longitude = -74.0060 + (i * 0.0001),
                Description = $"Load test SOS trigger #{i}",
                TriggerSource = "MANUAL_BUTTON"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await httpClient.PostAsync("/api/response/trigger", content, ct);
                sw.Stop();

                lock (lockObj)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);

                    if (!response.IsSuccessStatusCode &&
                        response.StatusCode != HttpStatusCode.Accepted)
                    {
                        errors.Add($"User {i}: HTTP {(int)response.StatusCode} {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                lock (lockObj)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                    errors.Add($"User {i}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        await Task.WhenAll(tasks);

        // Assert — compute percentiles and verify targets
        Assert.That(latencies, Is.Not.Empty, "No latency measurements collected");

        var sorted = latencies.OrderBy(l => l).ToList();
        var p50 = Percentile(sorted, 0.50);
        var p95 = Percentile(sorted, 0.95);
        var p99 = Percentile(sorted, 0.99);
        var avg = sorted.Average();
        var max = sorted.Max();
        var min = sorted.Min();
        var errorRate = (double)errors.Count / latencies.Count * 100;

        // Log results for CI output
        TestContext.Out.WriteLine("╔══════════════════════════════════════════════════╗");
        TestContext.Out.WriteLine("║  SOS Dispatch Burst Load Test Results            ║");
        TestContext.Out.WriteLine("╠══════════════════════════════════════════════════╣");
        TestContext.Out.WriteLine($"║  Concurrent Users:  {DefaultConcurrentUsers,6}                       ║");
        TestContext.Out.WriteLine($"║  Total Requests:    {latencies.Count,6}                       ║");
        TestContext.Out.WriteLine($"║  Errors:            {errors.Count,6} ({errorRate:F1}%)               ║");
        TestContext.Out.WriteLine("╠══════════════════════════════════════════════════╣");
        TestContext.Out.WriteLine($"║  Min Latency:       {min,8:F1}ms                    ║");
        TestContext.Out.WriteLine($"║  Avg Latency:       {avg,8:F1}ms                    ║");
        TestContext.Out.WriteLine($"║  p50 Latency:       {p50,8:F1}ms (target <{TargetP50Ms}ms)   ║");
        TestContext.Out.WriteLine($"║  p95 Latency:       {p95,8:F1}ms (target <{TargetP95Ms}ms)   ║");
        TestContext.Out.WriteLine($"║  p99 Latency:       {p99,8:F1}ms (target <{TargetP99Ms}ms)   ║");
        TestContext.Out.WriteLine($"║  Max Latency:       {max,8:F1}ms                    ║");
        TestContext.Out.WriteLine("╚══════════════════════════════════════════════════╝");

        if (errors.Count > 0)
        {
            TestContext.Out.WriteLine($"\nFirst 10 errors:");
            foreach (var err in errors.Take(10))
                TestContext.Out.WriteLine($"  - {err}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(errorRate, Is.LessThanOrEqualTo(MaxErrorRatePercent),
                $"Error rate {errorRate:F1}% exceeds maximum {MaxErrorRatePercent}%");
            Assert.That(p50, Is.LessThanOrEqualTo(TargetP50Ms),
                $"p50 latency {p50:F1}ms exceeds target {TargetP50Ms}ms");
            Assert.That(p95, Is.LessThanOrEqualTo(TargetP95Ms),
                $"p95 latency {p95:F1}ms exceeds target {TargetP95Ms}ms");
            Assert.That(p99, Is.LessThanOrEqualTo(TargetP99Ms),
                $"p99 latency {p99:F1}ms exceeds target {TargetP99Ms}ms");
        });
    }

    // ── Test: Sustained Load ─────────────────────────────────────────────

    /// <summary>
    /// Fires SOS triggers continuously for the configured duration, maintaining
    /// a steady request rate. Verifies the API does not degrade over time.
    /// </summary>
    [Test]
    public async Task SustainedSosTrigger_SteadyRate_NoLatencyDegradation()
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

        // Collect latencies in time-bucketed windows (1-second buckets)
        var buckets = new Dictionary<int, List<double>>();
        var lockObj = new object();
        var overallErrors = 0;
        var overallRequests = 0;

        var testDuration = TimeSpan.FromSeconds(DefaultDurationSeconds);
        var startTime = Stopwatch.StartNew();
        var requestIndex = 0;

        // Act — sustain ~10 requests/second for the duration
        var requestsPerSecond = 10;
        var delayBetweenBatches = TimeSpan.FromMilliseconds(1000.0 / requestsPerSecond);

        while (startTime.Elapsed < testDuration && !ct.IsCancellationRequested)
        {
            var idx = Interlocked.Increment(ref requestIndex);
            var secondBucket = (int)startTime.Elapsed.TotalSeconds;

            _ = Task.Run(async () =>
            {
                var request = new
                {
                    UserId = $"sustained-user-{idx:D6}",
                    Scope = "Emergency",
                    Latitude = 40.7128,
                    Longitude = -74.0060,
                    Description = $"Sustained load test #{idx}",
                    TriggerSource = "MANUAL_BUTTON"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var sw = Stopwatch.StartNew();
                try
                {
                    var response = await httpClient.PostAsync("/api/response/trigger", content, ct);
                    sw.Stop();

                    lock (lockObj)
                    {
                        Interlocked.Increment(ref overallRequests);
                        if (!buckets.ContainsKey(secondBucket))
                            buckets[secondBucket] = new List<double>();
                        buckets[secondBucket].Add(sw.Elapsed.TotalMilliseconds);

                        if (!response.IsSuccessStatusCode &&
                            response.StatusCode != HttpStatusCode.Accepted)
                        {
                            Interlocked.Increment(ref overallErrors);
                        }
                    }
                }
                catch
                {
                    sw.Stop();
                    lock (lockObj)
                    {
                        Interlocked.Increment(ref overallRequests);
                        Interlocked.Increment(ref overallErrors);
                        if (!buckets.ContainsKey(secondBucket))
                            buckets[secondBucket] = new List<double>();
                        buckets[secondBucket].Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
            }, ct);

            await Task.Delay(delayBetweenBatches, ct);
        }

        // Wait for in-flight requests to complete
        await Task.Delay(2000, ct);

        // Assert — verify no degradation trend
        var sortedBuckets = buckets.OrderBy(b => b.Key).ToList();

        TestContext.Out.WriteLine("╔══════════════════════════════════════════════════╗");
        TestContext.Out.WriteLine("║  SOS Dispatch Sustained Load Test Results        ║");
        TestContext.Out.WriteLine("╠══════════════════════════════════════════════════╣");
        TestContext.Out.WriteLine($"║  Duration:          {DefaultDurationSeconds}s                            ║");
        TestContext.Out.WriteLine($"║  Total Requests:    {overallRequests,6}                       ║");
        TestContext.Out.WriteLine($"║  Errors:            {overallErrors,6}                       ║");
        TestContext.Out.WriteLine("╠──────────────────────────────────────────────────╣");
        TestContext.Out.WriteLine("║  Second │ Requests │  p50 (ms) │  p99 (ms)       ║");
        TestContext.Out.WriteLine("╠──────────────────────────────────────────────────╣");

        var firstHalfLatencies = new List<double>();
        var secondHalfLatencies = new List<double>();
        var midpoint = sortedBuckets.Count / 2;

        for (var i = 0; i < sortedBuckets.Count; i++)
        {
            var bucket = sortedBuckets[i];
            var sorted = bucket.Value.OrderBy(l => l).ToList();
            var bucketP50 = Percentile(sorted, 0.50);
            var bucketP99 = Percentile(sorted, 0.99);

            TestContext.Out.WriteLine(
                $"║  {bucket.Key,6} │ {sorted.Count,8} │ {bucketP50,9:F1} │ {bucketP99,9:F1}       ║");

            if (i < midpoint)
                firstHalfLatencies.AddRange(bucket.Value);
            else
                secondHalfLatencies.AddRange(bucket.Value);
        }

        TestContext.Out.WriteLine("╚══════════════════════════════════════════════════╝");

        if (firstHalfLatencies.Count > 0 && secondHalfLatencies.Count > 0)
        {
            var firstHalfP99 = Percentile(firstHalfLatencies.OrderBy(l => l).ToList(), 0.99);
            var secondHalfP99 = Percentile(secondHalfLatencies.OrderBy(l => l).ToList(), 0.99);

            TestContext.Out.WriteLine($"\nFirst half p99:  {firstHalfP99:F1}ms");
            TestContext.Out.WriteLine($"Second half p99: {secondHalfP99:F1}ms");

            // The second half should not be more than 2x the first half
            // (allows for warmup in the first half)
            Assert.That(secondHalfP99, Is.LessThanOrEqualTo(firstHalfP99 * 2.0 + 50),
                $"Latency degraded: second half p99 ({secondHalfP99:F1}ms) > 2x first half p99 ({firstHalfP99:F1}ms)");
        }

        var overallErrorRate = (double)overallErrors / Math.Max(overallRequests, 1) * 100;
        Assert.That(overallErrorRate, Is.LessThanOrEqualTo(MaxErrorRatePercent),
            $"Error rate {overallErrorRate:F1}% exceeds maximum {MaxErrorRatePercent}%");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Computes the percentile value from a sorted list of doubles.</summary>
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
