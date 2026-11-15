using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NexusClean.Application.Models;

namespace NexusClean.Infrastructure.OS;

/// <summary>
/// Parameterized VSS shadow storage manager preserving at least the latest restore point (&gt;= 2 GB).
/// </summary>
public sealed class VssShadowStorageManager
{
    private static readonly Regex VolumeLetterRegex = new(@"^[A-Za-z]:$", RegexOptions.Compiled);

    public async Task<AdminOperationOutcome> ResizeShadowStorageAsync(
        string findingId,
        string volume,
        int maxSizeGb,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(volume) || !VolumeLetterRegex.IsMatch(volume.Trim()))
        {
            return new AdminOperationOutcome(
                findingId,
                AdminOperationKind.ResizeVssShadowStorage,
                false,
                2,
                $"Ký hiệu ổ đĩa '{volume}' không hợp lệ (ví dụ hợp lệ: C:).");
        }

        if (maxSizeGb < 2)
        {
            return new AdminOperationOutcome(
                findingId,
                AdminOperationKind.ResizeVssShadowStorage,
                false,
                2,
                "Giới hạn Shadow Copy tối thiểu phải từ 2 GB trở lên để giữ điểm khôi phục gần nhất.");
        }

        var normalizedVol = volume.Trim().ToUpperInvariant();
        var psi = new ProcessStartInfo("vssadmin.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("resize");
        psi.ArgumentList.Add("shadowstorage");
        psi.ArgumentList.Add($"/for={normalizedVol}");
        psi.ArgumentList.Add($"/on={normalizedVol}");
        psi.ArgumentList.Add($"/maxsize={maxSizeGb}GB");

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return new AdminOperationOutcome(
                findingId,
                AdminOperationKind.ResizeVssShadowStorage,
                false,
                -1,
                "Không thể khởi chạy công cụ hệ thống vssadmin.exe.");
        }

        var stdOut = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stdErr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var ok = proc.ExitCode == 0;
        return new AdminOperationOutcome(
            findingId,
            AdminOperationKind.ResizeVssShadowStorage,
            ok,
            proc.ExitCode,
            ok
                ? $"Đã giới hạn Shadow Copy trên {normalizedVol} xuống tối đa {maxSizeGb} GB thành công."
                : $"vssadmin trả về mã {proc.ExitCode}.",
            stdOut.Trim(),
            stdErr.Trim());
    }
}

/// <summary>
/// Isolated UAC-elevated runner executing strictly validated AdminMaintenanceRequest manifests.
/// Never executes raw shell command strings.
/// </summary>
public sealed class ElevatedAdminScriptRunner : IElevatedAdminExecutor
{
    private static readonly Regex VolumeLetterRegex = new(@"^[A-Za-z]:$", RegexOptions.Compiled);
    private static readonly Regex SafeDistroNameRegex = new(@"^[A-Za-z0-9_.-]{1,64}$", RegexOptions.Compiled);

    public static readonly JsonSerializerOptions IpcJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    private readonly VssShadowStorageManager _vssManager;
    private readonly string? _cliExecutableOverride;

    public ElevatedAdminScriptRunner(
        VssShadowStorageManager? vssManager = null,
        string? cliExecutableOverride = null)
    {
        _vssManager = vssManager ?? new VssShadowStorageManager();
        _cliExecutableOverride = cliExecutableOverride;
    }

    public IReadOnlyList<string> ValidateRequest(AdminMaintenanceRequest request)
    {
        var errors = new List<string>();
        if (request is null)
        {
            errors.Add("Yêu cầu AdminMaintenanceRequest rỗng.");
            return errors;
        }

        if (request.Operations is null || request.Operations.Count == 0)
        {
            errors.Add("Danh sách tác vụ quản trị viên (Operations) không được rỗng.");
            return errors;
        }

        foreach (var op in request.Operations)
        {
            if (string.IsNullOrWhiteSpace(op.FindingId))
            {
                errors.Add("Thiếu mã định danh findingId trong tác vụ quản trị viên.");
            }

            switch (op.OperationKind)
            {
                case AdminOperationKind.ResizeVssShadowStorage:
                    if (string.IsNullOrWhiteSpace(op.Volume) || !VolumeLetterRegex.IsMatch(op.Volume.Trim()))
                    {
                        errors.Add($"Ổ đĩa '{op.Volume}' không hợp lệ cho ResizeVssShadowStorage.");
                    }

                    if (op.MaxShadowSizeGb is null or < 2 or > 200)
                    {
                        errors.Add($"maxShadowSizeGb ({op.MaxShadowSizeGb}) phải nằm trong khoảng 2..200 GB để bảo toàn điểm khôi phục.");
                    }
                    break;

                case AdminOperationKind.MoveWslDistribution:
                    if (string.IsNullOrWhiteSpace(op.DistroName) || !SafeDistroNameRegex.IsMatch(op.DistroName.Trim()))
                    {
                        errors.Add($"Tên bản phân phối WSL '{op.DistroName}' không hợp lệ.");
                    }

                    if (string.IsNullOrWhiteSpace(op.TargetDirectory))
                    {
                        errors.Add("Thiếu thư mục đích targetDirectory khi di dời WSL.");
                    }
                    else
                    {
                        var root = Path.GetPathRoot(op.TargetDirectory);
                        if (string.IsNullOrEmpty(root) || root.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Thư mục đích WSL '{op.TargetDirectory}' phải nằm trên ổ đĩa phụ (khác ổ C:).");
                        }
                    }
                    break;

                default:
                    errors.Add($"Loại tác vụ '{op.OperationKind}' không nằm trong danh sách cho phép.");
                    break;
            }
        }

        return errors;
    }

    public async Task<AdminMaintenanceResult> ExecuteElevatedAsync(
        AdminMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", validationErrors));
        }

        var ipcDir = Path.Combine(Path.GetTempPath(), "NexusClean", "AdminIpc");
        Directory.CreateDirectory(ipcDir);
        var reqPath = Path.Combine(ipcDir, $"req-{request.RequestId:N}.json");
        var resPath = Path.Combine(ipcDir, $"res-{request.RequestId:N}.json");

        try
        {
            await File.WriteAllTextAsync(
                reqPath,
                JsonSerializer.Serialize(request, IpcJsonOptions),
                cancellationToken).ConfigureAwait(false);

            var cliExe = ResolveCliExecutable();
            ProcessStartInfo psi;
            if (cliExe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo("dotnet")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    Arguments = $"\"{cliExe}\" admin-exec --request \"{reqPath}\" --output \"{resPath}\""
                };
            }
            else
            {
                psi = new ProcessStartInfo(cliExe)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    Arguments = $"admin-exec --request \"{reqPath}\" --output \"{resPath}\""
                };
            }

            try
            {
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Error 1223 = ERROR_CANCELLED (User declined UAC elevation prompt).
                return new AdminMaintenanceResult(
                    request.RequestId,
                    DateTime.UtcNow,
                    WasCancelledByUser: true,
                    Outcomes: Array.Empty<AdminOperationOutcome>());
            }

            if (File.Exists(resPath))
            {
                var json = await File.ReadAllTextAsync(resPath, cancellationToken).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<AdminMaintenanceResult>(json, IpcJsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }

            return new AdminMaintenanceResult(
                request.RequestId,
                DateTime.UtcNow,
                WasCancelledByUser: false,
                Outcomes: new[]
                {
                    new AdminOperationOutcome(
                        request.Operations[0].FindingId,
                        request.Operations[0].OperationKind,
                        false,
                        -1,
                        "Tiến trình quản trị viên kết thúc nhưng không tạo tệp phản hồi.")
                });
        }
        finally
        {
            TryDeleteTempFile(reqPath);
            TryDeleteTempFile(resPath);
        }
    }

    public async Task<AdminMaintenanceResult> ExecuteInCurrentElevatedProcessAsync(
        AdminMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", validationErrors));
        }

        var outcomes = new List<AdminOperationOutcome>(request.Operations.Count);
        foreach (var op in request.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (op.OperationKind == AdminOperationKind.ResizeVssShadowStorage)
            {
                var outcome = await _vssManager.ResizeShadowStorageAsync(
                    op.FindingId,
                    op.Volume!,
                    op.MaxShadowSizeGb!.Value,
                    cancellationToken).ConfigureAwait(false);
                outcomes.Add(outcome);
            }
            else if (op.OperationKind == AdminOperationKind.MoveWslDistribution)
            {
                var outcome = await MoveWslDistributionAsync(
                    op.FindingId,
                    op.DistroName!,
                    op.TargetDirectory!,
                    cancellationToken).ConfigureAwait(false);
                outcomes.Add(outcome);
            }
        }

        return new AdminMaintenanceResult(
            request.RequestId,
            DateTime.UtcNow,
            WasCancelledByUser: false,
            Outcomes: outcomes);
    }

    private static async Task<AdminOperationOutcome> MoveWslDistributionAsync(
        string findingId,
        string distroName,
        string targetDirectory,
        CancellationToken ct)
    {
        Directory.CreateDirectory(targetDirectory);

        using (var shutdownProc = Process.Start(new ProcessStartInfo("wsl.exe")
               {
                   UseShellExecute = false,
                   CreateNoWindow = true,
                   ArgumentList = { "--shutdown" }
               }))
        {
            if (shutdownProc is not null)
            {
                await shutdownProc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }

        var movePsi = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        movePsi.ArgumentList.Add("--manage");
        movePsi.ArgumentList.Add(distroName.Trim());
        movePsi.ArgumentList.Add("--move");
        movePsi.ArgumentList.Add(Path.GetFullPath(targetDirectory));

        using var proc = Process.Start(movePsi);
        if (proc is null)
        {
            return new AdminOperationOutcome(
                findingId,
                AdminOperationKind.MoveWslDistribution,
                false,
                -1,
                "Không thể khởi chạy wsl.exe.");
        }

        var stdOut = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stdErr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        var ok = proc.ExitCode == 0;
        return new AdminOperationOutcome(
            findingId,
            AdminOperationKind.MoveWslDistribution,
            ok,
            proc.ExitCode,
            ok
                ? $"Đã di chuyển máy ảo WSL '{distroName}' sang '{targetDirectory}' thành công."
                : $"wsl --manage trả về mã {proc.ExitCode}.",
            stdOut.Trim(),
            stdErr.Trim());
    }

    private string ResolveCliExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_cliExecutableOverride) && File.Exists(_cliExecutableOverride))
        {
            return _cliExecutableOverride;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "nexusclean.exe"),
            Path.Combine(baseDir, "nexusclean.dll"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "NexusClean.Cli", "bin", "Debug", "net8.0", "nexusclean.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "NexusClean.Cli", "bin", "Release", "net8.0", "nexusclean.exe"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore temp IPC cleanup failure.
        }
    }
}
