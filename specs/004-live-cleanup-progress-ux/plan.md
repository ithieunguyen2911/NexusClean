# Implementation Plan: Live Cleanup Progress Bar & Active Operation UX

**Branch**: `004-live-cleanup-progress-ux` | **Date**: 2026-11-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-live-cleanup-progress-ux/spec.md`

## Summary

Resolve the "missing progress bar / stuck at `0/13 • 0 B`" UX issue observed when cleaning multi-gigabyte folders (`28.47 GB` `%TEMP%`):
1. **Intra-Folder Throttled Telemetry (`Domain` + `Infrastructure` + `Application`)**: Add pure `CleanupItemProgress` and byte-weighted `TotalTargetBytes` to `CleanupProgress`, and emit throttled (`~120 ms` / `200` files) progress updates from inside `SafeDeletionEngine.TraverseAndCleanDirectory` so bytes, file counts, and `%` advance continuously even inside item `#1`.
2. **Prominent Windows 11 Progress Bar & Active Footer Swap (`Controls.xaml` + `MainWindow.xaml` + `MainDashboardViewModel.cs`)**: Add a custom `FluentProgressBarStyle` (`Height="7"`, rounded, Light/Dark themed, indeterminate marquee support) and replace the hidden `3px` top sliver with an unmistakable **Active Operation Progress Panel** in the bottom action bar showing live title, `%`, bytes/files processed, and a **`Dừng`** button, while disabling conflicting action buttons via `IsNotBusy`.

## Technical Context

**Language/Version**: C# 12 on `.NET 8.0` (`net8.0` core + `net8.0-windows` WPF)

**Primary Dependencies**: Native `.NET 8` BCL, WPF `PresentationFramework`, `CommunityToolkit.Mvvm 8.3.2`

**Storage**: N/A (existing JSONL audit log unchanged)

**Testing**: `xUnit` sandbox tests (`HighVolumeAsyncCleanupTests` verifying intra-folder incremental progress emission)

**Target Platform**: Windows 10 & Windows 11 x64

**Project Type**: 4-Layer Clean Architecture Desktop Application

**Performance Goals**: Throttled telemetry (`~120 ms` / `200` files) adds `< 0.1%` overhead; UI renders at `60 FPS`

**Constraints**: `NexusClean.Domain` remains 100% pure C#; zero Unicode emojis; zero breaking changes to existing CLI or test contracts

**Scale/Scope**: Smooth progress visibility across `1` to `500,000+` files and `0 KB` to `100+ GB` batches

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Constitution Principle | Pre-Design Gate | Post-Design Gate | Verification Notes |
| :--- | :--- | :--- | :--- |
| **I. 4-Layer Clean Architecture & Domain Purity** | PASS | PASS | `CleanupItemProgress` & `CleanupProgress` are pure C# records in `NexusClean.Domain`. |
| **II. SOLID & Canonical OOD Patterns** | PASS | PASS | Default interface method on `ICleanupStrategy` preserves backward compatibility & OCP. |
| **III. Zero-Data-Loss Safety Guards & Profile Preservation** | PASS | PASS | Zero changes to safety guard logic. |
| **IV. 5-Tier Impact Analysis & Relocation-Over-Deletion** | PASS | PASS | Unchanged. |
| **V. Process-Lock Awareness, Isolated Admin Flow & Auditability** | PASS | PASS | Skipped locked count is now surfaced live in `ActiveOperationDetail`. |
| **VI. Sandbox-Isolated Automated Testing** | PASS | PASS | Verified via `HighVolumeAsyncCleanupTests` in `%TEMP%\NexusClean.Tests\<guid>`. |

## Project Structure

### Documentation (this feature)

```text
specs/004-live-cleanup-progress-ux/
├── spec.md              # Screenshot diagnosis & live progress UX requirements
├── plan.md              # This implementation plan (/speckit-plan output)
├── research.md          # Phase 0 telemetry throttling & Fluent ProgressBar design
├── data-model.md        # Phase 1 CleanupItemProgress, CleanupProgress & ViewModel properties
├── quickstart.md        # Phase 1 automated & live UI verification guide
├── contracts/
│   └── live-progress-ux-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks — generated next)
```

### Source Code (repository root)

```text
src/
├── NexusClean.Domain/
│   └── Interfaces/DomainContracts.cs            # Add CleanupItemProgress, TotalTargetBytes, default ICleanupStrategy overloads
├── NexusClean.Application/
│   └── UseCases/SimulateAndExecuteCleanupUseCases.cs # Aggregate intra-folder deltas + TotalTargetBytes into CleanupProgress
├── NexusClean.Infrastructure/
│   └── Strategies/CleanupStrategies.cs          # Throttled (~120ms / 200 files) intra-folder progress reporting in SafeDeletionEngine
└── NexusClean.UI/
    ├── Themes/Controls.xaml                     # Add FluentProgressBarStyle (Height=7, determinate + indeterminate animation)
    ├── ViewModels/MainDashboardViewModel.cs     # Add IsNotBusy, IsOperationIndeterminate, OperationProgressPercentLabel, ActiveOperationTitle/Detail
    └── Views/MainWindow.xaml                    # Prominent Active Operation Progress Panel in bottom bar + IsNotBusy bindings

tests/
└── NexusClean.Infrastructure.Tests/
    └── Strategies/HighVolumeAsyncCleanupTests.cs # Assert incremental intra-folder progress updates before item finishes
```

**Structure Decision**: Minimal, surgical additions across `DomainContracts.cs`, `SimulateAndExecuteCleanupUseCases.cs`, `CleanupStrategies.cs`, and `NexusClean.UI` without altering any public CLI contracts or safety invariants.

## Complexity Tracking

*Zero Constitution violations — all 6 gates pass unconditionally.*
