using System.Diagnostics;
using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.UseCases;

/// <summary>
/// UseCase #2: Executes non-destructive Dry-Run simulation on background worker threads and writes an audit record.
/// </summary>
public sealed class SimulateCleanupUseCase
{
    private readonly IReadOnlyDictionary<CleanupActionType, ICleanupStrategy> _strategies;
    private readonly ISystemSnapshotProvider _snapshotProvider;
    private readonly EvaluateImpactUseCase _evaluateImpact;
    private readonly IAuditLogRepository _auditLog;

    public SimulateCleanupUseCase(
        IEnumerable<ICleanupStrategy> strategies,
        ISystemSnapshotProvider snapshotProvider,
        EvaluateImpactUseCase evaluateImpact,
        IAuditLogRepository auditLog)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToDictionary(s => s.SupportedAction);
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _evaluateImpact = evaluateImpact ?? throw new ArgumentNullException(nameof(evaluateImpact));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    }

    public Task<CleanupBatchReport> ExecuteAsync(
        IReadOnlyList<StorageFinding> selectedFindings,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFindings);

        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var snapshot = _snapshotProvider.CaptureSnapshot();
            var itemResults = new List<CleanupItemResult>(selectedFindings.Count);

            var completed = 0;
            var total = selectedFindings.Count;
            var totalTargetBytes = selectedFindings.Sum(f => Math.Max(0, f.SizeInBytes));
            long cumBytes = 0;
            var cumFiles = 0;
            var cumSkipped = 0;

            foreach (var finding in selectedFindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CleanupProgress(
                    finding.RuleId,
                    finding.DisplayName,
                    completed,
                    total,
                    cumBytes,
                    cumFiles,
                    cumSkipped,
                    IsDryRun: true,
                    TotalTargetBytes: totalTargetBytes));

                // Re-verify immutable root & browser profile guards before simulating
                _evaluateImpact.Evaluate(finding, snapshot);

                if (!_strategies.TryGetValue(finding.ActionType, out var strategy))
                {
                    strategy = _strategies[CleanupActionType.BlockedNoOp];
                }

                IProgress<CleanupItemProgress>? itemProgress = progress is null
                    ? null
                    : new DirectProgress<CleanupItemProgress>(item =>
                        progress.Report(new CleanupProgress(
                            finding.RuleId,
                            finding.DisplayName,
                            completed,
                            total,
                            cumBytes + item.BytesProcessedInItem,
                            cumFiles + item.FilesProcessedInItem,
                            cumSkipped + item.SkippedLockedInItem,
                            IsDryRun: true,
                            TotalTargetBytes: totalTargetBytes)));

                var simResult = await strategy.SimulateAsync(finding, itemProgress, cancellationToken).ConfigureAwait(false);
                itemResults.Add(simResult);

                completed++;
                cumBytes += simResult.BytesReclaimed;
                cumFiles += simResult.FilesDeletedCount;
                cumSkipped += simResult.SkippedLockedPaths.Count;

                progress?.Report(new CleanupProgress(
                    finding.RuleId,
                    finding.DisplayName,
                    completed,
                    total,
                    cumBytes,
                    cumFiles,
                    cumSkipped,
                    IsDryRun: true,
                    TotalTargetBytes: totalTargetBytes));
            }

            sw.Stop();
            var batch = new CleanupBatchReport(
                BatchId: Guid.NewGuid(),
                IsDryRun: true,
                IsElevatedBatch: snapshot.IsCurrentProcessElevated,
                ExecutedAtUtc: DateTime.UtcNow,
                DurationMs: sw.ElapsedMilliseconds,
                ItemResults: itemResults);

            await _auditLog.AppendBatchReportAsync(batch, cancellationToken).ConfigureAwait(false);
            return batch;
        }, cancellationToken);
    }
}

/// <summary>
/// UseCase #3: Executes guarded user-space cleanup strictly on background worker threads with pre-flight guard re-evaluation and JSONL audit logging.
/// </summary>
public sealed class ExecuteCleanupUseCase
{
    private readonly IReadOnlyDictionary<CleanupActionType, ICleanupStrategy> _strategies;
    private readonly ISystemSnapshotProvider _snapshotProvider;
    private readonly EvaluateImpactUseCase _evaluateImpact;
    private readonly IAuditLogRepository _auditLog;

    public ExecuteCleanupUseCase(
        IEnumerable<ICleanupStrategy> strategies,
        ISystemSnapshotProvider snapshotProvider,
        EvaluateImpactUseCase evaluateImpact,
        IAuditLogRepository auditLog)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToDictionary(s => s.SupportedAction);
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _evaluateImpact = evaluateImpact ?? throw new ArgumentNullException(nameof(evaluateImpact));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    }

    public Task<CleanupBatchReport> ExecuteAsync(
        IReadOnlyList<StorageFinding> selectedFindings,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFindings);

        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var snapshot = _snapshotProvider.CaptureSnapshot();
            var itemResults = new List<CleanupItemResult>(selectedFindings.Count);

            var completed = 0;
            var total = selectedFindings.Count;
            var totalTargetBytes = selectedFindings.Sum(f => Math.Max(0, f.SizeInBytes));
            long cumBytes = 0;
            var cumFiles = 0;
            var cumSkipped = 0;

            foreach (var finding in selectedFindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CleanupProgress(
                    finding.RuleId,
                    finding.DisplayName,
                    completed,
                    total,
                    cumBytes,
                    cumFiles,
                    cumSkipped,
                    IsDryRun: false,
                    TotalTargetBytes: totalTargetBytes));

                _evaluateImpact.Evaluate(finding, snapshot);

                if (finding.Impact.Level == ImpactLevel.PROTECTED_NEVER_DELETE ||
                    finding.ActionType == CleanupActionType.BlockedNoOp)
                {
                    itemResults.Add(new CleanupItemResult(
                        finding.Id,
                        finding.RuleId,
                        finding.TargetPath,
                        CleanupActionType.BlockedNoOp,
                        CleanupItemStatus.BlockedByGuard,
                        BytesReclaimed: 0,
                        FilesDeletedCount: 0,
                        SkippedLockedPaths: Array.Empty<string>(),
                        Errors: Array.Empty<string>(),
                        MessageVi: finding.Impact.SummaryVi));
                    completed++;
                    continue;
                }

                if (finding.Impact.RequiresAdmin && !snapshot.IsCurrentProcessElevated)
                {
                    itemResults.Add(new CleanupItemResult(
                        finding.Id,
                        finding.RuleId,
                        finding.TargetPath,
                        finding.ActionType,
                        CleanupItemStatus.AdminQueued,
                        BytesReclaimed: 0,
                        FilesDeletedCount: 0,
                        SkippedLockedPaths: Array.Empty<string>(),
                        Errors: Array.Empty<string>(),
                        MessageVi: "Mục này cần quyền Quản trị viên (UAC); đã giữ lại cho Hàng đợi Quản trị viên."));
                    completed++;
                    continue;
                }

                if (!_strategies.TryGetValue(finding.ActionType, out var strategy))
                {
                    strategy = _strategies[CleanupActionType.BlockedNoOp];
                }

                IProgress<CleanupItemProgress>? itemProgress = progress is null
                    ? null
                    : new DirectProgress<CleanupItemProgress>(item =>
                        progress.Report(new CleanupProgress(
                            finding.RuleId,
                            finding.DisplayName,
                            completed,
                            total,
                            cumBytes + item.BytesProcessedInItem,
                            cumFiles + item.FilesProcessedInItem,
                            cumSkipped + item.SkippedLockedInItem,
                            IsDryRun: false,
                            TotalTargetBytes: totalTargetBytes)));

                var execResult = await strategy.ExecuteAsync(finding, itemProgress, cancellationToken).ConfigureAwait(false);
                itemResults.Add(execResult);

                completed++;
                cumBytes += execResult.BytesReclaimed;
                cumFiles += execResult.FilesDeletedCount;
                cumSkipped += execResult.SkippedLockedPaths.Count;

                progress?.Report(new CleanupProgress(
                    finding.RuleId,
                    finding.DisplayName,
                    completed,
                    total,
                    cumBytes,
                    cumFiles,
                    cumSkipped,
                    IsDryRun: false,
                    TotalTargetBytes: totalTargetBytes));
            }

            sw.Stop();
            var batch = new CleanupBatchReport(
                BatchId: Guid.NewGuid(),
                IsDryRun: false,
                IsElevatedBatch: snapshot.IsCurrentProcessElevated,
                ExecutedAtUtc: DateTime.UtcNow,
                DurationMs: sw.ElapsedMilliseconds,
                ItemResults: itemResults);

            await _auditLog.AppendBatchReportAsync(batch, cancellationToken).ConfigureAwait(false);
            return batch;
        }, cancellationToken);
    }
}

internal sealed class DirectProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}
