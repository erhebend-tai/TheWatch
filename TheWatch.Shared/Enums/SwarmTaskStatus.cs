// SwarmTaskStatus — state of an individual task within a swarm.
// Example:
//   if (task.Status == SwarmTaskStatus.HandedOff) Console.WriteLine($"Routed to {task.AssignedAgentId}");

namespace TheWatch.Shared.Enums;

public enum SwarmTaskStatus
{
    Queued,
    Assigned,
    InProgress,
    HandedOff,
    AwaitingReview,
    Completed,
    Failed,
    Retrying,
    Cancelled
}
