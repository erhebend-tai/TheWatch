// =============================================================================
// DashboardApp — Main Terminal.Gui application orchestrator
// =============================================================================
// Builds the full-screen TUI layout:
//
//   ┌──────────────────────────────────────────────────────────────────────────┐
//   │ TheWatch Command Center                    [F1 Help] [F5 Refresh] [Q]  │
//   ├──────────────────────┬──────────────────┬────────────────────────────────┤
//   │  FEATURES (40%)      │  AGENTS (25%)    │  SERVICES (35%)              │
//   │  ✓ Evidence Upload   │  ● ClaudeCode    │  ● SQL Server       HEALTHY  │
//   │  ✓ SignalR Hub       │    feat-xyz      │  ● PostgreSQL       HEALTHY  │
//   │  ◻ EULA Mgmt         │  ● Gemini        │  ● Redis            HEALTHY  │
//   │  ◻ Data Export        │    scan-docs     │  ○ Cosmos DB       DEGRADED  │
//   │                      │                  │  ● RabbitMQ          HEALTHY  │
//   ├──────────┬───────────┴──────┬───────────┴────────────────────────────────┤
//   │ Term 1   │ Term 2           │ Term 3                                    │
//   │ $ claude │ $ dotnet watch   │ $ git log --oneline                       │
//   │ ...      │ ...              │ ...                                       │
//   └──────────┴──────────────────┴───────────────────────────────────────────┘
//
// WAL: Application.Invoke() is the ONLY safe way to mutate UI from background threads.
//      All SignalR callbacks and polling timers marshal through it.
// =============================================================================

using Terminal.Gui;
using TheWatch.Cli.Panels;
using TheWatch.Cli.Services;
using TheWatch.Cli.Services.Roslyn;
using TheWatch.Cli.Services.TreeSitter;
using TheWatch.Cli.Terminals;

namespace TheWatch.Cli.App;

public class DashboardApp
{
    private readonly DashboardConfig _config;
    private readonly DashboardApiClient _apiClient;
    private readonly CancellationTokenSource _cts = new();

    // Panels
    private FeaturePanel _featurePanel = null!;
    private AgentPanel _agentPanel = null!;
    private ServicePanel _servicePanel = null!;
    private IoTPanel _iotPanel = null!;

    // Terminals
    private EmbeddedTerminal _terminal1 = null!;
    private EmbeddedTerminal _terminal2 = null!;
    private EmbeddedTerminal _terminal3 = null!;

    // Services (Roslyn, Build, CodeGen, Tree-sitter)
    private BuildOrchestrator _buildOrchestrator = null!;
    private RoslynAnalyzerService? _roslynAnalyzer;
    private CodeGenerator _codeGenerator = null!;
    private TreeSitterService _treeSitter = null!;

    public DashboardApp(DashboardConfig config)
    {
        _config = config;
        _apiClient = new DashboardApiClient(config);

        // Wire up build/analysis services
        var solutionRoot = FindSolutionRoot();
        _buildOrchestrator = new BuildOrchestrator("https+http://build-server", solutionRoot);
        _codeGenerator = new CodeGenerator(solutionRoot);
        _treeSitter = new TreeSitterService(solutionRoot);
        _treeSitter.ConfigureLsifEndpoint("https+http://build-server");

        // Roslyn analyzer is optional — needs MSBuild SDK
        try
        {
            var slnPath = File.Exists(Path.Combine(solutionRoot, "TheWatch.slnx"))
                ? Path.Combine(solutionRoot, "TheWatch.slnx")
                : Path.Combine(solutionRoot, "TheWatch.sln");
            _roslynAnalyzer = new RoslynAnalyzerService(slnPath);
        }
        catch { /* MSBuild not found — Roslyn analysis unavailable */ }
    }

    // Run the Aspire AppHost (boots the local orchestrator and all contained projects)
    private void RunAppHost()
    {
        try
        {
            var solutionRoot = FindSolutionRoot();
            // Use terminal2 to run the app host so user can see logs
            var cmd = OperatingSystem.IsWindows()
                ? $"cd \"{solutionRoot}\" && dotnet run --project TheWatch.AppHost --no-launch-profile"
                : $"cd \"{solutionRoot}\" && dotnet run --project TheWatch.AppHost";

            _terminal2.SendInput($"{cmd}\n");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Run AppHost", "Failed to start AppHost: " + ex.Message, "OK");
        }
    }

    public async Task RunAsync()
    {
        Application.Init();

        try
        {
            var top = BuildLayout();
            StartBackgroundServices();
            Application.Run(top);
        }
        finally
        {
            _cts.Cancel();
            _terminal1?.Dispose();
            _terminal2?.Dispose();
            _terminal3?.Dispose();
            await _apiClient.DisposeAsync();
            Application.Shutdown();
        }
    }

    private Toplevel BuildLayout()
    {
        var top = new Toplevel();

        // ── Status Bar ──────────────────────────────────────────────
        var statusBar = new StatusBar(new Shortcut[]
        {
            new (Key.F1, "Help", ShowHelp),
            new (Key.F11, "AppHost", () => RunAppHost()),
            new (Key.F2, "Claude", () => _terminal1.SendInput("claude\n")),
            new (Key.F3, "Term", CycleTerminalFocus),
            new (Key.F5, "Refresh", () => _ = RefreshAllAsync()),
            new (Key.F6, "Build", () => _ = TriggerBuildAsync()),
            new (Key.F7, "Analyze", () => _ = RunAnalysisAsync()),
            new (Key.F8, "CodeGen", ShowCodeGenMenu),
            new (Key.F9, "Symbols", () => _ = ShowCrossProjectSymbolsAsync()),
            new (Key.F10, "IoT", () => _ = ShowIoTMenuAsync()),
            new (Key.Q.WithCtrl, "Quit", () => Application.RequestStop()),
        });
        top.Add(statusBar);

        // ── Title Bar ───────────────────────────────────────────────
        var titleBar = new Label()
        {
            Text = " TheWatch Command Center  |  CLI Dashboard  |  " + DateTime.Now.ToString("yyyy-MM-dd"),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.White, Color.DarkGray)
            }
        };
        top.Add(titleBar);

        // ── Top Row: Dashboard Panels (Features | Agents | Services) ──
        var topRow = new FrameView()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Percent(45),
            BorderStyle = LineStyle.Single
        };

        _featurePanel = new FeaturePanel()
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Fill()
        };

        _agentPanel = new AgentPanel()
        {
            X = Pos.Right(_featurePanel),
            Y = 0,
            Width = Dim.Percent(20),
            Height = Dim.Fill()
        };

        _iotPanel = new IoTPanel()
        {
            X = Pos.Right(_agentPanel),
            Y = 0,
            Width = Dim.Percent(25),
            Height = Dim.Fill()
        };

        _servicePanel = new ServicePanel()
        {
            X = Pos.Right(_iotPanel),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        topRow.Add(_featurePanel, _agentPanel, _iotPanel, _servicePanel);
        top.Add(topRow);

        // ── Bottom Row: Three Terminal Panes ────────────────────────
        var bottomRow = new FrameView()
        {
            X = 0,
            Y = Pos.Bottom(topRow),
            Width = Dim.Fill(),
            Height = Dim.Fill(1), // leave room for status bar
            BorderStyle = LineStyle.Single
        };

        _terminal1 = new EmbeddedTerminal("Terminal 1 - Claude Code", GetShellCommand(), isClaudeTerminal: true)
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(33),
            Height = Dim.Fill()
        };

        _terminal2 = new EmbeddedTerminal("Terminal 2", GetShellCommand())
        {
            X = Pos.Right(_terminal1),
            Y = 0,
            Width = Dim.Percent(34),
            Height = Dim.Fill()
        };

        _terminal3 = new EmbeddedTerminal("Terminal 3", GetShellCommand())
        {
            X = Pos.Right(_terminal2),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        bottomRow.Add(_terminal1, _terminal2, _terminal3);
        top.Add(bottomRow);

        return top;
    }

    private void StartBackgroundServices()
    {
        // Start polling loop
        _ = Task.Run(async () =>
        {
            // Initial data load
            await RefreshAllAsync();

            // Connect SignalR if enabled
            if (_config.EnableSignalR)
            {
                await _apiClient.ConnectSignalRAsync(_cts.Token);
                _apiClient.OnFeatureUpdated += feature =>
                    Application.Invoke(() => _featurePanel.UpdateFeature(feature));
                _apiClient.OnAgentActivity += activity =>
                    Application.Invoke(() => _agentPanel.UpdateAgent(activity));
                _apiClient.OnServiceHealthChanged += health =>
                    Application.Invoke(() => _servicePanel.UpdateService(health));

                // IoT real-time events
                _apiClient.OnIoTAlertReceived += alert =>
                    Application.Invoke(() => _iotPanel.AddAlert(alert));
                _apiClient.OnIoTAlertCancelled += cancelled =>
                    Application.Invoke(() => _iotPanel.CancelAlert(cancelled.AlertId));
                _apiClient.OnIoTCheckInEscalation += checkIn =>
                    Application.Invoke(() => _iotPanel.AddCheckIn(checkIn));
                _apiClient.OnIoTWebhookAlertReceived += webhook =>
                    Application.Invoke(() => _iotPanel.AddWebhookAlert(webhook));
            }

            // Periodic polling fallback
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollIntervalSeconds));
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(_cts.Token);
                    await RefreshAllAsync();
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow polling errors, dashboard stays up */ }
            }
        }, _cts.Token);
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var featuresTask = _apiClient.GetFeaturesAsync(_cts.Token);
            var agentsTask = _apiClient.GetAgentsAsync(_cts.Token);
            var servicesTask = _apiClient.GetServicesAsync(_cts.Token);
            var iotStatusTask = _apiClient.GetIoTStatusAsync(ct: _cts.Token);

            await Task.WhenAll(featuresTask, agentsTask, servicesTask, iotStatusTask);

            Application.Invoke(() =>
            {
                _featurePanel.SetFeatures(featuresTask.Result);
                _agentPanel.SetAgents(agentsTask.Result);
                _servicePanel.SetServices(servicesTask.Result);

                // IoT panel — load device status if API returned data
                var iotStatus = iotStatusTask.Result;
                if (iotStatus is not null)
                {
                    _iotPanel.SetDeviceStatus(iotStatus);
                }
            });
        }
        catch
        {
            // API unreachable — panels show last known data with offline indicator
            Application.Invoke(() =>
            {
                _servicePanel.SetOfflineMode();
            });
        }
    }

    private int _focusedTerminal;
    private void CycleTerminalFocus()
    {
        _focusedTerminal = (_focusedTerminal + 1) % 3;
        var target = _focusedTerminal switch
        {
            0 => (View)_terminal1,
            1 => _terminal2,
            _ => _terminal3
        };
        target.SetFocus();
    }

    private static string GetShellCommand()
    {
        // Detect platform shell
        if (OperatingSystem.IsWindows())
            return "powershell.exe";
        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    // ── Build (F6) ────────────────────────────────────────────────

    private async Task TriggerBuildAsync()
    {
        var choice = MessageBox.Query("Build", "Select build action:", "Full Build", "Test", "LSIF Reindex", "Cancel");

        if (choice == 3) return; // Cancel

        _terminal2.SendInput("echo '[Build triggered from dashboard]'\n");

        if (choice == 0) // Full Build
        {
            var run = await _buildOrchestrator.QueueBuildAsync("Manual", "CLI Dashboard", _cts.Token);
            _terminal2.SendInput($"echo 'Build queued: {run.Id} — Status: {run.Status}'\n");

            if (_buildOrchestrator.IsServerReachable)
            {
                _ = Task.Run(async () =>
                {
                    var result = await _buildOrchestrator.PollUntilCompleteAsync(run.Id, ct: _cts.Token);
                    Application.Invoke(() =>
                    {
                        var msg = result.Status == "Succeeded"
                            ? $"BUILD PASSED in {result.Duration?.TotalSeconds:F1}s"
                            : $"BUILD FAILED — {result.Status}";
                        MessageBox.Query("Build Result", msg, "OK");
                    });
                });
            }
        }
        else if (choice == 1) // Test
        {
            _terminal2.SendInput("dotnet test TheWatch.slnx --no-build -v:minimal\n");
        }
        else if (choice == 2) // LSIF Reindex
        {
            var summary = await _buildOrchestrator.TriggerReindexAsync(ct: _cts.Token);
            if (summary is not null)
                MessageBox.Query("LSIF Reindex",
                    $"Indexed: {summary.SymbolsIndexed} symbols, {summary.PortAdapterLinksFound} port-adapter links\nDuration: {summary.Duration.TotalSeconds:F1}s", "OK");
            else
                MessageBox.ErrorQuery("LSIF Reindex", "BuildServer unreachable — start with:\n  dotnet run --project TheWatch.BuildServer", "OK");
        }
    }

    // ── Analyze (F7) ────────────────────────────────────────────────

    private async Task RunAnalysisAsync()
    {
        if (_roslynAnalyzer is null)
        {
            MessageBox.ErrorQuery("Roslyn Analyzer", "Roslyn not available — MSBuild SDK not found.\nInstall .NET SDK or Visual Studio Build Tools.", "OK");
            return;
        }

        var choice = MessageBox.Query("Analysis", "Select analysis:", "Full Analysis", "Doc Coverage", "Port/Adapter Gaps", "Security Scan", "Cancel");
        if (choice == 4) return;

        MessageBox.Query("Analysis", "Running analysis — this may take a moment...", "OK");

        try
        {
            await _roslynAnalyzer.OpenSolutionAsync(_cts.Token);

            if (choice == 0) // Full Analysis
            {
                var report = await _roslynAnalyzer.RunFullAnalysisAsync(_cts.Token);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Duration: {report.Duration.TotalSeconds:F1}s");
                sb.AppendLine($"Errors:   {report.TotalErrors}");
                sb.AppendLine($"Warnings: {report.TotalWarnings}");
                sb.AppendLine($"Doc Coverage: {report.DocCoveragePercent:F1}%");
                sb.AppendLine($"Port/Adapter Gaps: {report.PortAdapterGaps.Count}");
                sb.AppendLine();
                foreach (var proj in report.Projects.Take(10))
                    sb.AppendLine($"  {proj.ProjectName}: {proj.Errors.Count}E {proj.Warnings.Count}W {proj.DocCoveragePercent:F0}% doc");

                MessageBox.Query("Analysis Report", sb.ToString(), "OK");
            }
            else if (choice == 1) // Doc Coverage
            {
                var coverage = await _roslynAnalyzer.GetDocCoverageAsync(_cts.Token);
                var sb = new System.Text.StringBuilder();
                foreach (var c in coverage)
                    sb.AppendLine($"  {c.ProjectName}: {c.CoveragePercent:F1}% ({c.DocumentedMembers}/{c.TotalMembers})");
                MessageBox.Query("Documentation Coverage", sb.ToString(), "OK");
            }
            else if (choice == 2) // Port/Adapter Gaps
            {
                var gaps = await _roslynAnalyzer.ValidatePortAdaptersAsync(_cts.Token);
                if (gaps.Count == 0)
                {
                    MessageBox.Query("Port/Adapter Validation", "All ports have at least one adapter implementation!", "OK");
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"{gaps.Count} ports missing adapters:\n");
                    foreach (var gap in gaps.Take(15))
                        sb.AppendLine($"  {gap.PortInterface} (defined in {Path.GetFileName(gap.DefinedIn)})");
                    MessageBox.Query("Port/Adapter Gaps", sb.ToString(), "OK");
                }
            }
            else if (choice == 3) // Security Scan
            {
                var report = await _roslynAnalyzer.RunFullAnalysisAsync(_cts.Token);
                var allFindings = report.Projects.SelectMany(p => p.SecurityFindings).ToList();
                if (allFindings.Count == 0)
                {
                    MessageBox.Query("Security Scan", "No security issues found!", "OK");
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"{allFindings.Count} findings:\n");
                    foreach (var f in allFindings.Take(20))
                        sb.AppendLine($"  [{f.Severity}] {f.Category}: {Path.GetFileName(f.FilePath)}:{f.Line}");
                    MessageBox.Query("Security Findings", sb.ToString(), "OK");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Analysis Error", ex.Message, "OK");
        }
    }

    // ── CodeGen (F8) ────────────────────────────────────────────────

    private void ShowCodeGenMenu()
    {
        var choice = MessageBox.Query("Code Generator", "Generate:", "Adapter from Port", "Test Stubs", "DTO from Model", "Scaffold New Port", "Cancel");
        if (choice == 4) return;

        if (choice == 0) // Adapter from Port
        {
            var dialog = new Dialog() { Title = "Generate Adapter", Width = 60, Height = 10 };
            var portLabel = new Label() { Text = "Port Interface:", X = 1, Y = 1 };
            var portField = new TextField() { Text = "IEvidencePort", X = 18, Y = 1, Width = 35 };
            var adapterLabel = new Label() { Text = "Adapter Name:", X = 1, Y = 2 };
            var adapterField = new TextField() { Text = "Mock", X = 18, Y = 2, Width = 35 };
            var okBtn = new Button() { Text = "Generate", X = Pos.Center(), Y = 4 };
            okBtn.Accepting += (s, e) =>
            {
                _ = Task.Run(async () =>
                {
                    var result = await _codeGenerator.GenerateAdapterAsync(
                        portField.Text.ToString()!, adapterField.Text.ToString()!, _cts.Token);
                    Application.Invoke(() =>
                    {
                        if (result.Success)
                            MessageBox.Query("Generated", $"Created {result.GeneratedTypeName}\nat {result.OutputPath}\n({result.MembersGenerated} methods)", "OK");
                        else
                            MessageBox.ErrorQuery("Error", result.ErrorMessage ?? "Unknown error", "OK");
                    });
                });
                Application.RequestStop();
            };
            dialog.Add(portLabel, portField, adapterLabel, adapterField, okBtn);
            var cancelBtn = new Button() { Text = "Cancel" };
            cancelBtn.Accepting += (s, e) => Application.RequestStop();
            dialog.AddButton(cancelBtn);
            Application.Run(dialog);
        }
        else if (choice == 1) // Test Stubs
        {
            var input = new Dialog() { Title = "Generate Test Stubs", Width = 50, Height = 8 };
            var label = new Label() { Text = "Class Name:", X = 1, Y = 1 };
            var field = new TextField() { Text = "EvidenceController", X = 14, Y = 1, Width = 30 };
            var okBtn = new Button() { Text = "Generate", X = Pos.Center(), Y = 3 };
            okBtn.Accepting += (s, e) =>
            {
                _ = Task.Run(async () =>
                {
                    var result = await _codeGenerator.GenerateTestStubAsync(field.Text.ToString()!, _cts.Token);
                    Application.Invoke(() =>
                    {
                        if (result.Success)
                            MessageBox.Query("Generated", $"Created {result.GeneratedTypeName}\n({result.MembersGenerated} test methods)", "OK");
                        else
                            MessageBox.ErrorQuery("Error", result.ErrorMessage ?? "Unknown error", "OK");
                    });
                });
                Application.RequestStop();
            };
            input.Add(label, field, okBtn);
            var cancelBtn = new Button() { Text = "Cancel" };
            cancelBtn.Accepting += (s, e) => Application.RequestStop();
            input.AddButton(cancelBtn);
            Application.Run(input);
        }
        else if (choice == 2) // DTO from Model
        {
            var input = new Dialog() { Title = "Generate DTO", Width = 50, Height = 8 };
            var label = new Label() { Text = "Model Class:", X = 1, Y = 1 };
            var field = new TextField() { Text = "FeatureImplementation", X = 14, Y = 1, Width = 30 };
            var okBtn = new Button() { Text = "Generate", X = Pos.Center(), Y = 3 };
            okBtn.Accepting += (s, e) =>
            {
                _ = Task.Run(async () =>
                {
                    var result = await _codeGenerator.GenerateDtoAsync(field.Text.ToString()!, _cts.Token);
                    Application.Invoke(() =>
                    {
                        if (result.Success)
                            MessageBox.Query("Generated", $"Created {result.GeneratedTypeName}\n({result.MembersGenerated} properties)", "OK");
                        else
                            MessageBox.ErrorQuery("Error", result.ErrorMessage ?? "Unknown error", "OK");
                    });
                });
                Application.RequestStop();
            };
            input.Add(label, field, okBtn);
            var cancelBtn = new Button() { Text = "Cancel" };
            cancelBtn.Accepting += (s, e) => Application.RequestStop();
            input.AddButton(cancelBtn);
            Application.Run(input);
        }
        else if (choice == 3) // Scaffold New Port
        {
            var input = new Dialog() { Title = "Scaffold Port + Mock Adapter", Width = 60, Height = 12 };
            var label = new Label() { Text = "Port Name:", X = 1, Y = 1 };
            var field = new TextField() { Text = "IAlertPort", X = 14, Y = 1, Width = 40 };
            var methodLabel = new Label() { Text = "Methods (one per line):", X = 1, Y = 3 };
            var methodView = new TextView()
            {
                X = 1, Y = 4, Width = 55, Height = 4,
                Text = "Task SendAlertAsync(string userId, string message, CancellationToken ct = default)"
            };
            var okBtn = new Button() { Text = "Scaffold", X = Pos.Center(), Y = 9 };
            okBtn.Accepting += (s, e) =>
            {
                var methods = methodView.Text.ToString()!.Split('\n')
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                _ = Task.Run(async () =>
                {
                    var results = await _codeGenerator.ScaffoldPortAsync(field.Text.ToString()!, methods, _cts.Token);
                    Application.Invoke(() =>
                    {
                        var summary = string.Join("\n", results.Select(r =>
                            r.Success ? $"  Created: {r.GeneratedTypeName}" : $"  Failed: {r.ErrorMessage}"));
                        MessageBox.Query("Scaffold Complete", summary, "OK");
                    });
                });
                Application.RequestStop();
            };
            input.Add(label, field, methodLabel, methodView, okBtn);
            var cancelBtn = new Button() { Text = "Cancel" };
            cancelBtn.Accepting += (s, e) => Application.RequestStop();
            input.AddButton(cancelBtn);
            Application.Run(input);
        }
    }

    // ── IoT Menu (F10) ────────────────────────────────────────────────

    private async Task ShowIoTMenuAsync()
    {
        var choice = MessageBox.Query("IoT / Voice Assistants",
            "Select action:", "Test Alexa Alert", "Test Google Alert", "View Devices",
            "View Linked Accounts", "Refresh IoT Status", "Cancel");

        if (choice == 5) return; // Cancel

        if (choice == 0) // Test Alexa Alert
        {
            var result = await _apiClient.TriggerTestIoTAlertAsync("Alexa", "VOICE_COMMAND", _cts.Token);
            if (result is not null)
                MessageBox.Query("Test Alert Result",
                    $"Alert ID: {result.AlertId}\n" +
                    $"Status: {result.Status}\n" +
                    $"Responders: {result.RespondersNotified}\n" +
                    $"Message: {result.Message}", "OK");
            else
                MessageBox.ErrorQuery("Test Alert", "Failed — Dashboard API unreachable.\nEnsure the API is running at " + _config.ApiBaseUrl, "OK");
        }
        else if (choice == 1) // Test Google Alert
        {
            var result = await _apiClient.TriggerTestIoTAlertAsync("GoogleHome", "VOICE_COMMAND", _cts.Token);
            if (result is not null)
                MessageBox.Query("Test Alert Result",
                    $"Alert ID: {result.AlertId}\n" +
                    $"Status: {result.Status}\n" +
                    $"Responders: {result.RespondersNotified}\n" +
                    $"Message: {result.Message}", "OK");
            else
                MessageBox.ErrorQuery("Test Alert", "Failed — Dashboard API unreachable.", "OK");
        }
        else if (choice == 2) // View Devices
        {
            var devices = await _apiClient.GetIoTDevicesAsync(ct: _cts.Token);
            if (devices.Count == 0)
            {
                MessageBox.Query("IoT Devices", "No devices registered.\n\nLink devices via:\n  - Alexa app -> Skills -> 'The Watch Safety'\n  - Google Home app -> Works with Google\n  - SmartThings app -> Connected Services", "OK");
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{devices.Count} registered devices:\n");
                foreach (var dev in devices)
                {
                    var status = dev.IsOnline ? "Online" : "Offline";
                    var battery = dev.BatteryLevel.HasValue ? $" ({dev.BatteryLevel}%)" : "";
                    sb.AppendLine($"  [{dev.Source}] {dev.DeviceName} -- {status}{battery}");
                    sb.AppendLine($"    Capabilities: {string.Join(", ", dev.Capabilities)}");
                    sb.AppendLine($"    Zone: {dev.InstallationZone ?? "N/A"} | Last seen: {dev.LastSeenAt:HH:mm:ss}");
                }
                MessageBox.Query("IoT Devices", sb.ToString(), "OK");
            }
        }
        else if (choice == 3) // View Linked Accounts
        {
            var iotStatus = await _apiClient.GetIoTStatusAsync(ct: _cts.Token);
            if (iotStatus is null)
            {
                MessageBox.ErrorQuery("Linked Accounts", "API unreachable.", "OK");
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"User: {iotStatus.UserId}");
                sb.AppendLine($"Active Alerts: {iotStatus.ActiveAlerts}");
                sb.AppendLine($"Nearby Responders: {iotStatus.NearbyResponders}");
                sb.AppendLine($"Last Check-In: {iotStatus.LastCheckIn?.ToString("yyyy-MM-dd HH:mm") ?? "Never"}");
                sb.AppendLine($"Registered Devices: {iotStatus.RegisteredDevices.Count}");
                sb.AppendLine();

                var sourceGroups = iotStatus.RegisteredDevices.GroupBy(d => d.Source);
                foreach (var group in sourceGroups)
                {
                    sb.AppendLine($"  {group.Key}: {group.Count()} device(s)");
                }

                MessageBox.Query("IoT Account Summary", sb.ToString(), "OK");
            }
        }
        else if (choice == 4) // Refresh IoT Status
        {
            var iotStatus = await _apiClient.GetIoTStatusAsync(ct: _cts.Token);
            if (iotStatus is not null)
            {
                Application.Invoke(() => _iotPanel.SetDeviceStatus(iotStatus));
                MessageBox.Query("IoT Refresh", $"Refreshed: {iotStatus.RegisteredDevices.Count} devices, {iotStatus.ActiveAlerts} active alerts.", "OK");
            }
            else
            {
                MessageBox.ErrorQuery("IoT Refresh", "API unreachable.", "OK");
            }
        }
    }

    // ── Cross-Project Symbols (F9) ──────────────────────────────────

    private async Task ShowCrossProjectSymbolsAsync()
    {
        var choice = MessageBox.Query("Symbol Search", "Search across:", "All Platforms", "iOS (Swift)", "Android (Kotlin)", "Aspire (.NET)", "Cancel");
        if (choice == 4) return;

        var queryDialog = new Dialog() { Title = "Symbol Query", Width = 50, Height = 8 };
        var label = new Label() { Text = "Search:", X = 1, Y = 1 };
        var field = new TextField() { Text = "", X = 10, Y = 1, Width = 35 };
        var okBtn = new Button() { Text = "Search", X = Pos.Center(), Y = 3 };

        okBtn.Accepting += (s, e) =>
        {
            _ = Task.Run(async () =>
            {
                var query = field.Text.ToString() ?? "";
                List<SymbolEntry> results;

                if (choice == 0) // All
                {
                    var index = await _treeSitter.GetCrossProjectSymbolsAsync(_cts.Token);
                    results = index.SearchByName(query).Take(50).ToList();
                }
                else
                {
                    var platform = choice switch { 1 => "iOS/Swift", 2 => "Android/Kotlin", _ => "Aspire/.NET" };
                    var index = await _treeSitter.GetCrossProjectSymbolsAsync(_cts.Token);
                    results = index.GetByPlatform(platform)
                        .Where(sym => sym.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Take(50).ToList();
                }

                Application.Invoke(() =>
                {
                    if (results.Count == 0)
                    {
                        MessageBox.Query("Results", $"No symbols matching '{query}' found.", "OK");
                    }
                    else
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"{results.Count} symbols found:\n");
                        foreach (var sym in results.Take(30))
                        {
                            var file = Path.GetFileName(sym.FilePath);
                            sb.AppendLine($"  [{sym.Kind}] {sym.Name}  ({sym.Language}) {file}:{sym.Line}");
                        }
                        MessageBox.Query("Symbol Results", sb.ToString(), "OK");
                    }
                });
            });
            Application.RequestStop();
        };

        queryDialog.Add(label, field, okBtn);
        var cancelBtn = new Button() { Text = "Cancel" };
        cancelBtn.Accepting += (s, e) => Application.RequestStop();
        queryDialog.AddButton(cancelBtn);
        Application.Run(queryDialog);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string FindSolutionRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "TheWatch.slnx")) ||
                File.Exists(Path.Combine(dir, "TheWatch.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }

    private void ShowHelp()
    {
        MessageBox.Query("TheWatch CLI Command Center", @"
Keyboard Shortcuts:
  F1          — Show this help
  F2          — Launch Claude Code in Terminal 1
  F3          — Cycle terminal focus
  F5          — Force refresh all dashboard data
  F6          — Build menu (Full Build, Test, LSIF Reindex)
  F7          — Roslyn Analysis (Diagnostics, Doc Coverage, Security)
  F8          — Code Generator (Adapters, Tests, DTOs, Ports)
  F9          — Cross-project symbol search (C#, Swift, Kotlin)
  F10         — IoT menu (Test alerts, View devices, Linked accounts)
  Ctrl+Q      — Quit

Dashboard Panels:
  Features    — Implementation status (✓ done, ► wip, ✗ blocked)
  Agents      — Active AI agents and their current tasks
  IoT         — Connected voice assistants & smart home devices
                Alexa, Google Home, SmartThings, Ring, Matter, etc.
                Live alert feed + check-in history + device status
  Services    — Infrastructure health (DB, cache, messaging, cloud)

IoT Integration:
  Alexa:       TheWatch-Alexa/  (ASK CLI + Lambda)
  Google Home: TheWatch-GoogleHome/  (Actions SDK + Cloud Function)
  Backend:     /api/iot/*  (IoTAlertController, IoTAccountLinkController)
  Test alerts: F10 -> 'Test Alexa Alert' or 'Test Google Alert'

Build Integration:
  Routes through BuildServer REST API via Aspire service discovery (https+http://build-server)
  Falls back to local `dotnet build` if BuildServer is offline

Terminals:
  Terminal 1  — Pre-wired for Claude Code (press F2)
  Terminal 2  — General purpose shell
  Terminal 3  — General purpose shell

Connection:
  Dashboard API: " + _config.ApiBaseUrl + @"
  Build Server:  https+http://build-server (Aspire service discovery)
  SignalR:       " + (_config.EnableSignalR ? "Enabled" : "Disabled") + @"
  Polling:       Every " + _config.PollIntervalSeconds + @"s
", "OK");
    }
}
