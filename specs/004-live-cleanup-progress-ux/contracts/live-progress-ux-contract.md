# Contract: Live Cleanup Progress Bar & Active State UX

**Feature**: `004-live-cleanup-progress-ux`

---

## 1. Progress Telemetry Contract

1. **Intra-Folder Update Cadence**:
   - `SafeDeletionEngine` MUST report incremental `(BytesProcessedInItem, FilesProcessedInItem, SkippedLockedInItem)` every `200` processed entries OR every `120 ms` (whichever comes first), so multi-gigabyte directories never leave the UI stuck at `0 B`.
2. **Byte-Weighted Percentage**:
   - When `TotalTargetBytes > 0`, `OperationProgressPercent = Clamp(CumulativeBytesProcessed * 100.0 / TotalTargetBytes, 2.0, 99.0)` while running, transitioning to `100.0` on completion.
   - During initial snapshot capture (before the first item tick), `IsOperationIndeterminate = true`.

---

## 2. Visual Progress Bar & Control State Contract (`MainWindow.xaml` + `Controls.xaml`)

1. **Custom `FluentProgressBarStyle`**:
   - Minimum `Height="7"`, `CornerRadius="3.5"`, themed `BarFreeTrackBrush` track and `AccentPrimaryBrush` indicator, supporting smooth `IsIndeterminate` marquee animation without Win32 green classic styling.
2. **Active Footer Swap (`IsBusy == true`)**:
   - Bottom action bar swaps seamlessly from idle summary + buttons to the **Live Progress Panel** showing `ActiveOperationTitle`, `ActiveOperationDetail`, `FluentProgressBarStyle` (`Height="7"`), `OperationProgressPercentLabel`, and **`Dừng`** button.
   - `Quét ổ đĩa`, `Phân vùng ComboBox`, `Chọn mục an toàn`, `Bỏ chọn`, `Xem trước (Dry-Run)`, and `Dọn dẹp` MUST be disabled (`IsEnabled="{Binding IsNotBusy}"`) while `IsBusy == true`.
