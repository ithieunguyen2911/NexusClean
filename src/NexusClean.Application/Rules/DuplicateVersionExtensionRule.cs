using System.Text.Json;
using System.Text.RegularExpressions;
using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.Rules;

/// <summary>
/// Detects superseded duplicate IDE extension versions (preserving highest SemVer + active extensions.json entries)
/// and stale .uuid temporary directories older than 24 hours.
/// </summary>
public sealed class DuplicateVersionExtensionRule : IDetectionRule
{
    private static readonly Regex ExtensionFolderRegex = new(
        @"^(?<id>[a-z0-9_-]+\.[a-z0-9_.-]+?)-(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?!(?:(?:win32|linux|darwin|alpine)-(?:x64|arm64|ia32)|universal|web)$)(?<prerelease>[0-9A-Za-z.-]+?))?(?:-(?<platform>(?:win32|linux|darwin|alpine)-(?:x64|arm64|ia32)|universal|web))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DotUuidFolderRegex = new(
        @"^\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly SignatureRuleDefinition _definition;

    public string RuleId => _definition.RuleId;
    public string Category => _definition.Category;

    public DuplicateVersionExtensionRule(SignatureRuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public async Task<IReadOnlyList<StorageFinding>> DetectAsync(
        IScanContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_definition.ExtensionRoot))
        {
            return Array.Empty<StorageFinding>();
        }

        var extRoot = context.ExpandPath(_definition.ExtensionRoot);
        if (!Directory.Exists(extRoot) ||
            !JsonSignatureDetectionRule.IsMatchingTargetDrive(extRoot, context.TargetDriveRoot))
        {
            return Array.Empty<StorageFinding>();
        }

        var prunableDirs = IdentifyPrunableExtensionDirectories(
            extRoot,
            _definition.PruneOlderDuplicateVersions,
            _definition.PruneDotUuidTempFolders,
            DateTime.UtcNow);

        if (prunableDirs.Count == 0)
        {
            return Array.Empty<StorageFinding>();
        }

        var verifiedSubPaths = new List<string>();
        long totalBytes = 0;
        int totalFiles = 0;
        DateTime latestMod = DateTime.UnixEpoch;

        foreach (var dir in prunableDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snap = await context.DirectorySizer.ComputeSizeAsync(dir, cancellationToken).ConfigureAwait(false);
            verifiedSubPaths.Add(dir);
            totalBytes += snap.SizeInBytes;
            totalFiles += snap.FileCount;
            if (snap.LastModifiedUtc > latestMod)
            {
                latestMod = snap.LastModifiedUtc;
            }
        }

        if (totalBytes <= 0 && verifiedSubPaths.Count == 0)
        {
            return Array.Empty<StorageFinding>();
        }

        var impact = new ImpactAssessment(
            level: Enum.Parse<ImpactLevel>(_definition.ImpactLevel, ignoreCase: true),
            riskScore: _definition.RiskScore,
            summaryVi: _definition.SummaryVi,
            sideEffectIfDeleted: _definition.SideEffectIfDeleted,
            preservedAssets: _definition.PreservedAssets,
            recommendedAction: _definition.RecommendedAction,
            lockingProcesses: _definition.LockingProcesses ?? new[] { "Code" },
            requiresAdmin: _definition.RequiresAdmin);

        var finding = new StorageFinding(
            ruleId: _definition.RuleId,
            displayName: $"{_definition.DisplayName} (Extension trùng & tạm mồ côi)",
            category: _definition.Category,
            targetPath: extRoot,
            sizeInBytes: totalBytes,
            fileCount: totalFiles,
            lastModifiedUtc: latestMod == DateTime.UnixEpoch ? DateTime.UtcNow : latestMod,
            actionType: CleanupActionType.SemVerDuplicatePrune,
            impact: impact,
            subPathsToClean: verifiedSubPaths);

        return new[] { finding };
    }

    public static IReadOnlyList<string> IdentifyPrunableExtensionDirectories(
        string extensionRoot,
        bool pruneOlderDuplicates,
        bool pruneDotUuidFolders,
        DateTime nowUtc)
    {
        var activeRelativeLocations = ReadActiveExtensionLocations(extensionRoot);
        var prunable = new List<string>();
        var groupedByExtension = new Dictionary<string, List<(string FullPath, string FolderName, ParsedExtensionVersion Version)>>(
            StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> subDirs;
        try
        {
            subDirs = Directory.EnumerateDirectories(extensionRoot);
        }
        catch
        {
            return prunable;
        }

        foreach (var subDir in subDirs)
        {
            var folderName = Path.GetFileName(subDir);
            if (pruneDotUuidFolders && DotUuidFolderRegex.IsMatch(folderName))
            {
                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = Directory.GetLastWriteTimeUtc(subDir);
                }
                catch
                {
                    continue;
                }

                // Only prune orphaned .uuid temp folders older than 24 hours so active installs are never interrupted.
                if (nowUtc - lastWriteUtc >= TimeSpan.FromHours(24))
                {
                    prunable.Add(subDir);
                }

                continue;
            }

            if (!pruneOlderDuplicates)
            {
                continue;
            }

            var match = ExtensionFolderRegex.Match(folderName);
            if (!match.Success)
            {
                continue;
            }

            var id = match.Groups["id"].Value.ToLowerInvariant();
            var platform = match.Groups["platform"].Success ? match.Groups["platform"].Value.ToLowerInvariant() : string.Empty;
            var key = $"{id}|{platform}";
            var parsedVer = new ParsedExtensionVersion(
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value),
                match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null);

            if (!groupedByExtension.TryGetValue(key, out var list))
            {
                list = new List<(string, string, ParsedExtensionVersion)>();
                groupedByExtension[key] = list;
            }

            list.Add((subDir, folderName, parsedVer));
        }

        foreach (var group in groupedByExtension.Values)
        {
            if (group.Count <= 1)
            {
                continue;
            }

            var ordered = group.OrderByDescending(x => x.Version).ToList();
            // Always keep index 0 (highest SemVer) AND any folder actively referenced in extensions.json!
            for (var i = 1; i < ordered.Count; i++)
            {
                if (!activeRelativeLocations.Contains(ordered[i].FolderName))
                {
                    prunable.Add(ordered[i].FullPath);
                }
            }
        }

        return prunable;
    }

    private static HashSet<string> ReadActiveExtensionLocations(string extensionRoot)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(extensionRoot, "extensions.json");
        if (!File.Exists(manifestPath))
        {
            return active;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("relativeLocation", out var relProp) &&
                        relProp.ValueKind == JsonValueKind.String)
                    {
                        var rel = relProp.GetString();
                        if (!string.IsNullOrWhiteSpace(rel))
                        {
                            active.Add(rel.Trim());
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed manifest and fall back to highest-SemVer retention.
        }

        return active;
    }

    private readonly record struct ParsedExtensionVersion(
        int Major,
        int Minor,
        int Patch,
        string? Prerelease) : IComparable<ParsedExtensionVersion>
    {
        public int CompareTo(ParsedExtensionVersion other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;

            // Stable release > prerelease per SemVer 2.0.0
            if (Prerelease is null && other.Prerelease is null) return 0;
            if (Prerelease is null) return 1;
            if (other.Prerelease is null) return -1;
            return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
        }
    }
}
