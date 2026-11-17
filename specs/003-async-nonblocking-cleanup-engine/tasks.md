---
description: "Dependency-ordered implementation tasks for Non-Blocking Async Cleanup Engine & High-File-Count Optimization"
---

# Tasks: Non-Blocking Async Cleanup Engine & High-File-Count Optimization

**Input**: Design documents from `/specs/003-async-nonblocking-cleanup-engine/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/async-cleanup-responsiveness-contract.md](./contracts/async-cleanup-responsiveness-contract.md)

**Tests**: MANDATORY sandbox responsiveness, high-file-count throughput, and mid-flight cancellation test in `tests/NexusClean.Infrastructure.Tests/Strategies/HighVolumeAsyncCleanupTests.cs`.

**Organization**: Tasks are grouped by user story (`US1` and `US2`) while maintaining strict 4-Layer Clean Architecture (`Domain` → `Application` → `Infrastructure` → `UI`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (distinct files, no unmet dependencies)
- **[Story]**: User story label (`[US1]`, `[US2]`)
- Exact repository-relative file paths included in every task

---

## Phase 1: Setup & Foundational Progress Contract

**Purpose**: Add pure `CleanupProgress` value object in `NexusClean.Domain` and thread `IProgress<CleanupProgress>?` + `Task.Run` background isolation through `Application`.

- [X] T001 Add pure `CleanupProgress` record (`CurrentRuleId`, `CurrentDisplayName`, `CompletedItems`, `TotalItems`, `CumulativeBytesProcessed`, `CumulativeFilesProcessed`, `CumulativeSkippedLocked`, `IsDryRun`) in `src/NexusClean.Domain/Interfaces/DomainContracts.cs`
- [X] T002 Offload initial `LoadRules` + `CaptureSnapshot` onto `Task.Run` in `src/NexusClean.Application/UseCases/AnalyzeDriveUseCase.cs` (`FR-PERF-001`)
- [X] T003 Offload `SimulateCleanupUseCase.ExecuteAsync` and `ExecuteCleanupUseCase.ExecuteAsync` onto `Task.Run` with real-time `IProgress<CleanupProgress>?` reporting and wire through `DiskAnalysisOrchestrator.CleanAsync` in `src/NexusClean.Application/UseCases/SimulateAndExecuteCleanupUseCases.cs` and `src/NexusClean.Application/Orchestration/DiskAnalysisOrchestrator.cs` (`FR-PERF-001`, `FR-PERF-005`)

---

## Phase 2: User Story 2 - Defender-Friendly High-Throughput Enumeration & Deletion Engine (Priority: P1)

**Goal**: Eliminate the `FileAccess.ReadWrite` Windows Defender (`WdFilter.sys`) bottleneck and `.NET` `IOException` storm in `SafeDeletionEngine` using single-pass `DirectoryInfo` `EnumerationOptions` (`WIN32_FIND_DATA`), Win32 `CreateFileW(DELETE = 0x00010000)` lock probes, and Win32 `DeleteFileW` error-code classification wrapped in `Task.Run` (`FR-PERF-003`, `FR-PERF-004`).

**Independent Test**: Run `HighVolumeAsyncCleanupTests` over 5,000+ sandbox files with active `FileShare.None` locks; verify `< 15 ms` caller yield time, `< 1.5 s` total Dry-Run + Execute completion, and accurate `SkippedLockedPaths`.

### Sandbox Tests for User Story 2

- [X] T004 [P] [US2] Write sandbox responsiveness, 5,000-file throughput, and mid-flight cancellation tests in `tests/NexusClean.Infrastructure.Tests/Strategies/HighVolumeAsyncCleanupTests.cs` (`SC-PERF-001`, `SC-PERF-002`, `SC-PERF-003`)

### Implementation for User Story 2

- [X] T005 [US2] Upgrade `SafeDeletionEngine` and all mutating `ICleanupStrategy` classes to use `Task.Run`, single-pass `DirectoryInfo.EnumerateFiles`/`EnumerateDirectories` (`EnumerationOptions`), Win32 `CreateFileW(0x00010000)` `DELETE`-only lock probing, and exception-free `DeleteFileW` in `src/NexusClean.Infrastructure/Strategies/CleanupStrategies.cs` (`FR-PERF-001`, `FR-PERF-003`, `FR-PERF-004`)

**Checkpoint**: High-file-count Dry-Run and Cleanup execute entirely on background worker threads at native Win32 speed without triggering Defender stream scans.

---

## Phase 3: User Story 1 - Instant Confirmation Modal & Zero-Freeze Background Cleanup UI (Priority: P1) 🎯 MVP

**Goal**: Open the confirmation modal in `< 50 ms` on clicking `Dọn dẹp` (removing the redundant pre-modal 29 GB Dry-Run walk), stream live `CleanupProgress` to the top progress bar and status strip, support cancelling active Dry-Run/Cleanup via `Dừng`, and clearly state standard-user permission execution (`FR-PERF-002`, `FR-PERF-005`, `FR-PERF-006`).

**Independent Test**: Launch `NexusClean.UI`, scan `C:`, click `Dọn dẹp (29,20 GB)` and confirm the modal appears instantaneously (`< 50 ms`) with zero `(Not Responding)` freeze, live progress updates, and active `Dừng` button.

### Implementation for User Story 1

- [X] T006 [US1] Refactor `MainDashboardViewModel.SimulateDryRunAsync`, `ExecuteCleanupAsync`, and `CancelScan` (unified `_activeOperationCts` for Scan, Dry-Run, and Cleanup) to open `Modal.ShowConfirmAsync` immediately (`< 50 ms`), stream `IProgress<CleanupProgress>`, handle `OperationCanceledException`, and display clear standard-user permission messaging in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs` (`FR-PERF-002`, `FR-PERF-005`, `FR-PERF-006`)

---

## Phase 4: Polish & End-to-End Verification

**Purpose**: Verify full solution build (`0 Warning(s)`), all sandbox tests (`33/33 PASSED`), and relaunch `NexusClean.UI`.

- [X] T007 Execute `dotnet build NexusClean.sln -c Release`, `dotnet test NexusClean.sln -c Release`, and live `NexusClean.UI` smoke verification per `specs/003-async-nonblocking-cleanup-engine/quickstart.md` (`SC-PERF-004`)

---

## Dependencies & Execution Order

1. **Phase 1 (`T001`–`T003`)**: Domain `CleanupProgress` + Application `Task.Run` & progress plumbing.
2. **Phase 2 (`T004`–`T005`)**: Sandbox test `HighVolumeAsyncCleanupTests` + Infrastructure `SafeDeletionEngine` Win32 `CreateFileW(DELETE)` / `DeleteFileW` & `Task.Run`.
3. **Phase 3 (`T006`)**: `MainDashboardViewModel` instant confirmation modal & live cancelable cleanup progress.
4. **Phase 4 (`T007`)**: Build, test suite verification, commit, push, and relaunch `NexusClean.UI`.
