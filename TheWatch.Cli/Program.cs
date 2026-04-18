using System.CommandLine;
using TheWatch.Cli.App;
using TheWatch.Cli.Commands;

var rootCommand = new RootCommand("TheWatch CLI Command Center");

rootCommand.Add(new IngestCommand());
rootCommand.Add(SwarmCommand.Build(null, null)); // Placeholder for now
rootCommand.Add(PlanCommand.Build(null)); // Placeholder
rootCommand.Add(CodeGenCommand.Build());
rootCommand.Add(CodeIndexCommand.Build());
rootCommand.Add(CodeIndexDbCommand.Build());

rootCommand.SetAction(async (parseResult) =>
{
    var app = new DashboardApp(new DashboardConfig());
    await app.RunAsync();
});

return await rootCommand.InvokeAsync(args);
