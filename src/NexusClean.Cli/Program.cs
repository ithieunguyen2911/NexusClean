using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NexusClean.Application.Models;
using NexusClean.Application.Orchestration;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;
using NexusClean.Infrastructure;
using NexusClean.Infrastructure.OS;

namespace NexusClean.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions CliJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var customRules = GetOption(args, "--rules") ?? GetOption(args, "--file");
        var jsonMode = HasFlag(args, "--json");

        var services = new ServiceCollection();
        services.AddNexusCleanCore(customCatalogPath: customRules);
        await using var provider = services.BuildServiceProvider();

        try
        {
            return command switch
            {
                "scan" => await RunScanAsync(provider, args, jsonMode, cts.Token),
                "simulate" => await RunCleanOrSimulateAsync(provider, args, dryRun: true, jsonMode, cts.Token),
                "clean" => await RunCleanOrSimulateAsync(provider, args, dryRun: false, jsonMode, cts.Token),
                "relocate-plan" => await RunRelocatePlanAsync(provider, args, jsonMode, cts.Token),
                "rules" => RunRulesCommand(provider, args, jsonMode),
                "watch" => await RunWatchAsync(provider, args, jsonMode, cts.Token),
                "admin-exec" => await RunAdminExecAsync(provider, args, cts.Token),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Đã hủy tác vụ theo yêu cầu người dùng (Cancelled).");
            return 3;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Lỗi: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunScanAsync(
        IServiceProvider provider,
        string[] args,
        bool jsonMode,
        CancellationToken ct)
    {
        var drive = GetOption(args, "--drive") ?? "C:";
        var rulesPath = GetOption(args, "--rules");
        var catalogProvider = provider.GetRequiredService<ISignatureRuleCatalogProvider>();

        if (!string.IsNullOrWhiteSpace(rulesPath))
        {
            var val = catalogProvider.ValidateCatalogFile(rulesPath);
            if (!val.IsValid && val.ValidRuleCount == 0)
            {
                if (jsonMode) Console.WriteLine(JsonSerializer.Serialize(val, CliJsonOptions));
                else await Console.Error.WriteLineAsync($"Tệp quy tắc không hợp lệ: {string.Join("; ", val.Issues.Select(i => i.MessageVi))}");
                return 2;
            }
        }

        var orchestrator = provider.GetRequiredService<DiskAnalysisOrchestrator>();
        var progress = new Progress<ScanProgress>(p =>
        {
            if (!jsonMode)
            {
                Console.Error.Write($"\r[{p.CompletedRules}/{p.TotalRules}] {p.CurrentAreaDescription,-65}");
            }
        });

        var report = await orchestrator.AnalyzeAsync(drive, rulesPath, progress: progress, cancellationToken: ct);
        if (!jsonMode)
        {
            await Console.Error.WriteLineAsync();
            PrintHumanScanReport(report);
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CliJsonOptions));
        }

        return 0;
    }

    private static async Task<int> RunCleanOrSimulateAsync(
        IServiceProvider provider,
        string[] args,
        bool dryRun,
        bool jsonMode,
        CancellationToken ct)
    {
        var drive = GetOption(args, "--drive") ?? "C:";
        var rulesPath = GetOption(args, "--rules");
        var idsRaw = GetOption(args, "--ids");
        var tier = (GetOption(args, "--tier") ?? "safe").ToLowerInvariant();
        var autoConfirm = HasFlag(args, "--yes") || HasFlag(args, "-y");

        var orchestrator = provider.GetRequiredService<DiskAnalysisOrchestrator>();
        var scanReport = await orchestrator.AnalyzeAsync(drive, rulesPath, cancellationToken: ct);

        IReadOnlyList<StorageFinding> targets;
        if (!string.IsNullOrWhiteSpace(idsRaw))
        {
            var idSet = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            targets = scanReport.Findings.Where(f => idSet.Contains(f.Id)).ToArray();
        }
        else if (tier == "safe")
        {
            targets = scanReport.Findings.Where(f => f.Impact.IsDefaultSafeSelection).ToArray();
        }
        else
        {
            await Console.Error.WriteLineAsync("Chỉ hỗ trợ '--tier safe' hoặc chỉ định '--ids <id1,id2>' rõ ràng.");
            return 2;
        }

        if (targets.Any(t => t.Impact.Level == ImpactLevel.PROTECTED_NEVER_DELETE))
        {
            await Console.Error.WriteLineAsync("Chặn bởi Safety Guard: Danh sách chọn chứa mục PROTECTED_NEVER_DELETE.");
            return 4;
        }

        if (!dryRun && !autoConfirm)
        {
            var simPreview = await orchestrator.CleanAsync(targets, dryRun: true, ct);
            Console.Error.WriteLine($"[Dry-Run Preview] Sẽ giải phóng dự kiến {FormatSize(simPreview.TotalBytesReclaimed)} ({simPreview.TotalFilesProcessed} tệp).");
            Console.Error.Write("Xác nhận thực thi dọn dẹp thật? (y/N): ");
            var reply = Console.ReadLine()?.Trim();
            if (!string.Equals(reply, "y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reply, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Đã hủy thực thi dọn dẹp.");
                return 0;
            }
        }

        var batchReport = await orchestrator.CleanAsync(targets, dryRun, ct);
        var auditRepo = provider.GetRequiredService<IAuditLogRepository>();

        if (jsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(batchReport, CliJsonOptions));
        }
        else
        {
            PrintHumanBatchReport(batchReport, auditRepo.LogFilePath);
        }

        return batchReport.ItemResults.Any(r => r.Status == CleanupItemStatus.Failed) ? 1 : 0;
    }

    private static async Task<int> RunRelocatePlanAsync(
        IServiceProvider provider,
        string[] args,
        bool jsonMode,
        CancellationToken ct)
    {
        var drive = GetOption(args, "--drive") ?? "C:";
        var findingId = GetOption(args, "--id");
        var targetDrive = GetOption(args, "--target-drive");

        var orchestrator = provider.GetRequiredService<DiskAnalysisOrchestrator>();
        var report = await orchestrator.AnalyzeAsync(drive, cancellationToken: ct);

        var finding = !string.IsNullOrWhiteSpace(findingId)
            ? report.Findings.FirstOrDefault(f => string.Equals(f.Id, findingId, StringComparison.OrdinalIgnoreCase) ||
                                                 f.RuleId.Contains(findingId, StringComparison.OrdinalIgnoreCase))
            : report.Findings.FirstOrDefault(f => f.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE);

        if (finding is null)
        {
            await Console.Error.WriteLineAsync("Không tìm thấy mục cần tư vấn di chuyển (dùng 'nexusclean scan' để lấy ID).");
            return 2;
        }

        var plan = orchestrator.PlanRelocation(finding, targetDrive);
        if (jsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, CliJsonOptions));
        }
        else
        {
            Console.WriteLine($"=== KẾ HOẠCH DI CHUYỂN SANG Ổ PHỤ: {finding.DisplayName} ===");
            Console.WriteLine($"Đường dẫn gốc      : {plan.SourcePath} ({FormatSize(plan.SourceSizeInBytes)})");
            Console.WriteLine($"Ổ đích khuyến nghị : {plan.RecommendedTargetDrive ?? "(Không phát hiện ổ phụ)"} -> {plan.RecommendedTargetPath ?? "N/A"}");
            Console.WriteLine($"Cần trống (+20%)   : {FormatSize(plan.RequiredBytesWithBuffer)} | Hiện trống: {FormatSize(plan.AvailableTargetBytes)}");
            Console.WriteLine("Hướng dẫn từng bước:");
            foreach (var step in plan.StepByStepGuideVi)
            {
                Console.WriteLine($"  - {step}");
            }
        }

        return plan.RecommendedTargetDrive is not null && !plan.HasSufficientSpace ? 5 : 0;
    }

    private static int RunRulesCommand(IServiceProvider provider, string[] args, bool jsonMode)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "validate";
        if (sub != "validate")
        {
            Console.Error.WriteLine($"Lệnh con không hợp lệ '{sub}'. Dùng: nexusclean rules validate --file <path>");
            return 2;
        }

        var file = GetOption(args, "--file") ?? GetOption(args, "--rules") ??
                   Infrastructure.Persistence.JsonSignatureRuleProvider.ResolveDefaultCatalogPath();
        var catalogProvider = provider.GetRequiredService<ISignatureRuleCatalogProvider>();
        var result = catalogProvider.ValidateCatalogFile(file);

        if (jsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions));
        }
        else
        {
            Console.WriteLine($"Kiểm định tệp quy tắc: {result.SourceFilePath}");
            Console.WriteLine($"Trạng thái: {(result.IsValid ? "HỢP LỆ (PASS)" : "KHÔNG HỢP LỆ (FAIL)")} | Số rule hợp lệ: {result.ValidRuleCount}");
            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"  [!] ({issue.Code}) {issue.RuleId}: {issue.MessageVi}");
            }
        }

        return result.IsValid ? 0 : 2;
    }

    private static async Task<int> RunWatchAsync(
        IServiceProvider provider,
        string[] args,
        bool jsonMode,
        CancellationToken ct)
    {
        var drive = GetOption(args, "--drive") ?? "C:";
        var minFreeGb = long.TryParse(GetOption(args, "--min-free-gb"), out var mf) ? mf : 15L;
        var deltaGb = long.TryParse(GetOption(args, "--delta-gb"), out var dg) ? dg : 2L;
        var runOnce = HasFlag(args, "--once");

        var watcher = provider.GetRequiredService<IDiskGrowthWatcher>();
        watcher.GrowthAlertRaised += (_, alert) =>
        {
            if (jsonMode)
            {
                Console.WriteLine(JsonSerializer.Serialize(alert, CliJsonOptions));
            }
            else
            {
                Console.WriteLine($"[CẢNH BÁO DUNG LƯỢNG {alert.ObservedAtUtc:HH:mm:ss}] {alert.RecommendedActionVi}");
            }
        };

        var options = new WatcherOptions(
            drive,
            new[]
            {
                @"%LOCALAPPDATA%\Temp",
                @"%APPDATA%\ZaloData\media",
                @"%LOCALAPPDATA%\antigravity-updater"
            },
            MinFreeDriveBytesThreshold: minFreeGb * 1024L * 1024L * 1024L,
            FolderGrowthDeltaBytesThreshold: deltaGb * 1024L * 1024L * 1024L,
            PollingInterval: TimeSpan.FromSeconds(30));

        if (runOnce && watcher is DiskGrowthFileSystemWatcher concrete)
        {
            await concrete.EvaluateTickAsync(options, ct);
            return 0;
        }

        if (!jsonMode)
        {
            Console.WriteLine($"Đang theo dõi tăng trưởng ổ {drive} (Cảnh báo khi trống < {minFreeGb} GB hoặc thư mục nóng tăng >= {deltaGb} GB). Nhấn Ctrl+C để dừng...");
        }

        await watcher.StartAsync(options, ct);
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            await watcher.StopAsync();
        }

        return 0;
    }

    private static async Task<int> RunAdminExecAsync(
        IServiceProvider provider,
        string[] args,
        CancellationToken ct)
    {
        var reqPath = GetOption(args, "--request");
        var outPath = GetOption(args, "--output");
        if (string.IsNullOrWhiteSpace(reqPath) || !File.Exists(reqPath) || string.IsNullOrWhiteSpace(outPath))
        {
            await Console.Error.WriteLineAsync("Thiếu tham số --request hoặc --output hợp lệ.");
            return 2;
        }

        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                await Console.Error.WriteLineAsync("Lệnh admin-exec bắt buộc phải chạy dưới quyền Administrator (UAC).");
                return 6;
            }
        }

        var json = await File.ReadAllTextAsync(reqPath, ct);
        var req = JsonSerializer.Deserialize<AdminMaintenanceRequest>(json, ElevatedAdminScriptRunner.IpcJsonOptions)
                  ?? throw new InvalidOperationException("Không đọc được AdminMaintenanceRequest.");

        var executor = provider.GetRequiredService<IElevatedAdminExecutor>();
        var result = await executor.ExecuteInCurrentElevatedProcessAsync(req, ct);
        await File.WriteAllTextAsync(
            outPath,
            JsonSerializer.Serialize(result, ElevatedAdminScriptRunner.IpcJsonOptions),
            ct);

        return result.Outcomes.All(o => o.Succeeded) ? 0 : 7;
    }

    private static void PrintHumanScanReport(DriveAnalysisReport report)
    {
        Console.WriteLine($"==========================================================================================");
        Console.WriteLine($" NEXUSCLEAN — BÁO CÁO KIỂM TOÁN Ổ ĐĨA {report.DriveName} (Thời gian quét: {report.DurationMs} ms)");
        Console.WriteLine($"==========================================================================================");
        Console.WriteLine($" Tổng dung lượng ổ đĩa           : {FormatSize(report.TotalCapacityBytes)} (Đang trống: {FormatSize(report.FreeBytesBeforeScan)})");
        Console.WriteLine($" [Tier 1+2] Dọn An Toàn Ngay     : {FormatSize(report.SafeReclaimableBytes)} (Giữ 100% Profile & Mật khẩu)");
        Console.WriteLine($" [Tier 3]   Nên Dời Sang Ổ Phụ   : {FormatSize(report.RelocatableBytes)} (Zalo / Pagefile)");
        Console.WriteLine($" [Tier 4]   Hàng Đợi Quản Trị    : {FormatSize(report.AdminReclaimableBytes)} (Shadow Copy / WSL2)");
        Console.WriteLine($"------------------------------------------------------------------------------------------");
        Console.WriteLine($" {"ID",-16} | {"Dung lượng",12} | {"Cấp độ Tác động",-28} | {"Tên mục phát hiện"}");
        Console.WriteLine($"------------------------------------------------------------------------------------------");

        foreach (var f in report.Findings)
        {
            Console.WriteLine($" {f.Id,-16} | {FormatSize(f.SizeInBytes),12} | {f.Impact.Level,-28} | {f.DisplayName}");
            Console.WriteLine($"   -> Tác động : {f.Impact.SummaryVi}");
            Console.WriteLine($"   -> Giữ nguyên: {string.Join(", ", f.Impact.PreservedAssets)}");
            if (f.Impact.IsLockedByProcess)
            {
                Console.WriteLine($"   -> Khóa bởi : {string.Join(", ", f.Impact.LockingProcesses)}");
            }
        }
    }

    private static void PrintHumanBatchReport(CleanupBatchReport batch, string auditPath)
    {
        var mode = batch.IsDryRun ? "MÔ PHỎNG (DRY-RUN — KHÔNG XÓA THẬT)" : "THỰC THI DỌN DẸP AN TOÀN";
        Console.WriteLine($"==========================================================================================");
        Console.WriteLine($" KẾT QUẢ {mode}");
        Console.WriteLine($"==========================================================================================");
        Console.WriteLine($" Tổng dung lượng thu hồi : {FormatSize(batch.TotalBytesReclaimed)}");
        Console.WriteLine($" Tổng số tệp xử lý       : {batch.TotalFilesProcessed:N0}");
        Console.WriteLine($" Tệp đang mở được bỏ qua : {batch.TotalSkippedLockedCount:N0}");
        Console.WriteLine($" Nhật ký kiểm toán (JSONL): {auditPath}");
        Console.WriteLine($"------------------------------------------------------------------------------------------");
        foreach (var item in batch.ItemResults)
        {
            Console.WriteLine($" [{item.Status,-18}] {FormatSize(item.BytesReclaimed),12} | {item.RuleId} -> {item.MessageVi}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            NexusClean CLI v1.0.0 — Hệ thống Kiểm toán & Dọn dẹp Ổ đĩa Thông minh An toàn (.NET 8 Clean Architecture)

            Cách sử dụng:
              nexusclean scan [--drive C:] [--rules <path>] [--json]
              nexusclean simulate [--drive C:] [--tier safe | --ids <id1,id2>] [--json]
              nexusclean clean [--drive C:] [--tier safe | --ids <id1,id2>] [--yes] [--json]
              nexusclean relocate-plan [--drive C:] [--id <findingId>] [--target-drive D:] [--json]
              nexusclean rules validate [--file <path>] [--json]
              nexusclean watch [--drive C:] [--min-free-gb 15] [--delta-gb 2] [--json]
            """);
    }

    private static int PrintUnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Không nhận diện được lệnh '{cmd}'. Chạy 'nexusclean --help' để xem hướng dẫn.");
        return 2;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{bytes / (1024d * 1024d * 1024d):F2} GB";
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024d * 1024d):F2} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024d:F1} KB";
        return $"{bytes} B";
    }
}
