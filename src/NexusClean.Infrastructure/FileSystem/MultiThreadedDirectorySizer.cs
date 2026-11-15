using System.IO.Enumeration;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.FileSystem;

/// <summary>
/// High-throughput non-hydrating directory &amp; file sizer using Win32 FileSystemEnumerable.
/// Skips NTFS junctions/reparse loops and OneDrive cloud-only placeholders (RecallOnDataAccess / RecallOnOpen).
/// </summary>
public sealed class MultiThreadedDirectorySizer : IDirectorySizer
{
    private const FileAttributes CloudRecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes CloudRecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes CloudPinnedUnpinnedMask = FileAttributes.Offline | CloudRecallOnOpen | CloudRecallOnDataAccess;

    private readonly int _maxDegreeOfParallelism;

    public MultiThreadedDirectorySizer(int maxDegreeOfParallelism = 0)
    {
        _maxDegreeOfParallelism = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Math.Clamp(Environment.ProcessorCount, 2, 8);
    }

    public async Task<DirectorySizeSnapshot> ComputeSizeAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new DirectorySizeSnapshot(0, 0, DateTime.UtcNow);
        }

        var normalized = Path.GetFullPath(targetPath);

        // Handle single-file targets (including locked OS files like C:\pagefile.sys or C:\hiberfil.sys)
        // via parent-directory metadata enumeration so we never open a file handle!
        if (!Directory.Exists(normalized))
        {
            return TrySizeSingleFileWithoutOpening(normalized);
        }

        try
        {
            var rootAttrs = File.GetAttributes(normalized);
            if ((rootAttrs & FileAttributes.ReparsePoint) != 0)
            {
                return new DirectorySizeSnapshot(0, 0, DateTime.UtcNow, SkippedReparseOrCloudCount: 1);
            }
        }
        catch
        {
            return new DirectorySizeSnapshot(0, 0, DateTime.UtcNow);
        }

        long totalBytes = 0;
        int totalFiles = 0;
        int skippedSpecial = 0;
        long latestTicksUtc = DateTime.UnixEpoch.Ticks;

        // Enumerate top-level files + immediate child directories, then size child subtrees in parallel.
        var childDirs = new List<string>();
        SizeSingleDirectoryNonRecursive(
            normalized,
            childDirs,
            ref totalBytes,
            ref totalFiles,
            ref skippedSpecial,
            ref latestTicksUtc,
            cancellationToken);

        if (childDirs.Count > 0)
        {
            await Parallel.ForEachAsync(
                childDirs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                (subDir, ct) =>
                {
                    long localBytes = 0;
                    int localFiles = 0;
                    int localSkipped = 0;
                    long localMaxTicks = 0;

                    var stack = new Stack<string>();
                    stack.Push(subDir);

                    while (stack.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        var current = stack.Pop();
                        var nextChildren = new List<string>();
                        SizeSingleDirectoryNonRecursive(
                            current,
                            nextChildren,
                            ref localBytes,
                            ref localFiles,
                            ref localSkipped,
                            ref localMaxTicks,
                            ct);

                        for (var i = 0; i < nextChildren.Count; i++)
                        {
                            stack.Push(nextChildren[i]);
                        }
                    }

                    Interlocked.Add(ref totalBytes, localBytes);
                    Interlocked.Add(ref totalFiles, localFiles);
                    Interlocked.Add(ref skippedSpecial, localSkipped);
                    UpdateMaxInterlocked(ref latestTicksUtc, localMaxTicks);
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
        }

        var lastMod = latestTicksUtc <= DateTime.UnixEpoch.Ticks
            ? DateTime.UtcNow
            : new DateTime(latestTicksUtc, DateTimeKind.Utc);

        return new DirectorySizeSnapshot(totalBytes, totalFiles, lastMod, skippedSpecial);
    }

    private static void SizeSingleDirectoryNonRecursive(
        string directoryPath,
        List<string> discoveredSubdirectories,
        ref long totalBytes,
        ref int totalFiles,
        ref int skippedCount,
        ref long latestTicksUtc,
        CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0 // Inspect attributes explicitly so we can count skipped ReparsePoints & Cloud placeholders!
        };

        try
        {
            var enumerable = new FileSystemEnumerable<(bool IsDir, bool IsSkipped, long Length, long LastWriteTicks, string? SubDirPath)>(
                directoryPath,
                (ref FileSystemEntry entry) =>
                {
                    var attrs = entry.Attributes;
                    if ((attrs & FileAttributes.ReparsePoint) != 0 ||
                        (attrs & CloudPinnedUnpinnedMask) != 0)
                    {
                        return (entry.IsDirectory, true, 0L, 0L, null);
                    }

                    if (entry.IsDirectory)
                    {
                        return (true, false, 0L, entry.LastWriteTimeUtc.UtcTicks, entry.ToFullPath());
                    }

                    return (false, false, entry.Length, entry.LastWriteTimeUtc.UtcTicks, null);
                },
                options);

            foreach (var item in enumerable)
            {
                ct.ThrowIfCancellationRequested();
                if (item.IsSkipped)
                {
                    skippedCount++;
                    continue;
                }

                if (item.LastWriteTicks > latestTicksUtc)
                {
                    latestTicksUtc = item.LastWriteTicks;
                }

                if (item.IsDir)
                {
                    if (item.SubDirPath is not null)
                    {
                        discoveredSubdirectories.Add(item.SubDirPath);
                    }
                }
                else
                {
                    totalBytes += item.Length;
                    totalFiles++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Inaccessible OS folder (e.g., C:\System Volume Information in non-admin mode): return 0 cleanly.
        }
    }

    private static DirectorySizeSnapshot TrySizeSingleFileWithoutOpening(string filePath)
    {
        var parent = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(fileName) || !Directory.Exists(parent))
        {
            return new DirectorySizeSnapshot(0, 0, DateTime.UtcNow);
        }

        try
        {
            var enumerable = new FileSystemEnumerable<(long Length, DateTime LastWriteUtc)>(
                parent,
                (ref FileSystemEntry entry) => (entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    MatchCasing = MatchCasing.CaseInsensitive
                })
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory &&
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    entry.FileName.Equals(fileName.AsSpan(), StringComparison.OrdinalIgnoreCase)
            };

            foreach (var match in enumerable)
            {
                return new DirectorySizeSnapshot(match.Length, 1, match.LastWriteUtc);
            }
        }
        catch
        {
            // Ignore inaccessible parent.
        }

        return new DirectorySizeSnapshot(0, 0, DateTime.UtcNow);
    }

    private static void UpdateMaxInterlocked(ref long target, long candidate)
    {
        long current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            var prev = Interlocked.CompareExchange(ref target, candidate, current);
            if (prev == current)
            {
                break;
            }

            current = prev;
        }
    }
}
