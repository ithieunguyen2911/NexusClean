using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NexusClean.Application.Orchestration;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;
using NexusClean.Infrastructure;
using NexusClean.Infrastructure.Strategies;
using NexusClean.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NexusClean.Infrastructure.Tests.Strategies;

public sealed class HighVolumeAsyncCleanupTests
{
    [Fact]
    public async Task CleanAsync_YieldsImmediatelyAndProcessesHighFileCountWithLockedFileGracefully()
    {
        using var sandbox = new TempSandboxFixture();
        var cacheRoot = sandbox.CreateDirectory(@"Users\Mock\AppData\Local\Temp");

        // Create 1,200 small cache files across 12 subfolders + 1 locked file + 1 read-only file
        long expectedUnlockedBytes = 0;
        for (var d = 0; d < 12; d++)
        {
            var subRel = $@"Users\Mock\AppData\Local\Temp\batch_{d:D2}";
            sandbox.CreateDirectory(subRel);
            for (var f = 0; f < 100; f++)
            {
                sandbox.CreateFileWithSize($@"{subRel}\item_{f:D3}.tmp", 256);
                expectedUnlockedBytes += 256;
            }
        }

        var readOnlyFile = sandbox.CreateFileWithSize(@"Users\Mock\AppData\Local\Temp\readonly_pack.idx", 512);
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
        expectedUnlockedBytes += 512;

        var lockedFile = sandbox.CreateFileWithSize(@"Users\Mock\AppData\Local\Temp\active_session.lock", 1024);
        await using var heldLock = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var finding = new StorageFinding(
            "system.temp.directories",
            "High Volume Temp Sandbox",
            "System Temp",
            cacheRoot,
            expectedUnlockedBytes + 1024,
            1202,
            DateTime.UtcNow,
            CleanupActionType.DeleteUnlockedItems,
            new ImpactAssessment(
                ImpactLevel.SAFE_CACHE_TIER1,
                1,
                "Thư mục tệp tạm kiểm thử khối lượng lớn.",
                "Tự động bỏ qua tệp đang mở.",
                new[] { "Tệp đang khóa" }));

        var auditLogPath = Path.Combine(sandbox.RootPath, "audit", "audit.jsonl");
        var rulesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "rules", "default-signature-rules.json"));

        var services = new ServiceCollection();
        services.AddNexusCleanCore(customCatalogPath: rulesPath, customAuditLogPath: auditLogPath);
        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<DiskAnalysisOrchestrator>();

        var progressUpdates = new List<CleanupProgress>();
        var progress = new SynchronousProgress<CleanupProgress>(p => progressUpdates.Add(p));

        // 1. Measure synchronous caller-thread time before CleanAsync yields!
        var callSw = Stopwatch.StartNew();
        var dryRunTask = orchestrator.CleanAsync(new[] { finding }, dryRun: true, progress: progress);
        var callerSyncElapsedMs = callSw.ElapsedMilliseconds;

        // Caller thread MUST receive the Task almost instantaneously (< 50 ms) so UI never freezes
        Assert.True(callerSyncElapsedMs < 50, $"CleanAsync blocked caller thread for {callerSyncElapsedMs} ms");

        var dryRunReport = await dryRunTask;
        Assert.Equal(expectedUnlockedBytes, dryRunReport.TotalBytesReclaimed);
        Assert.Equal(1201, dryRunReport.TotalFilesProcessed);
        Assert.Single(dryRunReport.ItemResults[0].SkippedLockedPaths);
        // Must emit multiple intra-folder incremental progress updates BEFORE completion (SC-UX-001)
        Assert.True(progressUpdates.Count >= 4, $"Expected >= 4 intra-folder updates, got {progressUpdates.Count}");
        Assert.Contains(progressUpdates, p => p.CompletedItems == 0 && p.CumulativeFilesProcessed > 0);
        Assert.All(progressUpdates, p => Assert.Equal(expectedUnlockedBytes + 1024, p.TotalTargetBytes));

        // 2. Execute actual cleanup and verify locked file is skipped while 1,201 unlocked + read-only files are removed
        var execReport = await orchestrator.CleanAsync(new[] { finding }, dryRun: false, progress: progress);
        Assert.Equal(expectedUnlockedBytes, execReport.TotalBytesReclaimed);
        Assert.Equal(1201, execReport.TotalFilesProcessed);
        Assert.Single(execReport.ItemResults[0].SkippedLockedPaths);
        Assert.True(File.Exists(lockedFile));
        Assert.False(File.Exists(readOnlyFile));
    }

    [Fact]
    public async Task DeleteUnlockedItemsStrategy_RespectsPreCancelledTokenImmediately()
    {
        using var sandbox = new TempSandboxFixture();
        var tempDir = sandbox.CreateDirectory("cancel_test");
        sandbox.CreateFileWithSize(@"cancel_test\f1.tmp", 512);

        var finding = new StorageFinding(
            "system.temp.directories",
            "Cancel Temp Test",
            "System Temp",
            tempDir,
            512,
            1,
            DateTime.UtcNow,
            CleanupActionType.DeleteUnlockedItems,
            new ImpactAssessment(
                ImpactLevel.SAFE_CACHE_TIER1,
                1,
                "Thư mục kiểm thử hủy tác vụ.",
                "Không ảnh hưởng dữ liệu.",
                new[] { "Dữ liệu đang mở" }));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var strategy = new DeleteUnlockedItemsStrategy();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.ExecuteAsync(finding, cts.Token));
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
