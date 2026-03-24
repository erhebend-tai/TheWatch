namespace TheWatch.Shared.Domain.Models;

public class BranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastCommitDate { get; set; }
    public string? PrStatus { get; set; }
}
