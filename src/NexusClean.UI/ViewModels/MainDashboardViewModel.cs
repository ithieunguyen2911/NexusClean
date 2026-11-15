using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusClean.Application.Models;
using NexusClean.Application.Orchestration;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.UI.ViewModels;

public sealed partial class FindingItemViewModel : ObservableObject
{
    public StorageFinding Model { get; }

    [ObservableProperty]
    private bool _isSelected;

    public FindingItemViewModel(StorageFinding model, Action onSelectionChanged)
    {
        Model = model;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = CanSelectForStandardCleanup && model.Impact.IsDefaultSafeSelection;
    }

    private readonly Action _onSelectionChanged;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Category => Model.Category;
    public string TargetPath => Model.TargetPath;
    public long SizeInBytes => Model.SizeInBytes;
    public string FormattedSize => FormatBytes(Model.SizeInBytes);
    public int FileCount => Model.FileCount;
    public int RiskScore => Model.Impact.RiskScore;

    /// <summary>
    /// Enforces FR-010: Checkboxes are ONLY enabled for SAFE_CACHE_TIER1 and REBUILD_ON_DEMAND.
    /// Relocation, Admin, and Protected tiers disable bulk delete selection.
    /// </summary>
    public bool CanSelectForStandardCleanup =>
        Model.Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND &&
        !Model.Impact.RequiresAdmin;

    public string TierBadgeLabel => Model.Impact.Level switch
    {
        ImpactLevel.SAFE_CACHE_TIER1 => "TIER 1 • XÓA AN TOÀN 100%",
        ImpactLevel.REBUILD_ON_DEMAND => "TIER 2 • BỘ ĐỆM TỰ TẠO LẠI",
        ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE => "TIER 3 • KHUYẾN NGHỊ DỜI SANG Ổ D:",
        ImpactLevel.REQUIRES_ADMIN_ACTION => "TIER 4 • HÀNG ĐỢI ADMIN (UAC)",
        ImpactLevel.PROTECTED_NEVER_DELETE => "KHÓA BẢO VỆ • CẤM XÓA",
        _ => Model.Impact.Level.ToString()
    };

    public string LockStatusLabel => Model.Impact.IsLockedByProcess
        ? $"Đang mở ({string.Join(", ", Model.Impact.LockingProcesses)})"
        : "Sẵn sàng";

    public static string FormatBytes(long bytes)
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

public sealed partial class MainDashboardViewModel : ObservableObject
{
    private readonly DiskAnalysisOrchestrator _orchestrator;
    private readonly ISignatureRuleCatalogProvider _catalogProvider;
    private readonly IAuditLogRepository _auditLog;
    private readonly IElevatedAdminExecutor _adminExecutor;
    private readonly IDiskGrowthWatcher _growthWatcher;

    private CancellationTokenSource? _scanCts;

    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<FindingItemViewModel> Findings { get; } = new();
    public ObservableCollection<string> SelectedPreservedAssets { get; } = new();
    public ObservableCollection<string> RelocationSteps { get; } = new();
    public ObservableCollection<string> RecentAlerts { get; } = new();

    [ObservableProperty] private string _selectedDrive = @"C:\";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusBannerText = "Sẵn sàng kiểm toán ổ đĩa hệ thống. Nhấn 'Quét Ổ Đĩa' để phân tích dữ liệu ẩn an toàn.";
    [ObservableProperty] private double _scanProgressPercent;

    [ObservableProperty] private string _driveCapacitySummary = "Chưa quét";
    [ObservableProperty] private string _safeReclaimableSummary = "0.00 GB";
    [ObservableProperty] private string _relocatableSummary = "0.00 GB";
    [ObservableProperty] private string _adminSummary = "0.00 GB";
    [ObservableProperty] private string _selectedSummaryText = "Đã chọn: 0 mục (0 B)";
    [ObservableProperty] private string _lastDryRunSummary = "Chưa chạy Mô phỏng (Dry-Run)";
    [ObservableProperty] private bool _isWatcherEnabled;

    [ObservableProperty] private FindingItemViewModel? _selectedFinding;
    [ObservableProperty] private bool _hasSelectedFinding;
    [ObservableProperty] private bool _showRelocationCard;
    [ObservableProperty] private bool _showAdminCard;
    [ObservableProperty] private string _relocationHeaderText = string.Empty;
    [ObservableProperty] private string _adminActionDescription = string.Empty;
    [ObservableProperty] private string _wslDistroInput = "Ubuntu";
    [ObservableProperty] private string _wslTargetFolderInput = @"D:\WSL\Ubuntu";
    [ObservableProperty] private int _vssMaxGbInput = 5;

    public MainDashboardViewModel(
        DiskAnalysisOrchestrator orchestrator,
        ISignatureRuleCatalogProvider catalogProvider,
        IAuditLogRepository auditLog,
        IElevatedAdminExecutor adminExecutor,
        IDiskGrowthWatcher growthWatcher)
    {
        _orchestrator = orchestrator;
        _catalogProvider = catalogProvider;
        _auditLog = auditLog;
        _adminExecutor = adminExecutor;
        _growthWatcher = growthWatcher;

        _growthWatcher.GrowthAlertRaised += (_, alert) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var entry = $"[{alert.ObservedAtUtc.ToLocalTime():HH:mm:ss}] {alert.RecommendedActionVi}";
                RecentAlerts.Insert(0, entry);
                StatusBannerText = entry;
            });
        };

        LoadFixedDrives();
    }

    private void LoadFixedDrives()
    {
        AvailableDrives.Clear();
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                AvailableDrives.Add(d.Name);
            }
        }
        catch
        {
            // Fallback
        }

        if (AvailableDrives.Count == 0)
        {
            AvailableDrives.Add(@"C:\");
        }

        SelectedDrive = AvailableDrives[0];
    }

    partial void OnSelectedFindingChanged(FindingItemViewModel? value)
    {
        SelectedPreservedAssets.Clear();
        RelocationSteps.Clear();

        if (value is null)
        {
            HasSelectedFinding = false;
            ShowRelocationCard = false;
            ShowAdminCard = false;
            return;
        }

        HasSelectedFinding = true;
        foreach (var asset in value.Model.Impact.PreservedAssets)
        {
            SelectedPreservedAssets.Add($"✓ Giữ nguyên: {asset}");
        }

        ShowRelocationCard = value.Model.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE;
        if (ShowRelocationCard)
        {
            var plan = _orchestrator.PlanRelocation(value.Model);
            RelocationHeaderText = plan.RecommendedTargetPath is not null
                ? $"Đích khuyến nghị: {plan.RecommendedTargetPath} (Trống: {FindingItemViewModel.FormatBytes(plan.AvailableTargetBytes)})"
                : "Không phát hiện phân vùng ổ phụ cố định trên máy";
            foreach (var step in plan.StepByStepGuideVi)
            {
                RelocationSteps.Add(step);
            }
        }

        ShowAdminCard = value.Model.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION || value.Model.Impact.RequiresAdmin;
        if (ShowAdminCard)
        {
            AdminActionDescription = value.Model.ActionType == CleanupActionType.ResizeShadowStorageAdmin
                ? "Giới hạn dung lượng tối đa System Restore (Shadow Copy) xuống mức an toàn (mặc định 5 GB) để thu hồi ngay ổ C: mà vẫn giữ điểm khôi phục gần nhất."
                : "Di chuyển đĩa ảo máy ảo Linux WSL2 (ext4.vhdx) sang ổ đĩa phụ thông qua tiến trình quyền Quản trị viên cô lập.";
        }
    }

    [RelayCommand]
    private async Task ScanDriveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Findings.Clear();
        SelectedFinding = null;
        ScanProgressPercent = 0;

        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressPercent = p.TotalRules > 0 ? (p.CompletedRules * 100.0 / p.TotalRules) : 0;
            StatusBannerText = $"{p.CurrentAreaDescription} — Đã phát hiện {FindingItemViewModel.FormatBytes(p.CumulativeBytesDiscovered)}";
        });

        try
        {
            var report = await _orchestrator.AnalyzeAsync(SelectedDrive, progress: progress, cancellationToken: _scanCts.Token);
            foreach (var f in report.Findings)
            {
                Findings.Add(new FindingItemViewModel(f, UpdateSelectionMetrics));
            }

            if (Findings.Count > 0)
            {
                SelectedFinding = Findings[0];
            }

            var usedPct = report.TotalCapacityBytes > 0
                ? ((report.TotalCapacityBytes - report.FreeBytesBeforeScan) * 100.0 / report.TotalCapacityBytes)
                : 0;
            DriveCapacitySummary = $"{FindingItemViewModel.FormatBytes(report.FreeBytesBeforeScan)} trống / {FindingItemViewModel.FormatBytes(report.TotalCapacityBytes)} ({usedPct:F1}% đã dùng)";
            SafeReclaimableSummary = FindingItemViewModel.FormatBytes(report.SafeReclaimableBytes);
            RelocatableSummary = FindingItemViewModel.FormatBytes(report.RelocatableBytes);
            AdminSummary = FindingItemViewModel.FormatBytes(report.AdminReclaimableBytes);

            UpdateSelectionMetrics();
            StatusBannerText = $"Hoàn tất kiểm toán ổ {report.DriveName} trong {report.DurationMs} ms — Phát hiện {Findings.Count} mục (An toàn dọn ngay: {SafeReclaimableSummary}).";
        }
        catch (OperationCanceledException)
        {
            StatusBannerText = "Đã hủy quét ổ đĩa theo yêu cầu.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi khi quét ổ đĩa: {ex.Message}";
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private void SelectAllSafe()
    {
        foreach (var item in Findings)
        {
            if (item.CanSelectForStandardCleanup)
            {
                item.IsSelected = true;
            }
        }

        UpdateSelectionMetrics();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Findings)
        {
            item.IsSelected = false;
        }

        UpdateSelectionMetrics();
    }

    [RelayCommand]
    private async Task SimulateDryRunAsync()
    {
        if (IsBusy) return;
        var selected = Findings.Where(f => f.IsSelected && f.CanSelectForStandardCleanup).Select(f => f.Model).ToArray();
        if (selected.Length == 0)
        {
            StatusBannerText = "Hãy tích chọn ít nhất 1 mục bộ nhớ đệm an toàn (Tier 1 hoặc Tier 2) trước khi chạy Mô phỏng Dry-Run.";
            return;
        }

        IsBusy = true;
        try
        {
            var simReport = await _orchestrator.CleanAsync(selected, dryRun: true);
            LastDryRunSummary = $"Dry-Run: Dự kiến thu hồi {FindingItemViewModel.FormatBytes(simReport.TotalBytesReclaimed)} ({simReport.TotalFilesProcessed:N0} tệp, bỏ qua {simReport.TotalSkippedLockedCount:N0} tệp đang mở)";
            StatusBannerText = $"[MÔ PHỎNG DRY-RUN HOÀN TẤT — CHƯA XÓA FILE NÀO] {LastDryRunSummary}.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi mô phỏng Dry-Run: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteCleanupAsync()
    {
        if (IsBusy) return;
        var selected = Findings.Where(f => f.IsSelected && f.CanSelectForStandardCleanup).Select(f => f.Model).ToArray();
        if (selected.Length == 0)
        {
            StatusBannerText = "Chưa có mục an toàn nào được chọn để dọn dẹp.";
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _orchestrator.CleanAsync(selected, dryRun: true);
            var confirm = MessageBox.Show(
                $"Kết quả kiểm tra trước khi xóa (Dry-Run):\n\n" +
                $"• Số mục đã chọn: {selected.Length}\n" +
                $"• Dung lượng thu hồi dự kiến: {FindingItemViewModel.FormatBytes(preview.TotalBytesReclaimed)}\n" +
                $"• Tệp đang mở được tự động bỏ qua: {preview.TotalSkippedLockedCount:N0}\n" +
                $"• Cam kết Safety Guard: Giữ nguyên 100% mật khẩu trình duyệt, Cookie, Bookmark và cấu hình ứng dụng.\n\n" +
                $"Bạn có chắc chắn muốn thực thi dọn dẹp ngay bây giờ?",
                "NexusClean — Xác nhận Dọn dẹp An toàn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirm != MessageBoxResult.Yes)
            {
                StatusBannerText = "Đã hủy thao tác thực thi dọn dẹp.";
                return;
            }

            var execReport = await _orchestrator.CleanAsync(selected, dryRun: false);
            StatusBannerText = $"ĐÃ DỌN DẸP THÀNH CÔNG +{FindingItemViewModel.FormatBytes(execReport.TotalBytesReclaimed)} ({execReport.TotalFilesProcessed:N0} tệp)! Nhật ký lưu tại: {_auditLog.LogFilePath}";
            LastDryRunSummary = $"Đã thu hồi: +{FindingItemViewModel.FormatBytes(execReport.TotalBytesReclaimed)}";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi thực thi dọn dẹp: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await ScanDriveAsync();
    }

    [RelayCommand]
    private async Task ExecuteAdminActionAsync()
    {
        if (SelectedFinding is null || IsBusy) return;
        var model = SelectedFinding.Model;

        AdminMaintenanceOperation op;
        string previewDesc;
        if (model.ActionType == CleanupActionType.ResizeShadowStorageAdmin)
        {
            var gb = Math.Clamp(VssMaxGbInput, 2, 200);
            var vol = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(model.TargetPath) ?? "C:");
            op = new AdminMaintenanceOperation(model.Id, AdminOperationKind.ResizeVssShadowStorage, Volume: vol, MaxShadowSizeGb: gb);
            previewDesc = $"vssadmin resize shadowstorage /for={vol} /on={vol} /maxsize={gb}GB";
        }
        else if (model.ActionType == CleanupActionType.RelocateOrUnregisterAdmin)
        {
            op = new AdminMaintenanceOperation(
                model.Id,
                AdminOperationKind.MoveWslDistribution,
                DistroName: WslDistroInput.Trim(),
                TargetDirectory: WslTargetFolderInput.Trim());
            previewDesc = $"wsl --shutdown && wsl --manage {WslDistroInput.Trim()} --move \"{WslTargetFolderInput.Trim()}\"";
        }
        else
        {
            return;
        }

        var req = new AdminMaintenanceRequest(Guid.NewGuid(), DateTime.UtcNow, new[] { op });
        var errors = _adminExecutor.ValidateRequest(req);
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Tác vụ bảo trì hệ thống sau sẽ yêu cầu xác nhận quyền Quản trị viên (UAC):\n\n" +
            $"Lệnh tham số hóa:\n  {previewDesc}\n\n" +
            $"Tiếp tục thực thi?",
            "NexusClean — Hàng đợi Quản trị viên (UAC)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var res = await _adminExecutor.ExecuteElevatedAsync(req);
            if (res.WasCancelledByUser)
            {
                StatusBannerText = "Người dùng đã từ chối hộp thoại UAC — tác vụ quản trị viên được hủy an toàn.";
                return;
            }

            var summary = string.Join(" | ", res.Outcomes.Select(o => o.MessageVi));
            StatusBannerText = summary;
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi tác vụ Quản trị viên: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ValidateAndReloadRules()
    {
        var (_, _, validation) = _catalogProvider.LoadRules();
        StatusBannerText = validation.IsValid
            ? $"Đã nạp & xác thực thành công {validation.ValidRuleCount} quy tắc nhận diện từ '{Path.GetFileName(validation.SourceFilePath)}'."
            : $"Cảnh báo tệp quy tắc ({validation.Issues.Count} lỗi): {string.Join("; ", validation.Issues.Select(i => i.MessageVi))}";
    }

    [RelayCommand]
    private async Task ToggleGrowthWatcherAsync()
    {
        if (IsWatcherEnabled)
        {
            await _growthWatcher.StopAsync();
            IsWatcherEnabled = false;
            StatusBannerText = "Đã tắt bộ theo dõi tăng trưởng dung lượng ngầm.";
        }
        else
        {
            var options = new WatcherOptions(
                SelectedDrive,
                new[]
                {
                    @"%LOCALAPPDATA%\Temp",
                    @"%APPDATA%\ZaloData\media",
                    @"%LOCALAPPDATA%\antigravity-updater"
                },
                MinFreeDriveBytesThreshold: 15L * 1024 * 1024 * 1024,
                FolderGrowthDeltaBytesThreshold: 2L * 1024 * 1024 * 1024,
                PollingInterval: TimeSpan.FromMinutes(2));

            await _growthWatcher.StartAsync(options);
            IsWatcherEnabled = true;
            StatusBannerText = "Đã BẬT theo dõi tăng trưởng chủ động (Cảnh báo khi ổ C: trống < 15 GB hoặc thư mục nóng phình >= +2 GB).";
        }
    }

    [RelayCommand]
    private void OpenAuditLogFolder()
    {
        var dir = Path.GetDirectoryName(_auditLog.LogFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
    }

    private void UpdateSelectionMetrics()
    {
        var selected = Findings.Where(f => f.IsSelected && f.CanSelectForStandardCleanup).ToList();
        var bytes = selected.Sum(s => s.SizeInBytes);
        SelectedSummaryText = $"Đã chọn an toàn: {selected.Count} mục ({FindingItemViewModel.FormatBytes(bytes)})";
    }
}
