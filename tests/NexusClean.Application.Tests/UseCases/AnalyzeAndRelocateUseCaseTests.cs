using NexusClean.Application.Models;
using NexusClean.Application.UseCases;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;
using Xunit;

namespace NexusClean.Application.Tests.UseCases;

public sealed class AnalyzeAndRelocateUseCaseTests
{
    private sealed class StubSnapshotProvider(IReadOnlyList<VolumeSnapshot> volumes) : ISystemSnapshotProvider
    {
        public ISystemSnapshot CaptureSnapshot(
            IReadOnlyList<string>? additionalProtectedRoots = null,
            IReadOnlyList<string>? additionalProtectedFileNames = null) =>
            new StubSnapshot(volumes);

        private sealed class StubSnapshot(IReadOnlyList<VolumeSnapshot> vols) : ISystemSnapshot
        {
            public bool IsCurrentProcessElevated => false;
            public IReadOnlySet<string> RunningProcessNames { get; } = new HashSet<string>();
            public IReadOnlyList<string> AdditionalProtectedRoots => Array.Empty<string>();
            public IReadOnlyList<string> AdditionalProtectedFileNames => Array.Empty<string>();
            public IReadOnlyList<VolumeSnapshot> FixedVolumes => vols;
            public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths) => Array.Empty<string>();
        }
    }

    private sealed class ThrowingGuard : ISafetyGuard
    {
        public string GuardName => "FaultyTestGuard";
        public int Order => 5;
        public GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot) =>
            throw new InvalidOperationException("Simulated unexpected guard error");
    }

    [Fact]
    public void EvaluateImpactUseCase_FailsClosedWhenAnyGuardThrowsException()
    {
        var evaluate = new EvaluateImpactUseCase(new ISafetyGuard[]
        {
            new ThrowingGuard(),
            new RootSystemFolderGuard()
        });

        var provider = new StubSnapshotProvider(Array.Empty<VolumeSnapshot>());
        var finding = new StorageFinding(
            "system.temp.directories",
            "Temp",
            "System Temp",
            @"C:\Temp",
            4096,
            2,
            DateTime.UtcNow,
            CleanupActionType.DeleteUnlockedItems,
            new ImpactAssessment(
                ImpactLevel.SAFE_CACHE_TIER1,
                1,
                "Thư mục tạm kiểm thử.",
                "Không ảnh hưởng.",
                new[] { "Phiên hiện hành" }));

        evaluate.Evaluate(finding, provider.CaptureSnapshot());

        Assert.Equal(ImpactLevel.PROTECTED_NEVER_DELETE, finding.Impact.Level);
        Assert.Equal(10, finding.Impact.RiskScore);
        Assert.Equal(CleanupActionType.BlockedNoOp, finding.ActionType);
    }

    [Fact]
    public void PlanSecondaryRelocationUseCase_EnforcesTwentyPercentBufferOnDriveD()
    {
        var tenGb = 10L * 1024 * 1024 * 1024;
        var fifteenGb = 15L * 1024 * 1024 * 1024;
        var elevenGb = 11L * 1024 * 1024 * 1024;

        var zaloFinding = new StorageFinding(
            "chat.zalo.media_storage",
            "Zalo Media",
            "Messaging & Sync",
            @"C:\Users\Test\AppData\Roaming\ZaloData\media",
            tenGb,
            1500,
            DateTime.UtcNow,
            CleanupActionType.RecommendRelocateToSecondaryDrive,
            new ImpactAssessment(
                ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE,
                5,
                "Dữ liệu ảnh và video Zalo.",
                "Xóa sẽ mất ảnh cũ.",
                new[] { "Tin nhắn Zalo" }));

        // Case 1: D: has 15 GB free (>= 12 GB required buffer for 10 GB source) -> HasSufficientSpace = true
        var plentyProvider = new StubSnapshotProvider(new[]
        {
            new VolumeSnapshot(@"C:\", 250 * tenGb / 10, tenGb / 2),
            new VolumeSnapshot(@"D:\", 500 * tenGb / 10, fifteenGb)
        });
        var planOk = new PlanSecondaryRelocationUseCase(plentyProvider).CreatePlan(zaloFinding);
        Assert.Equal("D:", planOk.RecommendedTargetDrive);
        Assert.Equal(@"D:\ZaloData", planOk.RecommendedTargetPath);
        Assert.True(planOk.HasSufficientSpace);

        // Case 2: D: has only 11 GB free (< 12 GB required buffer for 10 GB source) -> HasSufficientSpace = false
        var tightProvider = new StubSnapshotProvider(new[]
        {
            new VolumeSnapshot(@"C:\", 250 * tenGb / 10, tenGb / 2),
            new VolumeSnapshot(@"D:\", 500 * tenGb / 10, elevenGb)
        });
        var planTight = new PlanSecondaryRelocationUseCase(tightProvider).CreatePlan(zaloFinding);
        Assert.Equal("D:", planTight.RecommendedTargetDrive);
        Assert.False(planTight.HasSufficientSpace);

        // Case 3: Single partition C: only -> RecommendedTargetDrive = null
        var singleDriveProvider = new StubSnapshotProvider(new[]
        {
            new VolumeSnapshot(@"C:\", 250 * tenGb / 10, tenGb / 2)
        });
        var planSingle = new PlanSecondaryRelocationUseCase(singleDriveProvider).CreatePlan(zaloFinding);
        Assert.Null(planSingle.RecommendedTargetDrive);
        Assert.False(planSingle.HasSufficientSpace);
        Assert.NotEmpty(planSingle.StepByStepGuideVi);
    }

    [Fact]
    public void DriveAnalysisReport_ComputesTierAggregatesAccurately()
    {
        var fSafe = new StorageFinding(
            "app.electron.auto_updaters", "Updater", "Updaters", @"C:\Temp\Updater", 1000, 1, DateTime.UtcNow,
            CleanupActionType.DeleteContents,
            new ImpactAssessment(ImpactLevel.SAFE_CACHE_TIER1, 0, "Bộ cài cập nhật cũ.", "Không ảnh hưởng.", new[] { "App" }));

        var fReloc = new StorageFinding(
            "chat.zalo.media_storage", "Zalo", "Chat", @"C:\Temp\Zalo", 2500, 5, DateTime.UtcNow,
            CleanupActionType.RecommendRelocateToSecondaryDrive,
            new ImpactAssessment(ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE, 5, "Ảnh video Zalo.", "Mất ảnh cũ.", new[] { "Zalo" }));

        var fAdmin = new StorageFinding(
            "system.vss.shadow_copies", "VSS", "System", @"C:\System Volume Information", 4000, 1, DateTime.UtcNow,
            CleanupActionType.ResizeShadowStorageAdmin,
            new ImpactAssessment(ImpactLevel.REQUIRES_ADMIN_ACTION, 4, "Bản chụp khôi phục hệ thống.", "Giữ bản mới nhất.", new[] { "Restore point" }, requiresAdmin: true));

        var report = new DriveAnalysisReport(Guid.NewGuid(), @"C:\", 100_000, 10_000, DateTime.UtcNow, 50, new[] { fSafe, fReloc, fAdmin });

        Assert.Equal(1000, report.SafeReclaimableBytes);
        Assert.Equal(2500, report.RelocatableBytes);
        Assert.Equal(4000, report.AdminReclaimableBytes);
    }
}
