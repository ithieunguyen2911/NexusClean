using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NexusClean.Domain.Enums;

namespace NexusClean.Domain.Entities;

/// <summary>
/// Core Domain entity representing a sized storage consumer and its verified safety state.
/// </summary>
public sealed class StorageFinding
{
    private static readonly Regex RuleIdPattern = new(
        @"^[a-z0-9_]+(\.[a-z0-9_]+){1,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Id { get; }
    public string RuleId { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string TargetPath { get; }
    public IReadOnlyList<string> SubPathsToClean { get; }
    public long SizeInBytes { get; }
    public int FileCount { get; }
    public DateTime LastModifiedUtc { get; }
    public CleanupActionType ActionType { get; private set; }
    public ImpactAssessment Impact { get; private set; }
    public IReadOnlyList<GuardVerdict> GuardVerdicts { get; private set; }

    public StorageFinding(
        string ruleId,
        string displayName,
        string category,
        string targetPath,
        long sizeInBytes,
        int fileCount,
        DateTime lastModifiedUtc,
        CleanupActionType actionType,
        ImpactAssessment impact,
        IReadOnlyList<string>? subPathsToClean = null,
        IReadOnlyList<GuardVerdict>? guardVerdicts = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId) || !RuleIdPattern.IsMatch(ruleId))
        {
            throw new ArgumentException($"RuleId '{ruleId}' does not match canonical pattern.", nameof(ruleId));
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
        {
            throw new ArgumentException("DisplayName must be non-empty and <= 160 chars.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 64)
        {
            throw new ArgumentException("Category must be non-empty and <= 64 chars.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("TargetPath is required.", nameof(targetPath));
        }

        if (sizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
        }

        if (fileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileCount));
        }

        ArgumentNullException.ThrowIfNull(impact);

        var normalizedTarget = RelocationPlan.NormalizeWindowsPath(targetPath);
        var requiredPrefix = Path.TrimEndingDirectorySeparator(normalizedTarget) + Path.DirectorySeparatorChar;

        var normalizedSubPaths = new List<string>();
        if (subPathsToClean is not null)
        {
            foreach (var rawSub in subPathsToClean)
            {
                if (string.IsNullOrWhiteSpace(rawSub))
                {
                    continue;
                }

                var normalizedSub = RelocationPlan.NormalizeWindowsPath(rawSub);
                if (!normalizedSub.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"SubPath '{normalizedSub}' must reside strictly inside TargetPath '{normalizedTarget}'.",
                        nameof(subPathsToClean));
                }

                normalizedSubPaths.Add(normalizedSub);
            }
        }

        RuleId = ruleId;
        DisplayName = displayName.Trim();
        Category = category.Trim();
        TargetPath = normalizedTarget;
        SubPathsToClean = normalizedSubPaths;
        SizeInBytes = sizeInBytes;
        FileCount = fileCount;
        LastModifiedUtc = lastModifiedUtc.Kind == DateTimeKind.Utc
            ? lastModifiedUtc
            : lastModifiedUtc.ToUniversalTime();
        Impact = impact;
        ActionType = impact.Level == ImpactLevel.PROTECTED_NEVER_DELETE
            ? CleanupActionType.BlockedNoOp
            : actionType;
        GuardVerdicts = guardVerdicts?.ToArray() ?? Array.Empty<GuardVerdict>();
        Id = ComputeDeterministicId(RuleId, TargetPath);
    }

    public void ApplyGuardVerdicts(
        IReadOnlyList<GuardVerdict> verdicts,
        IReadOnlyList<string>? detectedLockingProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        GuardVerdicts = verdicts.ToArray();

        var mergedLocks = Impact.LockingProcesses
            .Concat(detectedLockingProcesses ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var blockVerdict = verdicts.FirstOrDefault(v => v.Severity == GuardSeverity.Block);
        if (blockVerdict is not null)
        {
            ActionType = CleanupActionType.BlockedNoOp;
            Impact = new ImpactAssessment(
                level: ImpactLevel.PROTECTED_NEVER_DELETE,
                riskScore: 10,
                summaryVi: $"[ĐÃ KHÓA BẢO VỆ] {blockVerdict.MessageVi}",
                sideEffectIfDeleted: "Thao tác xóa trực tiếp đã bị vô hiệu hóa hoàn toàn bởi hệ thống Safety Guard.",
                preservedAssets: Impact.PreservedAssets.Count > 0
                    ? Impact.PreservedAssets
                    : new[] { "Toàn bộ dữ liệu hệ thống và Profile người dùng" },
                recommendedAction: "Không thực hiện xóa thư mục được bảo vệ này.",
                isLockedByProcess: mergedLocks.Length > 0 || Impact.IsLockedByProcess,
                lockingProcesses: mergedLocks,
                requiresAdmin: Impact.RequiresAdmin);
            return;
        }

        var requiresElevation = verdicts.Any(v => v.Severity == GuardSeverity.RequireElevation) || Impact.RequiresAdmin;
        var hasWarnLock = verdicts.Any(v => v.Severity == GuardSeverity.Warn && v.ReasonCode.Contains("LOCK", StringComparison.OrdinalIgnoreCase));

        var updatedLevel = requiresElevation && Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND
            ? ImpactLevel.REQUIRES_ADMIN_ACTION
            : Impact.Level;
        var updatedRisk = updatedLevel == ImpactLevel.REQUIRES_ADMIN_ACTION && Impact.RiskScore < 5
            ? 5
            : Impact.RiskScore;

        Impact = Impact with
        {
            Level = updatedLevel,
            RiskScore = updatedRisk,
            IsLockedByProcess = Impact.IsLockedByProcess || hasWarnLock || mergedLocks.Length > 0,
            LockingProcesses = mergedLocks,
            RequiresAdmin = requiresElevation
        };
    }

    public static string ComputeDeterministicId(string ruleId, string normalizedTargetPath)
    {
        var input = $"{ruleId.ToLowerInvariant()}|{normalizedTargetPath.ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
