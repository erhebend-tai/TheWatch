// DashboardConfig — runtime configuration parsed from CLI args.
// Example: new DashboardConfig { ApiBaseUrl = "https://localhost:5001", PollIntervalSeconds = 10 }

namespace TheWatch.Cli.App;

public class DashboardConfig
{
    public string ApiBaseUrl { get; set; } = "https://localhost:5001";
    public bool EnableSignalR { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
}
