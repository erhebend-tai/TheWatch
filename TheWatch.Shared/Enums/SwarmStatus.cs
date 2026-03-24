// SwarmStatus — lifecycle state of a swarm instance.
// Example:
//   if (swarm.Status == SwarmStatus.Running) await swarm.WaitForCompletionAsync();

namespace TheWatch.Shared.Enums;

public enum SwarmStatus
{
    /// <summary>Swarm definition created but not yet started.</summary>
    Created,

    /// <summary>Swarm is initializing agents and connections.</summary>
    Initializing,

    /// <summary>Swarm is actively processing tasks.</summary>
    Running,

    /// <summary>Swarm is paused (can be resumed).</summary>
    Paused,

    /// <summary>All tasks completed successfully.</summary>
    Completed,

    /// <summary>Swarm terminated due to an unrecoverable error.</summary>
    Failed,

    /// <summary>Swarm was cancelled by user or system.</summary>
    Cancelled,

    /// <summary>Swarm is draining — finishing in-progress tasks, not accepting new ones.</summary>
    Draining
}
