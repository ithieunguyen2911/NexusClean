# Phase 0 Research: Non-Blocking Async Cleanup Engine & High-File-Count Optimization

**Feature**: `003-async-nonblocking-cleanup-engine`  
**Date**: 2026-11-17  
**Scope**: `NexusClean.Domain` (progress value object), `NexusClean.Application` (async background offloading & progress pipeline), `NexusClean.Infrastructure` (Win32 `DELETE`-only probe & exception-free `DeleteFileW`), `NexusClean.UI` (instant modal & cancelable live cleanup progress)

---

## 1. Why `NexusClean.UI` Entered `(Not Responding)` on `Dọn dẹp (29,20 GB)`

- **Decision**: Enforce a strict `Task.Run` background execution boundary in `AnalyzeDriveUseCase`, `SimulateCleanupUseCase`, `ExecuteCleanupUseCase`, and `SafeDeletionEngine`, and remove the redundant pre-confirmation `CleanAsync(..., dryRun: true)` pass from `MainDashboardViewModel.ExecuteCleanupAsync`.
- **Rationale**:
  - In C#/WPF, an `async Task` method runs **synchronously on the UI `Dispatcher` thread** until it hits an `await` on an incomplete `Task`.
  - All 8 `ICleanupStrategy` implementations previously returned `Task.FromResult(SafeDeletionEngine.ProcessDirectoriesContents(...))` — an already-completed task whose argument expression evaluated 100% synchronously on the UI thread!
  - Furthermore, `ExecuteCleanupAsync` ran a full `dryRun: true` pass across 29.20 GB *before* opening `Modal.ShowConfirmAsync(...)`, freezing the UI immediately upon button click before the modal could even render.
- **Alternatives Considered**:
  - *`Dispatcher.Yield()` inside loops*: Rejected because synchronous kernel I/O inside UI-thread continuations still causes frame drops and jank; full `Task.Run` background thread-pool isolation keeps the UI thread 100% free.

---

## 2. Eliminating Windows Defender (`WdFilter.sys`) & `.NET` Exception Storms

- **Decision**:
  1. Replace `Directory.GetFiles` + `new FileInfo` with single-pass `DirectoryInfo.EnumerateFiles("*", NonRecursiveOptions)` (`EnumerationOptions { AttributesToSkip = SkipMask, IgnoreInaccessible = true }`) so `FileInfo.Length` and `FileInfo.Attributes` come directly from `WIN32_FIND_DATA` with zero extra `GetFileAttributesExW` syscalls.
  2. Replace `new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, ...)` in Dry-Run with a lightweight Win32 `kernel32.dll` `CreateFileW` probe requesting **only `DELETE` (`0x00010000`) access** (zero `FILE_READ_DATA` / `FILE_WRITE_DATA` flags) + `CloseHandle`, and short-circuit per-file lock probing on unlocked immutable package caches (`Developer Caches` / `App Updaters`) once no locking processes are active.
  3. Replace exception-heavy `File.Delete` loops with Win32 `DeleteFileW` (`SetLastError = true`), classifying `ERROR_SHARING_VIOLATION (32)`, `ERROR_LOCK_VIOLATION (33)`, and `ERROR_ACCESS_DENIED (5)` via integer error codes instead of throwing/unwinding thousands of `.NET` `IOException` / `UnauthorizedAccessException` stack traces in `%TEMP%` and browser caches.
- **Rationale**:
  - Requesting `FileAccess.ReadWrite` on 100,000–300,000 files in `.cache`, `uv\cache`, `npm-cache`, and `%TEMP%` forces Windows Defender's minifilter (`WdFilter.sys`) to inspect file streams. Requesting `DELETE` disposition metadata access (`0x00010000`) checks exact NTFS delete/share permissions without opening data streams.
  - Avoiding `.NET` exception unwinding on hundreds of locked Chrome/Edge/Zalo/System temp files eliminates multi-second CLR stack-capture stalls.
- **Alternatives Considered**:
  - *Skipping lock checks completely in Dry-Run*: Rejected because `LockAndAdminSecurityTests` and `SC-004` require Dry-Run on locked `%TEMP%` directories to accurately identify `SkippedLockedPaths` (`ERROR_SHARING_VIOLATION`). `CreateFileW(DELETE)` preserves 100% accuracy on locked folders at native C speed without triggering Defender stream scans.

---

## 3. Permissions Architecture: Does Standard Cleanup Need Admin / UAC?

- **Decision**: Keep `NexusClean.UI` running strictly as a **standard non-elevated Windows user** (`asInvoker`). Never prompt for UAC on `Dọn dẹp` (`SAFE_CACHE_TIER1` / `REBUILD_ON_DEMAND`).
- **Rationale**:
  - Every `Tier 1` and `Tier 2` path (`%LOCALAPPDATA%\Temp`, `%USERPROFILE%\.cache`, `uv\cache`, `npm-cache`, `NuGet\v3-cache`, `SquirrelTemp`, and Chromium `User Data\...\Cache_Data`) is owned by the current user (`C:\Users\<User>\...`).
  - Occasional files inside `%TEMP%` created by `SYSTEM` or Windows Update services return `ERROR_ACCESS_DENIED (5)` and are cleanly recorded in `SkippedLockedPaths` without error or UAC interruption.
  - Administrator UAC remains strictly isolated to `Tier 4` (`ExecuteAdminActionCommand` -> `ElevatedAdminScriptRunner` for `vssadmin` and `wsl --manage --move`) per Constitution Principle V.
