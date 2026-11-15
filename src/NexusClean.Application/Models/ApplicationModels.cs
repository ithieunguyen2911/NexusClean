using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.Models;

public sealed record SignatureCatalogDocument(
    string Version,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> ProtectedCriticalFileNames,
    IReadOnlyList<string> ProtectedRootPaths,
    IReadOnlyList<SignatureRuleDefinition> Rules);

public sealed record SignatureRuleDefinition(
    string RuleId,
    string DisplayName,
    string Category,
    string ActionType,
    string ImpactLevel,
    int RiskScore,
    string SummaryVi,
    string SideEffectIfDeleted,
    IReadOnlyList<string> PreservedAssets,
    IReadOnlyList<string>? TargetPaths = null,
    IReadOnlyList<string>? BasePaths = null,
    string? ProfileFolderRegex = null,
    IReadOnlyList<string>? AllowedCacheSubfolders = null,
    string? ExtensionRoot = null,
    bool PruneOlderDuplicateVersions = false,
    bool PruneDotUuidTempFolders = false,
    bool RequiresAdmin = false,
    IReadOnlyList<string>? LockingProcesses = null,
    string? RecommendedAction = null);

public sealed record CatalogValidationIssue(
    string? RuleId,
    string Code,
    string MessageVi);

public sealed record CatalogValidationReport(
    string SourceFilePath,
    bool IsValid,
    int ValidRuleCount,
    IReadOnlyList<CatalogValidationIssue> Issues);

public sealed record DriveAnalysisReport(
    Guid ScanId,
    string DriveName,
    long TotalCapacityBytes,
    long FreeBytesBeforeScan,
    DateTime StartedAtUtc,
    long DurationMs,
    IReadOnlyList<StorageFinding> Findings)
{
    public long SafeReclaimableBytes => Findings
        .Where(f => f.Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND && !f.Impact.RequiresAdmin)
        .Sum(f => f.SizeInBytes);

    public long RelocatableBytes => Findings
        .Where(f => f.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE)
        .Sum(f => f.SizeInBytes);

    public long AdminReclaimableBytes => Findings
        .Where(f => f.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION || f.Impact.RequiresAdmin)
        .Sum(f => f.SizeInBytes);

    public long ProtectedBlockedBytes => Findings
        .Where(f => f.Impact.Level == ImpactLevel.PROTECTED_NEVER_DELETE)
        .Sum(f => f.SizeInBytes);
}

public sealed record CleanupBatchReport(
    Guid BatchId,
    bool IsDryRun,
    bool IsElevatedBatch,
    DateTime ExecutedAtUtc,
    long DurationMs,
    IReadOnlyList<CleanupItemResult> ItemResults)
{
    public long TotalBytesReclaimed => ItemResults.Sum(r => r.BytesReclaimed);
    public int TotalFilesProcessed => ItemResults.Sum(r => r.FilesDeletedCount);
    public int TotalSkippedLockedCount => ItemResults.Sum(r => r.SkippedLockedPaths.Count);
}

public enum AdminOperationKind
{
    ResizeVssShadowStorage = 0,
    MoveWslDistribution = 1
}

public sealed record AdminMaintenanceOperation(
    string FindingId,
    AdminOperationKind OperationKind,
    string? Volume = null,
    int? MaxShadowSizeGb = null,
    string? DistroName = null,
    string? TargetDirectory = null);

public sealed record AdminMaintenanceRequest(
    Guid RequestId,
    DateTime CreatedAtUtc,
    IReadOnlyList<AdminMaintenanceOperation> Operations);

public sealed record AdminOperationOutcome(
    string FindingId,
    AdminOperationKind OperationKind,
    bool Succeeded,
    int ExitCode,
    string MessageVi,
    string StandardOutput = "",
    string StandardError = "");

public sealed record AdminMaintenanceResult(
    Guid RequestId,
    DateTime CompletedAtUtc,
    bool WasCancelledByUser,
    IReadOnlyList<AdminOperationOutcome> Outcomes);

public interface ISignatureRuleCatalogProvider
{
    CatalogValidationReport ValidateCatalogFile(string filePath);
    (SignatureCatalogDocument Catalog, IReadOnlyList<IDetectionRule> Rules, CatalogValidationReport Validation) LoadRules(string? customFilePath = null);
}

public interface IAuditLogRepository
{
    string LogFilePath { get; }
    Task AppendBatchReportAsync(CleanupBatchReport report, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupBatchReport>> ReadRecentReportsAsync(int maxCount = 20, CancellationToken cancellationToken = default);
}

public interface ISystemSnapshotProvider
{
    ISystemSnapshot CaptureSnapshot(
        IReadOnlyList<string>? additionalProtectedRoots = null,
        IReadOnlyList<string>? additionalProtectedFileNames = null);
}

public interface IElevatedAdminExecutor
{
    IReadOnlyList<string> ValidateRequest(AdminMaintenanceRequest request);
    Task<AdminMaintenanceResult> ExecuteElevatedAsync(
        AdminMaintenanceRequest request,
        CancellationToken cancellationToken = default);
    Task<AdminMaintenanceResult> ExecuteInCurrentElevatedProcessAsync(
        AdminMaintenanceRequest request,
        CancellationToken cancellationToken = default);
}
