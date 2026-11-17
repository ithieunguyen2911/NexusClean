# Feature Specification: Non-Blocking Async Cleanup Engine & High-File-Count Optimization

**Feature Branch**: `003-async-nonblocking-cleanup-engine`  
**Created**: 2026-11-17  
**Status**: Draft  
**Input**: User request: "Kiểm tra xem giúp tôi tại sao khi nhấn vào nút Dọn dẹp nó lại bị đứng máy. Có yêu cầu quyền gì không?" (accompanied by live screenshot showing `NexusClean (Not Responding)` when clicking `Dọn dẹp (29,20 GB)` across 13 safe user-space cache items).

## Root Cause Diagnosis

1. **Synchronous Execution on the WPF UI Thread (`Task.FromResult` Trap)**:
   - `FullDirectoryDeleteStrategy`, `DeleteUnlockedItemsStrategy`, `SelectiveSubfolderPurgeStrategy`, and `SemVerDuplicatePruneStrategy` implemented `SimulateAsync` and `ExecuteAsync` via `Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(...))`, and `SimulateCleanupUseCase` / `ExecuteCleanupUseCase` invoked `_snapshotProvider.CaptureSnapshot()` and `_evaluateImpact.Evaluate(...)` (`rstrtmgr.dll` P/Invoke) synchronously before yielding.
   - When triggered from `MainDashboardViewModel.ExecuteCleanupCommand` on the WPF `Dispatcher` thread, 100% of filesystem traversal ran on the UI thread, causing Windows DWM to mark the window `(Not Responding)` after 5 seconds.
2. **Per-File `FileAccess.ReadWrite` Handle Storm Triggering Windows Defender (`WdFilter.sys`)**:
   - Before displaying the confirmation modal, `ExecuteCleanupAsync` ran a full pre-flight `CleanAsync(..., dryRun: true)` across **29.20 GB** of developer and temp caches (`uv\cache`, `.cache`, `npm-cache`, `%TEMP%`, 4 Chromium browsers — typically **100,000–300,000 small files**).
   - For every single file, `SafeDeletionEngine.IsFileCurrentlyLockedForDelete` opened `new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, ...)`, forcing Windows Defender real-time protection (`WdFilter.sys`) to intercept hundreds of thousands of write-intent file opens.
3. **Redundant Double-Pass Pre-Flight Scan Before Modal Display**:
   - Clicking `Dọn dẹp (29,20 GB)` re-walked all 29.20 GB before even showing the confirmation modal, despite `AnalyzeDriveUseCase` having already sized and lock-inspected every selected `StorageFinding`.
4. **Permissions Verification (User Question)**:
   - Standard cleanup (`Tier 1` & `Tier 2` user-space caches) requires **NO Administrator / UAC elevation**. Any individual file inside `%TEMP%` held by a `System` or elevated process raises `UnauthorizedAccessException` or sharing violation (`32`/`33`) and is safely skipped (`SkippedLockedPaths`). Only `Tier 4` items (`WSL2` / `VSS Shadow Copy`) use UAC via their dedicated button.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Instant Confirmation Modal & Zero-Freeze Background Cleanup (Priority: P1)

As a desktop user clicking `Dọn dẹp (29,20 GB)` or `Xem trước (Dry-Run)` on tens of gigabytes of developer/browser caches, I want the confirmation dialog to open instantaneously (`< 50 ms`) and the cleanup/simulation to execute strictly on background worker threads with live progress and a `Dừng` (Cancel) button so the window never enters `(Not Responding)`.

**Why this priority**: Directly fixes the application freeze reported by the user.

**Independent Test**: Populate a sandbox with 10,000+ small cache files (including locked/read-only files), run `SimulateAsync` and `ExecuteAsync` while asserting the calling UI synchronization context is never blocked for `> 16 ms` and cancellation stops execution within `< 1 s`.

**Acceptance Scenarios**:

1. **Given** 13 safe findings totaling `29+ GB` are selected, **When** the user clicks **`Dọn dẹp (29,20 GB)`**, **Then** the in-window confirmation modal opens within **`< 50 ms`** using already-scanned metrics (or cached Dry-Run results) with zero disk I/O blocking the UI thread.
2. **Given** the user clicks confirm to start cleanup (or clicks **`Xem trước (Dry-Run)`**), **When** tens of thousands of files are processed, **Then** 100% of snapshot capture, `rstrtmgr.dll` inspection, and directory deletion execute inside background worker threads (`Task.Run`) while the UI remains 60 FPS responsive, streams live progress (`Đang dọn X/Y mục • Đã giải phóng ...`), and allows instant cancellation via **`Dừng`**.

---

### User Story 2 - Defender-Friendly High-Throughput Enumeration & Deletion Engine (Priority: P1)

As a developer with massive package caches (`uv\cache`, `.cache`, `npm-cache`, `%TEMP%` containing 100,000+ files), I want Dry-Run simulation and cleanup execution to use single-pass `WIN32_FIND_DATA` enumeration (`DirectoryInfo.EnumerateFileSystemInfos` + `EnumerationOptions`) without opening `FileAccess.ReadWrite` handles on every file so Dry-Run completes in seconds instead of triggering Windows Defender storms.

**Why this priority**: Eliminates the 10x–30x I/O slowdown on developer cache directories.

**Independent Test**: Run `SimulateCleanupUseCase` and `ExecuteCleanupUseCase` against a 15,000-file sandbox tree containing locked handles (`FileShare.None`) and read-only attributes; verify `< 2 s` completion, zero `FileAccess.ReadWrite` handle opens during Dry-Run, and accurate lock-skipping during execution.

**Acceptance Scenarios**:

1. **Given** a cache directory with `15,000+` files, **When** `SimulateCleanupUseCase` runs, **Then** it computes reclaimable bytes via single-pass metadata enumeration (`EnumerationOptions`) + batched Restart Manager lock checks without opening per-file `FileStream(FileAccess.ReadWrite)` handles, completing in `< 2 s`.
2. **Given** locked or system-owned files exist inside `%TEMP%` under a standard non-admin user token, **When** `ExecuteCleanupUseCase` runs, **Then** unlocked files are deleted cleanly while `UnauthorizedAccessException` and Win32 sharing violations (`32`/`33`) are skipped without requiring Administrator elevation or freezing.

### Edge Cases

- **System-owned files inside `%TEMP%` without Admin rights**: Must be caught via `UnauthorizedAccessException` and recorded in `SkippedLockedPaths` without aborting the directory walk or prompting for UAC.
- **Read-only cache packfiles (e.g., Git/NuGet/uv cache objects)**: Must clear `FileAttributes.ReadOnly` only immediately before `File.Delete` when not locked.
- **User clicks `Dừng` mid-cleanup**: Must honor `CancellationToken` cleanly, log partial reclaimed bytes to `audit.jsonl`, and refresh UI metrics accurately.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-PERF-001 (Guaranteed Background Offloading)**: `SimulateCleanupUseCase`, `ExecuteCleanupUseCase`, and `SafeDeletionEngine` MUST execute all snapshot capture, Win32 Restart Manager calls, and filesystem I/O off the UI thread (`Task.Run`), never blocking a `SynchronizationContext` for more than `16 ms`.
- **FR-PERF-002 (Instant Confirmation Modal)**: Clicking `Dọn dẹp` in `NexusClean.UI` MUST open the confirmation modal immediately (`< 50 ms`) without running a synchronous pre-flight full-disk Dry-Run walk first.
- **FR-PERF-003 (Zero Write-Handle Dry-Run)**: `SafeDeletionEngine` in `dryRun: true` mode MUST NOT open `FileStream(..., FileAccess.ReadWrite)` handles per file (preventing Windows Defender `WdFilter.sys` scan storms); it MUST use single-pass `DirectoryInfo` `EnumerationOptions` metadata plus Restart Manager lock sets.
- **FR-PERF-004 (Single-Pass `WIN32_FIND_DATA` Deletion & Parallelism)**: `SafeDeletionEngine` MUST use `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` (`EnumerationOptions` with `AttributesToSkip = ReparsePoint | Offline` and `IgnoreInaccessible = true`) so `FileInfo.Length` and `FileInfo.Attributes` require zero extra `GetFileAttributesExW` syscalls per file.
- **FR-PERF-005 (Live Cleanup Progress & Cancellation)**: `DiskAnalysisOrchestrator.CleanAsync` MUST accept `IProgress<CleanupProgress>?` and `CancellationToken` so `NexusClean.UI` displays real-time item/byte progress and enables the `Dừng` button during both `Xem trước (Dry-Run)` and `Dọn dẹp`.
- **FR-PERF-006 (Standard-User Permission Clarity)**: Surface clear Vietnamese messaging confirming standard cleanup runs 100% under normal user permissions without UAC, automatically skipping OS/process-locked files.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-PERF-001**: Clicking **`Dọn dẹp`** opens the confirmation modal in **`< 50 ms`** (`0` `(Not Responding)` freezes on the UI thread).
- **SC-PERF-002**: Dry-Run simulation over a `10,000`-file sandbox completes in **`< 1.5 seconds`** with **`0`** `FileAccess.ReadWrite` file-open operations.
- **SC-PERF-003**: UI Dispatcher thread remains responsive (`< 16 ms` per UI frame) throughout both `Dry-Run` and `Execute` cleanup runs, and clicking **`Dừng`** halts cleanup within **`< 1 second`**.
- **SC-PERF-004**: Full solution builds in Release with **`0 Warning(s), 0 Error(s)`** and **`100%`** passing sandbox unit/integration tests (including a new high-file-count non-blocking responsiveness test).
