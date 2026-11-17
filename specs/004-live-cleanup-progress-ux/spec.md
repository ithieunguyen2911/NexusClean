# Feature Specification: Live Cleanup Progress Bar & Active Operation UX

**Feature Branch**: `004-live-cleanup-progress-ux`  
**Created**: 2026-11-17  
**Status**: Draft  
**Input**: User request: "Nó thiếu 1 thanh process bar để biết được là nó đang dọn dẹp, cải thiện UI|UX giúp tôi." (accompanied by live screenshot showing `Đang dọn dẹp (0/13): Thư mục tệp tạm thời (%TEMP% & C:\Temp) (Temp) • Đã giải phóng 0 B` with only a barely-visible `3px` top line and `28.47 GB` inside the first item).

## Problem Diagnosis (Visual & Telemetry Audit of Current Screenshot)

1. **Invisible `3px` Progress Bar Hidden Inside the Top Drive Card**:
   - The existing `ProgressBar` in `MainWindow.xaml` is only `Height="3"`, tucked between the drive title and the 5-segment storage bar, making it look like a decorative divider rather than an active task progress indicator.
2. **Stuck `(0/13) • Đã giải phóng 0 B` Counter During Large Folders (`28.47 GB` `%TEMP%`)**:
   - `SimulateCleanupUseCase` and `ExecuteCleanupUseCase` only emit `CleanupProgress` at the start and end of each `StorageFinding`. When the first item (`%TEMP%`) is `28.47 GB` with tens of thousands of files, the UI stays frozen at `0/13 • 0 B` for the entire duration of item #1, creating the impression that cleanup has stalled.
3. **Weak Active-State Feedback in the Bottom Action Bar & Toolbar**:
   - While cleaning or scanning, the bottom action bar simply dims `Xem trước (Dry-Run)` and `Dọn dẹp (32,86 GB)` without showing a clear, readable progress bar, percentage, processed file count, or elapsed/live byte counter right where the user just clicked **`Dọn dẹp`**.
   - Meanwhile, the **`Quét ổ đĩa`** button remains visually solid blue (`IsEnabled` not bound to `!IsBusy`), and the **`Dừng`** button lacks a subtle danger/caution emphasis when an operation is actively running.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prominent Live Progress Bar & Real-Time Byte/File Counter (Priority: P1)

As a user who just clicked **`Dọn dẹp (32,86 GB)`** or **`Xem trước (Dry-Run)`**, I want to see a clear, unmistakable Windows 11-style progress bar and live counter (percentage, current item name, files processed, and bytes reclaimed updating continuously every ~150 ms even inside a single 28 GB folder) right in the bottom action bar / active status banner so I always know the app is actively working.

**Why this priority**: Directly answers the user's feedback ("thiếu 1 thanh process bar để biết được là nó đang dọn dẹp").

**Independent Test**: Run `ExecuteCleanupUseCase` / `SimulateCleanupUseCase` over a 1,200+ file single `StorageFinding` and verify that `IProgress<CleanupProgress>` emits multiple incremental intra-folder updates (`CumulativeBytesProcessed > 0`, `CumulativeFilesProcessed > 0`) *before* item #1 finishes, and verify in `NexusClean.UI` that the dedicated progress bar, percentage text, and active footer state render smoothly.

**Acceptance Scenarios**:

1. **Given** a cleanup or Dry-Run is running on a multi-gigabyte folder (`%TEMP%` `28.47 GB`), **When** files are being processed, **Then** `SafeDeletionEngine` throttles and emits intra-folder progress updates (every `150 ms` or `200` files) so the UI counter and progress bar advance smoothly in real time instead of sitting at `0/13 • 0 B`.
2. **Given** `IsBusy == true` during Scan, Dry-Run, or Cleanup, **When** viewing the bottom action bar, **Then** a dedicated, clearly visible **Active Operation Progress Panel** displays a `6px–8px` rounded progress bar, operation badge (`Đang quét...` / `Đang xem trước...` / `Đang dọn dẹp...`), percentage (`%`), live byte/file counter, and an accessible **`Dừng`** button.
3. **Given** `IsBusy == true`, **When** viewing action buttons (`Quét ổ đĩa`, `Chọn mục an toàn`, `Bỏ chọn`, `Xem trước`, `Dọn dẹp`), **Then** conflicting action buttons are cleanly disabled (`IsEnabled = !IsBusy`) while **`Dừng`** is highlighted and immediately clickable.

---

### User Story 2 - Clean Active-State Visual Hierarchy & Result Summary Banner (Priority: P2)

As a user watching cleanup complete, I want a calm, clear completion state in the status/footer area showing exact reclaimed GB, skipped locked files count, and zero-clutter layout consistent with our native Windows 11 Light/Dark design system (zero emojis, zero neon glow).

**Why this priority**: Completes the end-to-end cleanup UX loop from click → live progress → clear completion feedback.

**Independent Test**: Complete a Dry-Run or Cleanup pass in `NexusClean.UI` and verify the progress panel transitions cleanly into the result summary (`Đã giải phóng ... • Bỏ qua ... tệp đang mở`) and buttons re-enable properly.

### Edge Cases

- **Indeterminate / Initial Phase (`0%` before first file count arrives)**: Progress bar should show `IsIndeterminate="True"` during initial snapshot/startup (`< 5%`) and switch to smooth determinate percentage (`0–100%`) as bytes/items progress.
- **Very fast small-folder cleanup (`< 100 ms`)**: Progress panel must still transition cleanly back to idle without leaving stale percentage or stuck `IsBusy` state.
- **User clicks `Dừng` mid-folder**: Cancellation must abort cleanly within `< 1 s`, reset `IsIndeterminate = false`, and re-enable controls after refreshing drive metrics.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-UX-001 (Intra-Folder Throttled Progress Reporting)**: `SafeDeletionEngine` and `ICleanupStrategy` MUST accept an optional progress callback (`Action<long, int, int>?` for delta bytes, files, skipped locked) throttled at ~`150 ms` / `200` files so `CleanupProgress` streams live `CumulativeBytesProcessed` and `CumulativeFilesProcessed` *within* large folders like `28.47 GB` `%TEMP%`.
- **FR-UX-002 (Byte-Weighted Progress Percentage)**: `MainDashboardViewModel` MUST compute determinate `OperationProgressPercent` weighted by processed bytes vs. total selected bytes (or completed rules during Scan), falling back to `IsIndeterminateProgress = true` during initial snapshot capture.
- **FR-UX-003 (Prominent Bottom Action Progress Bar)**: `MainWindow.xaml` MUST replace the hidden `3px` line with a clear, native Windows 11 `Active Operation Progress` bar (`6px` height + live label + `%` + live reclaimed bytes/files + **`Dừng`** button) integrated cleanly into the bottom action bar / active banner when `IsBusy == true`.
- **FR-UX-004 (Consistent Busy Button States)**: Bind `IsEnabled="{Binding IsNotBusy}"` on `ScanDrive`, `SelectAllSafe`, `ClearSelection`, `SimulateDryRun`, `ExecuteCleanup`, and `ComboBox` drive selector so users cannot trigger overlapping operations, while keeping `CancelScanCommand` (`Dừng`) enabled and clearly styled during `IsBusy`.
- **FR-UX-005 (Zero-Emoji Native Windows 11 Compliance)**: All new progress controls and labels MUST use `Theme.Light.xaml` / `Theme.Dark.xaml` tokens and vector icons (`Icons.xaml`) with zero Unicode emojis.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-UX-001**: During cleanup of a `1,200+` file single finding, `IProgress<CleanupProgress>` emits **multiple incremental updates** with `CumulativeFilesProcessed > 0` before the finding completes.
- **SC-UX-002**: During `IsBusy == true`, a visible `>= 6px` progress bar, percentage, live reclaimed bytes, and **`Dừng`** button are immediately visible in the UI without scrolling.
- **SC-UX-003**: Full solution builds with **`0 Warning(s), 0 Error(s)`**, passes all automated sandbox tests (`100%`), and passes the zero-emoji check.
