using System.Text.RegularExpressions;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Domain.Guards;

/// <summary>
/// Guard #1 (Order 10): Unconditionally blocks direct deletion of Windows, Program Files,
/// UserProfile, AppData\Local, AppData\Roaming, or any drive root.
/// </summary>
public sealed class RootSystemFolderGuard : ISafetyGuard
{
    private static readonly Regex DriveRootRegex = new(
        @"^[A-Za-z]:\\?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string GuardName => nameof(RootSystemFolderGuard);
    public int Order => 10;

    public static IReadOnlyList<string> BuildImmutableProtectedRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfPresent(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
                }
                catch
                {
                    // Ignore malformed environment values.
                }
            }
        }

        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        AddIfPresent(@"C:\Windows");
        AddIfPresent(@"C:\Program Files");
        AddIfPresent(@"C:\Program Files (x86)");
        AddIfPresent(@"C:\Users");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddIfPresent(Path.Combine(localAppData, "NexusClean"));
        }

        return roots.ToArray();
    }

    public GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(snapshot);

        var protectedSet = new HashSet<string>(BuildImmutableProtectedRoots(), StringComparer.OrdinalIgnoreCase);
        foreach (var extra in snapshot.AdditionalProtectedRoots)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                var expanded = Environment.ExpandEnvironmentVariables(extra);
                if (!expanded.Contains('%'))
                {
                    try
                    {
                        protectedSet.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded)));
                    }
                    catch
                    {
                        // Ignore invalid custom path entry.
                    }
                }
            }
        }

        var pathsToCheck = new List<string> { finding.TargetPath };
        pathsToCheck.AddRange(finding.SubPathsToClean);

        foreach (var candidate in pathsToCheck)
        {
            if (DriveRootRegex.IsMatch(candidate.Trim()))
            {
                return GuardVerdict.Block(
                    GuardName,
                    "PROTECTED_DRIVE_ROOT",
                    $"Cấm tuyệt đối thao tác xóa trực tiếp gốc ổ đĩa '{candidate}'.");
            }

            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (protectedSet.Contains(normalized))
            {
                return GuardVerdict.Block(
                    GuardName,
                    "PROTECTED_ROOT_PATH",
                    $"Thư mục cốt lõi '{normalized}' được khóa bảo vệ cứng để giữ an toàn hệ điều hành và tài khoản.");
            }

            // Never allow deleting C:\Windows\* core OS subdirectories directly.
            if (normalized.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(@"C:\Windows\Temp", StringComparison.OrdinalIgnoreCase))
            {
                return GuardVerdict.Block(
                    GuardName,
                    "PROTECTED_WINDOWS_SYSTEM_SUBTREE",
                    $"Nhánh hệ điều hành '{normalized}' thuộc vùng bất khả xâm phạm của Windows.");
            }
        }

        return GuardVerdict.Allow(GuardName);
    }
}
