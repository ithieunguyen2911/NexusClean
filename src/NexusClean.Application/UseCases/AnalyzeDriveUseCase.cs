using System.Diagnostics;
using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.UseCases;

/// <summary>
/// UseCase #1: Performs concurrent read-only signature discovery, sizing, and 5-tier impact/guard evaluation.
/// </summary>
public sealed class AnalyzeDriveUseCase
{
    private readonly ISignatureRuleCatalogProvider _catalogProvider;
    private readonly IDirectorySizer _directorySizer;
    private readonly ISystemSnapshotProvider _snapshotProvider;
    private readonly EvaluateImpactUseCase _evaluateImpact;

    public AnalyzeDriveUseCase(
        ISignatureRuleCatalogProvider catalogProvider,
        IDirectorySizer directorySizer,
        ISystemSnapshotProvider snapshotProvider,
        EvaluateImpactUseCase evaluateImpact)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _directorySizer = directorySizer ?? throw new ArgumentNullException(nameof(directorySizer));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _evaluateImpact = evaluateImpact ?? throw new ArgumentNullException(nameof(evaluateImpact));
    }

    public async Task<DriveAnalysisReport> ExecuteAsync(
        string driveName = "C:",
        string? customRulesPath = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var startedAtUtc = DateTime.UtcNow;

        var normalizedDrive = NormalizeDriveRoot(driveName);
        var (catalog, rules, _) = _catalogProvider.LoadRules(customRulesPath);
        var snapshot = _snapshotProvider.CaptureSnapshot(
            catalog.ProtectedRootPaths,
            catalog.ProtectedCriticalFileNames);

        var context = new DefaultScanContext(normalizedDrive, _directorySizer, environmentOverrides);
        var discovered = new List<StorageFinding>();
        var syncGate = new object();

        var completed = 0;
        var totalRules = rules.Count;
        long cumulativeBytes = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(rules, parallelOptions, async (rule, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            lock (syncGate)
            {
                progress?.Report(new ScanProgress(
                    rule.RuleId,
                    $"Đang quét danh mục {rule.Category} ({rule.RuleId})...",
                    completed,
                    totalRules,
                    cumulativeBytes));
            }

            var ruleFindings = await rule.DetectAsync(context, ct).ConfigureAwait(false);
            var evaluatedBatch = new List<StorageFinding>(ruleFindings.Count);
            foreach (var f in ruleFindings)
            {
                _evaluateImpact.Evaluate(f, snapshot);
                evaluatedBatch.Add(f);
            }

            lock (syncGate)
            {
                foreach (var ef in evaluatedBatch)
                {
                    discovered.Add(ef);
                    cumulativeBytes += ef.SizeInBytes;
                }

                completed++;
                progress?.Report(new ScanProgress(
                    rule.RuleId,
                    $"Hoàn tất {rule.RuleId}",
                    completed,
                    totalRules,
                    cumulativeBytes));
            }
        }).ConfigureAwait(false);

        var orderedFindings = discovered
            .OrderBy(f => f.Impact.Level)
            .ThenByDescending(f => f.SizeInBytes)
            .ToArray();

        var driveVol = snapshot.FixedVolumes.FirstOrDefault(v =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(v.DriveName),
                Path.TrimEndingDirectorySeparator(normalizedDrive),
                StringComparison.OrdinalIgnoreCase));

        long totalCap = driveVol?.TotalCapacityBytes ?? 0;
        long freeCap = driveVol?.AvailableFreeBytes ?? 0;
        if (totalCap == 0)
        {
            try
            {
                var di = new DriveInfo(normalizedDrive);
                if (di.IsReady)
                {
                    totalCap = di.TotalSize;
                    freeCap = di.AvailableFreeSpace;
                }
            }
            catch
            {
                // Ignore synthetic sandbox root paths.
            }
        }

        sw.Stop();
        return new DriveAnalysisReport(
            ScanId: Guid.NewGuid(),
            DriveName: normalizedDrive,
            TotalCapacityBytes: totalCap,
            FreeBytesBeforeScan: freeCap,
            StartedAtUtc: startedAtUtc,
            DurationMs: sw.ElapsedMilliseconds,
            Findings: orderedFindings);
    }

    internal static string NormalizeDriveRoot(string rawDrive)
    {
        if (string.IsNullOrWhiteSpace(rawDrive))
        {
            return @"C:\";
        }

        var trimmed = rawDrive.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        }

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        }

        return Path.GetFullPath(trimmed);
    }

    private sealed class DefaultScanContext : IScanContext
    {
        public string TargetDriveRoot { get; }
        public IDirectorySizer DirectorySizer { get; }
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

        public DefaultScanContext(
            string targetDriveRoot,
            IDirectorySizer directorySizer,
            IReadOnlyDictionary<string, string>? overrides)
        {
            TargetDriveRoot = targetDriveRoot;
            DirectorySizer = directorySizer;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string k && entry.Value is string v)
                {
                    dict[k] = v;
                }
            }

            if (overrides is not null)
            {
                foreach (var kv in overrides)
                {
                    dict[kv.Key] = kv.Value;
                }
            }

            EnvironmentVariables = dict;
        }

        public string ExpandPath(string rawTemplatePath)
        {
            if (string.IsNullOrWhiteSpace(rawTemplatePath))
            {
                return string.Empty;
            }

            var result = rawTemplatePath;
            foreach (var kv in EnvironmentVariables)
            {
                var token = $"%{kv.Key}%";
                if (result.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Replace(token, kv.Value, StringComparison.OrdinalIgnoreCase);
                }
            }

            result = Environment.ExpandEnvironmentVariables(result);
            if (result.Contains('%'))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(result);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
