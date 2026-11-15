using NexusClean.Domain.Enums;

namespace NexusClean.Domain.Entities;

/// <summary>
/// Transparent safety and impact assessment attached to every StorageFinding.
/// </summary>
public sealed record ImpactAssessment
{
    public ImpactLevel Level { get; init; }
    public int RiskScore { get; init; }
    public string SummaryVi { get; init; }
    public string SideEffectIfDeleted { get; init; }
    public string? RecommendedAction { get; init; }
    public IReadOnlyList<string> PreservedAssets { get; init; }
    public bool IsLockedByProcess { get; init; }
    public IReadOnlyList<string> LockingProcesses { get; init; }
    public bool RequiresAdmin { get; init; }

    public bool IsDefaultSafeSelection =>
        Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND &&
        !RequiresAdmin;

    public ImpactAssessment(
        ImpactLevel level,
        int riskScore,
        string summaryVi,
        string sideEffectIfDeleted,
        IReadOnlyList<string>? preservedAssets,
        string? recommendedAction = null,
        bool isLockedByProcess = false,
        IReadOnlyList<string>? lockingProcesses = null,
        bool requiresAdmin = false)
    {
        if (riskScore is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(riskScore), "RiskScore must be bounded between 0 and 10.");
        }

        if (level == ImpactLevel.PROTECTED_NEVER_DELETE && riskScore != 10)
        {
            throw new ArgumentException("PROTECTED_NEVER_DELETE findings must carry RiskScore == 10.", nameof(riskScore));
        }

        if (string.IsNullOrWhiteSpace(summaryVi) || summaryVi.Trim().Length < 10)
        {
            throw new ArgumentException("SummaryVi must be at least 10 characters.", nameof(summaryVi));
        }

        if (string.IsNullOrWhiteSpace(sideEffectIfDeleted))
        {
            throw new ArgumentException("SideEffectIfDeleted is required.", nameof(sideEffectIfDeleted));
        }

        var assets = (preservedAssets ?? Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();

        if (level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND && assets.Length == 0)
        {
            throw new ArgumentException("PreservedAssets must contain at least one item for safe cache tiers.", nameof(preservedAssets));
        }

        var locks = (lockingProcesses ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Level = level;
        RiskScore = riskScore;
        SummaryVi = summaryVi.Trim();
        SideEffectIfDeleted = sideEffectIfDeleted.Trim();
        RecommendedAction = string.IsNullOrWhiteSpace(recommendedAction) ? null : recommendedAction.Trim();
        PreservedAssets = assets;
        IsLockedByProcess = isLockedByProcess || locks.Length > 0;
        LockingProcesses = locks;
        RequiresAdmin = requiresAdmin || level == ImpactLevel.REQUIRES_ADMIN_ACTION;
    }
}
