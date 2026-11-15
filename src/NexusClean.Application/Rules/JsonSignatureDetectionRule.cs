using System.Text.RegularExpressions;
using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.Rules;

/// <summary>
/// Standard signature rule scanning explicit targetPaths (files or directories).
/// </summary>
public sealed class JsonSignatureDetectionRule : IDetectionRule
{
    private readonly SignatureRuleDefinition _definition;
    private readonly CleanupActionType _actionType;
    private readonly ImpactLevel _impactLevel;

    public string RuleId => _definition.RuleId;
    public string Category => _definition.Category;

    public JsonSignatureDetectionRule(
        SignatureRuleDefinition definition,
        CleanupActionType actionType,
        ImpactLevel impactLevel)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _actionType = actionType;
        _impactLevel = impactLevel;
    }

    public async Task<IReadOnlyList<StorageFinding>> DetectAsync(
        IScanContext context,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<StorageFinding>();
        if (_definition.TargetPaths is null || _definition.TargetPaths.Count == 0)
        {
            return findings;
        }

        foreach (var rawTarget in _definition.TargetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expanded = context.ExpandPath(rawTarget);
            if (string.IsNullOrWhiteSpace(expanded) || !IsMatchingTargetDrive(expanded, context.TargetDriveRoot))
            {
                continue;
            }

            var snapshot = await context.DirectorySizer.ComputeSizeAsync(expanded, cancellationToken).ConfigureAwait(false);
            // System protected dirs like C:\System Volume Information may have 0 accessible user-mode bytes
            // yet still represent actionable Admin findings if the directory exists.
            var existsOnDisk = Directory.Exists(expanded) || File.Exists(expanded);
            if (!existsOnDisk || (snapshot.SizeInBytes == 0 && !_definition.RequiresAdmin))
            {
                continue;
            }

            var impact = new ImpactAssessment(
                level: _impactLevel,
                riskScore: _definition.RiskScore,
                summaryVi: _definition.SummaryVi,
                sideEffectIfDeleted: _definition.SideEffectIfDeleted,
                preservedAssets: _definition.PreservedAssets,
                recommendedAction: _definition.RecommendedAction,
                isLockedByProcess: false,
                lockingProcesses: _definition.LockingProcesses,
                requiresAdmin: _definition.RequiresAdmin);

            findings.Add(new StorageFinding(
                ruleId: _definition.RuleId,
                displayName: $"{_definition.DisplayName} ({Path.GetFileName(Path.TrimEndingDirectorySeparator(expanded))})",
                category: _definition.Category,
                targetPath: expanded,
                sizeInBytes: snapshot.SizeInBytes,
                fileCount: snapshot.FileCount,
                lastModifiedUtc: snapshot.LastModifiedUtc,
                actionType: _actionType,
                impact: impact));
        }

        return findings;
    }

    internal static bool IsMatchingTargetDrive(string expandedPath, string targetDriveRoot)
    {
        if (string.IsNullOrWhiteSpace(targetDriveRoot))
        {
            return true;
        }

        var pathRoot = Path.GetPathRoot(expandedPath);
        var desiredRoot = Path.GetPathRoot(targetDriveRoot);
        if (string.IsNullOrEmpty(pathRoot) || string.IsNullOrEmpty(desiredRoot))
        {
            return true;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(pathRoot),
            Path.TrimEndingDirectorySeparator(desiredRoot),
            StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Selective browser cache detector scanning allowlisted cache subdirectories inside discovered browser profiles.
/// Never touches Login Data, Cookies, Bookmarks, Preferences, History, or Extensions.
/// </summary>
public sealed class BrowserSelectiveCacheRule : IDetectionRule
{
    private readonly SignatureRuleDefinition _definition;
    private readonly Regex _profileRegex;

    public string RuleId => _definition.RuleId;
    public string Category => _definition.Category;

    public BrowserSelectiveCacheRule(SignatureRuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        var pattern = string.IsNullOrWhiteSpace(definition.ProfileFolderRegex)
            ? @"^(Default|Profile \d+)$"
            : definition.ProfileFolderRegex;
        _profileRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public async Task<IReadOnlyList<StorageFinding>> DetectAsync(
        IScanContext context,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<StorageFinding>();
        if (_definition.BasePaths is null || _definition.AllowedCacheSubfolders is null)
        {
            return findings;
        }

        foreach (var rawBase in _definition.BasePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var userDataPath = context.ExpandPath(rawBase);
            if (!Directory.Exists(userDataPath) ||
                !JsonSignatureDetectionRule.IsMatchingTargetDrive(userDataPath, context.TargetDriveRoot))
            {
                continue;
            }

            var subPathsToClean = new List<string>();
            long totalBytes = 0;
            int totalFiles = 0;
            DateTime latestMod = DateTime.UnixEpoch;

            IEnumerable<string> candidateProfileDirs;
            try
            {
                candidateProfileDirs = Directory.EnumerateDirectories(userDataPath);
            }
            catch
            {
                continue;
            }

            foreach (var profileDir in candidateProfileDirs)
            {
                var folderName = Path.GetFileName(profileDir);
                // Support standard Default / Profile N and custom profile folders containing a Preferences file.
                var isMatchedProfile = _profileRegex.IsMatch(folderName) ||
                                       File.Exists(Path.Combine(profileDir, "Preferences"));
                if (!isMatchedProfile)
                {
                    continue;
                }

                foreach (var allowedCacheRel in _definition.AllowedCacheSubfolders)
                {
                    var candidateCacheDir = Path.Combine(profileDir, allowedCacheRel);
                    if (!Directory.Exists(candidateCacheDir))
                    {
                        continue;
                    }

                    var snap = await context.DirectorySizer.ComputeSizeAsync(candidateCacheDir, cancellationToken).ConfigureAwait(false);
                    if (snap.SizeInBytes <= 0)
                    {
                        continue;
                    }

                    subPathsToClean.Add(candidateCacheDir);
                    totalBytes += snap.SizeInBytes;
                    totalFiles += snap.FileCount;
                    if (snap.LastModifiedUtc > latestMod)
                    {
                        latestMod = snap.LastModifiedUtc;
                    }
                }
            }

            if (subPathsToClean.Count == 0 || totalBytes <= 0)
            {
                continue;
            }

            var browserLabel = InferBrowserName(userDataPath);
            var impact = new ImpactAssessment(
                level: Enum.Parse<ImpactLevel>(_definition.ImpactLevel, ignoreCase: true),
                riskScore: _definition.RiskScore,
                summaryVi: _definition.SummaryVi,
                sideEffectIfDeleted: _definition.SideEffectIfDeleted,
                preservedAssets: _definition.PreservedAssets,
                recommendedAction: _definition.RecommendedAction,
                lockingProcesses: _definition.LockingProcesses ?? InferBrowserProcesses(userDataPath),
                requiresAdmin: _definition.RequiresAdmin);

            findings.Add(new StorageFinding(
                ruleId: _definition.RuleId,
                displayName: $"{_definition.DisplayName} — {browserLabel}",
                category: _definition.Category,
                targetPath: userDataPath,
                sizeInBytes: totalBytes,
                fileCount: totalFiles,
                lastModifiedUtc: latestMod == DateTime.UnixEpoch ? DateTime.UtcNow : latestMod,
                actionType: CleanupActionType.SelectiveSubfolderPurge,
                impact: impact,
                subPathsToClean: subPathsToClean));
        }

        return findings;
    }

    private static string InferBrowserName(string userDataPath)
    {
        if (userDataPath.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (userDataPath.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (userDataPath.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return "Brave Browser";
        if (userDataPath.Contains("CocCoc", StringComparison.OrdinalIgnoreCase)) return "Cốc Cốc";
        return Path.GetFileName(Path.GetDirectoryName(userDataPath) ?? userDataPath);
    }

    private static IReadOnlyList<string> InferBrowserProcesses(string userDataPath)
    {
        if (userDataPath.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return new[] { "chrome" };
        if (userDataPath.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return new[] { "msedge" };
        if (userDataPath.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return new[] { "brave" };
        if (userDataPath.Contains("CocCoc", StringComparison.OrdinalIgnoreCase)) return new[] { "browser" };
        return Array.Empty<string>();
    }
}
