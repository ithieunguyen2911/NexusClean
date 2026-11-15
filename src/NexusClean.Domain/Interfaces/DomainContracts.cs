using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;

namespace NexusClean.Domain.Interfaces;

public sealed record DirectorySizeSnapshot(
    long SizeInBytes,
    int FileCount,
    DateTime LastModifiedUtc,
    int SkippedReparseOrCloudCount = 0);

public sealed record VolumeSnapshot(
    string DriveName,
    long TotalCapacityBytes,
    long AvailableFreeBytes,
    string DriveFormat = "NTFS",
    bool IsReady = true);

public sealed record ScanProgress(
    string CurrentRuleId,
    string CurrentAreaDescription,
    int CompletedRules,
    int TotalRules,
    long CumulativeBytesDiscovered);

public sealed record CleanupItemResult(
    string FindingId,
    string RuleId,
    string TargetPath,
    CleanupActionType ActionExecuted,
    CleanupItemStatus Status,
    long BytesReclaimed,
    int FilesDeletedCount,
    IReadOnlyList<string> SkippedLockedPaths,
    IReadOnlyList<string> Errors,
    string? MessageVi = null);

public sealed record DiskGrowthAlert(
    Guid AlertId,
    DateTime ObservedAtUtc,
    string TargetPath,
    string? RuleId,
    long PreviousBytes,
    long CurrentBytes,
    long DeltaBytes,
    long DriveFreeBytes,
    string RecommendedActionVi);

public sealed record WatcherOptions(
    string SystemDriveName,
    IReadOnlyList<string> WatchedPaths,
    long MinFreeDriveBytesThreshold = 15L * 1024 * 1024 * 1024,
    long FolderGrowthDeltaBytesThreshold = 2L * 1024 * 1024 * 1024,
    TimeSpan PollingInterval = default);

public interface IScanContext
{
    string TargetDriveRoot { get; }
    IDirectorySizer DirectorySizer { get; }
    IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
    string ExpandPath(string rawTemplatePath);
}

public interface ISystemSnapshot
{
    bool IsCurrentProcessElevated { get; }
    IReadOnlySet<string> RunningProcessNames { get; }
    IReadOnlyList<string> AdditionalProtectedRoots { get; }
    IReadOnlyList<string> AdditionalProtectedFileNames { get; }
    IReadOnlyList<VolumeSnapshot> FixedVolumes { get; }
    IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths);
}

public interface IDirectorySizer
{
    Task<DirectorySizeSnapshot> ComputeSizeAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}

public interface IDetectionRule
{
    string RuleId { get; }
    string Category { get; }
    Task<IReadOnlyList<StorageFinding>> DetectAsync(
        IScanContext context,
        CancellationToken cancellationToken = default);
}

public interface ISafetyGuard
{
    string GuardName { get; }
    int Order { get; }
    GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot);
}

public interface ICleanupStrategy
{
    CleanupActionType SupportedAction { get; }
    Task<CleanupItemResult> SimulateAsync(
        StorageFinding finding,
        CancellationToken cancellationToken = default);
    Task<CleanupItemResult> ExecuteAsync(
        StorageFinding finding,
        CancellationToken cancellationToken = default);
}

public interface IProcessLockInspector
{
    IReadOnlySet<string> GetRunningProcessNames();
    IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> targetPaths);
}

public interface IDiskGrowthWatcher : IAsyncDisposable
{
    event EventHandler<DiskGrowthAlert>? GrowthAlertRaised;
    Task StartAsync(WatcherOptions options, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
