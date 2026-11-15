<!--
Sync Impact Report
- Version change: 0.0.0 (template) → 1.0.0
- Modified principles:
  - [PRINCIPLE_1_NAME] → I. 4-Layer Clean Architecture & Domain Purity (NON-NEGOTIABLE)
  - [PRINCIPLE_2_NAME] → II. SOLID & Canonical OOD Design Patterns (NON-NEGOTIABLE)
  - [PRINCIPLE_3_NAME] → III. Zero-Data-Loss Safety Guards & Profile Preservation (NON-NEGOTIABLE)
  - [PRINCIPLE_4_NAME] → IV. 5-Tier Impact Analysis & Relocation-Over-Deletion (NON-NEGOTIABLE)
  - [PRINCIPLE_5_NAME] → V. Process-Lock Awareness, Isolated Admin Flow & Auditability (NON-NEGOTIABLE)
- Added principles & sections:
  - VI. Sandbox-Isolated Automated Testing (NON-NEGOTIABLE)
  - Windows Platform & Technical Constraints
  - Development Workflow & Quality Gates
- Removed sections: None
- Follow-up TODOs: None
-->

# NexusClean Constitution

## Core Principles

### I. 4-Layer Clean Architecture & Domain Purity (NON-NEGOTIABLE)
The codebase MUST enforce strict 4-layer Clean Architecture separated into concentric projects: `NexusClean.Domain`, `NexusClean.Application`, `NexusClean.Infrastructure`, and presentation clients (`NexusClean.UI` and `NexusClean.Cli`).
- `NexusClean.Domain` MUST contain only pure business entities (`StorageFinding`, `ImpactAssessment`), value objects, enums (`ImpactLevel`, `CleanupActionType`), deterministic domain guards, and abstract interfaces (`IDetectionRule`, `ISafetyGuard`, `ICleanupStrategy`, `IDirectorySizer`, `IDiskGrowthWatcher`). It MUST NOT reference Windows P/Invoke (`rstrtmgr.dll`), `System.Management`, external JSON/UI NuGet packages, or any outer layer.
- `NexusClean.Application` MUST orchestrate use cases (`AnalyzeDriveUseCase`, `EvaluateImpactUseCase`, `SimulateCleanupUseCase`, `ExecuteCleanupUseCase`, `DiskAnalysisOrchestrator`) strictly via Domain interfaces injected through Dependency Injection.
- Presentation projects (`NexusClean.UI`, `NexusClean.Cli`) MUST contain zero filesystem deletion, directory sizing, or safety guard evaluation logic.

### II. SOLID & Canonical OOD Design Patterns (NON-NEGOTIABLE)
Every module and class MUST adhere to SOLID principles and implement the four canonical NexusClean patterns:
- **Strategy Pattern (`IDetectionRule`, `ICleanupStrategy`)**: Encapsulate distinct cleanup behaviors (`FullDirectoryDeleteStrategy`, `DeleteUnlockedItemsStrategy`, `SelectiveSubfolderPurgeStrategy`, `SemVerDuplicatePruneStrategy`, `SecondaryDriveRelocateStrategy`, `ElevatedAdminScriptStrategy`, `BlockedNoOpStrategy`) without conditional branching inside orchestrators.
- **Open/Closed Principle (OCP)**: Adding support for a new application cache or developer tool MUST be achievable by adding a validated JSON rule entry or registering a new `IDetectionRule` implementation without modifying `DiskAnalysisOrchestrator`.
- **Chain of Responsibility (`ISafetyGuard`)**: Evaluate safety invariants sequentially across composable guards (`RootSystemFolderGuard` → `BrowserProfilePreservationGuard` → `RunningProcessLockGuard` → `AdminElevationGuard`).
- **Command + Dry-Run Pattern (`SimulateAsync` / `ExecuteAsync`)**: Every mutating strategy MUST support non-destructive dry-run simulation returning exact projected byte recovery and skipped/locked item counts before execution is permitted.
- **Observer Pattern (`IDiskGrowthWatcher`)**: Monitor high-churn directories (`%TEMP%`, `ZaloData\media`, `*-updater`, AI agent caches) asynchronously and publish threshold alerts without coupling watchers to UI controls.

### III. Zero-Data-Loss Safety Guards & Profile Preservation (NON-NEGOTIABLE)
User credentials, application installations, and operating system integrity take absolute precedence over reclaiming disk space:
- **Root Folder Lock (`RootSystemFolderGuard`)**: Direct deletion of `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`, or any drive root (`C:\`) MUST be unconditionally blocked at the Domain layer, even if misconfigured in an external JSON rule file.
- **100% Browser Profile Preservation (`BrowserProfilePreservationGuard`)**: When cleaning Chromium-based browsers (`Chrome`, `Edge`, `Brave`, `Cốc Cốc`), `Login Data`, `Login Data For Account`, `Cookies`, `Bookmarks`, `Preferences`, `Secure Preferences`, `Web Data`, `History`, `Extension Cookies`, and `Extensions` MUST NEVER be touched. Purge operations MUST be restricted to explicitly allowlisted transient cache subdirectories (`Cache\Cache_Data`, `Code Cache\js`, `Code Cache\wasm`, `GPUCache`, `Service Worker\CacheStorage`).
- **Fail-Closed Verdict**: If any `ISafetyGuard` in the chain returns `Block` (or throws an unexpected evaluation error), the finding MUST transition to `PROTECTED_NEVER_DELETE` (`RiskScore = 10`) and bind to `BlockedNoOpStrategy`.

### IV. 5-Tier Impact Analysis & Relocation-Over-Deletion (NON-NEGOTIABLE)
NexusClean MUST NEVER operate as a "blind one-click wiper." Every `StorageFinding` surfaced to the user MUST include a populated `ImpactAssessment`:
- Classify findings strictly into the 5-tier matrix: `SAFE_CACHE_TIER1` (Risk 0–1), `REBUILD_ON_DEMAND` (Risk 2–3), `RELOCATE_TO_SECONDARY_DRIVE` (Risk 4–6), `REQUIRES_ADMIN_ACTION` (Risk 5–7), or `PROTECTED_NEVER_DELETE` (Risk 10).
- Clearly articulate in Vietnamese (`SummaryVi`, `SideEffectIfDeleted`, `PreservedAssets`, `RecommendedAction`) what happens if cleaned, what assets remain untouched, and whether cached data will regenerate.
- **Relocate Over Blind Delete**: Continuously growing user-generated or OS-managed stores (`ZaloData\media`, active WSL2 `ext4.vhdx`, `pagefile.sys`) MUST default to `RELOCATE_TO_SECONDARY_DRIVE` or guided administrative migration to a secondary volume (e.g., `D:\`) rather than destructive deletion.

### V. Process-Lock Awareness, Isolated Admin Flow & Auditability (NON-NEGOTIABLE)
- **Pre-Execution Lock Inspection**: Files or directories locked by running processes (e.g., `vmmemWSL`, `wslservice`, active browser/IDE handles, open `%TEMP%` files) MUST be identified prior to deletion via Windows Restart Manager / handle inspection so cleanup operations skip or queue items gracefully without hanging UI threads.
- **Explicit Administrator Boundary**: Standard scan and user-space cache cleanup MUST run as a standard non-elevated user. System-level operations (`C:\System Volume Information` VSS shadow storage resizing via `vssadmin`, WSL distribution relocation, hibernation/pagefile tuning) MUST be isolated into an explicit UAC-elevated execution batch requiring separate user confirmation.
- **Immutable Audit Logging**: Every `SimulateAsync` and `ExecuteAsync` run MUST produce a structured `CleanupBatchReport` persisted to a local JSONL audit log recording timestamp, rule ID, target path, action taken, bytes reclaimed, skipped locked paths, and any non-fatal OS errors.

### VI. Sandbox-Isolated Automated Testing (NON-NEGOTIABLE)
All `ISafetyGuard`, `IDetectionRule`, `ICleanupStrategy`, SemVer duplicate pruners, and JSON rule schema validators MUST have deterministic unit and integration tests using xUnit. Automated tests MUST operate exclusively against ephemeral sandbox directories (`Path.GetTempPath()/NexusClean.Tests/<guid>`) or virtualized filesystem abstractions and MUST NEVER execute cleanup strategies against real user profile or OS directories.

## Windows Platform & Technical Constraints

- **Target Runtime & Language**: C# 12+ on modern `.NET` (`net8.0` LTS compatible baseline; buildable on `.NET 8 / 9 / 10` SDKs with `RollForward` support for Windows x64).
- **Signature Rule Engine**: Data-driven JSON signature rules (`docs/03-SEED-SIGNATURE-RULES.json` and user-extensible rule packs) MUST be validated against a strict JSON Schema (`signature-rules.v1.schema.json`) at startup before any rule is registered.
- **Performance Targets**: Full signature-based scan and multi-threaded directory sizing across `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, and `C:\` hotspots MUST stream progress asynchronously (`IProgress<ScanProgress>` + `CancellationToken`) without blocking the UI thread for more than 16 ms.

## Development Workflow & Quality Gates

1. **Spec-Driven Lifecycle**: Every capability MUST progress through GitHub Spec Kit (`/speckit-constitution` → `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`).
2. **Safety Gate Verification**: No `ICleanupStrategy` may be merged or enabled without passing unit tests proving `RootSystemFolderGuard` and `BrowserProfilePreservationGuard` block destructive paths.
3. **Cross-Artifact Consistency**: `spec.md`, `plan.md`, `data-model.md`, `contracts/`, and `tasks.md` MUST maintain 100% requirement coverage with zero unresolved constitution conflicts.

## Governance

This Constitution is the supreme technical and safety authority of the **NexusClean** repository and supersedes any conflicting ad-hoc instructions or third-party templates.
- **Amendment Procedure**: Modifying or removing any Core Principle requires an explicit constitution amendment via `/speckit-constitution`, documented rationale, impact review across `specs/`, and a MAJOR semantic version bump. Adding new principles or expanding non-breaking governance rules increments MINOR; clarifications increment PATCH.
- **Compliance Review**: Every specification, implementation plan, task list, and pull request MUST include a Constitution Check verifying compliance with Principles I through VI.

**Version**: 1.0.0 | **Ratified**: 2026-11-15 | **Last Amended**: 2026-11-15
