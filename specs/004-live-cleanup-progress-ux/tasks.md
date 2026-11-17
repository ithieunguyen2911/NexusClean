---
description: "Dependency-ordered implementation tasks for Live Cleanup Progress Bar & Active Operation UX"
---

# Tasks: Live Cleanup Progress Bar & Active Operation UX

**Input**: Design documents from `/specs/004-live-cleanup-progress-ux/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/live-progress-ux-contract.md](./contracts/live-progress-ux-contract.md)

**Tests**: Update `tests/NexusClean.Infrastructure.Tests/Strategies/HighVolumeAsyncCleanupTests.cs` to assert multiple incremental intra-folder `CleanupProgress` updates before item completion (`SC-UX-001`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (distinct files, no unmet dependencies)
- **[Story]**: User story label (`[US1]`, `[US2]`)

---

## Phase 1: Foundational Telemetry Contracts

- [X] T001 Add `CleanupItemProgress` record, `TotalTargetBytes` field on `CleanupProgress`, and default `IProgress<CleanupItemProgress>?` overloads on `ICleanupStrategy` in `src/NexusClean.Domain/Interfaces/DomainContracts.cs` (`FR-UX-001`)

---

## Phase 2: User Story 1 - Live Intra-Folder Progress & Prominent Progress Bar (Priority: P1) 🎯 MVP

**Goal**: Stream real-time intra-folder progress (`~120 ms` / `200` files) from `SafeDeletionEngine` and display a prominent `7px` Windows 11 progress bar, `%`, live bytes/files, and **`Dừng`** button in the bottom action bar (`FR-UX-001`–`FR-UX-004`).

### Implementation & Tests

- [X] T002 [US1] Emit throttled (`120 ms` / `200` files) `CleanupItemProgress` updates inside `SafeDeletionEngine` and wire `ICleanupStrategy` overloads in `src/NexusClean.Infrastructure/Strategies/CleanupStrategies.cs`
- [X] T003 [US1] Aggregate `TotalTargetBytes` and live intra-folder deltas into `CleanupProgress` in `src/NexusClean.Application/UseCases/SimulateAndExecuteCleanupUseCases.cs`
- [X] T004 [P] [US1] Assert incremental intra-folder `CleanupProgress` emissions (`>= 3` updates) in `tests/NexusClean.Infrastructure.Tests/Strategies/HighVolumeAsyncCleanupTests.cs` (`SC-UX-001`)
- [X] T005 [P] [US1] Add custom `FluentProgressBarStyle` (`Height=7`, rounded track, indeterminate animation) in `src/NexusClean.UI/Themes/Controls.xaml` (`FR-UX-003`, `FR-UX-005`)
- [X] T006 [US1] Add `IsNotBusy`, `IsOperationIndeterminate`, `OperationProgressPercentLabel`, `ActiveOperationTitle`, `ActiveOperationDetail` in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs` and render the Active Operation Progress Panel in the bottom bar of `src/NexusClean.UI/Views/MainWindow.xaml` (`FR-UX-002`–`FR-UX-004`)

---

## Phase 3: Polish & End-to-End Verification

- [X] T007 Run `dotnet build NexusClean.sln -c Release` (`0 Warning(s)`), `dotnet test NexusClean.sln -c Release`, zero-emoji check, and relaunch `NexusClean.UI` per `specs/004-live-cleanup-progress-ux/quickstart.md` (`SC-UX-002`, `SC-UX-003`)
