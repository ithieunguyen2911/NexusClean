using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.UseCases;

/// <summary>
/// Evaluates the 4-link ISafetyGuard Chain of Responsibility in deterministic order with fail-closed semantics.
/// </summary>
public sealed class EvaluateImpactUseCase
{
    private readonly IReadOnlyList<ISafetyGuard> _orderedGuards;

    public EvaluateImpactUseCase(IEnumerable<ISafetyGuard> guards)
    {
        ArgumentNullException.ThrowIfNull(guards);
        _orderedGuards = guards.OrderBy(g => g.Order).ToArray();
    }

    public StorageFinding Evaluate(StorageFinding finding, ISystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(snapshot);

        var verdicts = new List<GuardVerdict>(_orderedGuards.Count);
        foreach (var guard in _orderedGuards)
        {
            try
            {
                var verdict = guard.Inspect(finding, snapshot);
                verdicts.Add(verdict);
                if (verdict.Severity == GuardSeverity.Block)
                {
                    // Short-circuit immediately on Block per Constitution Principle III.
                    break;
                }
            }
            catch (Exception ex)
            {
                // Fail-closed: any unexpected guard exception immediately locks the finding!
                verdicts.Add(GuardVerdict.Block(
                    guard.GuardName,
                    "GUARD_INTERNAL_FAILURE",
                    $"Khóa an toàn tự động do lỗi kiểm tra tại {guard.GuardName}: {ex.Message}"));
                break;
            }
        }

        var probePaths = finding.SubPathsToClean.Count > 0
            ? finding.SubPathsToClean
            : new[] { finding.TargetPath };
        var detectedLocks = snapshot.GetLockingProcesses(probePaths);

        finding.ApplyGuardVerdicts(verdicts, detectedLocks);
        return finding;
    }
}

/// <summary>
/// Generates safe secondary-drive relocation plans with +20% free-space buffer verification.
/// </summary>
public sealed class PlanSecondaryRelocationUseCase
{
    private readonly ISystemSnapshotProvider _snapshotProvider;

    public PlanSecondaryRelocationUseCase(ISystemSnapshotProvider snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    public RelocationPlan CreatePlan(StorageFinding finding, string? preferredTargetDrive = null)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var snapshot = _snapshotProvider.CaptureSnapshot();
        var sourceRoot = NormalizeDrivePrefix(Path.GetPathRoot(finding.TargetPath) ?? "C:");

        var secondaryCandidates = snapshot.FixedVolumes
            .Where(v => v.IsReady &&
                        !string.Equals(
                            NormalizeDrivePrefix(v.DriveName),
                            sourceRoot,
                            StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.AvailableFreeBytes)
            .ToList();

        VolumeSnapshot? chosenVolume = null;
        if (!string.IsNullOrWhiteSpace(preferredTargetDrive))
        {
            var normalizedPreferred = NormalizeDrivePrefix(preferredTargetDrive);
            if (!normalizedPreferred.EndsWith(':'))
            {
                normalizedPreferred += ":";
            }

            chosenVolume = secondaryCandidates.FirstOrDefault(v =>
                string.Equals(NormalizeDrivePrefix(v.DriveName), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
        }

        chosenVolume ??= secondaryCandidates.FirstOrDefault();

        if (chosenVolume is null)
        {
            return new RelocationPlan(
                findingId: finding.Id,
                sourcePath: finding.TargetPath,
                sourceSizeInBytes: finding.SizeInBytes,
                recommendedTargetDrive: null,
                recommendedTargetPath: null,
                availableTargetBytes: 0,
                stepByStepGuideVi: new[]
                {
                    "Máy tính hiện chỉ có duy nhất 1 phân vùng ổ đĩa cố định (không phát hiện ổ phụ như D:).",
                    "Tuyệt đối KHÔNG bấm xóa hàng loạt thư mục dữ liệu người dùng này để tránh mất lịch sử công việc.",
                    "Khuyến nghị sao lưu các tệp/ảnh cũ sang ổ cứng gắn ngoài hoặc lưu trữ đám mây (Google Drive / OneDrive), sau đó lọc xóa theo độ tuổi tệp trong cài đặt gốc của ứng dụng."
                });
        }

        var drivePrefix = NormalizeDrivePrefix(chosenVolume.DriveName);
        var targetFolder = InferRecommendedTargetFolder(finding, drivePrefix);
        var steps = BuildVietnameseMigrationSteps(finding, targetFolder, chosenVolume.AvailableFreeBytes);

        return new RelocationPlan(
            findingId: finding.Id,
            sourcePath: finding.TargetPath,
            sourceSizeInBytes: finding.SizeInBytes,
            recommendedTargetDrive: drivePrefix,
            recommendedTargetPath: targetFolder,
            availableTargetBytes: chosenVolume.AvailableFreeBytes,
            stepByStepGuideVi: steps);
    }

    private static string InferRecommendedTargetFolder(StorageFinding finding, string drivePrefix)
    {
        if (finding.RuleId.Contains("zalo", StringComparison.OrdinalIgnoreCase))
        {
            return $@"{drivePrefix}\ZaloData";
        }

        if (finding.RuleId.Contains("wsl", StringComparison.OrdinalIgnoreCase))
        {
            return $@"{drivePrefix}\WSL";
        }

        if (finding.RuleId.Contains("pagefile", StringComparison.OrdinalIgnoreCase))
        {
            return $@"{drivePrefix}\pagefile.sys";
        }

        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(finding.TargetPath));
        return $@"{drivePrefix}\NexusCleanRelocated\{leaf}";
    }

    private static IReadOnlyList<string> BuildVietnameseMigrationSteps(
        StorageFinding finding,
        string targetFolder,
        long availableBytes)
    {
        var requiredBytes = (long)Math.Ceiling(finding.SizeInBytes * 1.20d);
        var spaceNote = availableBytes >= requiredBytes
            ? $"Đã kiểm tra dung lượng trống: Đạt yêu cầu an toàn (>= 120% dung lượng gốc)."
            : $"CẢNH BÁO: Dung lượng trống trên ổ đích ({FormatSize(availableBytes)}) thấp hơn mức đệm an toàn 120% ({FormatSize(requiredBytes)}). Hãy dọn ổ đích trước khi di chuyển!";

        if (finding.RuleId.Contains("zalo", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                spaceNote,
                $"Bước 1: Tạo sẵn thư mục đích '{targetFolder}' trên ổ đĩa phụ.",
                "Bước 2: Mở ứng dụng Zalo PC -> Nhấn biểu tượng Bánh răng (Cài đặt) góc dưới bên trái -> Chọn 'Quản lý dữ liệu'.",
                $"Bước 3: Tại mục 'Vị trí lưu trữ', nhấn 'Thay đổi' và chọn '{targetFolder}' để Zalo tự động di chuyển toàn bộ tin nhắn, ảnh và tệp đính kèm an toàn sang ổ phụ."
            };
        }

        if (finding.RuleId.Contains("wsl", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                spaceNote,
                "Bước 1: Đóng tất cả cửa sổ terminal Linux / Docker Desktop đang chạy.",
                "Bước 2: Mở PowerShell (Admin) hoặc dùng Hàng đợi Quản trị viên của NexusClean.",
                $"Bước 3: Chạy 'wsl --shutdown', sau đó chạy 'wsl --manage <Tên_Distro> --move {targetFolder}' để di dời toàn bộ đĩa ảo ext4.vhdx sang ổ phụ."
            };
        }

        if (finding.RuleId.Contains("pagefile", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                spaceNote,
                "Bước 1: Nhấn Win + R, nhập 'sysdm.cpl' -> Chọn tab 'Advanced' -> Nhấn 'Settings...' trong khung Performance.",
                "Bước 2: Chọn tab 'Advanced' -> Nhấn 'Change...' tại mục Virtual memory.",
                "Bước 3: Bỏ tích 'Automatically manage...', đặt ổ C: về kích thước cố định hợp lý (ví dụ 4096–8192 MB) và kích hoạt System managed size trên ổ SSD phụ D:, sau đó khởi động lại máy."
            };
        }

        return new[]
        {
            spaceNote,
            $"Bước 1: Đóng ứng dụng đang sử dụng thư mục '{finding.TargetPath}'.",
            $"Bước 2: Cấu hình lại đường dẫn lưu trữ trong phần Cài đặt của ứng dụng sang '{targetFolder}'."
        };
    }

    private static string NormalizeDrivePrefix(string rawDrive) =>
        rawDrive.Trim().TrimEnd('\\', '/');

    private static string FormatSize(long bytes) =>
        $"{bytes / (1024d * 1024d * 1024d):F2} GB";
}
