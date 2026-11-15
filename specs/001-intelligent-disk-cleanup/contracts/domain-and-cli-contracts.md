# Phase 1 Interface & CLI Contracts: NexusClean

**Feature**: `001-intelligent-disk-cleanup`  
**Date**: 2026-11-15

---

## 1. Domain & Application C# Interface Contracts (`NexusClean.Domain.Interfaces`)

All presentation layers (`NexusClean.UI` WPF Desktop and `NexusClean.Cli`) interact exclusively with `NexusClean.Application` use cases, which depend strictly on these pure Domain interfaces:

```csharp
namespace NexusClean.Domain.Interfaces;

public interface IDetectionRule
{
    string RuleId { get; }
    string Category { get; }
    Task<IReadOnlyList<StorageFinding>> DetectAsync(
        IScanContext context,
        CancellationToken cancellationToken = default);
}

public interface ISafetyGuard
{
    string GuardName { get; }
    int Order { get; }
    GuardVerdict Inspect(StorageFinding finding, ISystemSnapshot snapshot);
}

public interface ICleanupStrategy
{
    CleanupActionType SupportedAction { get; }
    Task<CleanupItemResult> SimulateAsync(
        StorageFinding finding,
        CancellationToken cancellationToken = default);
    Task<CleanupItemResult> ExecuteAsync(
        StorageFinding finding,
        CancellationToken cancellationToken = default);
}

public interface IDirectorySizer
{
    Task<DirectorySizeSnapshot> ComputeSizeAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}

public interface IProcessLockInspector
{
    IReadOnlyList<string> GetRunningProcessNames();
    IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> targetPaths);
}

public interface IDiskGrowthWatcher : IAsyncDisposable
{
    event EventHandler<DiskGrowthAlert>? GrowthAlertRaised;
    Task StartAsync(WatcherOptions options, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
```

---

## 2. CLI Command & JSON Output Contract (`NexusClean.Cli`)

`NexusClean.Cli` exposes deterministic human-readable tables by default and structured UTF-8 JSON on `stdout` when `--json` is passed (with diagnostics/progress routed to `stderr`).

### 2.1 Commands

| Command | Arguments & Flags | Purpose | Exit Codes |
| :--- | :--- | :--- | :--- |
| `nexusclean scan` | `--drive C:` `--rules <path>` `--json` | Run full read-only signature scan, 5-tier impact evaluation, and guard checks. | `0` OK, `2` Invalid rules, `3` Cancelled |
| `nexusclean simulate` | `--drive C:` `--ids <id1,id2>` `--tier safe` `--json` | Execute non-destructive Dry-Run simulation and log simulated `CleanupBatchReport`. | `0` OK, `4` Guard blocked selection |
| `nexusclean clean` | `--drive C:` `--ids <id1,id2>` `--tier safe` `--yes` `--json` | Execute guarded user-space cleanup and write audit log entry. | `0` OK, `1` Partial error, `4` Guard blocked |
| `nexusclean relocate-plan` | `--id <findingId>` `--target-drive D:` `--json` | Compute `RelocationPlan` with 20% capacity buffer check and Vietnamese step-by-step guide. | `0` OK, `5` Insufficient target space |
| `nexusclean rules validate` | `--file <path>` `--json` | Validate a JSON signature catalog against `signature-rules.v1.schema.json` + Domain root guards. | `0` Valid, `2` Schema/safety violation |
| `nexusclean watch` | `--drive C:` `--min-free-gb 15` `--delta-gb 2` `--json` | Run foreground/tray observer emitting `DiskGrowthAlert` events. | `0` Clean stop |
| `nexusclean admin-exec` | `--request <req.json>` `--output <res.json>` | Internal parameterized UAC-elevated worker for approved `REQUIRES_ADMIN_ACTION` tasks. | `0` OK, `6` Not elevated, `7` Command failed |

---

## 3. Isolated Elevated Maintenance IPC Contract (`admin-exec`)

To guarantee security across the UAC boundary (Constitution Principle V), `ElevatedAdminScriptRunner` only accepts strongly typed JSON manifests matching:

```json
{
  "requestId": "8f29e04d-90e3-44e5-9f9a-07a0e292d10a",
  "createdAtUtc": "2026-11-15T04:05:00Z",
  "operations": [
    {
      "findingId": "a19d0c41e82f910b",
      "operationKind": "ResizeVssShadowStorage",
      "volume": "C:",
      "maxShadowSizeGb": 5
    },
    {
      "findingId": "c48b2019f012334a",
      "operationKind": "MoveWslDistribution",
      "distroName": "Ubuntu",
      "targetDirectory": "D:\\WSL\\Ubuntu"
    }
  ]
}
```

**Security Invariants**:
1. Arbitrary command strings are **absent from the schema**; the elevated binary maps `operationKind` directly to `ProcessStartInfo.ArgumentList` (`vssadmin.exe` or `wsl.exe`).
2. `maxShadowSizeGb` MUST be `>= 2` so the latest System Restore point is preserved.
3. `targetDirectory` MUST reside on a ready fixed NTFS drive with `AvailableFreeSpace >= SourceVhdxBytes * 1.20`.

---

## 4. UI Presentation Contract (`NexusClean.UI` WPF Dashboard)

1. **Summary Header Cards**:
   - System Drive Capacity & Current Free Space (`GB` + `%`).
   - Safe Reclaimable Now (`SAFE_CACHE_TIER1` + `REBUILD_ON_DEMAND`).
   - Relocatable Growth Data (`RELOCATE_TO_SECONDARY_DRIVE`).
   - Elevated System Maintenance (`REQUIRES_ADMIN_ACTION`).
2. **Findings Master-Detail Grid**:
   - Checkbox column disabled (`IsEnabled = false`) automatically when `Impact.Level` is `PROTECTED_NEVER_DELETE`, `RELOCATE_TO_SECONDARY_DRIVE`, or `REQUIRES_ADMIN_ACTION` (the latter two expose dedicated **"Hướng dẫn Di chuyển"** and **" Hàng đợi Admin"** action buttons instead of checkboxes).
   - Selecting any row populates the **Impact Inspector Drawer** showing `SummaryVi`, `SideEffectIfDeleted`, green badges for `PreservedAssets`, risk score badge (`0–10`), and active `LockingProcesses`.
3. **Mandatory Two-Step Action Bar**:
   - **"Mô phỏng Dọn dẹp (Dry-Run)"** button always available first; **"Thực thi Dọn dẹp An toàn"** displays exact byte count verified by Dry-Run and links directly to the generated JSONL Audit Report upon completion.
