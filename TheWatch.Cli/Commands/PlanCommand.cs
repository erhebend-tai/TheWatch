// =============================================================================
// PlanCommand — CLI command that uses Claude Code or Azure OpenAI to plan swarms
// =============================================================================
// Analyzes a task description using an AI backend (Claude Code CLI or Azure OpenAI),
// recommends the optimal number of swarm agents, their roles, handoff topology,
// and system prompts, then creates the swarm definition and prints the agent TODOs.
//
// Usage:
//   thewatch plan "Handle a neighborhood SOS with evidence collection and 911 escalation"
//   thewatch plan "Audit ISO 27001 compliance for our auth module" --backend azure
//   thewatch plan "Build a code review pipeline for safety-critical C#" --backend claude
//   thewatch plan "Process guard reports with threat assessment" --backend claude --create
//   thewatch plan "Respond to silent duress signal" --max-agents 8
//
// How it works:
//   1. User provides a natural language goal/task description
//   2. PlanCommand sends a structured prompt to Claude Code CLI (--dangerously-skip-permissions -p)
//      or Azure OpenAI asking it to decompose the task into swarm agents
//   3. The AI returns a JSON plan: agent count, roles, instructions, handoff graph, and per-agent TODOs
//   4. PlanCommand displays the recommended plan with a topology diagram
//   5. If --create is passed, it creates the swarm definition via ISwarmPort
//
// WAL: The structured prompt constrains the AI output to a JSON schema so we can
//      reliably parse agent definitions. Falls back to text output if JSON parsing fails.
//      Claude Code uses --dangerously-skip-permissions because this is a non-interactive
//      scripted invocation where the CLI never touches files — it only generates a plan.
// =============================================================================

using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheWatch.Cli.Services;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Cli.Commands;

public static class PlanCommand
{
    // ── JSON contract for the AI's recommended plan ─────────────────
    // Both Claude Code and Azure OpenAI are asked to return this schema.

    private class SwarmPlan
    {
        [JsonPropertyName("swarm_name")]
        public string SwarmName { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("agent_count")]
        public int AgentCount { get; set; }

        [JsonPropertyName("agents")]
        public List<AgentPlan> Agents { get; set; } = [];

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = "";
    }

    private class AgentPlan
    {
        [JsonPropertyName("agent_id")]
        public string AgentId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("role")]
        public string Role { get; set; } = "Specialist";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gpt-4o";

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.7f;

        [JsonPropertyName("instructions")]
        public string Instructions { get; set; } = "";

        [JsonPropertyName("handoff_targets")]
        public List<string> HandoffTargets { get; set; } = [];

        [JsonPropertyName("tools")]
        public List<ToolPlan> Tools { get; set; } = [];

        [JsonPropertyName("is_entry_point")]
        public bool IsEntryPoint { get; set; }

        [JsonPropertyName("todo")]
        public string Todo { get; set; } = "";
    }

    private class ToolPlan
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("parameters_json")]
        public string ParametersJson { get; set; } = """{"type":"object","properties":{}}""";
    }

    // ── The structured prompt sent to both backends ──────────────────
    // Constrains the AI to produce a parseable JSON plan.

    private const string PlanningPromptTemplate = """
        You are a swarm architect for TheWatch, a life-safety emergency response application.
        TheWatch coordinates volunteer responders, 911 escalation, evidence collection, guard reports,
        CCTV analysis, geospatial lookups, and ISO/IEC compliance audits.

        Given a task description, design the optimal multi-agent swarm to handle it.

        RULES:
        - Each agent must have a clear, single responsibility
        - The entry-point agent is always a Triage agent that classifies and routes
        - Specialist agents handle domain-specific work (threat assessment, evidence, location, etc.)
        - A Reviewer or Supervisor agent should validate outputs for life-safety tasks
        - An Aggregator agent collects results from parallel specialists when applicable
        - Use low temperature (0.1-0.3) for deterministic/safety-critical agents
        - Use higher temperature (0.5-0.7) for creative/analysis agents
        - Include tool definitions for domain actions (e.g., lookup_location, classify_threat, initiate_911_call)
        - Every agent MUST have a "todo" field describing its specific task/objective for this plan
        - Minimum 2 agents, maximum {{MAX_AGENTS}} agents
        - Valid roles: Triage, Specialist, Reviewer, Supervisor, Orchestrator, Aggregator, Custom

        RESPOND WITH ONLY VALID JSON matching this schema (no markdown, no explanation outside JSON):
        {
          "swarm_name": "string — short name for the swarm",
          "description": "string — what this swarm does",
          "agent_count": number,
          "reasoning": "string — why you chose this topology (2-3 sentences)",
          "agents": [
            {
              "agent_id": "string — kebab-case ID like triage-01",
              "name": "string — human-readable name",
              "role": "Triage|Specialist|Reviewer|Supervisor|Orchestrator|Aggregator|Custom",
              "model": "gpt-4o|gpt-4o-mini",
              "temperature": 0.0-2.0,
              "instructions": "string — full system prompt for this agent",
              "handoff_targets": ["agent-id-1", "agent-id-2"],
              "tools": [
                {
                  "name": "function_name",
                  "description": "what this function does",
                  "parameters_json": "{\"type\":\"object\",\"properties\":{...}}"
                }
              ],
              "is_entry_point": true/false,
              "todo": "string — the specific objective/task this agent must accomplish"
            }
          ]
        }

        TASK TO PLAN:
        {{TASK_DESCRIPTION}}
        """;

    // ── Build the command tree ──────────────────────────────────────

    public static Command Build(Func<ISwarmPort?> getSwarmPort)
    {
        var taskArg = new Argument<string>("task")
        {
            Description = "Natural language description of the task/goal to plan a swarm for"
        };

        var backendOpt = new Option<string>("--backend")
        {
            Description = "AI backend to use for planning: 'claude' (Claude Code CLI) or 'azure' (Azure OpenAI)",
            DefaultValueFactory = _ => "claude"
        };

        var createOpt = new Option<bool>("--create")
        {
            Description = "Automatically create the swarm after planning (otherwise just prints the plan)",
            DefaultValueFactory = _ => false
        };

        var maxAgentsOpt = new Option<int>("--max-agents")
        {
            Description = "Maximum number of agents the planner can recommend",
            DefaultValueFactory = _ => 10
        };

        var modelOpt = new Option<string>("--model")
        {
            Description = "Model override for Azure backend (default: gpt-4o)",
            DefaultValueFactory = _ => "gpt-4o"
        };

        var cmd = new Command("plan",
            "Use Claude Code or Azure OpenAI to design a swarm — recommends agents, topology, and TODOs")
        {
            taskArg, backendOpt, createOpt, maxAgentsOpt, modelOpt
        };

        cmd.SetAction(async (parseResult) =>
        {
            var task = parseResult.GetValue(taskArg)!;
            var backend = parseResult.GetValue(backendOpt)!.ToLowerInvariant();
            var create = parseResult.GetValue(createOpt);
            var maxAgents = parseResult.GetValue(maxAgentsOpt);
            var model = parseResult.GetValue(modelOpt)!;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Planning swarm via {backend.ToUpperInvariant()} backend...");
            Console.ResetColor();
            Console.WriteLine($"  Task:       {Truncate(task, 100)}");
            Console.WriteLine($"  Max agents: {maxAgents}");
            Console.WriteLine();

            // Build the prompt
            var prompt = PlanningPromptTemplate
                .Replace("{{MAX_AGENTS}}", maxAgents.ToString())
                .Replace("{{TASK_DESCRIPTION}}", task);

            // Call the chosen backend
            string rawResponse;
            bool success;

            if (backend == "claude")
            {
                (success, rawResponse) = await CallClaudeCodeAsync(prompt);
            }
            else if (backend == "azure")
            {
                var port = getSwarmPort();
                if (port is null)
                {
                    PrintAzureSetupInstructions();
                    return;
                }
                (success, rawResponse) = await CallAzureOpenAIAsync(port, prompt, model);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unknown backend '{backend}'. Use 'claude' or 'azure'.");
                Console.ResetColor();
                return;
            }

            if (!success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Planning failed:");
                Console.ResetColor();
                Console.WriteLine(rawResponse);
                return;
            }

            // Parse the JSON plan
            SwarmPlan? plan;
            try
            {
                // Extract JSON from response (Claude Code may wrap in markdown code blocks)
                var json = ExtractJson(rawResponse);
                plan = JsonSerializer.Deserialize<SwarmPlan>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (plan is null || plan.Agents.Count == 0)
                    throw new JsonException("Parsed plan has no agents.");
            }
            catch (JsonException ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Could not parse structured plan. Showing raw AI response:");
                Console.ResetColor();
                Console.WriteLine(new string('─', 80));
                Console.WriteLine(rawResponse);
                Console.WriteLine(new string('─', 80));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Parse error: {ex.Message}");
                Console.ResetColor();
                return;
            }

            // Display the plan
            PrintPlan(plan);

            // Create if requested
            if (create)
            {
                var port = getSwarmPort();
                if (port is null)
                {
                    PrintAzureSetupInstructions();
                    return;
                }

                var swarm = ConvertToSwarmDefinition(plan);
                var result = await port.CreateSwarmAsync(swarm);

                if (result.Success && result.Data is not null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nSwarm created: {result.Data.SwarmId}");
                    Console.ResetColor();
                    Console.WriteLine($"Run it: swarm run {result.Data.SwarmId} --input \"your prompt\"");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nFailed to create swarm: {result.ErrorMessage}");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\nAdd --create to auto-create this swarm, or manually create with:");
                Console.WriteLine("  swarm create --name \"...\"");
                Console.ResetColor();
            }
        });

        return cmd;
    }

    // ── Claude Code CLI Backend ─────────────────────────────────────
    // Calls: claude --dangerously-skip-permissions -p "prompt" --output json
    // The --dangerously-skip-permissions flag is required because this is a
    // non-interactive scripted invocation. Claude Code never touches files here —
    // it only generates a plan as JSON output.

    private static async Task<(bool Success, string Response)> CallClaudeCodeAsync(string prompt)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Invoking: claude --dangerously-skip-permissions -p <prompt>");
        Console.ResetColor();

        var bridge = new ClaudeCodeBridge();

        // Use RunClaudeAsync with the dangerously-skip-permissions flag
        // ClaudeCodeBridge.RunPrintAsync uses -p, we need to add the permissions flag
        var result = await RunClaudeWithFlagsAsync(bridge, prompt);

        if (result.Success)
        {
            return (true, result.Output);
        }

        // If claude isn't found or fails, provide helpful error
        if (result.Error.Contains("not found") || result.Error.Contains("not recognized")
            || result.ExitCode == -1)
        {
            return (false, "Claude Code CLI not found. Install with:\n" +
                "  npm install -g @anthropic-ai/claude-code\n\n" +
                "Then ensure ANTHROPIC_API_KEY is set in your environment.\n\n" +
                $"Error: {result.Error}");
        }

        return (false, $"Claude Code returned exit code {result.ExitCode}:\n{result.Error}\n{result.Output}");
    }

    private static async Task<ClaudeCodeResult> RunClaudeWithFlagsAsync(ClaudeCodeBridge bridge, string prompt)
    {
        // We bypass ClaudeCodeBridge's normal methods to add --dangerously-skip-permissions
        // This calls the claude binary directly with the required flags
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = FindClaudeBinary(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            // claude --dangerously-skip-permissions -p "prompt" --output-format json
            psi.ArgumentList.Add("--dangerously-skip-permissions");
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(prompt);
            psi.ArgumentList.Add("--output-format");
            psi.ArgumentList.Add("json");

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new ClaudeCodeResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = stdout,
                Error = stderr
            };
        }
        catch (Exception ex)
        {
            return new ClaudeCodeResult
            {
                Success = false,
                ExitCode = -1,
                Output = "",
                Error = ex.Message
            };
        }
    }

    /// <summary>Find the claude binary in PATH or common install locations.</summary>
    private static string FindClaudeBinary()
    {
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

        foreach (var dir in pathDirs)
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { "claude.cmd", "claude.exe", "claude.ps1" }
                : new[] { "claude" };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(dir, candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        // Common npm global install locations
        var npmPaths = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "claude.cmd"),
            }
            : new[]
            {
                "/usr/local/bin/claude",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global", "bin", "claude"),
            };

        foreach (var path in npmPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "claude";
    }

    // ── Azure OpenAI Backend ────────────────────────────────────────
    // Creates a temporary single-agent swarm whose only job is to produce the plan JSON.

    private static async Task<(bool Success, string Response)> CallAzureOpenAIAsync(
        ISwarmPort port, string prompt, string model)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Invoking Azure OpenAI model: {model}");
        Console.ResetColor();

        // Create a temporary planner swarm with a single agent
        var plannerSwarm = new SwarmDefinition
        {
            SwarmId = $"_planner-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Swarm Planner (temporary)",
            Description = "Temporary swarm used to generate a plan. Auto-deleted after use.",
            EntryPointAgentId = "planner-01",
            Agents =
            [
                new SwarmAgentDefinition
                {
                    AgentId = "planner-01",
                    Name = "Swarm Architect",
                    Role = SwarmRole.Specialist,
                    Model = model,
                    Temperature = 0.4f,
                    MaxTokens = 8192,
                    IsEntryPoint = true,
                    Instructions = "You are a swarm architect. Respond with ONLY valid JSON. No markdown. No explanation outside the JSON object."
                }
            ]
        };

        var createResult = await port.CreateSwarmAsync(plannerSwarm);
        if (!createResult.Success)
            return (false, $"Failed to create planner swarm: {createResult.ErrorMessage}");

        try
        {
            var task = new SwarmTask
            {
                SwarmId = plannerSwarm.SwarmId,
                Input = prompt
            };

            var runResult = await port.RunTaskAsync(task);

            if (runResult.Success && runResult.Data?.Output is not null)
                return (true, runResult.Data.Output);

            return (false, $"Azure OpenAI planning failed: {runResult.ErrorMessage ?? runResult.Data?.ErrorMessage ?? "No output"}");
        }
        finally
        {
            // Clean up the temporary planner swarm
            await port.DeleteSwarmAsync(plannerSwarm.SwarmId);
        }
    }

    // ── Plan Display ────────────────────────────────────────────────

    private static void PrintPlan(SwarmPlan plan)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"SWARM PLAN: {plan.SwarmName}");
        Console.ResetColor();
        Console.WriteLine(new string('═', 90));
        Console.WriteLine($"  Description:  {plan.Description}");
        Console.WriteLine($"  Agent Count:  {plan.AgentCount}");
        Console.WriteLine($"  Reasoning:    {plan.Reasoning}");
        Console.WriteLine();

        // Agent details
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("AGENTS:");
        Console.ResetColor();
        Console.WriteLine(new string('─', 90));

        foreach (var agent in plan.Agents)
        {
            var entryTag = agent.IsEntryPoint ? " [ENTRY POINT]" : "";
            Console.ForegroundColor = agent.Role.ToLower() switch
            {
                "triage" => ConsoleColor.Cyan,
                "specialist" => ConsoleColor.Yellow,
                "reviewer" => ConsoleColor.Green,
                "supervisor" => ConsoleColor.Magenta,
                "aggregator" => ConsoleColor.Blue,
                "orchestrator" => ConsoleColor.DarkCyan,
                _ => ConsoleColor.White
            };
            Console.WriteLine($"\n  {agent.Name} ({agent.Role}){entryTag}");
            Console.ResetColor();

            Console.WriteLine($"    ID:          {agent.AgentId}");
            Console.WriteLine($"    Model:       {agent.Model}  (temp={agent.Temperature:F1})");

            if (agent.HandoffTargets.Count > 0)
                Console.WriteLine($"    Handoffs:    → {string.Join(", ", agent.HandoffTargets)}");

            if (agent.Tools.Count > 0)
            {
                Console.WriteLine($"    Tools ({agent.Tools.Count}):");
                foreach (var tool in agent.Tools)
                    Console.WriteLine($"      - {tool.Name}: {Truncate(tool.Description, 60)}");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"    TODO:        {agent.Todo}");
            Console.ResetColor();
        }

        // Topology diagram
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("TOPOLOGY:");
        Console.ResetColor();
        Console.WriteLine(new string('─', 90));

        var entry = plan.Agents.FirstOrDefault(a => a.IsEntryPoint) ?? plan.Agents.FirstOrDefault();
        if (entry is not null)
        {
            var visited = new HashSet<string>();
            PrintTopologyTree(plan.Agents, entry.AgentId, 0, visited);

            // Show unreachable agents
            foreach (var agent in plan.Agents.Where(a => !visited.Contains(a.AgentId)))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"    ⚠ {agent.Name} ({agent.Role}) [{agent.AgentId}] — UNREACHABLE");
                Console.ResetColor();
            }
        }

        // TODO Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("AGENT TODOs:");
        Console.ResetColor();
        Console.WriteLine(new string('─', 90));

        for (var i = 0; i < plan.Agents.Count; i++)
        {
            var agent = plan.Agents[i];
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  [{i + 1}] {agent.Name}: ");
            Console.ResetColor();
            Console.WriteLine(agent.Todo);
        }
    }

    private static void PrintTopologyTree(List<AgentPlan> agents, string agentId, int depth, HashSet<string> visited)
    {
        if (!visited.Add(agentId)) return;
        var agent = agents.FirstOrDefault(a => a.AgentId == agentId);
        if (agent is null) return;

        var indent = new string(' ', depth * 4);
        var marker = depth == 0 ? "►" : "├";
        Console.WriteLine($"  {indent}{marker} {agent.Name} ({agent.Role}) [{agent.AgentId}]");

        foreach (var target in agent.HandoffTargets)
            PrintTopologyTree(agents, target, depth + 1, visited);
    }

    // ── Conversion to Domain Model ──────────────────────────────────

    private static SwarmDefinition ConvertToSwarmDefinition(SwarmPlan plan)
    {
        var swarm = new SwarmDefinition
        {
            Name = plan.SwarmName,
            Description = plan.Description,
            EntryPointAgentId = plan.Agents.FirstOrDefault(a => a.IsEntryPoint)?.AgentId
                ?? plan.Agents.First().AgentId,
            Agents = plan.Agents.Select(a => new SwarmAgentDefinition
            {
                AgentId = a.AgentId,
                Name = a.Name,
                Role = Enum.TryParse<SwarmRole>(a.Role, ignoreCase: true, out var r) ? r : SwarmRole.Custom,
                Model = a.Model,
                Temperature = a.Temperature,
                Instructions = a.Instructions,
                HandoffTargets = a.HandoffTargets,
                IsEntryPoint = a.IsEntryPoint,
                Tools = a.Tools.Select(t => new SwarmToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    ParametersJson = t.ParametersJson
                }).ToList(),
                Metadata = new Dictionary<string, string> { ["todo"] = a.Todo }
            }).ToList()
        };

        return swarm;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Extract JSON from a response that may be wrapped in markdown code blocks
    /// or have text before/after the JSON object.
    /// </summary>
    private static string ExtractJson(string raw)
    {
        // Strip markdown code fences if present
        var trimmed = raw.Trim();

        // Handle ```json ... ``` wrapping
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];

            var lastFence = trimmed.LastIndexOf("```");
            if (lastFence > 0)
                trimmed = trimmed[..lastFence];
        }

        // If Claude Code returned --output-format json, the response is a JSON object
        // with a "result" field containing the actual content
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            // Check if this is a Claude Code JSON envelope
            if (doc.RootElement.TryGetProperty("result", out var resultEl))
            {
                var resultText = resultEl.GetString() ?? trimmed;
                // The result itself may contain the plan JSON
                return ExtractJsonObject(resultText);
            }

            // Already valid JSON with our expected shape — check for swarm_name or agents
            if (doc.RootElement.TryGetProperty("swarm_name", out _) ||
                doc.RootElement.TryGetProperty("agents", out _))
            {
                return trimmed;
            }
        }
        catch { }

        // Try to find the first { ... } block
        return ExtractJsonObject(trimmed);
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return text; // Give up, let the caller handle the parse error

        // Find the matching closing brace by counting depth
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;

            if (depth == 0)
                return text[start..(i + 1)];
        }

        return text[start..]; // Unterminated, return what we have
    }

    private static void PrintAzureSetupInstructions()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Azure OpenAI not configured.");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Set up by providing environment variables:");
        Console.WriteLine("  AZURE_OPENAI_ENDPOINT=https://your-instance.openai.azure.com/");
        Console.WriteLine("  AZURE_OPENAI_API_KEY=your-api-key");
        Console.WriteLine();
        Console.WriteLine("Or pass via CLI:");
        Console.WriteLine("  --aoai-endpoint https://your-instance.openai.azure.com/ --aoai-key your-key");
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
}
