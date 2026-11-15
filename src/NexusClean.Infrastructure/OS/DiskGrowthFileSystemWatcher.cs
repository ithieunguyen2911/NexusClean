using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.OS;

/// <summary>
/// Observer watching high-churn directories (%TEMP%, ZaloData\media, *-updater) and system drive free space.
/// </summary>
public sealed class DiskGrowthFileSystemWatcher : IDiskGrowthWatcher
{
    private readonly IDirectorySizer _directorySizer;
    private readonly Func<string, long>? _freeSpaceResolver;
    private readonly Dictionary<string, long> _baselineBytes = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public event EventHandler<DiskGrowthAlert>? GrowthAlertRaised;

    public DiskGrowthFileSystemWatcher(
        IDirectorySizer directorySizer,
        Func<string, long>? freeSpaceResolver = null)
    {
        _directorySizer = directorySizer ?? throw new ArgumentNullException(nameof(directorySizer));
        _freeSpaceResolver = freeSpaceResolver;
    }

    public async Task StartAsync(WatcherOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _baselineBytes.Clear();
        foreach (var rawPath in options.WatchedPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(rawPath);
            if (Directory.Exists(expanded))
            {
                var snap = await _directorySizer.ComputeSizeAsync(expanded, cancellationToken).ConfigureAwait(false);
                _baselineBytes[expanded] = snap.SizeInBytes;
            }
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interval = options.PollingInterval <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(5)
            : options.PollingInterval;

        _loopTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(_loopCts.Token).ConfigureAwait(false))
            {
                await EvaluateTickAsync(options, _loopCts.Token).ConfigureAwait(false);
            }
        }, _loopCts.Token);
    }

    public async Task EvaluateTickAsync(WatcherOptions options, CancellationToken cancellationToken = default)
    {
        var freeBytes = ResolveFreeSpace(options.SystemDriveName);
        if (freeBytes > 0 && freeBytes < options.MinFreeDriveBytesThreshold)
        {
            GrowthAlertRaised?.Invoke(this, new DiskGrowthAlert(
                Guid.NewGuid(),
                DateTime.UtcNow,
                options.SystemDriveName,
                "system.drive.low_space",
                PreviousBytes: 0,
                CurrentBytes: 0,
                DeltaBytes: 0,
                DriveFreeBytes: freeBytes,
                RecommendedActionVi: $"Dung lượng trống ổ {options.SystemDriveName} đã giảm xuống dưới ngưỡng an toàn ({freeBytes / (1024d * 1024d * 1024d):F2} GB). Khuyến nghị mở NexusClean quét và chạy Dry-Run."));
        }

        foreach (var rawPath in options.WatchedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expanded = Environment.ExpandEnvironmentVariables(rawPath);
            if (!Directory.Exists(expanded))
            {
                continue;
            }

            var currentSnap = await _directorySizer.ComputeSizeAsync(expanded, cancellationToken).ConfigureAwait(false);
            if (!_baselineBytes.TryGetValue(expanded, out var previous))
            {
                _baselineBytes[expanded] = currentSnap.SizeInBytes;
                continue;
            }

            var delta = currentSnap.SizeInBytes - previous;
            if (delta >= options.FolderGrowthDeltaBytesThreshold)
            {
                _baselineBytes[expanded] = currentSnap.SizeInBytes;
                var recommendation = expanded.Contains("ZaloData", StringComparison.OrdinalIgnoreCase)
                    ? "Thư mục ZaloData\\media đang tăng trưởng nhanh — khuyến nghị di chuyển sang ổ phụ D:\\ZaloData."
                    : $"Thư mục '{expanded}' vừa phình thêm +{delta / (1024d * 1024d):F1} MB — khuyến nghị kiểm tra và dọn bộ đệm.";

                GrowthAlertRaised?.Invoke(this, new DiskGrowthAlert(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    expanded,
                    null,
                    PreviousBytes: previous,
                    CurrentBytes: currentSnap.SizeInBytes,
                    DeltaBytes: delta,
                    DriveFreeBytes: freeBytes,
                    RecommendedActionVi: recommendation));
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_loopCts is null)
        {
            return;
        }

        await _loopCts.CancelAsync().ConfigureAwait(false);
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private long ResolveFreeSpace(string driveName)
    {
        if (_freeSpaceResolver is not null)
        {
            return _freeSpaceResolver(driveName);
        }

        try
        {
            var di = new DriveInfo(driveName);
            return di.IsReady ? di.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
    }
}
