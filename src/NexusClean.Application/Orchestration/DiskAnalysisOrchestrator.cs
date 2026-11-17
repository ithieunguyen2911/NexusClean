using NexusClean.Application.Models;
using NexusClean.Application.UseCases;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.Orchestration;

/// <summary>
/// High-level facade coordinating drive analysis, Dry-Run simulation, guarded execution, and relocation planning
/// across both NexusClean.UI (WPF) and NexusClean.Cli.
/// </summary>
public sealed class DiskAnalysisOrchestrator
{
    private readonly AnalyzeDriveUseCase _analyzeUseCase;
    private readonly SimulateCleanupUseCase _simulateUseCase;
    private readonly ExecuteCleanupUseCase _executeUseCase;
    private readonly PlanSecondaryRelocationUseCase _relocateUseCase;

    private DriveAnalysisReport? _lastReport;

    public DiskAnalysisOrchestrator(
        AnalyzeDriveUseCase analyzeUseCase,
        SimulateCleanupUseCase simulateUseCase,
        ExecuteCleanupUseCase executeUseCase,
        PlanSecondaryRelocationUseCase relocateUseCase)
    {
        _analyzeUseCase = analyzeUseCase ?? throw new ArgumentNullException(nameof(analyzeUseCase));
        _simulateUseCase = simulateUseCase ?? throw new ArgumentNullException(nameof(simulateUseCase));
        _executeUseCase = executeUseCase ?? throw new ArgumentNullException(nameof(executeUseCase));
        _relocateUseCase = relocateUseCase ?? throw new ArgumentNullException(nameof(relocateUseCase));
    }

    public DriveAnalysisReport? LastAnalysisReport => _lastReport;

    public async Task<DriveAnalysisReport> AnalyzeAsync(
        string driveName = "C:",
        string? customRulesPath = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = await _analyzeUseCase.ExecuteAsync(
            driveName,
            customRulesPath,
            environmentOverrides,
            progress,
            cancellationToken).ConfigureAwait(false);

        _lastReport = report;
        return report;
    }

    public Task<CleanupBatchReport> CleanAsync(
        IReadOnlyList<StorageFinding> selectedFindings,
        bool dryRun,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return dryRun
            ? _simulateUseCase.ExecuteAsync(selectedFindings, progress, cancellationToken)
            : _executeUseCase.ExecuteAsync(selectedFindings, progress, cancellationToken);
    }

    public async Task<CleanupBatchReport> CleanByIdsAsync(
        IReadOnlyList<string> findingIds,
        bool dryRun,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_lastReport is null)
        {
            throw new InvalidOperationException("Chưa có kết quả quét ổ đĩa; hãy chạy AnalyzeAsync trước.");
        }

        var idSet = new HashSet<string>(findingIds, StringComparer.OrdinalIgnoreCase);
        var matched = _lastReport.Findings.Where(f => idSet.Contains(f.Id)).ToArray();
        return await CleanAsync(matched, dryRun, progress, cancellationToken).ConfigureAwait(false);
    }

    public RelocationPlan PlanRelocation(StorageFinding finding, string? preferredTargetDrive = null) =>
        _relocateUseCase.CreatePlan(finding, preferredTargetDrive);
}
