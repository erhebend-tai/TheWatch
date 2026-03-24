namespace TheWatch.Shared.Domain.Models;

public class Milestone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int TotalIssues { get; set; }
    public int ClosedIssues { get; set; }

    public int PercentComplete => TotalIssues > 0 ? (ClosedIssues * 100) / TotalIssues : 0;
}
