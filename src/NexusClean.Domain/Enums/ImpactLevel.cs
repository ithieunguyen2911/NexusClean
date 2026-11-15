namespace NexusClean.Domain.Enums;

/// <summary>
/// 5-tier impact classification matrix defined in NexusClean Constitution Principle IV.
/// </summary>
public enum ImpactLevel
{
    SAFE_CACHE_TIER1 = 0,
    REBUILD_ON_DEMAND = 1,
    RELOCATE_TO_SECONDARY_DRIVE = 2,
    REQUIRES_ADMIN_ACTION = 3,
    PROTECTED_NEVER_DELETE = 4
}
