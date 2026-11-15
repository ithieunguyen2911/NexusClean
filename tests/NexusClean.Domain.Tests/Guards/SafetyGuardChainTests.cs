using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;
using Xunit;

namespace NexusClean.Domain.Tests.Guards;

public sealed class SafetyGuardChainTests
{
    private sealed class FakeSystemSnapshot : ISystemSnapshot
    {
        public bool IsCurrentProcessElevated { get; init; }
        public IReadOnlySet<string> RunningProcessNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> AdditionalProtectedRoots { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AdditionalProtectedFileNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<VolumeSnapshot> FixedVolumes { get; init; } = Array.Empty<VolumeSnapshot>();
        public Func<IReadOnlyList<string>, IReadOnlyList<string>> LockResolver { get; init; } = _ => Array.Empty<string>();

        public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths) =>
            LockResolver(candidatePaths);
    }

    private static ImpactAssessment CreateTier1Impact() =>
        new(
            ImpactLevel.SAFE_CACHE_TIER1,
            riskScore: 1,
            summaryVi: "Bộ nhớ đệm kiểm thử an toàn.",
            sideEffectIfDeleted: "Không ảnh hưởng dữ liệu.",
            preservedAssets: new[] { "Tài khoản người dùng" });

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\Windows\System32")]
    public void RootSystemFolderGuard_UnconditionallyBlocksProtectedSystemRoots(string dangerousPath)
    {
        var guard = new RootSystemFolderGuard();
        var finding = new StorageFinding(
            "test.dangerous.root",
            "Dangerous Target",
            "Test",
            dangerousPath,
            sizeInBytes: 1024,
            fileCount: 1,
            DateTime.UtcNow,
            CleanupActionType.DeleteContents,
            CreateTier1Impact());

        var verdict = guard.Inspect(finding, new FakeSystemSnapshot());

        Assert.Equal(GuardSeverity.Block, verdict.Severity);
        finding.ApplyGuardVerdicts(new[] { verdict });
        Assert.Equal(ImpactLevel.PROTECTED_NEVER_DELETE, finding.Impact.Level);
        Assert.Equal(10, finding.Impact.RiskScore);
        Assert.Equal(CleanupActionType.BlockedNoOp, finding.ActionType);
    }

    [Fact]
    public void RootSystemFolderGuard_BlocksRealLocalAndRoamingAppDataRoots()
    {
        var guard = new RootSystemFolderGuard();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var root in new[] { localAppData, roamingAppData })
        {
            var finding = new StorageFinding(
                "test.appdata.root",
                "AppData Root",
                "Test",
                root,
                1000,
                10,
                DateTime.UtcNow,
                CleanupActionType.DeleteContents,
                CreateTier1Impact());

            var verdict = guard.Inspect(finding, new FakeSystemSnapshot());
            Assert.Equal(GuardSeverity.Block, verdict.Severity);
        }
    }

    [Theory]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Login Data")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Cookies")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Microsoft\Edge\User Data\Profile 1\Bookmarks")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\History")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\CocCoc\Browser\User Data\Default\Extensions")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Preferences")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Secure Preferences")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Web Data")]
    [InlineData(@"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default\Extension Cookies")]
    public void BrowserProfilePreservationGuard_BlocksEveryProtectedCredentialAsset(string sensitivePath)
    {
        var guard = new BrowserProfilePreservationGuard();
        var finding = new StorageFinding(
            "browser.bad.rule",
            "Bad Browser Rule",
            "Web Browsers",
            sensitivePath,
            2048,
            1,
            DateTime.UtcNow,
            CleanupActionType.DeleteContents,
            CreateTier1Impact());

        var verdict = guard.Inspect(finding, new FakeSystemSnapshot());

        Assert.Equal(GuardSeverity.Block, verdict.Severity);
    }

    [Fact]
    public void BrowserProfilePreservationGuard_BlocksWholeProfileDeleteEvenWithoutCredentialFileName()
    {
        var guard = new BrowserProfilePreservationGuard();
        var profileRoot = @"C:\Users\TestUser\AppData\Local\Google\Chrome\User Data\Default";
        var finding = new StorageFinding(
            "browser.whole.profile",
            "Whole Chrome Profile",
            "Web Browsers",
            profileRoot,
            50_000_000,
            500,
            DateTime.UtcNow,
            CleanupActionType.DeleteContents,
            CreateTier1Impact());

        var verdict = guard.Inspect(finding, new FakeSystemSnapshot());

        Assert.Equal(GuardSeverity.Block, verdict.Severity);
        Assert.Equal("NON_SELECTIVE_BROWSER_ACTION", verdict.ReasonCode);
    }

    [Fact]
    public void BrowserProfilePreservationGuard_AllowsExplicitlyAllowlistedCacheSubfolders()
    {
        var guard = new BrowserProfilePreservationGuard();
        var userDataRoot = @"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data";
        var subPaths = new[]
        {
            @"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data",
            @"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data\Profile 2\Code Cache\js",
            @"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data\Profile 2\GPUCache",
            @"C:\Users\TestUser\AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\Service Worker\CacheStorage"
        };

        var finding = new StorageFinding(
            "browser.chromium.selective_cache",
            "Brave Selective Cache",
            "Web Browsers",
            userDataRoot,
            10_000_000,
            120,
            DateTime.UtcNow,
            CleanupActionType.SelectiveSubfolderPurge,
            new ImpactAssessment(
                ImpactLevel.REBUILD_ON_DEMAND,
                riskScore: 1,
                summaryVi: "Chỉ xóa bộ nhớ đệm tạm thời của trình duyệt.",
                sideEffectIfDeleted: "Giữ nguyên 100% tài khoản, mật khẩu và dấu trang.",
                preservedAssets: new[] { "Login Data", "Cookies", "Bookmarks" }),
            subPathsToClean: subPaths);

        var verdict = guard.Inspect(finding, new FakeSystemSnapshot());

        Assert.Equal(GuardSeverity.Allow, verdict.Severity);
    }

    [Fact]
    public void AdminElevationAndLockGuards_FlagWslAndNonElevatedSystemMaintenance()
    {
        var lockGuard = new RunningProcessLockGuard();
        var adminGuard = new AdminElevationGuard();
        var snapshot = new FakeSystemSnapshot
        {
            IsCurrentProcessElevated = false,
            RunningProcessNames = new HashSet<string>(new[] { "vmmemWSL", "wslservice" }, StringComparer.OrdinalIgnoreCase)
        };

        var finding = new StorageFinding(
            "vm.wsl.virtual_disk",
            "WSL2 Virtual Disk",
            "Virtualization",
            @"C:\Users\TestUser\AppData\Local\wsl",
            5_000_000_000L,
            2,
            DateTime.UtcNow,
            CleanupActionType.RelocateOrUnregisterAdmin,
            new ImpactAssessment(
                ImpactLevel.REQUIRES_ADMIN_ACTION,
                riskScore: 6,
                summaryVi: "Đĩa ảo máy ảo Linux WSL2.",
                sideEffectIfDeleted: "Cần di chuyển bằng wsl --manage.",
                preservedAssets: new[] { "Windows OS" },
                lockingProcesses: new[] { "vmmemWSL", "wslservice" },
                requiresAdmin: true));

        var verdicts = new[]
        {
            lockGuard.Inspect(finding, snapshot),
            adminGuard.Inspect(finding, snapshot)
        };

        finding.ApplyGuardVerdicts(verdicts);

        Assert.All(verdicts, v => Assert.Equal(GuardSeverity.RequireElevation, v.Severity));
        Assert.True(finding.Impact.IsLockedByProcess);
        Assert.True(finding.Impact.RequiresAdmin);
        Assert.Contains("vmmemWSL", finding.Impact.LockingProcesses);
    }
}
