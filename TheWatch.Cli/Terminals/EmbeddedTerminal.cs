// =============================================================================
// EmbeddedTerminal — Hosts a child process (shell or Claude Code) inside a
//                    Terminal.Gui TextView with stdin/stdout piping.
// =============================================================================
// This is NOT a full terminal emulator (no VT100 escape sequence interpretation).
// It captures process stdout/stderr into a scrollable TextView and forwards
// keystrokes to stdin. Good enough for interactive CLI tools like Claude Code,
// git, dotnet, and other line-oriented programs.
//
// For full VT100/xterm emulation, consider integrating a library like
// VtNetCore or running inside Windows Terminal panes externally.
//
// Architecture:
//   EmbeddedTerminal (FrameView)
//     ├── _outputView (TextView) — read-only, shows stdout+stderr
//     ├── _inputField (TextField) — user types commands here
//     └── _process (Process)      — the child process with redirected I/O
//
// Example:
//   var term = new EmbeddedTerminal("Claude", "powershell.exe", isClaudeTerminal: true);
//   term.SendInput("claude --help\n");
//
// WAL: Process.OutputDataReceived fires on a ThreadPool thread.
//      Must marshal to UI thread via Application.Invoke() before touching Views.
// =============================================================================

using System.Diagnostics;
using System.Text;
using Terminal.Gui;

namespace TheWatch.Cli.Terminals;

public class EmbeddedTerminal : FrameView, IDisposable
{
    private readonly TextView _outputView;
    private readonly TextField _inputField;
    private Process? _process;
    private readonly StringBuilder _outputBuffer = new();
    private readonly string _shellCommand;
    private readonly bool _isClaudeTerminal;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private const int MaxOutputLines = 5000;

    public EmbeddedTerminal(string title, string shellCommand, bool isClaudeTerminal = false)
    {
        Title = title;
        _shellCommand = shellCommand;
        _isClaudeTerminal = isClaudeTerminal;
        BorderStyle = LineStyle.Single;

        // Output area — scrollable, read-only text view
        _outputView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1), // leave 1 row for input
            ReadOnly = true,
            WordWrap = true,
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                Focus = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black)
            }
        };

        // Input field — user types commands here, Enter sends to process stdin
        _inputField = new TextField()
        {
            X = 0,
            Y = Pos.Bottom(_outputView),
            Width = Dim.Fill(),
            Height = 1,
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.White, Color.DarkGray),
                Focus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.DarkGray)
            }
        };

        _inputField.Text = isClaudeTerminal ? "Type 'claude' to start Claude Code..." : "";

        _inputField.KeyDown += (_, e) =>
        {
            if (e == Key.Enter)
            {
                var cmd = _inputField.Text?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    _commandHistory.Add(cmd);
                    _historyIndex = _commandHistory.Count;
                    SendInput(cmd + "\n");
                    AppendOutput($"$ {cmd}\n");
                    _inputField.Text = "";
                }
                e.Handled = true;
            }
            else if (e == Key.CursorUp)
            {
                // Command history: up arrow
                if (_historyIndex > 0)
                {
                    _historyIndex--;
                    _inputField.Text = _commandHistory[_historyIndex];
                    _inputField.CursorPosition = _inputField.Text.Length;
                }
                e.Handled = true;
            }
            else if (e == Key.CursorDown)
            {
                // Command history: down arrow
                if (_historyIndex < _commandHistory.Count - 1)
                {
                    _historyIndex++;
                    _inputField.Text = _commandHistory[_historyIndex];
                    _inputField.CursorPosition = _inputField.Text.Length;
                }
                else
                {
                    _historyIndex = _commandHistory.Count;
                    _inputField.Text = "";
                }
                e.Handled = true;
            }
            else if (e == Key.C.WithCtrl)
            {
                // Ctrl+C — send interrupt to process
                SendCtrlC();
                e.Handled = true;
            }
        };

        Add(_outputView, _inputField);

        // Start the shell process
        StartProcess();
    }

    private void StartProcess()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _shellCommand,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            // For PowerShell, disable the profile to speed up startup
            if (_shellCommand.Contains("powershell", StringComparison.OrdinalIgnoreCase))
            {
                psi.Arguments = "-NoProfile -NoLogo";
            }
            else if (_shellCommand.Contains("bash", StringComparison.OrdinalIgnoreCase))
            {
                psi.Arguments = "--norc";
            }

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Application.Invoke(() => AppendOutput(e.Data + "\n"));
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Application.Invoke(() => AppendOutput("[ERR] " + e.Data + "\n"));
            };

            _process.Exited += (_, _) =>
            {
                Application.Invoke(() =>
                {
                    AppendOutput("\n[Process exited. Press Enter to restart.]\n");
                    _inputField.KeyDown += RestartOnEnter;
                });
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            AppendOutput($"[{Title}] Shell started: {_shellCommand}\n");

            if (_isClaudeTerminal)
            {
                AppendOutput("[Tip] Type 'claude' to launch Claude Code CLI\n");
                AppendOutput("[Tip] Or type 'claude \"your prompt here\"' for one-shot mode\n\n");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] Failed to start process: {ex.Message}\n");
            AppendOutput($"[ERROR] Shell command: {_shellCommand}\n");
            AppendOutput("[Hint] Ensure the shell is available in PATH.\n");
        }
    }

    private void RestartOnEnter(object? sender, Key e)
    {
        if (e == Key.Enter)
        {
            _inputField.KeyDown -= RestartOnEnter;
            _outputBuffer.Clear();
            _outputView.Text = "";
            StartProcess();
            e.Handled = true;
        }
    }

    public void SendInput(string text)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.StandardInput.Write(text);
                _process.StandardInput.Flush();
            }
        }
        catch { /* process may have exited between check and write */ }
    }

    public void SendCtrlC()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                // Send Ctrl+C character (ETX = 0x03)
                _process.StandardInput.Write('\x03');
                _process.StandardInput.Flush();
            }
        }
        catch { /* process may have exited */ }
    }

    private void AppendOutput(string text)
    {
        _outputBuffer.Append(text);

        // Trim to max lines to prevent unbounded memory growth
        var content = _outputBuffer.ToString();
        var lines = content.Split('\n');
        if (lines.Length > MaxOutputLines)
        {
            var trimmed = string.Join('\n', lines[^MaxOutputLines..]);
            _outputBuffer.Clear();
            _outputBuffer.Append(trimmed);
            content = trimmed;
        }

        _outputView.Text = content;

        // Auto-scroll to bottom
        _outputView.MoveEnd();
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.Dispose();
            }
        }
        catch { /* best effort cleanup */ }
    }
}
