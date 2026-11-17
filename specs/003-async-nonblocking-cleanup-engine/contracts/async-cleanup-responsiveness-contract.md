# Contract: Async Cleanup Responsiveness & Win32 I/O Safety

**Feature**: `003-async-nonblocking-cleanup-engine`

---

## 1. Threading & UI Responsiveness Contract

1. **UI Thread Budget (`< 16 ms`)**:
   - Invoking `ScanDriveCommand`, `SimulateDryRunCommand`, or `ExecuteCleanupCommand` on the WPF `Dispatcher` thread MUST yield back to the message pump in `< 16 ms`.
   - `_snapshotProvider.CaptureSnapshot()`, `WindowsProcessInspector.GetLockingProcesses()` (`rstrtmgr.dll`), `SafeDeletionEngine`, and `JsonlAuditLogRepository` MUST NEVER run synchronously on the UI thread.
2. **Instant Confirmation Modal (`< 50 ms`)**:
   - `ExecuteCleanupCommand` MUST populate `ConfirmationModalViewModel` directly from the selected `FindingItemViewModel` collection (or most recent `CleanupBatchReport` if `Xem trước (Dry-Run)` was already executed on the same selection) without performing filesystem traversal before opening the modal.
3. **Unified Cancellation (`CancelScanCommand` -> `CancelActiveOperationCommand`)**:
   - The top **`Dừng`** button MUST cancel whichever background operation is currently running (`Scan`, `Dry-Run`, or `Cleanup`) within `< 1 second`.

---

## 2. Win32 Filesystem I/O Contract (`SafeDeletionEngine`)

| Operation Mode | Kernel API Used | Access Flags | Win32 Error Codes Treated as `SkippedLocked` | Error Codes Treated as Already Gone |
| :--- | :--- | :--- | :--- | :--- |
| **Directory Walk** | `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` | `AttributesToSkip = ReparsePoint \| Offline`, `IgnoreInaccessible = true` | Handled natively by `IgnoreInaccessible` | `ERROR_PATH_NOT_FOUND (3)` |
| **Dry-Run Lock Probe** | `CreateFileW` + `CloseHandle` (`kernel32.dll`) | `dwDesiredAccess = 0x00010000` (`DELETE` only — **zero** `GENERIC_READ` / `GENERIC_WRITE`) | `ERROR_ACCESS_DENIED (5)` (when not ReadOnly), `ERROR_SHARING_VIOLATION (32)`, `ERROR_LOCK_VIOLATION (33)` | `ERROR_FILE_NOT_FOUND (2)`, `ERROR_PATH_NOT_FOUND (3)` |
| **Execute File Delete** | `DeleteFileW` (`kernel32.dll`) | Clears `FileAttributes.ReadOnly` only if set, then `DeleteFileW` | `ERROR_ACCESS_DENIED (5)`, `ERROR_SHARING_VIOLATION (32)`, `ERROR_LOCK_VIOLATION (33)` | `ERROR_FILE_NOT_FOUND (2)`, `ERROR_PATH_NOT_FOUND (3)` |

---

## 3. Permissions Disclosure Contract

- Standard Cleanup confirmation modal MUST include the explicit bullet:
  `"Chạy bằng quyền người dùng chuẩn (không cần quyền Admin); các tệp đang được hệ thống hoặc ứng dụng mở sẽ tự động bỏ qua."`
