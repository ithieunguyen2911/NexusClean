using System.Diagnostics;
using NexusClean.Application.Models;
using NexusClean.Domain.Entities;
using NexusClean.Domain.Enums;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Application.UseCases;

/// <summary>
/// UseCase #2: Executes non-destructive Dry-Run simulation across selected findings and writes an audit record.
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

    public async Task<CleanupBatchReport> ExecuteAsync(
        IReadOnlyList<StorageFinding> selectedFindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFindings);
        var sw = Stopwatch.StartNew();
        var snapshot = _snapshotProvider.CaptureSnapshot();
        var itemResults = new List<CleanupItemResult>(selectedFindings.Count);

        foreach (var finding in selectedFindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _evaluateImpact.Evaluate(finding, snapshot);

            if (!_strategies.TryGetValue(finding.ActionType, out var strategy))
            {
                strategy = _strategies[CleanupActionType.BlockedNoOp];
            }

            var simResult = await strategy.SimulateAsync(finding, cancellationToken).ConfigureAwait(false);
            itemResults.Add(simResult);
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
    }
}

/// <summary>
/// UseCase #3: Executes guarded user-space cleanup with mandatory pre-flight guard re-evaluation and JSONL audit logging.
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

    public async Task<CleanupBatchReport> ExecuteAsync(
        IReadOnlyList<StorageFinding> selectedFindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFindings);
        var sw = Stopwatch.StartNew();
        var snapshot = _snapshotProvider.CaptureSnapshot();
        var itemResults = new List<CleanupItemResult>(selectedFindings.Count);

        foreach (var finding in selectedFindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                continue;
            }

            if (!_strategies.TryGetValue(finding.ActionType, out var strategy))
            {
                strategy = _strategies[CleanupActionType.BlockedNoOp];
            }

            var execResult = await strategy.ExecuteAsync(finding, cancellationToken).ConfigureAwait(false);
            itemResults.Add(execResult);
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
    }
}
