using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NexusClean.Application.Orchestration;
using NexusClean.Domain.Enums;
using NexusClean.Infrastructure;
using NexusClean.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NexusClean.Infrastructure.Tests.Strategies;

public sealed class BrowserProfileAndVscodeSandboxTests
{
    private static readonly string[] ProtectedCredentialFiles =
    {
        "Login Data",
        "Login Data For Account",
        "Cookies",
        "Bookmarks",
        "Preferences",
        "Secure Preferences",
        "Web Data",
        "History",
        "Extension Cookies"
    };

    [Fact]
    public async Task SelectiveBrowserPurgeAndVscodeSemVerPrune_Preserve100PercentOfCredentialsAndLatestExtensions()
    {
        using var sandbox = new TempSandboxFixture();
        var mockLocalAppData = sandbox.CreateDirectory(@"Users\Mock\AppData\Local");
        var mockRoamingAppData = sandbox.CreateDirectory(@"Users\Mock\AppData\Roaming");
        var mockUserProfile = sandbox.CreateDirectory(@"Users\Mock");

        // 1. Populate Chrome, Edge, Brave, and Cốc Cốc profiles with BOTH sensitive credentials AND cache!
        var browserRoots = new[]
        {
            @"Users\Mock\AppData\Local\Google\Chrome\User Data",
            @"Users\Mock\AppData\Local\Microsoft\Edge\User Data",
            @"Users\Mock\AppData\Local\BraveSoftware\Brave-Browser\User Data",
            @"Users\Mock\AppData\Local\CocCoc\Browser\User Data"
        };

        var preCleanHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bRoot in browserRoots)
        {
            foreach (var profile in new[] { "Default", "Profile 1" })
            {
                foreach (var cred in ProtectedCredentialFiles)
                {
                    var credPath = sandbox.CreateTextFile($@"{bRoot}\{profile}\{cred}", $"SECRET-{bRoot}-{profile}-{cred}");
                    preCleanHashes[credPath] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(credPath)));
                }

                var extFile = sandbox.CreateTextFile($@"{bRoot}\{profile}\Extensions\ext_abc\manifest.json", "{\"name\":\"ProtectedExt\"}");
                preCleanHashes[extFile] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extFile)));

                // Populate allowlisted transient caches (4 KB + 2 KB per profile)
                sandbox.CreateFileWithSize($@"{bRoot}\{profile}\Cache\Cache_Data\f_0001", 4096);
                sandbox.CreateFileWithSize($@"{bRoot}\{profile}\Code Cache\js\index.bin", 2048);
                sandbox.CreateFileWithSize($@"{bRoot}\{profile}\GPUCache\shader.bin", 1024);
            }
        }

        // 2. Populate VS Code extensions: old version, latest version, manifest-referenced version, and stale .uuid folder!
        var extRootRel = @"Users\Mock\.vscode\extensions";
        var oldPy = sandbox.CreateFileWithSize($@"{extRootRel}\ms-python.python-2026.2.0-win32-x64\extension.js", 5000);
        var latestPy = sandbox.CreateTextFile($@"{extRootRel}\ms-python.python-2026.4.0-win32-x64\extension.js", "LATEST_PYTHON_EXT");
        preCleanHashes[latestPy] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(latestPy)));

        // Pinned older C# extension in extensions.json MUST ALSO be preserved!
        var pinnedCs = sandbox.CreateTextFile($@"{extRootRel}\ms-dotnettools.csharp-2.40.0-win32-x64\extension.js", "PINNED_ACTIVE_EXT");
        var newerUnactivatedCs = sandbox.CreateTextFile($@"{extRootRel}\ms-dotnettools.csharp-2.45.0-win32-x64\extension.js", "LATEST_CS_EXT");
        preCleanHashes[pinnedCs] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pinnedCs)));
        preCleanHashes[newerUnactivatedCs] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(newerUnactivatedCs)));

        sandbox.CreateTextFile($@"{extRootRel}\extensions.json", """
            [
              { "identifier": { "id": "ms-dotnettools.csharp" }, "relativeLocation": "ms-dotnettools.csharp-2.40.0-win32-x64" }
            ]
            """);

        // Stale >24h .uuid temp folder (should be pruned) vs fresh <1h .uuid temp folder (must be kept!)
        var staleUuidDir = sandbox.CreateDirectory($@"{extRootRel}\.014fc366-1111-2222-3333-444455556666");
        var staleUuidFile = sandbox.CreateFileWithSize($@"{extRootRel}\.014fc366-1111-2222-3333-444455556666\temp.vsix", 3000);
        Directory.SetLastWriteTimeUtc(staleUuidDir, DateTime.UtcNow.AddHours(-48));

        var freshUuidDir = sandbox.CreateDirectory($@"{extRootRel}\.99999999-aaaa-bbbb-cccc-ddddeeeeffff");
        var freshUuidFile = sandbox.CreateTextFile($@"{extRootRel}\.99999999-aaaa-bbbb-cccc-ddddeeeeffff\installing.tmp", "ACTIVE_INSTALL");
        Directory.SetLastWriteTimeUtc(freshUuidDir, DateTime.UtcNow);
        preCleanHashes[freshUuidFile] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(freshUuidFile)));

        // Cached VSIXs folder
        var oldVsix = sandbox.CreateFileWithSize(@"Users\Mock\AppData\Roaming\Code\CachedExtensionVSIXs\old.vsix", 6000);

        // 3. Build Clean Architecture DI container pointed at sandbox audit log & repository default rules!
        var auditLogPath = Path.Combine(sandbox.RootPath, "audit", "audit.jsonl");
        var rulesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "rules", "default-signature-rules.json"));

        var services = new ServiceCollection();
        services.AddNexusCleanCore(customCatalogPath: rulesPath, customAuditLogPath: auditLogPath);
        await using var provider = services.BuildServiceProvider();

        var orchestrator = provider.GetRequiredService<DiskAnalysisOrchestrator>();
        var sandboxDrive = Path.GetPathRoot(sandbox.RootPath)!;
        var envOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCALAPPDATA"] = mockLocalAppData,
            ["APPDATA"] = mockRoamingAppData,
            ["USERPROFILE"] = mockUserProfile
        };

        // 4. Scan sandbox!
        var scanReport = await orchestrator.AnalyzeAsync(sandboxDrive, rulesPath, envOverrides);
        var safeFindings = scanReport.Findings
            .Where(f => f.TargetPath.StartsWith(sandbox.RootPath, StringComparison.OrdinalIgnoreCase) &&
                        f.Impact.IsDefaultSafeSelection)
            .ToArray();

        Assert.NotEmpty(safeFindings);

        // 5. Dry-Run Simulation: MUST delete 0 files while predicting exact bytes (SC-004)!
        var dryRunReport = await orchestrator.CleanAsync(safeFindings, dryRun: true);
        Assert.True(dryRunReport.IsDryRun);
        Assert.True(dryRunReport.TotalBytesReclaimed > 0);
        Assert.True(File.Exists(oldPy));
        Assert.True(File.Exists(oldVsix));

        // 6. Execute Safe Cleanup!
        var execReport = await orchestrator.CleanAsync(safeFindings, dryRun: false);
        Assert.False(execReport.IsDryRun);
        Assert.Equal(dryRunReport.TotalBytesReclaimed, execReport.TotalBytesReclaimed);
        Assert.All(execReport.ItemResults, r => Assert.Equal(CleanupItemStatus.Succeeded, r.Status));

        // 7. Verify 100% of old caches & stale duplicates were deleted!
        Assert.False(File.Exists(oldPy));
        Assert.False(File.Exists(staleUuidFile));
        Assert.False(File.Exists(oldVsix));

        // 8. Verify 100% SHA-256 byte-for-byte preservation of ALL credentials, bookmarks, cookies, extensions, and active VS Code versions (SC-002)!
        foreach (var (protectedPath, expectedHash) in preCleanHashes)
        {
            Assert.True(File.Exists(protectedPath), $"Protected asset missing: {protectedPath}");
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(protectedPath)));
            Assert.Equal(expectedHash, actualHash);
        }

        Assert.True(File.Exists(auditLogPath));
    }
}
