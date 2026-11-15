using System.Text.Json;
using System.Text.RegularExpressions;
using NexusClean.Application.Models;
using NexusClean.Application.Rules;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.Persistence;

/// <summary>
/// Loads and validates declarative JSON signature rule catalogs against schema and Domain safety invariants.
/// </summary>
public sealed class JsonSignatureRuleProvider : ISignatureRuleCatalogProvider
{
    private static readonly Regex SemVerRegex = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex RuleIdRegex = new(@"^[a-z0-9_]+(\.[a-z0-9_]+){1,4}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _defaultCatalogPath;

    public JsonSignatureRuleProvider(string? defaultCatalogPath = null)
    {
        _defaultCatalogPath = defaultCatalogPath ?? ResolveDefaultCatalogPath();
    }

    public static string ResolveDefaultCatalogPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "rules", "default-signature-rules.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "rules", "default-signature-rules.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "default-signature-rules.json")),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "03-SEED-SIGNATURE-RULES.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[1];
    }

    public CatalogValidationReport ValidateCatalogFile(string filePath)
    {
        var (_, _, report) = ParseAndValidateInternal(filePath);
        return report;
    }

    public (SignatureCatalogDocument Catalog, IReadOnlyList<IDetectionRule> Rules, CatalogValidationReport Validation) LoadRules(
        string? customFilePath = null)
    {
        var target = string.IsNullOrWhiteSpace(customFilePath) ? _defaultCatalogPath : customFilePath;
        return ParseAndValidateInternal(target);
    }

    private static (SignatureCatalogDocument Catalog, IReadOnlyList<IDetectionRule> Rules, CatalogValidationReport Validation) ParseAndValidateInternal(
        string filePath)
    {
        var issues = new List<CatalogValidationIssue>();
        var emptyDoc = new SignatureCatalogDocument(
            "0.0.0",
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<SignatureRuleDefinition>());

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            issues.Add(new CatalogValidationIssue(null, "FILE_NOT_FOUND", $"Không tìm thấy tệp quy tắc '{filePath}'."));
            return (emptyDoc, Array.Empty<IDetectionRule>(), new CatalogValidationReport(filePath ?? "", false, 0, issues));
        }

        RawCatalogDto? raw;
        try
        {
            var json = File.ReadAllText(filePath);
            raw = JsonSerializer.Deserialize<RawCatalogDto>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            issues.Add(new CatalogValidationIssue(null, "JSON_PARSE_ERROR", $"Lỗi cú pháp JSON: {ex.Message}"));
            return (emptyDoc, Array.Empty<IDetectionRule>(), new CatalogValidationReport(filePath, false, 0, issues));
        }

        if (raw is null)
        {
            issues.Add(new CatalogValidationIssue(null, "EMPTY_DOCUMENT", "Tệp JSON rỗng."));
            return (emptyDoc, Array.Empty<IDetectionRule>(), new CatalogValidationReport(filePath, false, 0, issues));
        }

        if (string.IsNullOrWhiteSpace(raw.Version) || !SemVerRegex.IsMatch(raw.Version))
        {
            issues.Add(new CatalogValidationIssue(null, "INVALID_VERSION", $"Trường version '{raw.Version}' phải theo chuẩn X.Y.Z."));
        }

        if (raw.ProtectedCriticalFileNames is null || raw.ProtectedCriticalFileNames.Count == 0)
        {
            issues.Add(new CatalogValidationIssue(null, "MISSING_PROTECTED_FILES", "Thiếu danh sách protectedCriticalFileNames."));
        }

        if (raw.ProtectedRootPaths is null || raw.ProtectedRootPaths.Count == 0)
        {
            issues.Add(new CatalogValidationIssue(null, "MISSING_PROTECTED_ROOTS", "Thiếu danh sách protectedRootPaths."));
        }

        if (raw.Rules is null || raw.Rules.Count == 0)
        {
            issues.Add(new CatalogValidationIssue(null, "EMPTY_RULES", "Danh sách rules không được rỗng."));
            return (emptyDoc, Array.Empty<IDetectionRule>(), new CatalogValidationReport(filePath, false, 0, issues));
        }

        var rootGuard = new RootSystemFolderGuard();
        var browserGuard = new BrowserProfilePreservationGuard();
        var dummySnapshot = new ValidationSnapshot(
            raw.ProtectedRootPaths ?? new List<string>(),
            raw.ProtectedCriticalFileNames ?? new List<string>());

        var validDefs = new List<SignatureRuleDefinition>();
        var builtRules = new List<IDetectionRule>();
        var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in raw.Rules)
        {
            var ruleId = r.RuleId?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(ruleId) || !RuleIdRegex.IsMatch(ruleId))
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_RULE_ID", $"ruleId '{ruleId}' không hợp lệ."));
                continue;
            }

            if (!seenRuleIds.Add(ruleId))
            {
                issues.Add(new CatalogValidationIssue(ruleId, "DUPLICATE_RULE_ID", $"Trùng lặp ruleId '{ruleId}'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.DisplayName) || r.DisplayName.Trim().Length is < 3 or > 160)
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_DISPLAY_NAME", "displayName phải từ 3 đến 160 ký tự."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.Category) || r.Category.Trim().Length is < 2 or > 64)
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_CATEGORY", "category phải từ 2 đến 64 ký tự."));
                continue;
            }

            if (!Enum.TryParse<CleanupActionType>(r.ActionType, ignoreCase: true, out var parsedAction))
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_ACTION_TYPE", $"actionType '{r.ActionType}' không được hỗ trợ."));
                continue;
            }

            if (!Enum.TryParse<ImpactLevel>(r.ImpactLevel, ignoreCase: true, out var parsedImpact))
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_IMPACT_LEVEL", $"impactLevel '{r.ImpactLevel}' không hợp lệ."));
                continue;
            }

            if (r.RiskScore is < 0 or > 10)
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INVALID_RISK_SCORE", "riskScore phải nằm trong khoảng 0..10."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.SummaryVi) || r.SummaryVi.Trim().Length < 10 ||
                string.IsNullOrWhiteSpace(r.SideEffectIfDeleted) || r.SideEffectIfDeleted.Trim().Length < 10 ||
                r.PreservedAssets is null || r.PreservedAssets.Count == 0)
            {
                issues.Add(new CatalogValidationIssue(ruleId, "INCOMPLETE_IMPACT_METADATA", "Thiếu summaryVi, sideEffectIfDeleted hoặc preservedAssets."));
                continue;
            }

            var hasTargets = r.TargetPaths is { Count: > 0 };
            var hasBrowserProfile = r.BasePaths is { Count: > 0 } &&
                                    !string.IsNullOrWhiteSpace(r.ProfileFolderRegex) &&
                                    r.AllowedCacheSubfolders is { Count: > 0 };
            var hasExtensionRoot = !string.IsNullOrWhiteSpace(r.ExtensionRoot);

            if (!hasTargets && !hasBrowserProfile && !hasExtensionRoot)
            {
                issues.Add(new CatalogValidationIssue(ruleId, "MISSING_RULE_SELECTOR", "Rule phải khai báo targetPaths, bộ basePaths trình duyệt, hoặc extensionRoot."));
                continue;
            }

            // Pre-flight Domain Safety Guard check on declared targetPaths & allowedCacheSubfolders!
            var unsafeTargetDetected = false;
            if (hasTargets)
            {
                foreach (var tp in r.TargetPaths!)
                {
                    var expanded = Environment.ExpandEnvironmentVariables(tp);
                    if (expanded.Contains('%')) continue;
                    var testFinding = new StorageFinding(
                        ruleId,
                        r.DisplayName,
                        r.Category,
                        expanded,
                        1,
                        1,
                        DateTime.UtcNow,
                        parsedAction,
                        new ImpactAssessment(parsedImpact, parsedImpact == ImpactLevel.PROTECTED_NEVER_DELETE ? 10 : r.RiskScore, r.SummaryVi, r.SideEffectIfDeleted, r.PreservedAssets));

                    var rootVerdict = rootGuard.Inspect(testFinding, dummySnapshot);
                    var browserVerdict = browserGuard.Inspect(testFinding, dummySnapshot);
                    if (rootVerdict.Severity == GuardSeverity.Block || browserVerdict.Severity == GuardSeverity.Block)
                    {
                        var msg = rootVerdict.Severity == GuardSeverity.Block ? rootVerdict.MessageVi : browserVerdict.MessageVi;
                        issues.Add(new CatalogValidationIssue(ruleId, "UNSAFE_RULE_BLOCKED", msg));
                        unsafeTargetDetected = true;
                        break;
                    }
                }
            }

            if (hasBrowserProfile)
            {
                foreach (var subRel in r.AllowedCacheSubfolders!)
                {
                    if (!BrowserProfilePreservationGuard.IsAllowedBrowserCachePath(@"C:\Profile\" + subRel))
                    {
                        issues.Add(new CatalogValidationIssue(ruleId, "UNSAFE_BROWSER_SUBFOLDER", $"Thư mục con '{subRel}' không thuộc danh sách cache trình duyệt an toàn."));
                        unsafeTargetDetected = true;
                        break;
                    }
                }
            }

            if (unsafeTargetDetected)
            {
                continue;
            }

            var def = new SignatureRuleDefinition(
                RuleId: ruleId,
                DisplayName: r.DisplayName.Trim(),
                Category: r.Category.Trim(),
                ActionType: parsedAction.ToString(),
                ImpactLevel: parsedImpact.ToString(),
                RiskScore: r.RiskScore,
                SummaryVi: r.SummaryVi.Trim(),
                SideEffectIfDeleted: r.SideEffectIfDeleted.Trim(),
                PreservedAssets: r.PreservedAssets,
                TargetPaths: r.TargetPaths,
                BasePaths: r.BasePaths,
                ProfileFolderRegex: r.ProfileFolderRegex,
                AllowedCacheSubfolders: r.AllowedCacheSubfolders,
                ExtensionRoot: r.ExtensionRoot,
                PruneOlderDuplicateVersions: r.PruneOlderDuplicateVersions,
                PruneDotUuidTempFolders: r.PruneDotUuidTempFolders,
                RequiresAdmin: r.RequiresAdmin,
                LockingProcesses: r.LockingProcesses,
                RecommendedAction: r.RecommendedAction);

            validDefs.Add(def);

            // Build polymorphic / composite detectors!
            if (hasBrowserProfile)
            {
                builtRules.Add(new BrowserSelectiveCacheRule(def));
            }

            if (hasTargets)
            {
                // For composite IDE rules (like ide.vscode.cached_vsix_and_orphans), targetPaths are direct cache folders!
                var targetAction = hasExtensionRoot && parsedAction == CleanupActionType.SemVerDuplicatePrune
                    ? CleanupActionType.DeleteUnlockedItems
                    : parsedAction;
                builtRules.Add(new JsonSignatureDetectionRule(def, targetAction, parsedImpact));
            }

            if (hasExtensionRoot)
            {
                builtRules.Add(new DuplicateVersionExtensionRule(def));
            }
        }

        var doc = new SignatureCatalogDocument(
            raw.Version ?? "1.0.0",
            raw.UpdatedAt,
            raw.ProtectedCriticalFileNames ?? new List<string>(),
            raw.ProtectedRootPaths ?? new List<string>(),
            validDefs);

        var isValid = issues.Count == 0 && validDefs.Count > 0;
        return (doc, builtRules, new CatalogValidationReport(filePath, isValid, validDefs.Count, issues));
    }

    private sealed class RawCatalogDto
    {
        public string? Version { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public List<string>? ProtectedCriticalFileNames { get; set; }
        public List<string>? ProtectedRootPaths { get; set; }
        public List<RawRuleDto>? Rules { get; set; }
    }

    private sealed class RawRuleDto
    {
        public string? RuleId { get; set; }
        public string? DisplayName { get; set; }
        public string? Category { get; set; }
        public List<string>? TargetPaths { get; set; }
        public List<string>? BasePaths { get; set; }
        public string? ProfileFolderRegex { get; set; }
        public List<string>? AllowedCacheSubfolders { get; set; }
        public string? ExtensionRoot { get; set; }
        public bool PruneOlderDuplicateVersions { get; set; }
        public bool PruneDotUuidTempFolders { get; set; }
        public string? ActionType { get; set; }
        public string? ImpactLevel { get; set; }
        public int RiskScore { get; set; }
        public bool RequiresAdmin { get; set; }
        public List<string>? LockingProcesses { get; set; }
        public string? SummaryVi { get; set; }
        public string? SideEffectIfDeleted { get; set; }
        public string? RecommendedAction { get; set; }
        public List<string>? PreservedAssets { get; set; }
    }

    private sealed class ValidationSnapshot(
        IReadOnlyList<string> roots,
        IReadOnlyList<string> files) : ISystemSnapshot
    {
        public bool IsCurrentProcessElevated => false;
        public IReadOnlySet<string> RunningProcessNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> AdditionalProtectedRoots => roots;
        public IReadOnlyList<string> AdditionalProtectedFileNames => files;
        public IReadOnlyList<VolumeSnapshot> FixedVolumes => Array.Empty<VolumeSnapshot>();
        public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths) => Array.Empty<string>();
    }
}
