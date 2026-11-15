using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Domain.Guards;

/// <summary>
/// Guard #3 (Order 30): Detects active OS process locks on candidate cleanup paths so cleanup never hangs.
/// </summary>
public sealed class RunningProcessLockGuard : ISafetyGuard
{
    public string GuardName => nameof(RunningProcessLockGuard);
    public int Order => 30;

    public GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(snapshot);

        var matchedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hintedProcess in finding.Impact.LockingProcesses)
        {
            if (snapshot.RunningProcessNames.Contains(hintedProcess))
            {
                matchedProcesses.Add(hintedProcess);
            }
        }

        var probePaths = finding.SubPathsToClean.Count > 0
            ? finding.SubPathsToClean
            : new[] { finding.TargetPath };

        foreach (var activeLocker in snapshot.GetLockingProcesses(probePaths))
        {
            if (!string.IsNullOrWhiteSpace(activeLocker))
            {
                matchedProcesses.Add(activeLocker);
            }
        }

        if (matchedProcesses.Count == 0)
        {
            return GuardVerdict.Allow(GuardName);
        }

        var joined = string.Join(", ", matchedProcesses.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        if (finding.ActionType is CleanupActionType.RelocateOrUnregisterAdmin or CleanupActionType.ResizeShadowStorageAdmin)
        {
            return GuardVerdict.RequireElevation(
                GuardName,
                "SYSTEM_SERVICE_LOCKED",
                $"Đang được giữ khóa bởi dịch vụ hệ thống ({joined}). Cần thực thi qua hàng đợi Quản trị viên (Admin).");
        }

        return GuardVerdict.Warn(
            GuardName,
            "PROCESS_LOCK_DETECTED",
            $"Phát hiện tiến trình đang chạy ({joined}) giữ một số tệp; các tệp đang mở sẽ được tự động bỏ qua an toàn.");
    }
}

/// <summary>
/// Guard #4 (Order 40): Isolates operations requiring Windows Administrator token into the Elevated Maintenance Queue.
/// </summary>
public sealed class AdminElevationGuard : ISafetyGuard
{
    public string GuardName => nameof(AdminElevationGuard);
    public int Order => 40;

    public GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(snapshot);

        var needsAdmin =
            finding.Impact.RequiresAdmin ||
            finding.Impact.Level == ImpactLevel.REQUIRES_ADMIN_ACTION ||
            finding.ActionType is CleanupActionType.RelocateOrUnregisterAdmin or CleanupActionType.ResizeShadowStorageAdmin;

        if (!needsAdmin)
        {
            return GuardVerdict.Allow(GuardName);
        }

        if (!snapshot.IsCurrentProcessElevated)
        {
            return GuardVerdict.RequireElevation(
                GuardName,
                "UAC_ADMIN_REQUIRED",
                "Mục này do hệ thống Windows/WSL quản lý; đã tách riêng sang Hàng đợi Thực thi Quyền Quản trị viên (UAC).");
        }

        return GuardVerdict.Allow(GuardName, "ELEVATED_CONTEXT_READY");
    }
}
