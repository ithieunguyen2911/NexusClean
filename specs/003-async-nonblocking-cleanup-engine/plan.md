# Implementation Plan: Non-Blocking Async Cleanup Engine & High-File-Count Optimization

**Branch**: `003-async-nonblocking-cleanup-engine` | **Date**: 2026-11-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-async-nonblocking-cleanup-engine/spec.md`

## Summary

Eliminate the WPF `(Not Responding)` freeze when clicking **`Dọn dẹp`** or **`Xem trước (Dry-Run)`** on multi-gigabyte / 100,000+ file cache directories (`uv\cache`, `.cache`, `npm-cache`, `%TEMP%`, Chromium caches) while maintaining standard non-admin user execution:
1. **Guaranteed Background Thread Offloading (`Task.Run`)**: Offload all synchronous snapshot inspection, `rstrtmgr.dll` P/Invoke, and directory traversal in `SimulateCleanupUseCase`, `ExecuteCleanupUseCase`, `AnalyzeDriveUseCase`, and `ICleanupStrategy` implementations so UI commands yield back to the WPF `Dispatcher` in `< 5 ms`.
2. **Instant Confirmation Modal**: Remove the redundant pre-modal `CleanAsync(..., dryRun: true)` 29 GB filesystem walk from `MainDashboardViewModel.ExecuteCleanupAsync`, opening the confirmation modal immediately using scanned metrics and adding live `IProgress<CleanupProgress>` + `Dừng` cancellation during execution.
3. **Defender-Safe Win32 `DELETE` Probe & Exception-Free `DeleteFileW`**: Refactor `SafeDeletionEngine` to use single-pass `DirectoryInfo.EnumerateFiles`/`EnumerateDirectories` (`EnumerationOptions`), Win32 `CreateFileW(dwDesiredAccess: DELETE = 0x00010000)` (zero `FILE_READ_DATA` / `FILE_WRITE_DATA` Defender triggers), and `DeleteFileW` error-code classification (eliminating thousands of `.NET` `IOException` stack unwinds on locked `%TEMP%` files).

## Technical Context

**Language/Version**: C# 12 on `.NET 8.0` (`net8.0` core libraries + `net8.0-windows` WPF UI)

**Primary Dependencies**: Native `.NET 8` BCL, Win32 `kernel32.dll` (`CreateFileW`, `CloseHandle`, `DeleteFileW` inside `NexusClean.Infrastructure`), `CommunityToolkit.Mvvm 8.3.2`

**Storage**: Bounded rolling JSONL audit log (`%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`)

**Testing**: `xUnit` inside `%TEMP%\NexusClean.Tests\<guid>` sandboxes (31 existing tests + new high-file-count non-blocking & cancellation tests)

**Target Platform**: Windows 10 & Windows 11 x64 (Standard User token `asInvoker` — no UAC required for Tier 1/2 cleanup)

**Project Type**: 4-Layer Clean Architecture Desktop & CLI Application

**Performance Goals**: `< 16 ms` synchronous UI-thread call time; `< 50 ms` modal open latency; `>= 50,000 files/sec` Dry-Run & deletion throughput on SSD caches; `< 1 s` cancellation latency

**Constraints**: `NexusClean.Domain` MUST remain 100% pure C# (Win32 `kernel32.dll` P/Invoke strictly confined to `NexusClean.Infrastructure`); zero compromise on `RootSystemFolderGuard` or `BrowserProfilePreservationGuard`

**Scale/Scope**: Handles `30+ GB` / `300,000+` cache files across `%TEMP%`, package managers, and multi-profile Chromium browsers without freezing UI threads or requiring Admin rights

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Constitution Principle | Pre-Design Gate | Post-Design Gate | Verification Notes |
| :--- | :--- | :--- | :--- |
| **I. 4-Layer Clean Architecture & Domain Purity** | PASS | PASS | `CleanupProgress` in `Domain` is pure C#; `CreateFileW`/`DeleteFileW` P/Invoke lives strictly inside `NexusClean.Infrastructure`. |
| **II. SOLID & Canonical OOD Patterns** | PASS | PASS | Preserves `ICleanupStrategy` Command + Dry-Run contract and `ISafetyGuard` Chain of Responsibility. |
| **III. Zero-Data-Loss Safety Guards & Profile Preservation** | PASS | PASS | `VerifyDefenseInDepth` and `ImmutableProtectedBrowserAssets` checks still run on every strategy and file. |
| **IV. 5-Tier Impact Analysis & Relocation-Over-Deletion** | PASS | PASS | Unchanged 5-tier rules; clearer standard-user permission disclosure in modal. |
| **V. Process-Lock Awareness, Isolated Admin Flow & Auditability** | PASS | PASS | Standard cleanup stays non-elevated; locked/System files skip cleanly via Win32 `5`/`32`/`33`; partial/cancelled runs still append `audit.jsonl`. |
| **VI. Sandbox-Isolated Automated Testing** | PASS | PASS | High-file-count responsiveness & cancellation tests execute strictly inside `%TEMP%\NexusClean.Tests\<guid>`. |

## Project Structure

### Documentation (this feature)

```text
specs/003-async-nonblocking-cleanup-engine/
├── spec.md              # Root-cause diagnosis, permission answer & requirements
├── plan.md              # This implementation plan (/speckit-plan output)
├── research.md          # Phase 0 threading, Windows Defender & Win32 P/Invoke decisions
├── data-model.md        # Phase 1 CleanupProgress & UI state machine
├── quickstart.md        # Phase 1 benchmark & live desktop verification guide
├── contracts/
│   └── async-cleanup-responsiveness-contract.md  # Threading budget & Win32 error contract
└── tasks.md             # Phase 2 output (/speckit-tasks — generated next)
```

### Source Code (repository root)

```text
src/
├── NexusClean.Domain/
│   └── Interfaces/DomainContracts.cs            # Add pure CleanupProgress record
├── NexusClean.Application/
│   ├── Orchestration/DiskAnalysisOrchestrator.cs # Thread IProgress<CleanupProgress> through CleanAsync
│   └── UseCases/
│       ├── AnalyzeDriveUseCase.cs               # Offload initial CaptureSnapshot via Task.Run
│       └── SimulateAndExecuteCleanupUseCases.cs # Offload full loop via Task.Run + stream CleanupProgress
├── NexusClean.Infrastructure/
│   └── Strategies/CleanupStrategies.cs          # Single-pass DirectoryInfo EnumerationOptions + Win32 CreateFileW(DELETE)/DeleteFileW + Task.Run
└── NexusClean.UI/
    └── ViewModels/MainDashboardViewModel.cs     # Instant modal (< 50ms), unified Cancel token, live CleanupProgress bar & permission copy

tests/
└── NexusClean.Infrastructure.Tests/
    └── Strategies/HighVolumeAsyncCleanupTests.cs # New sandbox test verifying < 16ms async yield, 5,000-file speed & mid-flight cancellation
```

**Structure Decision**: Targeted enhancements across existing Clean Architecture files plus one new sandbox benchmark/responsiveness test file `HighVolumeAsyncCleanupTests.cs`, keeping `NexusClean.Domain` 100% pure C# and confining Win32 `kernel32.dll` calls to `NexusClean.Infrastructure`.

## Complexity Tracking

*Zero Constitution violations — all 6 gates pass unconditionally.*
