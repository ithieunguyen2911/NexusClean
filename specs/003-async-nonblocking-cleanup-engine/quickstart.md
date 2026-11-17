# Quickstart & Responsiveness Verification Guide

**Feature**: `003-async-nonblocking-cleanup-engine`

---

## 1. Automated Build & High-File-Count Sandbox Benchmark

Run the full Release test suite (including the new 5,000+ file sandbox responsiveness & `SynchronizationContext` non-blocking test):

```powershell
cd D:\AI_Learning_Repository\NexusClean
dotnet test NexusClean.sln -c Release
```

**Expected**:
- `0 Warning(s), 0 Error(s)`
- Synchronous caller-thread time before `CleanAsync` yields: **`< 15 ms`**
- 5,000-file Dry-Run + Execute with live locked handles completes in **`< 1.5 s`** with `0` UI-thread blocking.

---

## 2. Live Desktop UI Verification (`29+ GB` Real-World Caches)

1. Launch the desktop app:
   ```powershell
   .\run_ui.ps1
   ```
2. Click **`Quét ổ đĩa`** (`C:\`).
3. With all safe cache items selected (e.g., `13 mục • 29,20 GB`), click **`Dọn dẹp (29,20 GB)`**:
   - **Expected**: Confirmation modal appears **instantly (`< 50 ms`)** — window title NEVER shows `(Not Responding)`, window can be dragged/resized smoothly, and `Để sau` / `Esc` dismisses immediately.
4. Click **`Xem trước (Dry-Run)`**:
   - **Expected**: Progress bar animates smoothly, status text updates live every few hundred files (`Đang xem trước ...`), **`Dừng`** button remains clickable at all times, and zero files are deleted.
5. Click **`Dọn dẹp (...)`** → Confirm:
   - **Expected**: Runs without requesting Administrator / UAC permissions, skips open Chrome/Edge/Zalo/System files cleanly without hanging, writes audit record to `%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`, and refreshes the storage bar automatically.
