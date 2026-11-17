# Phase 1 Data Model: Live Cleanup Progress Bar & Active Operation UX

**Feature**: `004-live-cleanup-progress-ux`

---

## 1. Domain Contract Extension (`NexusClean.Domain.Interfaces`)

### `CleanupItemProgress` (Pure C# Record)
Emitted by `SafeDeletionEngine` / `ICleanupStrategy` at throttled intervals (`~120 ms` / `200` files) inside large folders:

| Field | Type | Purpose |
| :--- | :--- | :--- |
| `BytesProcessedInItem` | `long` | Cumulative simulated or deleted bytes within the current `StorageFinding` |
| `FilesProcessedInItem` | `int` | Cumulative simulated or deleted files within the current `StorageFinding` |
| `SkippedLockedInItem` | `int` | Cumulative locked/inaccessible files skipped within the current `StorageFinding` |

### `CleanupProgress` (Extended Pure C# Record)
Existing record extended with optional `TotalTargetBytes` (`long = 0`) so the UI can compute accurate byte-weighted percentage (`0..100%`) across heterogeneous items (`28.47 GB` Temp vs `4 KB` SquirrelTemp):

| Field | Type | Purpose |
| :--- | :--- | :--- |
| `CurrentRuleId` | `string` | Active finding rule ID |
| `CurrentDisplayName` | `string` | Active finding display name |
| `CompletedItems` | `int` | Count of fully completed findings |
| `TotalItems` | `int` | Total findings in batch |
| `CumulativeBytesProcessed` | `long` | Live bytes across completed items + current item intra-folder progress |
| `TotalTargetBytes` | `long` | Sum of `SizeInBytes` across all selected findings (`>= 0`) |
| `CumulativeFilesProcessed` | `int` | Live files processed across batch |
| `CumulativeSkippedLocked` | `int` | Live locked files skipped across batch |
| `IsDryRun` | `bool` | `true` for Dry-Run preview, `false` for live cleanup |

---

## 2. Presentation State (`MainDashboardViewModel`)

| Property | Type | Purpose |
| :--- | :--- | :--- |
| `IsBusy` | `bool` | Active Scan, Dry-Run, or Cleanup in progress |
| `IsNotBusy` | `bool` | `!IsBusy` — disables conflicting toolbar/footer buttons cleanly |
| `IsOperationIndeterminate` | `bool` | `true` during initial snapshot capture before first progress tick arrives |
| `OperationProgressPercent` | `double` | `0..100` byte-weighted progress (or rule-weighted during Scan) |
| `OperationProgressPercentLabel` | `string` | e.g., `"38%"` or `"..."` when indeterminate |
| `ActiveOperationTitle` | `string` | e.g., `"Đang dọn dẹp (1/13): Thư mục tệp tạm thời..."` |
| `ActiveOperationDetail` | `string` | e.g., `"Đã giải phóng 12,45 GB / 32,86 GB • 48.210 tệp • Bỏ qua 14 tệp đang mở"` |
