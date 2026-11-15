using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Domain.Guards;

/// <summary>
/// Guard #2 (Order 20): Guarantees 100% preservation of browser credentials, cookies, bookmarks,
/// history, preferences, and extensions across Chrome, Edge, Brave, and Cốc Cốc.
/// </summary>
public sealed class BrowserProfilePreservationGuard : ISafetyGuard
{
    public static readonly IReadOnlySet<string> ImmutableProtectedBrowserAssets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Login Data",
            "Login Data-journal",
            "Login Data For Account",
            "Login Data For Account-journal",
            "Cookies",
            "Cookies-journal",
            "Bookmarks",
            "Bookmarks.bak",
            "Preferences",
            "Secure Preferences",
            "Web Data",
            "Web Data-journal",
            "History",
            "History-journal",
            "Extension Cookies",
            "Extensions",
            "Local Extension Settings",
            "Sync Data",
            "Sessions",
            "Local State"
        };

    public static readonly IReadOnlyList<string> AllowedBrowserCacheRelativeSuffixes = new[]
    {
        @"Cache\Cache_Data",
        @"Code Cache\js",
        @"Code Cache\wasm",
        @"GPUCache",
        @"Service Worker\CacheStorage",
        @"ShaderCache",
        @"GrShaderCache",
        @"DawnWebGPUCache",
        @"DawnGraphiteCache"
    };

    private static readonly string[] BrowserUserDataMarkers =
    {
        @"\Google\Chrome\User Data",
        @"\Microsoft\Edge\User Data",
        @"\BraveSoftware\Brave-Browser\User Data",
        @"\CocCoc\Browser\User Data"
    };

    public string GuardName => nameof(BrowserProfilePreservationGuard);
    public int Order => 20;

    public GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(snapshot);

        var protectedNames = new HashSet<string>(ImmutableProtectedBrowserAssets, StringComparer.OrdinalIgnoreCase);
        foreach (var extra in snapshot.AdditionalProtectedFileNames)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                protectedNames.Add(extra.Trim());
            }
        }

        // When a selective/SemVer strategy specifies explicit SubPathsToClean, TargetPath is only a container root
        // (e.g., .vscode\extensions or Chrome\User Data) and is never deleted itself; inspect the actual target subpaths.
        IReadOnlyList<string> effectivePaths =
            finding.SubPathsToClean.Count > 0 &&
            finding.ActionType is CleanupActionType.SelectiveSubfolderPurge or CleanupActionType.SemVerDuplicatePrune
                ? finding.SubPathsToClean
                : new List<string>(1 + finding.SubPathsToClean.Count) { finding.TargetPath }
                    .Concat(finding.SubPathsToClean)
                    .ToArray();

        // 1. Direct filename / segment check across all effective target paths.
        foreach (var path in effectivePaths)
        {
            var leafName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (protectedNames.Contains(leafName))
            {
                return GuardVerdict.Block(
                    GuardName,
                    "CRITICAL_CREDENTIAL_FILE",
                    $"Tệp/thư mục nhạy cảm '{leafName}' chứa thông tin đăng nhập hoặc Profile trình duyệt — bị khóa bảo vệ 100%.");
            }

            if (ContainsProtectedBrowserSegment(path, protectedNames, out var matchedAsset))
            {
                return GuardVerdict.Block(
                    GuardName,
                    "CRITICAL_CREDENTIAL_SUBPATH",
                    $"Đường dẫn '{path}' chạm vào dữ liệu nhạy cảm '{matchedAsset}' của trình duyệt.");
            }
        }

        // 2. Strict allowlist enforcement whenever TargetPath enters a Chromium User Data tree.
        if (IsInsideBrowserUserData(finding.TargetPath))
        {
            if (finding.ActionType != CleanupActionType.SelectiveSubfolderPurge)
            {
                return GuardVerdict.Block(
                    GuardName,
                    "NON_SELECTIVE_BROWSER_ACTION",
                    $"Thư mục trình duyệt '{finding.TargetPath}' bắt buộc phải dùng cơ chế lọc SelectiveSubfolderPurge; cấm xóa toàn bộ thư mục Profile.");
            }

            if (finding.SubPathsToClean.Count == 0)
            {
                return GuardVerdict.Block(
                    GuardName,
                    "EMPTY_BROWSER_SUBPATHS",
                    "Không có thư mục bộ nhớ đệm hợp lệ nào được chỉ định bên trong Profile trình duyệt.");
            }

            foreach (var subPath in finding.SubPathsToClean)
            {
                if (!IsAllowedBrowserCachePath(subPath))
                {
                    return GuardVerdict.Block(
                        GuardName,
                        "UNAPPROVED_BROWSER_SUBFOLDER",
                        $"Thư mục con '{subPath}' không nằm trong danh sách bộ nhớ đệm an toàn cho phép xóa của trình duyệt.");
                }
            }
        }

        return GuardVerdict.Allow(GuardName);
    }

    public static bool IsInsideBrowserUserData(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(path).Replace('/', '\\');
        return BrowserUserDataMarkers.Any(marker =>
            normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAllowedBrowserCachePath(string subPath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(subPath).Replace('/', '\\');
        return AllowedBrowserCacheRelativeSuffixes.Any(suffix =>
            normalized.EndsWith(@"\" + suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsProtectedBrowserSegment(
        string path,
        IReadOnlySet<string> protectedNames,
        out string matchedAsset)
    {
        matchedAsset = string.Empty;
        if (!IsInsideBrowserUserData(path))
        {
            return false;
        }

        var segments = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (protectedNames.Contains(segment))
            {
                matchedAsset = segment;
                return true;
            }
        }

        return false;
    }
}
