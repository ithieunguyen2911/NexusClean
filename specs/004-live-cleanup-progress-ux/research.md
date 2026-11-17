# Phase 0 Research: Live Cleanup Progress Bar & Active Operation UX

**Feature**: `004-live-cleanup-progress-ux`  
**Date**: 2026-11-17  
**Scope**: `Domain` (optional `ICleanupStrategy` progress overload or `IProgress<CleanupProgress>`), `Infrastructure` (`SafeDeletionEngine` intra-folder throttled progress), `Application` (byte-weighted progress aggregation), `UI` (`MainWindow.xaml` & `Controls.xaml` custom Fluent `ProgressBar` + active footer progress panel)

---

## 1. Why the Current Progress Bar Looked "Missing" & Stuck at `0/13 • 0 B`

- **Finding 1 (Visual Visibility)**: In `MainWindow.xaml`, the `ProgressBar` was placed inside the top drive card with `Height="3"` and default WPF Aero styling. Against the 5-segment storage bar (`Height="10"`), a `3px` strip is virtually invisible, while the bottom action bar (where the user's eye actually rests after clicking **`Dọn dẹp`**) had no progress bar at all.
- **Finding 2 (Telemetry Granularity)**: When `%TEMP%` is `28.47 GB` (`item #1 of 13`), `SafeDeletionEngine.ProcessDirectoriesContents` ran to completion before `SimulateCleanupUseCase` / `ExecuteCleanupUseCase` emitted the second `CleanupProgress` event. Thus, the UI stayed frozen at `(0/13) • Đã giải phóng 0 B` for the entire `%TEMP%` pass.

---

## 2. Intra-Folder Throttled Progress Without Hurting Win32 Throughput

- **Decision**: Pass an internal/interface-compatible throttled progress callback (`IProgress<(long BytesDelta, int FilesDelta, int SkippedDelta)>?` or an overloaded `SimulateAsync`/`ExecuteAsync` with default `ICleanupStrategy` interface method or `IProgress<CleanupItemProgress>`) into `SafeDeletionEngine.TraverseAndCleanDirectory`, emitting updates only when **`filesDelta >= 200` OR `Environment.TickCount64 - lastTick >= 120 ms`**.
- **Rationale**:
  - Updating WPF `Dispatcher` on every file (`100,000+` calls) would flood the UI message queue.
  - Throttling at `~120 ms` (`~8 FPS` telemetry updates) or `200` files adds `< 0.1%` CPU overhead while making the progress bar, percentage, file count, and reclaimed GB climb smoothly in real time even inside a single `28 GB` folder!
  - Adding a default interface method on `ICleanupStrategy` (pure C# in `NexusClean.Domain`) preserves 100% backward compatibility with any existing callers/tests.
- **Alternatives Considered**:
  - *Periodic polling timer reading `DirectoryInfo`*: Rejected—wasteful extra I/O and inaccurate.

---

## 3. Native Windows 11 `ProgressBar` Template & Active Footer Layout

- **Decision**:
  1. Add a custom `FluentProgressBarStyle` in `Themes/Controls.xaml` (`Height="7"`, `CornerRadius="3.5"`, smooth theme-aware `BarFreeTrackBrush` track and `AccentPrimaryBrush` indicator, plus crisp indeterminate marquee animation when `IsIndeterminate="True"`).
  2. Replace the bottom action bar's passive idle summary during `IsBusy == true` with a dedicated **Active Operation Progress Bar** (`Grid` swapped via `IsBusy` visibility) displaying:
     - Left: Operation title (`Đang dọn dẹp: Thư mục tệp tạm thời...` / `Đang quét ổ C:...` / `Đang xem trước Dry-Run...`) + live metric line (`12,45 GB / 32,86 GB • 48.210 tệp • Bỏ qua 14 tệp đang mở`)
     - Center/Bottom: Full-width `FluentProgressBarStyle` (`Height="7"`) + right-aligned percentage (`38%`)
     - Right: Prominent **`Dừng`** button so the user never has to hunt back up to the top-right corner to cancel.
  3. Keep a clean `IsNotBusy` binding on `ScanDrive`, `SelectAllSafe`, `ClearSelection`, `SimulateDryRun`, `ExecuteCleanup`, and `ComboBox` drive picker so buttons clearly reflect busy vs. ready states.
- **Rationale**:
  - Keeping the progress bar right in the bottom action bar (plus a subtle live pill in the top drive card) matches modern Windows 11 apps and installers where the primary CTA seamlessly transforms into the active progress + cancel strip.
