# Implementation Plan: NexusClean — Intelligent & Safe Windows Disk Cleanup System

**Branch**: `001-intelligent-disk-cleanup` | **Date**: 2026-11-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-intelligent-disk-cleanup/spec.md` and domain architecture docs in `/docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md`, `/docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`, and `/docs/03-SEED-SIGNATURE-RULES.json`.

## Summary

**NexusClean** solves the recurring Windows "98% full `C:` drive" problem where over 140 GB of hidden cache, superseded extension versions, updater packages, messaging media, WSL virtual disks, and VSS shadow copies accumulate silently outside standard user folders. Built on **4-Layer Clean Architecture** (`Domain`, `Application`, `Infrastructure`, `UI`/`Cli`) and **SOLID** OOD principles in C# / `.NET 8`, NexusClean combines high-speed non-hydrating directory sizing, a data-driven JSON signature engine, a fail-closed 4-guard `ISafetyGuard` chain (guaranteeing 0% root folder deletion and 100% browser profile credential preservation), a 5-tier Vietnamese Impact Assessment matrix, mandatory Dry-Run simulation, a Secondary Drive Relocation Advisor (`D:`), and an isolated UAC-elevated maintenance runner with JSONL audit logging.

## Technical Context

**Language/Version**: C# 12 targeting `.NET 8.0` LTS (`net8.0` for core/CLI/tests; `net8.0-windows` for WPF UI; verified buildable & runnable with installed `.NET SDK 10.0.400` and `Microsoft.WindowsDesktop.App 8.0.30`)

**Primary Dependencies**:
- `NexusClean.Domain`: Zero external NuGet dependencies (pure C# BCL)
- `NexusClean.Application`: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`
- `NexusClean.Infrastructure`: `System.Text.Json` (built-in), Win32 `rstrtmgr.dll` P/Invoke (Windows Restart Manager for lock detection)
- `NexusClean.UI`: Native WPF (`UseWPF=true`), `CommunityToolkit.Mvvm`
- `NexusClean.Cli`: `System.CommandLine` (or lightweight built-in subcommand router) + `Microsoft.Extensions.Hosting`

**Storage**: Local JSON signature catalogs (`docs/03-SEED-SIGNATURE-RULES.json` + `rules/default-signature-rules.json`) and bounded rolling JSONL audit logs (`%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`, max 5 MB × 3 files)

**Testing**: `xUnit`, `FluentAssertions` (or xUnit assertions), and `NSubstitute` operating strictly inside isolated `%TEMP%\NexusClean.Tests\<guid>` sandbox directories

**Target Platform**: Windows 10 (21H2+) and Windows 11 x64 (NTFS volumes)

**Project Type**: Clean Architecture Desktop Application (`NexusClean.UI` WPF) + CLI (`NexusClean.Cli`) + Reusable Core Libraries (`Domain`, `Application`, `Infrastructure`)

**Performance Goals**:
- Full signature-based scan across `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, and `C:\` hotspots in `< 15 seconds` on NVMe SSD (`< 5 seconds` warm cache)
- UI frame responsiveness `< 16 ms` (60 FPS) during concurrent scans via `IProgress<ScanProgress>`
- Cancellation latency `< 2 seconds`

**Constraints**:
- Must run standard scan and user-space cleanup without Administrator UAC prompt
- Must NEVER hydrate OneDrive/cloud-only placeholder files (`RecallOnDataAccess`) or follow junction/symlink loops
- Must function reliably even when `C:` free space is `< 100 MB`
- Zero modification or loss of browser `Login Data`, `Cookies`, `Bookmarks`, `Preferences`, `History`, or `Extensions`

**Scale/Scope**: 15+ built-in multi-category signature rules covering 140+ GB of real-world Windows bloat patterns, 8 cleanup strategies, 4 composable safety guards, 5 core use cases, WPF dashboard + full-featured CLI.

## Constitution Check

*GATE: Evaluated before Phase 0 research and re-verified after Phase 1 design (`data-model.md`, `contracts/`, `quickstart.md`).*

| Principle | Pre-Design Gate | Post-Design Gate | Verification Evidence |
| :--- | :---: | :---: | :--- |
| **I. 4-Layer Clean Architecture & Domain Purity** | PASS | PASS | `Domain` has zero NuGet/OS/P-Invoke imports; `UI` and `Cli` call `Application` use cases via DI (`contracts/domain-and-cli-contracts.md`). |
| **II. SOLID & Canonical OOD Patterns** | PASS | PASS | Strategy (`IDetectionRule`, `ICleanupStrategy`), Chain of Responsibility (`ISafetyGuard`), Command+DryRun (`SimulateAsync`/`ExecuteAsync`), Observer (`IDiskGrowthWatcher`). |
| **III. Zero-Data-Loss Safety Guards & Profile Preservation** | PASS | PASS | Hardcoded immutable root & credential baselines inside `RootSystemFolderGuard` and `BrowserProfilePreservationGuard` cannot be weakened by external JSON edits (`data-model.md` §3.1). |
| **IV. 5-Tier Impact Analysis & Relocation-Over-Deletion** | PASS | PASS | All 5 `ImpactLevel` values enforced; `RELOCATE_TO_SECONDARY_DRIVE` and `REQUIRES_ADMIN_ACTION` cannot be checked for bulk delete (`contracts/domain-and-cli-contracts.md` §4). |
| **V. Process-Lock Awareness, Isolated Admin Flow & Auditability** | PASS | PASS | Batched `rstrtmgr.dll` + `IOException` catch-and-skip (`research.md` R-03); parameterized `admin-exec` IPC manifest (`contracts` §3); rolling JSONL audit log (`research.md` R-08). |
| **VI. Sandbox-Isolated Automated Testing** | PASS | PASS | 3 dedicated xUnit test projects operating strictly on ephemeral `%TEMP%\NexusClean.Tests\<guid>` sandboxes (`quickstart.md` §2). |

## Project Structure

### Documentation (this feature)

```text
specs/001-intelligent-disk-cleanup/
├── spec.md                                  # Feature specification (/speckit-specify)
├── plan.md                                  # This implementation plan (/speckit-plan)
├── research.md                              # Phase 0 architectural decisions (/speckit-plan)
├── data-model.md                            # Phase 1 domain entities & state machine (/speckit-plan)
├── quickstart.md                            # Phase 1 validation scenarios (/speckit-plan)
├── checklists/
│   ├── requirements.md                      # Spec quality validation checklist
│   └── safety-and-architecture.md           # Domain requirements quality checklist (/speckit-checklist)
├── contracts/
│   ├── signature-rules.v1.schema.json       # JSON Schema validating 03-SEED-SIGNATURE-RULES.json
│   └── domain-and-cli-contracts.md          # C# interfaces, CLI flags, UAC IPC & UI contracts
└── tasks.md                                 # Dependency-ordered execution tasks (/speckit-tasks)
```

### Source Code (repository root)

```text
NexusClean/
├── NexusClean.sln
├── docs/
│   ├── 01-SESSION-CONTEXT-AND-CASE-STUDY.md
│   ├── 02-OOD-CLEAN-ARCHITECTURE-SPEC.md
│   ├── 03-SEED-SIGNATURE-RULES.json
│   └── 04-DOCS-AUDIT-AND-GAP-ANALYSIS.md
├── rules/
│   └── default-signature-rules.json         # Expanded v1.1 rule catalog (Chrome/Edge/Brave/Cốc Cốc, Notion, AI caches, WSL, VSS)
├── src/
│   ├── NexusClean.Domain/
│   │   ├── Entities/
│   │   │   ├── StorageFinding.cs
│   │   │   ├── ImpactAssessment.cs
│   │   │   ├── GuardVerdict.cs
│   │   │   └── RelocationPlan.cs
│   │   ├── Enums/
│   │   │   ├── ImpactLevel.cs
│   │   │   ├── CleanupActionType.cs
│   │   │   └── GuardSeverity.cs
│   │   ├── Interfaces/
│   │   │   ├── IDetectionRule.cs
│   │   │   ├── ISafetyGuard.cs
│   │   │   ├── ICleanupStrategy.cs
│   │   │   ├── IDirectorySizer.cs
│   │   │   ├── IProcessLockInspector.cs
│   │   │   └── IDiskGrowthWatcher.cs
│   │   └── Guards/
│   │       ├── RootSystemFolderGuard.cs
│   │       ├── BrowserProfilePreservationGuard.cs
│   │       ├── RunningProcessLockGuard.cs
│   │       └── AdminElevationGuard.cs
│   ├── NexusClean.Application/
│   │   ├── Models/
│   │   │   ├── SignatureCatalogDocument.cs
│   │   │   ├── DriveAnalysisReport.cs
│   │   │   ├── CleanupBatchReport.cs
│   │   │   └── DiskGrowthAlert.cs
│   │   ├── Orchestration/
│   │   │   └── DiskAnalysisOrchestrator.cs
│   │   ├── UseCases/
│   │   │   ├── AnalyzeDriveUseCase.cs
│   │   │   ├── EvaluateImpactUseCase.cs
│   │   │   ├── SimulateCleanupUseCase.cs
│   │   │   ├── ExecuteCleanupUseCase.cs
│   │   │   └── PlanSecondaryRelocationUseCase.cs
│   │   └── Rules/
│   │       ├── JsonSignatureDetectionRule.cs
│   │       ├── BrowserSelectiveCacheRule.cs
│   │       └── DuplicateVersionExtensionRule.cs
│   ├── NexusClean.Infrastructure/
│   │   ├── FileSystem/
│   │   │   └── MultiThreadedDirectorySizer.cs
│   │   ├── OS/
│   │   │   ├── WindowsProcessInspector.cs
│   │   │   ├── ElevatedAdminScriptRunner.cs
│   │   │   ├── VssShadowStorageManager.cs
│   │   │   └── DiskGrowthFileSystemWatcher.cs
│   │   ├── Persistence/
│   │   │   ├── JsonSignatureRuleProvider.cs
│   │   │   └── JsonlAuditLogRepository.cs
│   │   └── Strategies/
│   │       ├── FullDirectoryDeleteStrategy.cs
│   │       ├── DeleteUnlockedItemsStrategy.cs
│   │       ├── SelectiveSubfolderPurgeStrategy.cs
│   │       ├── SemVerDuplicatePruneStrategy.cs
│   │       ├── SecondaryDriveRelocateStrategy.cs
│   │       ├── ElevatedAdminScriptStrategy.cs
│   │       └── BlockedNoOpStrategy.cs
│   ├── NexusClean.Cli/
│   │   └── Program.cs
│   └── NexusClean.UI/
│       ├── App.xaml
│       ├── ViewModels/
│       │   ├── MainDashboardViewModel.cs
│       │   ├── ImpactInspectorViewModel.cs
│       │   └── RelocationAdvisorViewModel.cs
│       └── Views/
│           └── MainWindow.xaml
└── tests/
    ├── NexusClean.Domain.Tests/
    ├── NexusClean.Application.Tests/
    └── NexusClean.Infrastructure.Tests/
```

**Structure Decision**: Adopted the exact 4-layer Clean Architecture `.NET 8` layout defined in `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md` §6, enriched with `NexusClean.Cli` (enabling headless verification, JSON automation, and isolated `admin-exec` UAC execution) and `NexusClean.Infrastructure.Tests` (for sandboxed filesystem, Restart Manager lock, and JSON schema contract tests).

## Complexity Tracking

*No Constitution violations exist. All 6 non-negotiable principles pass without exception.*
