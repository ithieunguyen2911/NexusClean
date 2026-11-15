using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusClean.UI.ViewModels;

public enum AppThemeMode
{
    Light = 0,
    Dark = 1
}

public enum FindingFilterCategory
{
    All = 0,
    SafeCleanup = 1,
    Relocate = 2,
    AdminQueue = 3
}

public enum BadgeTone
{
    Safe = 0,
    Rebuild = 1,
    Relocate = 2,
    Admin = 3,
    Protected = 4
}

/// <summary>
/// Computes proportional star column widths for the Windows 11-style 5-segment storage breakdown bar.
/// </summary>
public sealed partial class StorageBarBreakdownViewModel : ObservableObject
{
    [ObservableProperty] private string _driveTitle = "Ổ đĩa hệ thống (C:)";
    [ObservableProperty] private string _usageHeadline = "Nhấn 'Quét ổ đĩa' để phân tích cơ cấu dung lượng hiện tại";
    [ObservableProperty] private string _otherUsedLabel = "Khác: --";
    [ObservableProperty] private string _safeLabel = "Dọn an toàn: 0 B";
    [ObservableProperty] private string _relocateLabel = "Nên dời ổ: 0 B";
    [ObservableProperty] private string _adminLabel = "Hệ thống/WSL: 0 B";
    [ObservableProperty] private string _freeLabel = "Còn trống: --";

    [ObservableProperty] private GridLength _otherUsedWidth = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _safeWidth = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _relocateWidth = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _adminWidth = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _freeWidth = new(1, GridUnitType.Star);

    public void UpdateFromMetrics(
        string driveName,
        long totalBytes,
        long freeBytes,
        long safeBytes,
        long relocateBytes,
        long adminBytes)
    {
        var cleanDrive = driveName.TrimEnd('\\', '/');
        DriveTitle = $"Ổ đĩa cục bộ ({cleanDrive})";

        if (totalBytes <= 0)
        {
            UsageHeadline = $"Đã phân tích {cleanDrive}";
            OtherUsedWidth = new GridLength(0, GridUnitType.Star);
            SafeWidth = ToStar(safeBytes, 1);
            RelocateWidth = ToStar(relocateBytes, 1);
            AdminWidth = ToStar(adminBytes, 1);
            FreeWidth = new GridLength(0.2, GridUnitType.Star);
            return;
        }

        var usedTotal = Math.Max(0, totalBytes - freeBytes);
        var categorized = safeBytes + relocateBytes + adminBytes;
        var otherUsed = Math.Max(0, usedTotal - categorized);
        var usedPct = usedTotal * 100.0 / totalBytes;

        UsageHeadline = $"Đã dùng {FindingItemViewModel.FormatBytes(usedTotal)} / {FindingItemViewModel.FormatBytes(totalBytes)} ({usedPct:F1}%)  •  Còn trống {FindingItemViewModel.FormatBytes(freeBytes)}";

        OtherUsedLabel = $"Hệ điều hành & ứng dụng ({FindingItemViewModel.FormatBytes(otherUsed)})";
        SafeLabel = $"Dọn an toàn ({FindingItemViewModel.FormatBytes(safeBytes)})";
        RelocateLabel = $"Nên dời ổ phụ ({FindingItemViewModel.FormatBytes(relocateBytes)})";
        AdminLabel = $"Bảo trì Admin ({FindingItemViewModel.FormatBytes(adminBytes)})";
        FreeLabel = $"Còn trống ({FindingItemViewModel.FormatBytes(freeBytes)})";

        OtherUsedWidth = ToStar(otherUsed, totalBytes);
        SafeWidth = ToStar(safeBytes, totalBytes);
        RelocateWidth = ToStar(relocateBytes, totalBytes);
        AdminWidth = ToStar(adminBytes, totalBytes);
        FreeWidth = ToStar(freeBytes, totalBytes);
    }

    private static GridLength ToStar(long partBytes, long totalBytes)
    {
        if (partBytes <= 0)
        {
            return new GridLength(0, GridUnitType.Pixel);
        }

        var ratio = Math.Max(0.012, (double)partBytes / Math.Max(1, totalBytes));
        return new GridLength(ratio * 100.0, GridUnitType.Star);
    }
}

/// <summary>
/// In-window Fluent ContentDialog overlay replacing legacy Win32 MessageBox popups.
/// </summary>
public sealed partial class ConfirmationModalViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _tcs;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _primaryMessage = string.Empty;
    [ObservableProperty] private string _confirmButtonText = "Xác nhận";
    [ObservableProperty] private string _cancelButtonText = "Để sau";
    [ObservableProperty] private bool _showCancelButton = true;

    public ObservableCollection<string> BulletItems { get; } = new();

    public Task<bool> ShowConfirmAsync(
        string title,
        string primaryMessage,
        IEnumerable<string> bullets,
        string confirmText,
        string cancelText = "Để sau",
        bool showCancel = true)
    {
        _tcs?.TrySetResult(false);
        Title = title;
        PrimaryMessage = primaryMessage;
        ConfirmButtonText = confirmText;
        CancelButtonText = cancelText;
        ShowCancelButton = showCancel;

        BulletItems.Clear();
        foreach (var b in bullets)
        {
            BulletItems.Add(b);
        }

        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsOpen = true;
        return _tcs.Task;
    }

    [RelayCommand]
    private void Confirm()
    {
        IsOpen = false;
        _tcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        _tcs?.TrySetResult(false);
    }
}
