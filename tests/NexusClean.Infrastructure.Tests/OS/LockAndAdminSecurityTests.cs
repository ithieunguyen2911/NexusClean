using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Infrastructure.OS;
using NexusClean.Infrastructure.Strategies;
using NexusClean.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NexusClean.Infrastructure.Tests.OS;

public sealed class LockAndAdminSecurityTests
{
    [Fact]
    public async Task WindowsProcessInspectorAndDeleteUnlockedStrategy_DetectLiveLockAndSkipGracefullyWithoutHanging()
    {
        using var sandbox = new TempSandboxFixture();
        var tempDir = sandbox.CreateDirectory("live_temp");
        var lockedFile = sandbox.CreateFileWithSize(@"live_temp\locked.tmp", 4096);
        var unlockedFile = sandbox.CreateFileWithSize(@"live_temp\unlocked.tmp", 8192);

        await using var exclusiveStream = new FileStream(
            lockedFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var inspector = new WindowsProcessInspector();
        var lockers = inspector.GetLockingProcesses(new[] { lockedFile });

        // Restart Manager on Windows identifies our active test host process!
        if (OperatingSystem.IsWindows())
        {
            Assert.NotEmpty(lockers);
        }

        var finding = new StorageFinding(
            "system.temp.directories",
            "Live Temp Test",
            "System Temp",
            tempDir,
            4096 + 8192,
            2,
            DateTime.UtcNow,
            CleanupActionType.DeleteUnlockedItems,
            new ImpactAssessment(
                ImpactLevel.SAFE_CACHE_TIER1,
                1,
                "Tệp tạm kiểm thử khóa tiến trình.",
                "Tự động bỏ qua tệp đang mở.",
                new[] { "Tệp đang khóa" }));

        var strategy = new DeleteUnlockedItemsStrategy();
        var sim = await strategy.SimulateAsync(finding);
        Assert.Equal(8192, sim.BytesReclaimed);
        Assert.Single(sim.SkippedLockedPaths);

        var exec = await strategy.ExecuteAsync(finding);
        Assert.Equal(CleanupItemStatus.PartiallySucceeded, exec.Status);
        Assert.Equal(8192, exec.BytesReclaimed);
        Assert.Single(exec.SkippedLockedPaths);
        Assert.False(File.Exists(unlockedFile));
        Assert.True(File.Exists(lockedFile));
    }

    [Fact]
    public void ElevatedAdminScriptRunner_ValidatesSecurityInvariantsStrictly()
    {
        var runner = new ElevatedAdminScriptRunner();

        // Rejects maxShadowSizeGb < 2 GB
        var badVss = new AdminMaintenanceRequest(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new[]
            {
                new AdminMaintenanceOperation("f1", AdminOperationKind.ResizeVssShadowStorage, Volume: "C:", MaxShadowSizeGb: 1)
            });
        Assert.NotEmpty(runner.ValidateRequest(badVss));

        // Rejects WSL relocation onto C: drive or command-injection characters in distro name
        var badWsl = new AdminMaintenanceRequest(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new[]
            {
                new AdminMaintenanceOperation("f2", AdminOperationKind.MoveWslDistribution, DistroName: "Ubuntu & calc.exe", TargetDirectory: @"C:\WSL")
            });
        var wslErrors = runner.ValidateRequest(badWsl);
        Assert.True(wslErrors.Count >= 2);

        // Accepts clean parameterized operations
        var validReq = new AdminMaintenanceRequest(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new[]
            {
                new AdminMaintenanceOperation("f1", AdminOperationKind.ResizeVssShadowStorage, Volume: "C:", MaxShadowSizeGb: 5),
                new AdminMaintenanceOperation("f2", AdminOperationKind.MoveWslDistribution, DistroName: "Ubuntu-24.04", TargetDirectory: @"D:\WSL\Ubuntu")
            });
        Assert.Empty(runner.ValidateRequest(validReq));
    }
}
