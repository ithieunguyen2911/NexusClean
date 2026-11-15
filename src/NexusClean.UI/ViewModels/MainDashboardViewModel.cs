using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
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
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");
    private readonly Action _onSelectionChanged;

    public StorageFinding Model { get; }

    [ObservableProperty]
    private bool _isSelected;

    public FindingItemViewModel(StorageFinding model, long maxBytesInBatch, Action onSelectionChanged)
    {
        Model = model;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = CanSelectForStandardCleanup && model.Impact.IsDefaultSafeSelection;

        var ratio = maxBytesInBatch > 0
            ? Math.Clamp((double)model.SizeInBytes / maxBytesInBatch, 0.0, 1.0)
            : 0.0;
        RelativeBarWidth = model.SizeInBytes > 0 ? Math.Max(4.0, ratio * 84.0) : 0.0;
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Category => Model.Category;
    public string TargetPath => Model.TargetPath;
    public long SizeInBytes => Model.SizeInBytes;
    public string FormattedSize => FormatBytes(Model.SizeInBytes);
    public double RelativeBarWidth { get; }
    public int FileCount => Model.FileCount;
    public int RiskScore => Model.Impact.RiskScore;

    /// <summary>
    /// Enforces FR-010: Checkboxes are ONLY enabled for SAFE_CACHE_TIER1 and REBUILD_ON_DEMAND.
    /// </summary>
    public bool CanSelectForStandardCleanup =>
        Model.Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND &&
        !Model.Impact.RequiresAdmin;

    public BadgeTone Tone => Model.Impact.Level switch
    {
        ImpactLevel.SAFE_CACHE_TIER1 => BadgeTone.Safe,
        ImpactLevel.REBUILD_ON_DEMAND => BadgeTone.Rebuild,
        ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE => BadgeTone.Relocate,
        ImpactLevel.REQUIRES_ADMIN_ACTION => BadgeTone.Admin,
        _ => BadgeTone.Protected
    };

    public string ShortBadgeText => Model.Impact.Level switch
    {
        ImpactLevel.SAFE_CACHE_TIER1 => "Xóa an toàn",
        ImpactLevel.REBUILD_ON_DEMAND => "Tự tạo lại",
        ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE => "Nên dời ổ",
        ImpactLevel.REQUIRES_ADMIN_ACTION => "Quyền Admin",
        ImpactLevel.PROTECTED_NEVER_DELETE => "Được bảo vệ",
        _ => "Khác"
    };

    public string RiskSummary => Model.Impact.RiskScore switch
    {
        <= 1 => $"Rủi ro rất thấp ({Model.Impact.RiskScore}/10)",
        <= 3 => $"Rủi ro thấp ({Model.Impact.RiskScore}/10)",
        <= 6 => $"Cần lưu ý ({Model.Impact.RiskScore}/10)",
        _ => $"Khóa bảo vệ ({Model.Impact.RiskScore}/10)"
    };

    public bool IsLocked => Model.Impact.IsLockedByProcess;

    public string LockStatusLabel => Model.Impact.IsLockedByProcess
        ? $"Đang được sử dụng bởi: {string.Join(", ", Model.Impact.LockingProcesses)}"
        : "Không có tiến trình nào đang mở tệp";

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return string.Format(ViCulture, "{0:F2} GB", bytes / (1024d * 1024d * 1024d));
        if (bytes >= 1024L * 1024L)
            return string.Format(ViCulture, "{0:F1} MB", bytes / (1024d * 1024d));
        if (bytes >= 1024L)
            return string.Format(ViCulture, "{0:F1} KB", bytes / 1024d);
        return $"{bytes} B";
    }
}

public sealed partial class MainDashboardViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly DiskAnalysisOrchestrator _orchestrator;
    private readonly ISignatureRuleCatalogProvider _catalogProvider;
    private readonly IAuditLogRepository _auditLog;
    private readonly IElevatedAdminExecutor _adminExecutor;
    private readonly IDiskGrowthWatcher _growthWatcher;

    private CancellationTokenSource? _scanCts;
    private bool _hasScannedAtLeastOnce;

    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<FindingItemViewModel> Findings { get; } = new();
    public ICollectionView FindingsView { get; }

    public ObservableCollection<string> SelectedPreservedAssets { get; } = new();
    public ObservableCollection<string> RelocationSteps { get; } = new();
    public ObservableCollection<string> RecentAlerts { get; } = new();

    public StorageBarBreakdownViewModel StorageBar { get; } = new();
    public ConfirmationModalViewModel Modal { get; } = new();

    [ObservableProperty] private AppThemeMode _currentTheme = AppThemeMode.Light;
    [ObservableProperty] private string _themeButtonLabel = "Giao diện: Sáng";

    [ObservableProperty] private FindingFilterCategory _selectedFilter = FindingFilterCategory.All;
    [ObservableProperty] private string _searchQuery = string.Empty;

    [ObservableProperty] private int _allCount;
    [ObservableProperty] private int _safeCount;
    [ObservableProperty] private int _relocateCount;
    [ObservableProperty] private int _adminCount;

    [ObservableProperty] private bool _showEmptyState = true;
    [ObservableProperty] private string _emptyStateTitle = "Sẵn sàng phân tích dung lượng";
    [ObservableProperty] private string _emptyStateSubtitle = "Chọn ổ đĩa ở thanh trên và nhấn 'Quét ổ đĩa' để kiểm tra bộ nhớ đệm ẩn (chỉ đọc 100%, không xóa dữ liệu).";

    [ObservableProperty] private string _selectedDrive = @"C:\";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusBannerText = "Chế độ an toàn: Bảo toàn 100% tài khoản trình duyệt và cấu hình ứng dụng.";
    [ObservableProperty] private double _scanProgressPercent;

    [ObservableProperty] private string _selectedSummaryText = "Chưa chọn mục nào";
    [ObservableProperty] private string _lastDryRunSummary = "Bạn có thể bấm 'Xem trước (Dry-Run)' để kiểm tra số tệp trước khi dọn.";
    [ObservableProperty] private string _cleanButtonLabel = "Dọn dẹp đã chọn";
    [ObservableProperty] private bool _isWatcherEnabled;
    [ObservableProperty] private string _watcherButtonLabel = "Cảnh báo đầy ổ: Tắt";

    [ObservableProperty] private FindingItemViewModel? _selectedFinding;
    [ObservableProperty] private bool _hasSelectedFinding;
    [ObservableProperty] private bool _showRelocationCard;
    [ObservableProperty] private bool _showAdminCard;
    [ObservableProperty] private bool _isVssAdminItem;
    [ObservableProperty] private bool _isWslAdminItem;
    [ObservableProperty] private string _relocationHeaderText = string.Empty;
    [ObservableProperty] private string _adminActionDescription = string.Empty;
    [ObservableProperty] private string _wslDistroInput = "Ubuntu";
    [ObservableProperty] private string _wslTargetFolderInput = @"D:\WSL\Ubuntu";
    [ObservableProperty] private int _vssMaxGbInput = 5;

    public bool IsFilterAll
    {
        get => SelectedFilter == FindingFilterCategory.All;
        set { if (value) SelectedFilter = FindingFilterCategory.All; }
    }

    public bool IsFilterSafe
    {
        get => SelectedFilter == FindingFilterCategory.SafeCleanup;
        set { if (value) SelectedFilter = FindingFilterCategory.SafeCleanup; }
    }

    public bool IsFilterRelocate
    {
        get => SelectedFilter == FindingFilterCategory.Relocate;
        set { if (value) SelectedFilter = FindingFilterCategory.Relocate; }
    }

    public bool IsFilterAdmin
    {
        get => SelectedFilter == FindingFilterCategory.AdminQueue;
        set { if (value) SelectedFilter = FindingFilterCategory.AdminQueue; }
    }

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

        FindingsView = CollectionViewSource.GetDefaultView(Findings);
        FindingsView.Filter = MatchesCurrentFilter;

        _growthWatcher.GrowthAlertRaised += (_, alert) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var entry = $"[{alert.ObservedAtUtc.ToLocalTime():HH:mm}] {alert.RecommendedActionVi}";
                RecentAlerts.Insert(0, entry);
                StatusBannerText = entry;
            });
        };

        LoadFixedDrives();
    }

    partial void OnSelectedFilterChanged(FindingFilterCategory value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterSafe));
        OnPropertyChanged(nameof(IsFilterRelocate));
        OnPropertyChanged(nameof(IsFilterAdmin));
        RefreshFilterAndEmptyState();
    }

    partial void OnSearchQueryChanged(string value) => RefreshFilterAndEmptyState();

    private bool MatchesCurrentFilter(object obj)
    {
        if (obj is not FindingItemViewModel item)
        {
            return false;
        }

        var categoryMatch = SelectedFilter switch
        {
            FindingFilterCategory.SafeCleanup => item.Model.Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND,
            FindingFilterCategory.Relocate => item.Model.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE,
            FindingFilterCategory.AdminQueue => item.Model.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION || item.Model.Impact.RequiresAdmin,
            _ => true
        };

        if (!categoryMatch)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }

        var q = SearchQuery.Trim();
        return item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               item.TargetPath.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilterAndEmptyState()
    {
        FindingsView.Refresh();
        var visibleItems = FindingsView.Cast<FindingItemViewModel>().ToList();
        ShowEmptyState = visibleItems.Count == 0;

        if (ShowEmptyState)
        {
            if (!_hasScannedAtLeastOnce)
            {
                EmptyStateTitle = "Sẵn sàng phân tích dung lượng";
                EmptyStateSubtitle = "Chọn ổ đĩa ở thanh trên và nhấn 'Quét ổ đĩa' để kiểm tra bộ nhớ đệm ẩn (chỉ đọc 100%, không xóa dữ liệu).";
            }
            else
            {
                EmptyStateTitle = "Không có mục nào khớp bộ lọc";
                EmptyStateSubtitle = "Thử chuyển sang nhóm 'Tất cả mục' ở thanh bên trái hoặc xóa từ khóa tìm kiếm.";
            }
        }
        else if (SelectedFinding is null || !visibleItems.Contains(SelectedFinding))
        {
            SelectedFinding = visibleItems[0];
        }
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
            IsVssAdminItem = false;
            IsWslAdminItem = false;
            return;
        }

        HasSelectedFinding = true;
        foreach (var asset in value.Model.Impact.PreservedAssets)
        {
            SelectedPreservedAssets.Add(asset);
        }

        ShowRelocationCard = value.Model.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE;
        if (ShowRelocationCard)
        {
            var plan = _orchestrator.PlanRelocation(value.Model);
            RelocationHeaderText = plan.RecommendedTargetPath is not null
                ? $"Thư mục đích gợi ý: {plan.RecommendedTargetPath} (Còn trống {FindingItemViewModel.FormatBytes(plan.AvailableTargetBytes)})"
                : "Máy hiện chỉ có một phân vùng ổ đĩa cố định";
            foreach (var step in plan.StepByStepGuideVi)
            {
                RelocationSteps.Add(step);
            }
        }

        ShowAdminCard = value.Model.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION || value.Model.Impact.RequiresAdmin;
        IsVssAdminItem = ShowAdminCard && value.Model.ActionType == CleanupActionType.ResizeShadowStorageAdmin;
        IsWslAdminItem = ShowAdminCard && value.Model.ActionType == CleanupActionType.RelocateOrUnregisterAdmin;

        if (ShowAdminCard)
        {
            AdminActionDescription = IsVssAdminItem
                ? "Giới hạn dung lượng tối đa dành cho System Restore (Shadow Copy) xuống mức hợp lý (mặc định 5 GB) để giải phóng ổ C: mà vẫn giữ điểm khôi phục gần nhất."
                : "Di chuyển đĩa ảo Linux WSL2 (ext4.vhdx) sang ổ đĩa phụ thông qua tiến trình quyền Quản trị viên (UAC).";
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == AppThemeMode.Light ? AppThemeMode.Dark : AppThemeMode.Light;
        App.ApplyTheme(CurrentTheme);
        ThemeButtonLabel = CurrentTheme == AppThemeMode.Light ? "Giao diện: Sáng" : "Giao diện: Tối";
    }

    [RelayCommand]
    private void SetFilter(string? filterKey)
    {
        SelectedFilter = filterKey switch
        {
            "Safe" => FindingFilterCategory.SafeCleanup,
            "Relocate" => FindingFilterCategory.Relocate,
            "Admin" => FindingFilterCategory.AdminQueue,
            _ => FindingFilterCategory.All
        };
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void OpenSelectedPath()
    {
        if (SelectedFinding is null) return;
        var target = SelectedFinding.TargetPath;

        try
        {
            if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
            }
            else if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            }
            else
            {
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
                }
                else
                {
                    StatusBannerText = "Đường dẫn này do Windows bảo vệ hoặc hiện không truy cập trực tiếp bằng Explorer.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Không thể mở File Explorer: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopySelectedPath()
    {
        if (SelectedFinding is null) return;
        try
        {
            Clipboard.SetText(SelectedFinding.TargetPath);
            StatusBannerText = $"Đã sao chép đường dẫn: {SelectedFinding.TargetPath}";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Không thể sao chép vào Clipboard: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ScanDriveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Findings.Clear();
        SelectedFinding = null;
        ScanProgressPercent = 5;

        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressPercent = p.TotalRules > 0 ? Math.Max(8, p.CompletedRules * 100.0 / p.TotalRules) : 0;
            StatusBannerText = $"Đang phân tích ({p.CompletedRules}/{p.TotalRules}) • Đã ghi nhận {FindingItemViewModel.FormatBytes(p.CumulativeBytesDiscovered)}";
        });

        try
        {
            var report = await _orchestrator.AnalyzeAsync(SelectedDrive, progress: progress, cancellationToken: _scanCts.Token);
            _hasScannedAtLeastOnce = true;

            var maxBytes = report.Findings.Count > 0 ? report.Findings.Max(x => x.SizeInBytes) : 0L;
            foreach (var f in report.Findings)
            {
                Findings.Add(new FindingItemViewModel(f, maxBytes, UpdateSelectionMetrics));
            }

            AllCount = Findings.Count;
            SafeCount = Findings.Count(f => f.Model.Impact.Level is ImpactLevel.SAFE_CACHE_TIER1 or ImpactLevel.REBUILD_ON_DEMAND);
            RelocateCount = Findings.Count(f => f.Model.Impact.Level == ImpactLevel.RELOCATE_TO_SECONDARY_DRIVE);
            AdminCount = Findings.Count(f => f.Model.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION || f.Model.Impact.RequiresAdmin);

            StorageBar.UpdateFromMetrics(
                report.DriveName,
                report.TotalCapacityBytes,
                report.FreeBytesBeforeScan,
                report.SafeReclaimableBytes,
                report.RelocatableBytes,
                report.AdminReclaimableBytes);

            RefreshFilterAndEmptyState();
            UpdateSelectionMetrics();

            var secondsStr = string.Format(ViCulture, "{0:F1}", report.DurationMs / 1000.0);
            var cleanDrive = report.DriveName.TrimEnd('\\', '/');
            StatusBannerText = $"Đã phân tích ổ {cleanDrive} ({secondsStr} giây) • {Findings.Count} mục phát hiện, có thể dọn an toàn {FindingItemViewModel.FormatBytes(report.SafeReclaimableBytes)}.";
        }
        catch (OperationCanceledException)
        {
            StatusBannerText = "Đã dừng quét ổ đĩa.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Không thể hoàn tất quét ổ đĩa: {ex.Message}";
        }
        finally
        {
            ScanProgressPercent = 0;
            _scanCts?.Dispose();
            _scanCts = null;
            IsBusy = false;
            RefreshFilterAndEmptyState();
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
            StatusBannerText = "Chưa có mục nào được tích chọn để chạy xem trước (Dry-Run).";
            return;
        }

        IsBusy = true;
        try
        {
            var simReport = await _orchestrator.CleanAsync(selected, dryRun: true);
            LastDryRunSummary = $"Xem trước Dry-Run: Có thể giải phóng {FindingItemViewModel.FormatBytes(simReport.TotalBytesReclaimed)} ({simReport.TotalFilesProcessed:N0} tệp, tự động bỏ qua {simReport.TotalSkippedLockedCount:N0} tệp đang mở)";
            StatusBannerText = $"Hoàn tất xem trước (chưa xóa tệp nào) • Dự kiến thu hồi {FindingItemViewModel.FormatBytes(simReport.TotalBytesReclaimed)}.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi khi chạy xem trước: {ex.Message}";
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
            StatusBannerText = "Hãy tích chọn ít nhất một mục bộ nhớ đệm an toàn trước khi dọn dẹp.";
            return;
        }

        IsBusy = true;
        CleanupBatchReport preview;
        try
        {
            preview = await _orchestrator.CleanAsync(selected, dryRun: true);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            StatusBannerText = $"Lỗi kiểm tra trước khi dọn: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var confirmed = await Modal.ShowConfirmAsync(
            title: "Xác nhận dọn dẹp bộ nhớ đệm",
            primaryMessage: $"NexusClean đã kiểm tra trước {selected.Length} mục bạn chọn và sẵn sàng giải phóng khoảng {FindingItemViewModel.FormatBytes(preview.TotalBytesReclaimed)}.",
            bullets: new[]
            {
                $"Tổng số tệp bộ nhớ đệm có thể xóa ngay: {preview.TotalFilesProcessed:N0} tệp",
                $"Tệp đang mở được tự động bỏ qua an toàn: {preview.TotalSkippedLockedCount:N0} tệp",
                "Giữ nguyên 100% tài khoản đăng nhập trình duyệt, Cookie, dấu trang và extension mới nhất"
            },
            confirmText: $"Dọn dẹp {FindingItemViewModel.FormatBytes(preview.TotalBytesReclaimed)}",
            cancelText: "Để sau");

        if (!confirmed)
        {
            StatusBannerText = "Đã hủy thao tác dọn dẹp.";
            return;
        }

        IsBusy = true;
        try
        {
            var execReport = await _orchestrator.CleanAsync(selected, dryRun: false);
            var reclaimedText = FindingItemViewModel.FormatBytes(execReport.TotalBytesReclaimed);
            LastDryRunSummary = $"Lần dọn gần nhất: Đã giải phóng {reclaimedText} ({execReport.TotalFilesProcessed:N0} tệp)";
            StatusBannerText = $"Đã giải phóng thành công {reclaimedText} ({execReport.TotalFilesProcessed:N0} tệp). Nhật ký đã được lưu.";
        }
        catch (Exception ex)
        {
            StatusBannerText = $"Lỗi khi dọn dẹp: {ex.Message}";
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
            await Modal.ShowConfirmAsync(
                title: "Thông số chưa hợp lệ",
                primaryMessage: "Vui lòng kiểm tra lại cấu hình trước khi chạy tác vụ Quản trị viên:",
                bullets: errors,
                confirmText: "Đã hiểu",
                showCancel: false);
            return;
        }

        var confirmed = await Modal.ShowConfirmAsync(
            title: "Xác nhận chạy tác vụ Quản trị viên (UAC)",
            primaryMessage: "Tác vụ bảo trì hệ thống này sẽ mở lời nhắc cấp quyền Quản trị viên của Windows (UAC):",
            bullets: new[]
            {
                $"Lệnh thực thi: {previewDesc}",
                "Tiến trình chính của NexusClean vẫn chạy ở quyền người dùng chuẩn an toàn"
            },
            confirmText: "Tiếp tục với quyền Admin",
            cancelText: "Hủy");

        if (!confirmed) return;

        IsBusy = true;
        try
        {
            var res = await _adminExecutor.ExecuteElevatedAsync(req);
            if (res.WasCancelledByUser)
            {
                StatusBannerText = "Bạn đã từ chối lời nhắc UAC — tác vụ quản trị viên đã được hủy.";
                return;
            }

            StatusBannerText = string.Join(" | ", res.Outcomes.Select(o => o.MessageVi));
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
            ? $"Bộ quy tắc hợp lệ: Đã nạp {validation.ValidRuleCount} quy tắc từ '{Path.GetFileName(validation.SourceFilePath)}'."
            : $"Phát hiện {validation.Issues.Count} lỗi trong tệp quy tắc: {string.Join("; ", validation.Issues.Select(i => i.MessageVi))}";
    }

    [RelayCommand]
    private async Task ToggleGrowthWatcherAsync()
    {
        if (IsWatcherEnabled)
        {
            await _growthWatcher.StopAsync();
            IsWatcherEnabled = false;
            WatcherButtonLabel = "Cảnh báo đầy ổ: Tắt";
            StatusBannerText = "Đã tắt theo dõi dung lượng nền.";
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
            WatcherButtonLabel = "Cảnh báo đầy ổ: Đang bật";
            StatusBannerText = "Đã bật cảnh báo nền (thông báo khi ổ trống dưới 15 GB hoặc thư mục nóng tăng thêm 2 GB).";
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

        if (selected.Count == 0)
        {
            SelectedSummaryText = "Chưa chọn mục nào để dọn dẹp";
            CleanButtonLabel = "Dọn dẹp đã chọn";
        }
        else
        {
            var formatted = FindingItemViewModel.FormatBytes(bytes);
            SelectedSummaryText = $"Đã chọn {selected.Count} mục an toàn • {formatted}";
            CleanButtonLabel = $"Dọn dẹp ({formatted})";
        }
    }
}
