// =============================================================================
// SwarmCommand — CLI commands for managing Azure OpenAI swarms
// =============================================================================
// Adds the `swarm` subcommand tree to the CLI:
//
//   thewatch swarm list                           — List all registered swarms
//   thewatch swarm presets                        — Show available preset templates
//   thewatch swarm create <preset-id>             — Create a swarm from a preset
//   thewatch swarm create --name "My Swarm" ...   — Create a custom swarm
//   thewatch swarm show <swarm-id>                — Show swarm topology details
//   thewatch swarm delete <swarm-id>              — Delete a swarm
//   thewatch swarm run <swarm-id> --input "..."   — Run a task through a swarm
//   thewatch swarm tasks [swarm-id]               — List recent tasks
//   thewatch swarm task <task-id>                 — Show task details and trace
//   thewatch swarm validate <swarm-id>            — Validate swarm topology
//   thewatch swarm add-agent <swarm-id> ...       — Add an agent to a swarm
//   thewatch swarm remove-agent <swarm-id> <id>   — Remove an agent from a swarm
//
// Example:
//   dotnet run --project TheWatch.Cli -- swarm presets
//   dotnet run --project TheWatch.Cli -- swarm create safety-report-pipeline
//   dotnet run --project TheWatch.Cli -- swarm run safety-report-pipeline --input "SOS triggered at 38.9072, -77.0369"
//   dotnet run --project TheWatch.Cli -- swarm run safety-report-pipeline --input "SOS at 38.9, -77.0" --stream
//
// WAL: The swarm port is resolved at command execution time. If no Azure OpenAI
//      endpoint is configured, commands fail gracefully with setup instructions.
//
// System.CommandLine 2.0.0-beta5 API notes:
//   - Argument<T>(name) — single-arg ctor; set Description and DefaultValueFactory as properties
//   - Option<T>(name) — single-arg ctor; set Description, DefaultValueFactory, Required as properties
//   - cmd.SetAction(ParseResult => ...) replaces cmd.SetHandler(...)
//   - parseResult.GetValue<T>(option/argument) replaces InvocationContext binding
// =============================================================================

using System.CommandLine;
using System.Text;
using System.Text.Json;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Cli.Commands;

public static class SwarmCommand
{
    /// <summary>
    /// Build the `swarm` command tree. Call from Program.cs:
    ///   rootCommand.AddCommand(SwarmCommand.Build(swarmPort));
    /// </summary>
    public static Command Build(Func<ISwarmPort?> getSwarmPort)
    {
        var swarmCmd = new Command("swarm", "Manage Azure OpenAI multi-agent swarms");

        swarmCmd.Subcommands.Add(BuildListCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildPresetsCommand());
        swarmCmd.Subcommands.Add(BuildCreateCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildShowCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildDeleteCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildRunCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildTasksCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildTaskCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildValidateCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildAddAgentCommand(getSwarmPort));
        swarmCmd.Subcommands.Add(BuildRemoveAgentCommand(getSwarmPort));

        return swarmCmd;
    }

    // ── swarm list ──────────────────────────────────────────────────

    private static Command BuildListCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var cmd = new Command("list", "List all registered swarms");
        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var result = await port.ListSwarmsAsync();
            if (!result.Success || result.Data is null || result.Data.Count == 0)
            {
                Console.WriteLine("No swarms registered. Use 'swarm create <preset-id>' to create one.");
                Console.WriteLine("Run 'swarm presets' to see available templates.");
                return;
            }

            Console.WriteLine($"{"ID",-30} {"Name",-35} {"Agents",-8} {"Status",-12} {"Created"}");
            Console.WriteLine(new string('─', 110));
            foreach (var s in result.Data)
            {
                Console.WriteLine($"{s.SwarmId,-30} {s.Name,-35} {s.Agents.Count,-8} {s.Status,-12} {s.CreatedAt:yyyy-MM-dd HH:mm}");
            }
        });
        return cmd;
    }

    // ── swarm presets ───────────────────────────────────────────────

    private static Command BuildPresetsCommand()
    {
        var cmd = new Command("presets", "Show available swarm preset templates");
        cmd.SetAction((parseResult) =>
        {
            Console.WriteLine("Available Swarm Presets:");
            Console.WriteLine(new string('═', 90));
            foreach (var (id, name, desc, agentCount) in SwarmPresets.ListPresets())
            {
                Console.WriteLine($"\n  {name}");
                Console.WriteLine($"  ID:     {id}");
                Console.WriteLine($"  Agents: {agentCount}");
                Console.WriteLine($"  Flow:   {desc}");
            }
            Console.WriteLine($"\nCreate with: swarm create <preset-id>");
            Console.WriteLine("Example:     swarm create safety-report-pipeline");
        });
        return cmd;
    }

    // ── swarm create ────────────────────────────────────────────────

    private static Command BuildCreateCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var presetArg = new Argument<string?>("preset-id")
        {
            Description = "Preset ID to create from (run 'swarm presets' to see options)",
            DefaultValueFactory = _ => null
        };

        var nameOpt = new Option<string?>("--name") { Description = "Custom swarm name" };
        var endpointOpt = new Option<string?>("--endpoint") { Description = "Azure OpenAI endpoint URL" };

        var cmd = new Command("create", "Create a swarm from a preset or custom definition")
        {
            presetArg, nameOpt, endpointOpt
        };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var presetId = parseResult.GetValue(presetArg);
            var name = parseResult.GetValue(nameOpt);
            var endpoint = parseResult.GetValue(endpointOpt);

            SwarmDefinition? swarm = null;

            if (!string.IsNullOrEmpty(presetId))
            {
                swarm = SwarmPresets.GetPreset(presetId);
                if (swarm is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Unknown preset '{presetId}'. Run 'swarm presets' to see available templates.");
                    Console.ResetColor();
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(name))
            {
                swarm = new SwarmDefinition { Name = name };
            }
            else
            {
                Console.WriteLine("Provide a preset ID or --name for a custom swarm.");
                Console.WriteLine("Run 'swarm presets' to see available templates.");
                return;
            }

            if (!string.IsNullOrEmpty(endpoint))
                swarm.AzureOpenAIEndpoint = endpoint;

            var result = await port.CreateSwarmAsync(swarm);
            if (result.Success && result.Data is not null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Swarm created: {result.Data.Name}");
                Console.ResetColor();
                Console.WriteLine($"  ID:         {result.Data.SwarmId}");
                Console.WriteLine($"  Agents:     {result.Data.Agents.Count}");
                Console.WriteLine($"  Entry:      {result.Data.EntryPointAgentId}");
                Console.WriteLine($"  Status:     {result.Data.Status}");

                Console.WriteLine("\nAgent Topology:");
                PrintTopology(result.Data);

                Console.WriteLine($"\nRun a task: swarm run {result.Data.SwarmId} --input \"your prompt here\"");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to create swarm: {result.ErrorMessage}");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── swarm show ──────────────────────────────────────────────────

    private static Command BuildShowCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var idArg = new Argument<string>("swarm-id") { Description = "Swarm ID to show" };
        var cmd = new Command("show", "Show swarm topology and configuration") { idArg };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(idArg);
            var result = await port.GetSwarmAsync(swarmId);
            if (!result.Success || result.Data is null)
            {
                Console.WriteLine($"Swarm '{swarmId}' not found.");
                return;
            }

            var s = result.Data;
            Console.WriteLine($"Swarm: {s.Name}");
            Console.WriteLine($"  ID:                  {s.SwarmId}");
            Console.WriteLine($"  Status:              {s.Status}");
            Console.WriteLine($"  Entry Point:         {s.EntryPointAgentId}");
            Console.WriteLine($"  Max Concurrent:      {s.MaxConcurrentTasks}");
            Console.WriteLine($"  Max Handoff Depth:   {s.MaxHandoffDepth}");
            Console.WriteLine($"  Agent Turn Timeout:  {s.AgentTurnTimeoutSeconds}s");
            Console.WriteLine($"  Task Timeout:        {s.TaskTimeoutSeconds}s");
            Console.WriteLine($"  Azure Endpoint:      {s.AzureOpenAIEndpoint ?? "(default)"}");
            Console.WriteLine($"  API Version:         {s.AzureOpenAIApiVersion}");
            Console.WriteLine($"  Created:             {s.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Updated:             {s.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

            Console.WriteLine($"\nAgents ({s.Agents.Count}):");
            Console.WriteLine(new string('─', 100));

            foreach (var agent in s.Agents)
            {
                var entry = agent.IsEntryPoint || agent.AgentId == s.EntryPointAgentId ? " [ENTRY]" : "";
                Console.ForegroundColor = agent.Role switch
                {
                    SwarmRole.Triage => ConsoleColor.Cyan,
                    SwarmRole.Specialist => ConsoleColor.Yellow,
                    SwarmRole.Reviewer => ConsoleColor.Green,
                    SwarmRole.Supervisor => ConsoleColor.Magenta,
                    SwarmRole.Aggregator => ConsoleColor.Blue,
                    _ => ConsoleColor.White
                };
                Console.WriteLine($"\n  {agent.Name} ({agent.Role}){entry}");
                Console.ResetColor();
                Console.WriteLine($"    ID:       {agent.AgentId}");
                Console.WriteLine($"    Model:    {agent.Model}  (temp={agent.Temperature}, max_tokens={agent.MaxTokens})");
                Console.WriteLine($"    Tools:    {agent.Tools.Count(t => !t.IsHandoff)} custom + {agent.Tools.Count(t => t.IsHandoff)} handoff");

                if (agent.Tools.Any(t => !t.IsHandoff))
                {
                    Console.WriteLine("    Functions:");
                    foreach (var tool in agent.Tools.Where(t => !t.IsHandoff))
                        Console.WriteLine($"      - {tool.Name}: {Truncate(tool.Description, 60)}");
                }

                if (agent.HandoffTargets.Count > 0)
                    Console.WriteLine($"    Handoffs: → {string.Join(", ", agent.HandoffTargets)}");
            }

            Console.WriteLine("\nTopology:");
            PrintTopology(s);
        });

        return cmd;
    }

    // ── swarm delete ────────────────────────────────────────────────

    private static Command BuildDeleteCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var idArg = new Argument<string>("swarm-id") { Description = "Swarm ID to delete" };
        var cmd = new Command("delete", "Delete a swarm definition") { idArg };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(idArg);
            var result = await port.DeleteSwarmAsync(swarmId);
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Swarm '{swarmId}' deleted.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {result.ErrorMessage}");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── swarm run ───────────────────────────────────────────────────

    private static Command BuildRunCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var idArg = new Argument<string>("swarm-id") { Description = "Swarm ID to run the task through" };
        var inputOpt = new Option<string>("--input") { Description = "Task input / prompt", Required = true };
        var streamOpt = new Option<bool>("--stream") { Description = "Stream agent turns and handoffs in real-time", DefaultValueFactory = _ => false };
        var contextOpt = new Option<string[]>("--context") { Description = "Context variables as key=value pairs", AllowMultipleArgumentsPerToken = true };

        var cmd = new Command("run", "Run a task through a swarm") { idArg, inputOpt, streamOpt, contextOpt };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(idArg);
            var input = parseResult.GetValue(inputOpt)!;
            var stream = parseResult.GetValue(streamOpt);
            var context = parseResult.GetValue(contextOpt);

            var task = new SwarmTask
            {
                SwarmId = swarmId,
                Input = input
            };

            // Parse context variables
            foreach (var ctx in context ?? [])
            {
                var parts = ctx.Split('=', 2);
                if (parts.Length == 2)
                    task.ContextVariables[parts[0]] = parts[1];
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Running task through swarm '{swarmId}'...");
            Console.ResetColor();
            Console.WriteLine($"  Task ID: {task.TaskId}");
            Console.WriteLine($"  Input:   {Truncate(input, 80)}");
            Console.WriteLine();

            StorageResult<SwarmTask> result;

            if (stream)
            {
                result = await port.RunTaskStreamingAsync(
                    task,
                    onMessage: msg =>
                    {
                        var agentLabel = msg.AgentId ?? "user";
                        Console.ForegroundColor = msg.Role switch
                        {
                            "assistant" => ConsoleColor.Green,
                            "tool" => ConsoleColor.DarkYellow,
                            _ => ConsoleColor.White
                        };
                        if (msg.Role == "assistant" && !string.IsNullOrEmpty(msg.Content))
                            Console.WriteLine($"  [{agentLabel}] {msg.Content}");
                        Console.ResetColor();
                    },
                    onHandoff: handoff =>
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"  ↪ HANDOFF: {handoff.FromAgentId} → {handoff.ToAgentId} ({handoff.Reason})");
                        Console.ResetColor();
                    },
                    onToolCall: toolCall =>
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"  🔧 {toolCall.FunctionName}({Truncate(toolCall.ArgumentsJson, 60)})");
                        Console.ResetColor();
                    });
            }
            else
            {
                result = await port.RunTaskAsync(task);
            }

            Console.WriteLine();
            if (result.Success && result.Data is not null)
            {
                var t = result.Data;
                Console.ForegroundColor = t.Status == SwarmTaskStatus.Completed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"Status: {t.Status}");
                Console.ResetColor();

                if (t.Status == SwarmTaskStatus.Completed)
                {
                    Console.WriteLine($"\nOutput:");
                    Console.WriteLine(new string('─', 80));
                    Console.WriteLine(t.Output);
                    Console.WriteLine(new string('─', 80));
                }
                else if (t.ErrorMessage is not null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {t.ErrorMessage}");
                    Console.ResetColor();
                }

                Console.WriteLine($"\nMetrics:");
                Console.WriteLine($"  Handoffs:  {t.HandoffCount}");
                Console.WriteLine($"  Messages:  {t.Messages.Count}");
                Console.WriteLine($"  Tokens:    {t.TotalTokensUsed:N0}");
                Console.WriteLine($"  Duration:  {t.Duration?.TotalSeconds:F1}s");

                if (t.HandoffHistory.Count > 0)
                {
                    Console.WriteLine($"\nHandoff Trace:");
                    foreach (var h in t.HandoffHistory)
                        Console.WriteLine($"  {h.Timestamp:HH:mm:ss} {h.FromAgentId} → {h.ToAgentId} ({h.Reason})");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Task failed: {result.ErrorMessage}");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── swarm tasks ─────────────────────────────────────────────────

    private static Command BuildTasksCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var swarmIdArg = new Argument<string?>("swarm-id")
        {
            Description = "Filter by swarm ID (optional)",
            DefaultValueFactory = _ => null
        };
        var limitOpt = new Option<int>("--limit") { Description = "Max tasks to show", DefaultValueFactory = _ => 20 };
        var cmd = new Command("tasks", "List recent swarm tasks") { swarmIdArg, limitOpt };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(swarmIdArg);
            var limit = parseResult.GetValue(limitOpt);

            var result = await port.ListTasksAsync(swarmId, limit);
            if (!result.Success || result.Data is null || result.Data.Count == 0)
            {
                Console.WriteLine("No tasks found.");
                return;
            }

            Console.WriteLine($"{"Task ID",-14} {"Swarm",-25} {"Status",-12} {"Handoffs",-10} {"Tokens",-10} {"Duration",-10} {"Created"}");
            Console.WriteLine(new string('─', 110));
            foreach (var t in result.Data)
            {
                var dur = t.Duration?.TotalSeconds.ToString("F1") + "s" ?? "—";
                Console.WriteLine($"{t.TaskId,-14} {t.SwarmId,-25} {t.Status,-12} {t.HandoffCount,-10} {t.TotalTokensUsed,-10:N0} {dur,-10} {t.CreatedAt:HH:mm:ss}");
            }
        });

        return cmd;
    }

    // ── swarm task ──────────────────────────────────────────────────

    private static Command BuildTaskCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var idArg = new Argument<string>("task-id") { Description = "Task ID to inspect" };
        var cmd = new Command("task", "Show detailed task trace") { idArg };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var taskId = parseResult.GetValue(idArg);
            var result = await port.GetTaskAsync(taskId);
            if (!result.Success || result.Data is null)
            {
                Console.WriteLine($"Task '{taskId}' not found.");
                return;
            }

            var t = result.Data;
            Console.WriteLine($"Task: {t.TaskId}");
            Console.WriteLine($"  Swarm:    {t.SwarmId}");
            Console.WriteLine($"  Status:   {t.Status}");
            Console.WriteLine($"  Input:    {Truncate(t.Input, 100)}");
            Console.WriteLine($"  Output:   {Truncate(t.Output ?? "(none)", 100)}");
            Console.WriteLine($"  Handoffs: {t.HandoffCount}");
            Console.WriteLine($"  Tokens:   {t.TotalTokensUsed:N0}");
            Console.WriteLine($"  Duration: {t.Duration?.TotalSeconds:F1}s");
            Console.WriteLine($"  Created:  {t.CreatedAt:yyyy-MM-dd HH:mm:ss}");

            if (t.ErrorMessage is not null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error:    {t.ErrorMessage}");
                Console.ResetColor();
            }

            Console.WriteLine($"\nConversation Trace ({t.Messages.Count} messages):");
            Console.WriteLine(new string('─', 90));
            foreach (var msg in t.Messages)
            {
                var agent = msg.AgentId is not null ? $" [{msg.AgentId}]" : "";
                Console.ForegroundColor = msg.Role switch
                {
                    "system" => ConsoleColor.DarkGray,
                    "user" => ConsoleColor.White,
                    "assistant" => ConsoleColor.Green,
                    "tool" => ConsoleColor.DarkYellow,
                    _ => ConsoleColor.White
                };
                Console.WriteLine($"  {msg.Timestamp:HH:mm:ss} {msg.Role}{agent}: {Truncate(msg.Content, 120)}");

                if (msg.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"             → {tc.FunctionName}({Truncate(tc.ArgumentsJson, 80)})");
                    }
                }
                Console.ResetColor();
            }

            if (t.HandoffHistory.Count > 0)
            {
                Console.WriteLine($"\nHandoff History:");
                foreach (var h in t.HandoffHistory)
                    Console.WriteLine($"  {h.Timestamp:HH:mm:ss} {h.FromAgentId} → {h.ToAgentId}: {h.Reason}");
            }

            // Get summary if available
            var summaryResult = await port.GetRunSummaryAsync(taskId);
            if (summaryResult.Success && summaryResult.Data is not null)
            {
                var s = summaryResult.Data;
                Console.WriteLine($"\nAgent Metrics:");
                Console.WriteLine($"  {"Agent",-25} {"Role",-15} {"Turns",-8} {"Tools",-8} {"Handoffs"}");
                Console.WriteLine($"  {new string('─', 70)}");
                foreach (var m in s.AgentMetrics)
                    Console.WriteLine($"  {m.AgentName,-25} {m.Role,-15} {m.TurnsHandled,-8} {m.ToolCallsMade,-8} {m.HandoffsInitiated}");
            }
        });

        return cmd;
    }

    // ── swarm validate ──────────────────────────────────────────────

    private static Command BuildValidateCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var idArg = new Argument<string>("swarm-id") { Description = "Swarm ID to validate" };
        var cmd = new Command("validate", "Validate swarm topology (entry point, handoff targets, reachability)") { idArg };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(idArg);
            var result = await port.GetSwarmAsync(swarmId);
            if (!result.Success || result.Data is null)
            {
                Console.WriteLine($"Swarm '{swarmId}' not found.");
                return;
            }

            var errors = result.Data.Validate();
            if (errors.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Swarm '{swarmId}' is valid. {result.Data.Agents.Count} agents, all reachable.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Swarm '{swarmId}' has {errors.Count} validation error(s):");
                Console.ResetColor();
                foreach (var err in errors)
                    Console.WriteLine($"  - {err}");
            }
        });

        return cmd;
    }

    // ── swarm add-agent ─────────────────────────────────────────────

    private static Command BuildAddAgentCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var swarmIdArg = new Argument<string>("swarm-id") { Description = "Swarm ID" };
        var agentIdOpt = new Option<string>("--agent-id") { Description = "Agent ID", Required = true };
        var nameOpt = new Option<string>("--name") { Description = "Agent name", Required = true };
        var roleOpt = new Option<SwarmRole>("--role") { Description = "Agent role", DefaultValueFactory = _ => SwarmRole.Specialist };
        var modelOpt = new Option<string>("--model") { Description = "Azure OpenAI deployment/model", DefaultValueFactory = _ => "gpt-4o" };
        var instructionsOpt = new Option<string>("--instructions") { Description = "System prompt", Required = true };
        var handoffOpt = new Option<string[]>("--handoff-to") { Description = "Agent IDs this agent can hand off to", AllowMultipleArgumentsPerToken = true };
        var entryOpt = new Option<bool>("--entry-point") { Description = "Set as swarm entry point", DefaultValueFactory = _ => false };

        var cmd = new Command("add-agent", "Add an agent to an existing swarm")
        {
            swarmIdArg, agentIdOpt, nameOpt, roleOpt, modelOpt, instructionsOpt, handoffOpt, entryOpt
        };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(swarmIdArg);
            var result = await port.GetSwarmAsync(swarmId);
            if (!result.Success || result.Data is null)
            {
                Console.WriteLine($"Swarm '{swarmId}' not found.");
                return;
            }

            var swarm = result.Data;
            var agent = new SwarmAgentDefinition
            {
                AgentId = parseResult.GetValue(agentIdOpt)!,
                Name = parseResult.GetValue(nameOpt)!,
                Role = parseResult.GetValue(roleOpt),
                Model = parseResult.GetValue(modelOpt)!,
                Instructions = parseResult.GetValue(instructionsOpt)!,
                HandoffTargets = (parseResult.GetValue(handoffOpt) ?? []).ToList(),
                IsEntryPoint = parseResult.GetValue(entryOpt)
            };

            if (agent.IsEntryPoint)
                swarm.EntryPointAgentId = agent.AgentId;

            swarm.Agents.Add(agent);
            var updateResult = await port.UpdateSwarmAsync(swarm);

            if (updateResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Agent '{agent.Name}' ({agent.AgentId}) added to swarm '{swarmId}'.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {updateResult.ErrorMessage}");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── swarm remove-agent ──────────────────────────────────────────

    private static Command BuildRemoveAgentCommand(Func<ISwarmPort?> getSwarmPort)
    {
        var swarmIdArg = new Argument<string>("swarm-id") { Description = "Swarm ID" };
        var agentIdArg = new Argument<string>("agent-id") { Description = "Agent ID to remove" };
        var cmd = new Command("remove-agent", "Remove an agent from a swarm") { swarmIdArg, agentIdArg };

        cmd.SetAction(async (parseResult) =>
        {
            var port = RequirePort(getSwarmPort);
            if (port is null) return;

            var swarmId = parseResult.GetValue(swarmIdArg);
            var agentId = parseResult.GetValue(agentIdArg);

            var result = await port.GetSwarmAsync(swarmId);
            if (!result.Success || result.Data is null)
            {
                Console.WriteLine($"Swarm '{swarmId}' not found.");
                return;
            }

            var swarm = result.Data;
            var removed = swarm.Agents.RemoveAll(a => a.AgentId == agentId);

            if (removed == 0)
            {
                Console.WriteLine($"Agent '{agentId}' not found in swarm '{swarmId}'.");
                return;
            }

            // Clean up handoff references
            foreach (var agent in swarm.Agents)
                agent.HandoffTargets.Remove(agentId);

            var updateResult = await port.UpdateSwarmAsync(swarm);
            if (updateResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Agent '{agentId}' removed from swarm '{swarmId}'.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {updateResult.ErrorMessage}");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static ISwarmPort? RequirePort(Func<ISwarmPort?> getSwarmPort)
    {
        var port = getSwarmPort();
        if (port is not null) return port;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Azure OpenAI swarm not configured.");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Set up by providing environment variables or appsettings:");
        Console.WriteLine("  AZURE_OPENAI_ENDPOINT=https://your-instance.openai.azure.com/");
        Console.WriteLine("  AZURE_OPENAI_API_KEY=your-api-key");
        Console.WriteLine();
        Console.WriteLine("Or pass via CLI:");
        Console.WriteLine("  --aoai-endpoint https://your-instance.openai.azure.com/ --aoai-key your-key");
        Console.WriteLine();
        Console.WriteLine("Ensure you have deployed models (gpt-4o, gpt-4o-mini) in your Azure OpenAI resource.");
        return null;
    }

    private static void PrintTopology(SwarmDefinition swarm)
    {
        var entryId = swarm.EntryPointAgentId;
        var visited = new HashSet<string>();
        var sb = new StringBuilder();

        void Walk(string agentId, int depth)
        {
            if (!visited.Add(agentId)) return;
            var agent = swarm.GetAgent(agentId);
            if (agent is null) return;

            var indent = new string(' ', depth * 4);
            var marker = depth == 0 ? "►" : "├";
            sb.AppendLine($"{indent}{marker} {agent.Name} ({agent.Role}) [{agent.AgentId}]");

            foreach (var target in agent.HandoffTargets)
                Walk(target, depth + 1);
        }

        Walk(entryId, 0);

        // Show any unvisited agents
        foreach (var agent in swarm.Agents.Where(a => !visited.Contains(a.AgentId)))
            sb.AppendLine($"    ⚠ {agent.Name} ({agent.Role}) [{agent.AgentId}] — UNREACHABLE");

        Console.WriteLine(sb.ToString());
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
}
