using NexusClean.Application.UseCases;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.Strategies;

/// <summary>
/// Shared defense-in-depth engine used by mutating strategies:
/// - Re-verifies RootSystemFolderGuard and BrowserProfilePreservationGuard right before touching disk.
/// - Never follows NTFS ReparsePoints or touches cloud-only RecallOnDataAccess placeholders.
/// - Permanently deletes cache files (bypassing $Recycle.Bin) while gracefully skipping locked handles.
/// </summary>
internal static class SafeDeletionEngine
{
    private const FileAttributes SkipMask =
        FileAttributes.ReparsePoint |
        FileAttributes.Offline |
        (FileAttributes)0x00040000 |
        (FileAttributes)0x00400000;

    private static readonly RootSystemFolderGuard RootGuard = new();
    private static readonly BrowserProfilePreservationGuard BrowserGuard = new();

    public static bool VerifyDefenseInDepth(StorageFinding finding, out string blockedReasonVi)
    {
        var minimalSnapshot = MinimalGuardSnapshot.Instance;
        var v1 = RootGuard.Inspect(finding, minimalSnapshot);
        if (v1.Severity == GuardSeverity.Block)
        {
            blockedReasonVi = v1.MessageVi;
            return false;
        }

        var v2 = BrowserGuard.Inspect(finding, minimalSnapshot);
        if (v2.Severity == GuardSeverity.Block)
        {
            blockedReasonVi = v2.MessageVi;
            return false;
        }

        blockedReasonVi = string.Empty;
        return true;
    }

    public static CleanupItemResult ProcessDirectoriesContents(
        StorageFinding finding,
        IReadOnlyList<string> directories,
        bool dryRun,
        bool deleteTargetRootDirWhenEmpty,
        CancellationToken ct)
    {
        if (!VerifyDefenseInDepth(finding, out var blockedMsg))
        {
            return new CleanupItemResult(
                finding.Id,
                finding.RuleId,
                finding.TargetPath,
                CleanupActionType.BlockedNoOp,
                CleanupItemStatus.BlockedByGuard,
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                blockedMsg);
        }

        long bytesReclaimed = 0;
        int filesProcessed = 0;
        var skippedLocked = new List<string>();
        var errors = new List<string>();

        foreach (var dir in directories)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                var dirAttr = File.GetAttributes(dir);
                if ((dirAttr & SkipMask) != 0)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            TraverseAndCleanDirectory(
                dir,
                dryRun,
                deleteDirectoryItself: deleteTargetRootDirWhenEmpty,
                ref bytesReclaimed,
                ref filesProcessed,
                skippedLocked,
                errors,
                ct);
        }

        var status = errors.Count > 0 && filesProcessed == 0
            ? CleanupItemStatus.Failed
            : skippedLocked.Count > 0
                ? (filesProcessed > 0 ? CleanupItemStatus.PartiallySucceeded : CleanupItemStatus.SkippedLocked)
                : CleanupItemStatus.Succeeded;

        return new CleanupItemResult(
            finding.Id,
            finding.RuleId,
            finding.TargetPath,
            finding.ActionType,
            status,
            bytesReclaimed,
            filesProcessed,
            skippedLocked,
            errors,
            dryRun
                ? $"[Dry-Run] Dự kiến giải phóng {bytesReclaimed:N0} bytes ({filesProcessed} tệp)."
                : $"Đã dọn {bytesReclaimed:N0} bytes ({filesProcessed} tệp).");
    }

    private static void TraverseAndCleanDirectory(
        string currentDir,
        bool dryRun,
        bool deleteDirectoryItself,
        ref long bytesReclaimed,
        ref int filesProcessed,
        List<string> skippedLocked,
        List<string> errors,
        CancellationToken ct)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(currentDir);
        }
        catch (UnauthorizedAccessException)
        {
            skippedLocked.Add(currentDir);
            return;
        }
        catch (Exception ex)
        {
            errors.Add($"{currentDir}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var leaf = Path.GetFileName(file);
            if (BrowserProfilePreservationGuard.ImmutableProtectedBrowserAssets.Contains(leaf))
            {
                // Hard safety net: never touch a protected credential filename even if misplaced!
                continue;
            }

            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & SkipMask) != 0)
                {
                    continue;
                }

                var fileLen = info.Length;
                if (dryRun)
                {
                    if (IsFileCurrentlyLockedForDelete(file))
                    {
                        skippedLocked.Add(file);
                    }
                    else
                    {
                        bytesReclaimed += fileLen;
                        filesProcessed++;
                    }
                }
                else
                {
                    if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        info.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    File.Delete(file);
                    bytesReclaimed += fileLen;
                    filesProcessed++;
                }
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                skippedLocked.Add(file);
            }
            catch (UnauthorizedAccessException)
            {
                skippedLocked.Add(file);
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(currentDir);
        }
        catch
        {
            return;
        }

        foreach (var sub in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var attr = File.GetAttributes(sub);
                if ((attr & SkipMask) != 0)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            TraverseAndCleanDirectory(
                sub,
                dryRun,
                deleteDirectoryItself: true,
                ref bytesReclaimed,
                ref filesProcessed,
                skippedLocked,
                errors,
                ct);
        }

        if (!dryRun && deleteDirectoryItself)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(currentDir).Any())
                {
                    Directory.Delete(currentDir, recursive: false);
                }
            }
            catch
            {
                // Leave directory in place if an active process still holds a working-directory handle.
            }
        }
    }

    private static bool IsFileCurrentlyLockedForDelete(string filePath)
    {
        try
        {
            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException ex) when (IsSharingOrLockViolation(ex))
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only files are cleared before delete; check whether it's simply ReadOnly attribute.
            try
            {
                var attr = File.GetAttributes(filePath);
                return (attr & FileAttributes.ReadOnly) == 0;
            }
            catch
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33; // ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33)
    }

    private sealed class MinimalGuardSnapshot : ISystemSnapshot
    {
        public static readonly MinimalGuardSnapshot Instance = new();
        public bool IsCurrentProcessElevated => false;
        public IReadOnlySet<string> RunningProcessNames { get; } = new HashSet<string>();
        public IReadOnlyList<string> AdditionalProtectedRoots => Array.Empty<string>();
        public IReadOnlyList<string> AdditionalProtectedFileNames => Array.Empty<string>();
        public IReadOnlyList<VolumeSnapshot> FixedVolumes => Array.Empty<VolumeSnapshot>();
        public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths) => Array.Empty<string>();
    }
}

public sealed class BlockedNoOpStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.BlockedNoOp;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateBlocked(finding));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateBlocked(finding));

    private static CleanupItemResult CreateBlocked(StorageFinding finding) =>
        new(
            finding.Id,
            finding.RuleId,
            finding.TargetPath,
            CleanupActionType.BlockedNoOp,
            CleanupItemStatus.BlockedByGuard,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            "Đã khóa bảo vệ an toàn (BlockedNoOp).");
}

public sealed class FullDirectoryDeleteStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.DeleteContents;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            new[] { finding.TargetPath },
            dryRun: true,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            new[] { finding.TargetPath },
            dryRun: false,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken));
}

public sealed class DeleteUnlockedItemsStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.DeleteUnlockedItems;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            new[] { finding.TargetPath },
            dryRun: true,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            new[] { finding.TargetPath },
            dryRun: false,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken));
}

public sealed class SelectiveSubfolderPurgeStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.SelectiveSubfolderPurge;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(RunSelective(finding, dryRun: true, cancellationToken));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(RunSelective(finding, dryRun: false, cancellationToken));

    private static CleanupItemResult RunSelective(StorageFinding finding, bool dryRun, CancellationToken ct)
    {
        if (finding.SubPathsToClean.Count == 0 ||
            finding.SubPathsToClean.Any(p => !BrowserProfilePreservationGuard.IsAllowedBrowserCachePath(p)))
        {
            return new CleanupItemResult(
                finding.Id,
                finding.RuleId,
                finding.TargetPath,
                CleanupActionType.BlockedNoOp,
                CleanupItemStatus.BlockedByGuard,
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "Chặn an toàn: Đường dẫn con trình duyệt không nằm trong danh mục bộ nhớ đệm cho phép.");
        }

        return SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            finding.SubPathsToClean,
            dryRun,
            deleteTargetRootDirWhenEmpty: false,
            ct);
    }
}

public sealed class SemVerDuplicatePruneStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.SemVerDuplicatePrune;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            finding.SubPathsToClean,
            dryRun: true,
            deleteTargetRootDirWhenEmpty: true,
            cancellationToken));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(
            finding,
            finding.SubPathsToClean,
            dryRun: false,
            deleteTargetRootDirWhenEmpty: true,
            cancellationToken));
}

public sealed class SecondaryDriveRelocateStrategy : ICleanupStrategy
{
    private readonly PlanSecondaryRelocationUseCase _relocateUseCase;

    public CleanupActionType SupportedAction => CleanupActionType.RecommendRelocateToSecondaryDrive;

    public SecondaryDriveRelocateStrategy(PlanSecondaryRelocationUseCase relocateUseCase)
    {
        _relocateUseCase = relocateUseCase ?? throw new ArgumentNullException(nameof(relocateUseCase));
    }

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildAdvisoryResult(finding));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildAdvisoryResult(finding));

    private CleanupItemResult BuildAdvisoryResult(StorageFinding finding)
    {
        var plan = _relocateUseCase.CreatePlan(finding);
        var msg = plan.RecommendedTargetPath is not null
            ? $"Khuyến nghị di dời sang '{plan.RecommendedTargetPath}' thay vì xóa trực tiếp."
            : plan.StepByStepGuideVi.FirstOrDefault() ?? "Khuyến nghị di chuyển dữ liệu sang ổ phụ.";

        return new CleanupItemResult(
            finding.Id,
            finding.RuleId,
            finding.TargetPath,
            SupportedAction,
            CleanupItemStatus.RelocationAdvised,
            BytesReclaimed: 0,
            FilesDeletedCount: 0,
            SkippedLockedPaths: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            MessageVi: msg);
    }
}

public sealed class ResizeShadowStorageAdminStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.ResizeShadowStorageAdmin;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CleanupItemResult(
            finding.Id,
            finding.RuleId,
            finding.TargetPath,
            SupportedAction,
            CleanupItemStatus.AdminQueued,
            BytesReclaimed: 0,
            FilesDeletedCount: 0,
            SkippedLockedPaths: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            MessageVi: "[Hàng đợi Admin] Sẵn sàng chạy 'vssadmin resize shadowstorage /for=C: /on=C: /maxsize=5GB'."));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SimulateAsync(finding, cancellationToken);
}

public sealed class RelocateWslAdminStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.RelocateOrUnregisterAdmin;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CleanupItemResult(
            finding.Id,
            finding.RuleId,
            finding.TargetPath,
            SupportedAction,
            CleanupItemStatus.AdminQueued,
            BytesReclaimed: 0,
            FilesDeletedCount: 0,
            SkippedLockedPaths: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            MessageVi: "[Hàng đợi Admin] Sẵn sàng di chuyển đĩa ảo WSL sang ổ phụ qua 'wsl --manage <Distro> --move'."));

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SimulateAsync(finding, cancellationToken);
}
