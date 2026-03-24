using System.Collections.ObjectModel;
using TheWatch.Maui.Services;

namespace TheWatch.Maui.ViewModels;

/// <summary>
/// ViewModel for the Test Dashboard -- the MAUI orchestrator's control panel.
/// Shows available test suites, lets you start runs on connected devices,
/// and displays real-time step results as they stream back via SignalR.
/// </summary>
public partial class TestDashboardViewModel : ObservableObject
{
    private readonly IDashboardRelay _relay;
    private readonly ILogger<TestDashboardViewModel> _logger;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial TestSuiteItem? SelectedSuite { get; set; }

    [ObservableProperty]
    public partial string TargetDevice { get; set; }

    [ObservableProperty]
    public partial TestRunItem? ActiveRun { get; set; }

    [ObservableProperty]
    public partial bool HasActiveRun { get; set; }

    public ObservableCollection<TestSuiteItem> Suites { get; } = new();
    public ObservableCollection<TestRunItem> RunHistory { get; } = new();
    public ObservableCollection<TestStepResultItem> LiveResults { get; } = new();

    public TestDashboardViewModel(IDashboardRelay relay, ILogger<TestDashboardViewModel> logger)
    {
        _relay = relay;
        _logger = logger;

        IsLoading = false;
        StatusMessage = "Load suites to begin";
        SelectedSuite = null;
        TargetDevice = "android_emulator";
        ActiveRun = null;
        HasActiveRun = false;

        // Subscribe to real-time test events from SignalR
        _relay.TestStepCompleted += OnTestStepCompleted;
        _relay.TestRunCompleted += OnTestRunCompleted;
    }

    [RelayCommand]
    public async Task LoadSuitesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading test suites...";

            var suites = await _relay.GetTestSuitesAsync();
            Suites.Clear();
            foreach (var s in suites)
            {
                Suites.Add(new TestSuiteItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    Platform = s.Platform,
                    StepCount = s.Steps?.Count ?? 0
                });
            }

            StatusMessage = $"{Suites.Count} suites available";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load test suites");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StartRunAsync()
    {
        if (SelectedSuite == null)
        {
            StatusMessage = "Select a test suite first";
            return;
        }

        try
        {
            IsLoading = true;
            LiveResults.Clear();
            StatusMessage = $"Starting '{SelectedSuite.Name}' on {TargetDevice}...";

            var run = await _relay.StartTestRunAsync(SelectedSuite.Id, TargetDevice);
            if (run != null)
            {
                ActiveRun = new TestRunItem
                {
                    Id = run.Id,
                    SuiteName = run.SuiteName,
                    Device = run.TargetDevice,
                    Status = run.Status,
                    TotalSteps = run.TotalSteps,
                    CompletedSteps = 0,
                    PassedSteps = 0,
                    FailedSteps = 0,
                    StartedAt = run.StartedAt.ToString("HH:mm:ss")
                };
                HasActiveRun = true;
                StatusMessage = $"Running: {SelectedSuite.Name} ({run.TotalSteps} steps)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start test run");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CancelRunAsync()
    {
        if (ActiveRun == null) return;

        try
        {
            await _relay.CancelTestRunAsync(ActiveRun.Id);
            ActiveRun.Status = "Cancelled";
            HasActiveRun = false;
            StatusMessage = "Test run cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel test run");
        }
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        try
        {
            var runs = await _relay.GetTestRunsAsync();
            RunHistory.Clear();
            foreach (var r in runs)
            {
                RunHistory.Add(new TestRunItem
                {
                    Id = r.Id,
                    SuiteName = r.SuiteName,
                    Device = r.TargetDevice,
                    Status = r.Status,
                    TotalSteps = r.TotalSteps,
                    CompletedSteps = r.CompletedSteps,
                    PassedSteps = r.PassedSteps,
                    FailedSteps = r.FailedSteps,
                    StartedAt = r.StartedAt.ToString("HH:mm:ss")
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load run history");
        }
    }

    // -- SignalR event handlers --

    private void OnTestStepCompleted(object? sender, TestStepResultDto result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LiveResults.Add(new TestStepResultItem
            {
                StepId = result.StepId,
                ScreenName = result.ScreenName,
                Action = result.Action,
                Passed = result.Passed,
                ErrorMessage = result.ErrorMessage,
                DurationMs = result.DurationMs,
                StatusIcon = result.Passed ? "PASS" : "FAIL"
            });

            if (ActiveRun != null)
            {
                ActiveRun.CompletedSteps = LiveResults.Count;
                ActiveRun.PassedSteps = LiveResults.Count(r => r.Passed);
                ActiveRun.FailedSteps = LiveResults.Count(r => !r.Passed);

                var pct = (int)(100.0 * ActiveRun.CompletedSteps / Math.Max(1, ActiveRun.TotalSteps));
                StatusMessage = $"Step {ActiveRun.CompletedSteps}/{ActiveRun.TotalSteps} ({pct}%) -- {(result.Passed ? "PASS" : "FAIL")}";
            }
        });
    }

    private void OnTestRunCompleted(object? sender, TestRunDto run)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ActiveRun != null)
            {
                ActiveRun.Status = run.Status;
                ActiveRun.CompletedSteps = run.CompletedSteps;
                ActiveRun.PassedSteps = run.PassedSteps;
                ActiveRun.FailedSteps = run.FailedSteps;
            }
            HasActiveRun = false;
            StatusMessage = $"Run complete: {run.Status} ({run.PassedSteps}/{run.TotalSteps} passed)";

            _ = LoadHistoryAsync();
        });
    }
}

// -- Display items --

public partial class TestSuiteItem : ObservableObject
{
    [ObservableProperty] public partial string Id { get; set; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty] public partial string Platform { get; set; }
    [ObservableProperty] public partial int StepCount { get; set; }

    public TestSuiteItem()
    {
        Id = "";
        Name = "";
        Description = "";
        Platform = "";
        StepCount = 0;
    }

    public string PlatformIcon => Platform switch
    {
        "Android" => "Android",
        "iOS" => "iOS",
        "Both" => "Both",
        _ => "Unknown"
    };
}

public partial class TestRunItem : ObservableObject
{
    [ObservableProperty] public partial string Id { get; set; }
    [ObservableProperty] public partial string SuiteName { get; set; }
    [ObservableProperty] public partial string Device { get; set; }
    [ObservableProperty] public partial string Status { get; set; }
    [ObservableProperty] public partial int TotalSteps { get; set; }
    [ObservableProperty] public partial int CompletedSteps { get; set; }
    [ObservableProperty] public partial int PassedSteps { get; set; }
    [ObservableProperty] public partial int FailedSteps { get; set; }
    [ObservableProperty] public partial string StartedAt { get; set; }

    public TestRunItem()
    {
        Id = "";
        SuiteName = "";
        Device = "";
        Status = "";
        TotalSteps = 0;
        CompletedSteps = 0;
        PassedSteps = 0;
        FailedSteps = 0;
        StartedAt = "";
    }

    public string StatusColor => Status switch
    {
        "Running" => "#2563EB",
        "Passed" => "#2E8B57",
        "Failed" => "#E6384B",
        "Cancelled" => "#6B7280",
        _ => "#6B7280"
    };

    public string StatusIcon => Status switch
    {
        "Running" => "Running",
        "Passed" => "Passed",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        _ => "Unknown"
    };
}

public partial class TestStepResultItem : ObservableObject
{
    [ObservableProperty] public partial string StepId { get; set; }
    [ObservableProperty] public partial string ScreenName { get; set; }
    [ObservableProperty] public partial string Action { get; set; }
    [ObservableProperty] public partial bool Passed { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial long DurationMs { get; set; }
    [ObservableProperty] public partial string StatusIcon { get; set; }

    public TestStepResultItem()
    {
        StepId = "";
        ScreenName = "";
        Action = "";
        Passed = false;
        ErrorMessage = null;
        DurationMs = 0;
        StatusIcon = "";
    }
}

// -- SignalR DTOs (consumed by DashboardRelay) --

public record TestSuiteDto(string Id, string Name, string Description, string Platform, List<TestStepDto>? Steps);
public record TestStepDto(string Id, int Order, string ScreenName, string Action, string Target, string? Value);
public record TestRunDto(string Id, string SuiteId, string SuiteName, string TargetDevice, string Status, DateTime StartedAt, DateTime? CompletedAt, int TotalSteps, int CompletedSteps, int PassedSteps, int FailedSteps);
public record TestStepResultDto(string StepId, string ScreenName, string Action, bool Passed, string? ErrorMessage, long DurationMs);
