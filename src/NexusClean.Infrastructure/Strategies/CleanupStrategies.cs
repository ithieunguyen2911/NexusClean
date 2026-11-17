using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NexusClean.Application.UseCases;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.Strategies;

/// <summary>
/// Shared defense-in-depth engine used by mutating strategies:
/// - Re-verifies RootSystemFolderGuard and BrowserProfilePreservationGuard right before touching disk.
/// - Uses single-pass WIN32_FIND_DATA DirectoryInfo enumeration skipping ReparsePoints and cloud placeholders.
/// - Uses Win32 CreateFileW(DELETE = 0x00010000) in Dry-Run (avoiding FILE_READ_DATA/FILE_WRITE_DATA Windows Defender storms)
///   and exception-free Win32 DeleteFileW in Execute mode so thousands of locked %TEMP% files never cause CLR exception stalls.
/// </summary>
internal static class SafeDeletionEngine
{
    private const FileAttributes SkipMask =
        FileAttributes.ReparsePoint |
        FileAttributes.Offline |
        (FileAttributes)0x00040000 |
        (FileAttributes)0x00400000;

    private const uint AccessDeleteOnly = 0x00010000;
    private const uint ShareReadWriteDelete = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;

    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private static readonly EnumerationOptions NonRecursiveOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = SkipMask
    };

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

    public static Task<CleanupItemResult> ProcessDirectoriesContentsAsync(
        StorageFinding finding,
        IReadOnlyList<string> directories,
        bool dryRun,
        bool deleteTargetRootDirWhenEmpty,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(
            () => ProcessDirectoriesContents(finding, directories, dryRun, deleteTargetRootDirWhenEmpty, ct),
            ct);
    }

    public static CleanupItemResult ProcessDirectoriesContents(
        StorageFinding finding,
        IReadOnlyList<string> directories,
        bool dryRun,
        bool deleteTargetRootDirWhenEmpty,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

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

        // For unlocked cold package caches (uv, pip, npm, NuGet, Playwright), once the initial sample of files
        // confirms 0 locked handles, skip redundant per-file kernel handle probes in Dry-Run across 100k+ blobs.
        var canFastPathUnlockedColdCacheInDryRun =
            dryRun &&
            !finding.Impact.IsLockedByProcess &&
            string.Equals(finding.Category, "Developer Caches", StringComparison.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir))
            {
                continue;
            }

            DirectoryInfo rootInfo;
            try
            {
                rootInfo = new DirectoryInfo(dir);
                if ((rootInfo.Attributes & SkipMask) != 0)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            TraverseAndCleanDirectory(
                rootInfo,
                dryRun,
                deleteDirectoryItself: deleteTargetRootDirWhenEmpty,
                canFastPathUnlockedColdCacheInDryRun,
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
        DirectoryInfo currentDir,
        bool dryRun,
        bool deleteDirectoryItself,
        bool canFastPathColdCacheInDryRun,
        ref long bytesReclaimed,
        ref int filesProcessed,
        List<string> skippedLocked,
        List<string> errors,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<FileInfo> files;
        try
        {
            files = currentDir.EnumerateFiles("*", NonRecursiveOptions);
        }
        catch (UnauthorizedAccessException)
        {
            skippedLocked.Add(currentDir.FullName);
            return;
        }
        catch (Exception ex)
        {
            errors.Add($"{currentDir.FullName}: {ex.Message}");
            return;
        }

        foreach (var info in files)
        {
            if ((filesProcessed & 63) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var leaf = info.Name;
            if (BrowserProfilePreservationGuard.ImmutableProtectedBrowserAssets.Contains(leaf))
            {
                continue;
            }

            try
            {
                var attr = info.Attributes;
                if ((attr & SkipMask) != 0)
                {
                    continue;
                }

                var fileLen = info.Length;
                var fullPath = info.FullName;

                if (dryRun)
                {
                    var skipHandleProbe = canFastPathColdCacheInDryRun && filesProcessed >= 128 && skippedLocked.Count == 0;
                    if (!skipHandleProbe && IsFileLockedForDeleteFast(fullPath, attr))
                    {
                        skippedLocked.Add(fullPath);
                    }
                    else
                    {
                        bytesReclaimed += fileLen;
                        filesProcessed++;
                    }
                }
                else
                {
                    if ((attr & FileAttributes.ReadOnly) != 0)
                    {
                        try
                        {
                            File.SetAttributes(fullPath, attr & ~FileAttributes.ReadOnly);
                        }
                        catch
                        {
                            skippedLocked.Add(fullPath);
                            continue;
                        }
                    }

                    if (TryDeleteFileFast(fullPath, out var isLockedOrDenied, out var win32Err))
                    {
                        bytesReclaimed += fileLen;
                        filesProcessed++;
                    }
                    else if (isLockedOrDenied)
                    {
                        skippedLocked.Add(fullPath);
                    }
                    else if (win32Err != 0)
                    {
                        errors.Add($"{fullPath}: Win32 error {win32Err}");
                    }
                }
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                skippedLocked.Add(info.FullName);
            }
            catch (UnauthorizedAccessException)
            {
                skippedLocked.Add(info.FullName);
            }
            catch (Exception ex)
            {
                errors.Add($"{info.FullName}: {ex.Message}");
            }
        }

        IEnumerable<DirectoryInfo> subDirs;
        try
        {
            subDirs = currentDir.EnumerateDirectories("*", NonRecursiveOptions);
        }
        catch
        {
            return;
        }

        foreach (var sub in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            TraverseAndCleanDirectory(
                sub,
                dryRun,
                deleteDirectoryItself: true,
                canFastPathColdCacheInDryRun,
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
                if (!Directory.EnumerateFileSystemEntries(currentDir.FullName).Any())
                {
                    Directory.Delete(currentDir.FullName, recursive: false);
                }
            }
            catch
            {
                // Leave directory in place if an active process still holds a working-directory handle.
            }
        }
    }

    /// <summary>
    /// Probes whether a file can be opened with DELETE (0x00010000) disposition access only.
    /// Does NOT request FILE_READ_DATA or FILE_WRITE_DATA, avoiding Windows Defender stream inspection.
    /// </summary>
    private static bool IsFileLockedForDeleteFast(string filePath, FileAttributes attributes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // Read-only files can still be locked by an exclusive FileShare.None handle; if not ReadOnly, probe DELETE directly.
        if ((attributes & FileAttributes.ReadOnly) == 0)
        {
            using var handle = CreateFileW(
                filePath,
                AccessDeleteOnly,
                ShareReadWriteDelete,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                return false;
            }

            var err = Marshal.GetLastWin32Error();
            if (err is ErrorFileNotFound or ErrorPathNotFound)
            {
                return false;
            }

            return err is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;
        }

        // For ReadOnly files, check if another process holds a sharing lock via READ_ATTRIBUTES (0x80) with ShareReadWriteDelete.
        using var roHandle = CreateFileW(
            filePath,
            0x00000080, // FILE_READ_ATTRIBUTES only
            ShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (!roHandle.IsInvalid)
        {
            return false;
        }

        var roErr = Marshal.GetLastWin32Error();
        return roErr is ErrorSharingViolation or ErrorLockViolation;
    }

    private static bool TryDeleteFileFast(string filePath, out bool isLockedOrDenied, out int win32Error)
    {
        isLockedOrDenied = false;
        win32Error = 0;

        if (!OperatingSystem.IsWindows())
        {
            File.Delete(filePath);
            return true;
        }

        if (DeleteFileW(filePath))
        {
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        if (win32Error is ErrorFileNotFound or ErrorPathNotFound)
        {
            // Ephemeral temp file already removed by the owning application
            win32Error = 0;
            return false;
        }

        if (win32Error is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation)
        {
            isLockedOrDenied = true;
            return false;
        }

        return false;
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is ErrorSharingViolation or ErrorLockViolation;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string lpFileName);

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
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            new[] { finding.TargetPath },
            dryRun: true,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken);

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            new[] { finding.TargetPath },
            dryRun: false,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken);
}

public sealed class DeleteUnlockedItemsStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.DeleteUnlockedItems;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            new[] { finding.TargetPath },
            dryRun: true,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken);

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            new[] { finding.TargetPath },
            dryRun: false,
            deleteTargetRootDirWhenEmpty: false,
            cancellationToken);
}

public sealed class SelectiveSubfolderPurgeStrategy : ICleanupStrategy
{
    public CleanupActionType SupportedAction => CleanupActionType.SelectiveSubfolderPurge;

    public Task<CleanupItemResult> SimulateAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        RunSelectiveAsync(finding, dryRun: true, cancellationToken);

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        RunSelectiveAsync(finding, dryRun: false, cancellationToken);

    private static Task<CleanupItemResult> RunSelectiveAsync(StorageFinding finding, bool dryRun, CancellationToken ct)
    {
        if (finding.SubPathsToClean.Count == 0 ||
            finding.SubPathsToClean.Any(p => !BrowserProfilePreservationGuard.IsAllowedBrowserCachePath(p)))
        {
            return Task.FromResult(new CleanupItemResult(
                finding.Id,
                finding.RuleId,
                finding.TargetPath,
                CleanupActionType.BlockedNoOp,
                CleanupItemStatus.BlockedByGuard,
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "Chặn an toàn: Đường dẫn con trình duyệt không nằm trong danh mục bộ nhớ đệm cho phép."));
        }

        return SafeDeletionEngine.ProcessDirectoriesContentsAsync(
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
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            finding.SubPathsToClean,
            dryRun: true,
            deleteTargetRootDirWhenEmpty: true,
            cancellationToken);

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        SafeDeletionEngine.ProcessDirectoriesContentsAsync(
            finding,
            finding.SubPathsToClean,
            dryRun: false,
            deleteTargetRootDirWhenEmpty: true,
            cancellationToken);
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
        Task.Run(() => BuildAdvisoryResult(finding), cancellationToken);

    public Task<CleanupItemResult> ExecuteAsync(StorageFinding finding, CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildAdvisoryResult(finding), cancellationToken);

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
