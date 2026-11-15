namespace NexusClean.Domain.Enums;

/// <summary>
/// Verdict severity emitted by each link in the ISafetyGuard Chain of Responsibility.
/// </summary>
public enum GuardSeverity
{
    Allow = 0,
    Warn = 1,
    RequireElevation = 2,
    Block = 3
}

/// <summary>
/// Outcome status of simulating or executing a single StorageFinding.
/// </summary>
public enum CleanupItemStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    SkippedLocked = 2,
    BlockedByGuard = 3,
    RelocationAdvised = 4,
    AdminQueued = 5,
    Failed = 6
}
