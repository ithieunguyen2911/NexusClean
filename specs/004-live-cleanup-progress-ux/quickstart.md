# Quickstart & Visual Verification Guide: Live Cleanup Progress UX

**Feature**: `004-live-cleanup-progress-ux`

---

## 1. Automated Build & Intra-Folder Progress Tests

```powershell
cd D:\AI_Learning_Repository\NexusClean
dotnet build NexusClean.sln -c Release
dotnet test NexusClean.sln -c Release
```

**Expected**:
- `0 Warning(s), 0 Error(s)`
- `HighVolumeAsyncCleanupTests` verifies that a single `1,200+` file finding emits **multiple** incremental `CleanupProgress` updates (`CumulativeFilesProcessed > 0` before completion).
- Zero Unicode emojis in `src/NexusClean.UI/`.

---

## 2. Live Desktop UI Verification

1. Launch `.\run_ui.ps1`.
2. Click **`Quét ổ đĩa`** (`C:`):
   - Verify the bottom bar transforms into the active progress panel with a clear `7px` Windows 11 progress bar, `%` readout, and **`Dừng`** button while `Quét ổ đĩa` is disabled.
3. Select safe cache items and click **`Xem trước (Dry-Run)`** or **`Dọn dẹp (...)`** → Confirm:
   - Verify the bottom progress bar advances smoothly, `ActiveOperationDetail` counts up live bytes (`MB`/`GB`) and files (`tệp`) *even while processing `%TEMP%` (`item 1/13`)*, and **`Dừng`** remains responsive at all times.
4. Verify both **Light** and **Dark** themes render the custom `ProgressBar` cleanly.
