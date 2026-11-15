using NexusClean.Application.Models;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;
using NexusClean.Infrastructure.FileSystem;
using NexusClean.Infrastructure.OS;
using NexusClean.Infrastructure.Persistence;
using NexusClean.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NexusClean.Infrastructure.Tests.Persistence;

public sealed class CatalogWatcherAndAuditTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusClean.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    [Fact]
    public void JsonSignatureRuleProvider_ValidatesBothSeedAndExpandedCatalogsCleanly()
    {
        var repoRoot = FindRepoRoot();
        var provider = new JsonSignatureRuleProvider();

        var seedReport = provider.ValidateCatalogFile(Path.Combine(repoRoot, "docs", "03-SEED-SIGNATURE-RULES.json"));
        Assert.True(seedReport.IsValid, string.Join("; ", seedReport.Issues.Select(i => i.MessageVi)));
        Assert.Equal(9, seedReport.ValidRuleCount);

        var defaultReport = provider.ValidateCatalogFile(Path.Combine(repoRoot, "rules", "default-signature-rules.json"));
        Assert.True(defaultReport.IsValid, string.Join("; ", defaultReport.Issues.Select(i => i.MessageVi)));
        Assert.True(defaultReport.ValidRuleCount >= 12);
    }

    [Fact]
    public async Task JsonSignatureRuleProvider_SupportsZeroRecompileCustomRuleAndRejectsUnsafeRootRule()
    {
        using var sandbox = new TempSandboxFixture();
        var customCacheDir = sandbox.CreateDirectory("custom_app_cache");
        sandbox.CreateFileWithSize(@"custom_app_cache\blob.cache", 16384);

        // 1. Valid custom rule works with zero recompilation (SC-006)
        var validCustomJson = $$"""
            {
              "version": "1.0.0",
              "protectedCriticalFileNames": ["Login Data", "Cookies"],
              "protectedRootPaths": ["C:\\Windows"],
              "rules": [
                {
                  "ruleId": "custom.myapp.cache",
                  "displayName": "Bộ nhớ đệm ứng dụng tùy chỉnh",
                  "category": "Custom Apps",
                  "targetPaths": [{{System.Text.Json.JsonSerializer.Serialize(customCacheDir)}}],
                  "actionType": "DeleteContents",
                  "impactLevel": "SAFE_CACHE_TIER1",
                  "riskScore": 0,
                  "summaryVi": "Bộ nhớ đệm tạm của ứng dụng tùy chỉnh mới thêm.",
                  "sideEffectIfDeleted": "Không ảnh hưởng dữ liệu người dùng.",
                  "preservedAssets": ["Cấu hình người dùng"]
                }
              ]
            }
            """;
        var validRuleFile = sandbox.CreateTextFile("custom-valid.json", validCustomJson);
        var provider = new JsonSignatureRuleProvider();
        var (_, rules, valReport) = provider.LoadRules(validRuleFile);

        Assert.True(valReport.IsValid);
        Assert.Single(rules);

        // 2. Malicious/misconfigured rule targeting C:\Windows or %LOCALAPPDATA% is blocked at load time!
        var unsafeCustomJson = """
            {
              "version": "1.0.0",
              "protectedCriticalFileNames": ["Login Data"],
              "protectedRootPaths": ["C:\\Windows"],
              "rules": [
                {
                  "ruleId": "bad.root.wiper",
                  "displayName": "Rule lỗi trỏ thẳng vào AppData gốc",
                  "category": "Bad",
                  "targetPaths": ["%LOCALAPPDATA%"],
                  "actionType": "DeleteContents",
                  "impactLevel": "SAFE_CACHE_TIER1",
                  "riskScore": 0,
                  "summaryVi": "Rule cố tình xóa thư mục gốc bị cấm.",
                  "sideEffectIfDeleted": "Hỏng toàn bộ phần mềm.",
                  "preservedAssets": ["None"]
                }
              ]
            }
            """;
        var unsafeFile = sandbox.CreateTextFile("custom-unsafe.json", unsafeCustomJson);
        var unsafeReport = provider.ValidateCatalogFile(unsafeFile);
        Assert.False(unsafeReport.IsValid);
        Assert.Contains(unsafeReport.Issues, i => i.Code == "UNSAFE_RULE_BLOCKED");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DiskGrowthFileSystemWatcher_EmitsAlertWhenWatchedFolderExceedsDelta()
    {
        using var sandbox = new TempSandboxFixture();
        var hotFolder = sandbox.CreateDirectory("hot_zalo_media");
        sandbox.CreateFileWithSize(@"hot_zalo_media\init.jpg", 1000);

        var watcher = new DiskGrowthFileSystemWatcher(
            new MultiThreadedDirectorySizer(),
            freeSpaceResolver: _ => 50L * 1024 * 1024 * 1024);

        var alerts = new List<DiskGrowthAlert>();
        watcher.GrowthAlertRaised += (_, a) => alerts.Add(a);

        var options = new WatcherOptions(
            @"C:\",
            new[] { hotFolder },
            MinFreeDriveBytesThreshold: 10L * 1024 * 1024 * 1024,
            FolderGrowthDeltaBytesThreshold: 4096,
            PollingInterval: TimeSpan.FromHours(1));

        await watcher.StartAsync(options);

        // Simulate +8192 bytes growth (>= 4096 threshold)
        sandbox.CreateFileWithSize(@"hot_zalo_media\new_video.mp4", 8192);
        await watcher.EvaluateTickAsync(options);
        await watcher.StopAsync();

        Assert.Single(alerts);
        Assert.True(alerts[0].DeltaBytes >= 8192);
    }

    [Fact]
    public async Task JsonlAuditLogRepository_RollsOverCleanlyWithinConfiguredCeiling()
    {
        using var sandbox = new TempSandboxFixture();
        var logFile = Path.Combine(sandbox.RootPath, "logs", "audit.jsonl");
        var repo = new JsonlAuditLogRepository(logFile, maxBytesPerFile: 1500, maxArchives: 2);

        for (var i = 0; i < 12; i++)
        {
            var batch = new CleanupBatchReport(
                Guid.NewGuid(),
                IsDryRun: true,
                IsElevatedBatch: false,
                DateTime.UtcNow,
                DurationMs: 10,
                ItemResults: new[]
                {
                    new CleanupItemResult(
                        $"id_{i}",
                        "system.temp.directories",
                        @"C:\Temp",
                        CleanupActionType.DeleteUnlockedItems,
                        CleanupItemStatus.Succeeded,
                        1024 * i,
                        i,
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        "Rollover test entry")
                });
            await repo.AppendBatchReportAsync(batch);
        }

        Assert.True(File.Exists(logFile));
        Assert.True(File.Exists(Path.Combine(sandbox.RootPath, "logs", "audit.1.jsonl")));
        Assert.True(File.Exists(Path.Combine(sandbox.RootPath, "logs", "audit.2.jsonl")));
        Assert.False(File.Exists(Path.Combine(sandbox.RootPath, "logs", "audit.3.jsonl")));

        var recent = await repo.ReadRecentReportsAsync(5);
        Assert.NotEmpty(recent);
    }
}
