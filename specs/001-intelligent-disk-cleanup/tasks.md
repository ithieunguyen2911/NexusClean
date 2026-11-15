---
description: "Dependency-ordered, user-story-driven implementation task list for NexusClean"
---

# Tasks: NexusClean — Intelligent & Safe Windows Disk Cleanup System

**Input**: Design documents from `/specs/001-intelligent-disk-cleanup/` and domain baselines in `/docs/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: MANDATORY per NexusClean Constitution Principle VI (`Sandbox-Isolated Automated Testing`). All tests MUST execute strictly inside ephemeral `%TEMP%\NexusClean.Tests\<guid>` sandboxes and NEVER mutate host `AppData` or `C:\` system directories.

**Organization**: Tasks are grouped by user story (`US1`–`US5`) so each slice can be built, verified, and demonstrated independently while maintaining strict 4-Layer Clean Architecture (`Domain` → `Application` → `Infrastructure` → `Cli` / `UI`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (distinct files, no unmet task dependencies)
- **[Story]**: User story traceability label (`[US1]` through `[US5]`) for Phases 3–7
- Every task specifies exact repository-relative file paths and verbatim data-model constraints

---

## Phase 1: Setup (Solution & Clean Architecture Project Scaffolding)

**Purpose**: Initialize the `.NET 8` solution, 4-layer project references, shared compiler rules, and test sandboxes.

- [ ] T001 Create `.NET 8` solution `NexusClean.sln` and shared build props `Directory.Build.props` (`Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true` for Domain/Application) at repository root `NexusClean.sln`
- [ ] T002 [P] Scaffold pure class library `src/NexusClean.Domain/NexusClean.Domain.csproj` (`TargetFramework=net8.0`, zero external `PackageReference` items per Constitution Principle I)
- [ ] T003 [P] Scaffold application layer `src/NexusClean.Application/NexusClean.Application.csproj` (`TargetFramework=net8.0`, referencing only `NexusClean.Domain` plus `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`)
- [ ] T004 [P] Scaffold infrastructure layer `src/NexusClean.Infrastructure/NexusClean.Infrastructure.csproj` (`TargetFramework=net8.0`, referencing `NexusClean.Application` and `NexusClean.Domain`)
- [ ] T005 [P] Scaffold CLI presentation host `src/NexusClean.Cli/NexusClean.Cli.csproj` (`TargetFramework=net8.0`, referencing `NexusClean.Application` and `NexusClean.Infrastructure`) and WPF desktop host `src/NexusClean.UI/NexusClean.UI.csproj` (`TargetFramework=net8.0-windows`, `UseWPF=true`, `CommunityToolkit.Mvvm`)
- [ ] T006 [P] Scaffold xUnit test suites `tests/NexusClean.Domain.Tests/NexusClean.Domain.Tests.csproj`, `tests/NexusClean.Application.Tests/NexusClean.Application.Tests.csproj`, and `tests/NexusClean.Infrastructure.Tests/NexusClean.Infrastructure.Tests.csproj` with shared disposable `TempSandboxFixture` in `tests/NexusClean.Infrastructure.Tests/Fixtures/TempSandboxFixture.cs`

---

## Phase 2: Foundational (Core Domain Model, Safety Guards & OS Infrastructure Primitives)

**Purpose**: Build pure Domain entities, enums, interfaces, immutable safety guards, and core JSON/audit infrastructure required by all user stories (`FR-003`, `FR-005`, `FR-006`, `FR-015`, `FR-016`).

**⚠️ CRITICAL**: Complete Phase 2 before starting User Story phases.

- [ ] T007 [P] Implement Domain enums `ImpactLevel` (`SAFE_CACHE_TIER1`..`PROTECTED_NEVER_DELETE`), `CleanupActionType` (all 8 canonical actions: `DeleteContents`, `DeleteUnlockedItems`, `SelectiveSubfolderPurge`, `SemVerDuplicatePrune`, `RecommendRelocateToSecondaryDrive`, `RelocateOrUnregisterAdmin`, `ResizeShadowStorageAdmin`, `BlockedNoOp`), and `GuardSeverity` (`Allow`, `Warn`, `RequireElevation`, `Block`) in `src/NexusClean.Domain/Enums/`
- [ ] T008 [P] Implement Domain value objects and entities `ImpactAssessment` (`0 <= RiskScore <= 10`, `RiskScore == 10` when `Level == PROTECTED_NEVER_DELETE`, `SummaryVi` min 10 chars, non-empty `SideEffectIfDeleted`, `PreservedAssets`), `GuardVerdict`, `RelocationPlan` (`RequiredBytesWithBuffer = ceil(SourceSizeInBytes * 1.20)`), and `StorageFinding` (16-char SHA-256 `Id`, canonicalized `TargetPath`, `SubPathsToClean` prefixed by `TargetPath + "\"`, `SizeInBytes >= 0`) in `src/NexusClean.Domain/Entities/`
- [ ] T009 [P] Define pure Domain interfaces `IDetectionRule`, `ISafetyGuard`, `ICleanupStrategy`, `IDirectorySizer`, `IProcessLockInspector`, `IScanContext`, `ISystemSnapshot`, and `IDiskGrowthWatcher` in `src/NexusClean.Domain/Interfaces/`
- [ ] T010 Implement `RootSystemFolderGuard` (hardcoded immutable blocklist for `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`, and drive roots `^[A-Za-z]:\\?$`) and `BrowserProfilePreservationGuard` (hardcoded immutable blocklist for `Login Data`, `Login Data For Account`, `Cookies`, `Bookmarks`, `Preferences`, `Secure Preferences`, `Web Data`, `History`, `Extension Cookies`, `Extensions` across Chrome/Edge/Brave/Cốc Cốc) in `src/NexusClean.Domain/Guards/`
- [ ] T011 [P] Implement `RunningProcessLockGuard` and `AdminElevationGuard` completing the 4-link `ISafetyGuard` Chain of Responsibility in `src/NexusClean.Domain/Guards/`
- [ ] T012 [P] Write exhaustive xUnit guard & invariant tests proving fail-closed `PROTECTED_NEVER_DELETE` (`RiskScore = 10`, `BlockedNoOp`) behavior for root paths, path traversal, and browser credential files in `tests/NexusClean.Domain.Tests/Guards/SafetyGuardChainTests.cs` (`SC-002`, `SC-003`)
- [ ] T013 [P] Implement Application DTOs `SignatureCatalogDocument`, `SignatureRuleDefinition` (`ruleId` pattern `^[a-z0-9_]+(\.[a-z0-9_]+){1,4}$`, `displayName` max 160 chars, `category` max 64 chars), `DriveAnalysisReport`, `CleanupBatchReport`, and `DiskGrowthAlert` in `src/NexusClean.Application/Models/`
- [ ] T014 Implement `JsonSignatureRuleProvider` validating `docs/03-SEED-SIGNATURE-RULES.json` and `rules/default-signature-rules.json` against `specs/001-intelligent-disk-cleanup/contracts/signature-rules.v1.schema.json` constraints in `src/NexusClean.Infrastructure/Persistence/JsonSignatureRuleProvider.cs` (`FR-015`)
- [ ] T015 [P] Implement bounded rolling audit logger `JsonlAuditLogRepository` (`%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`, 5 MB max per file, max 3 archives, self-excluded from scan) in `src/NexusClean.Infrastructure/Persistence/JsonlAuditLogRepository.cs` (`FR-016`)

**Checkpoint**: Domain purity, 4-guard safety chain, JSON rule loader, and bounded JSONL audit trail verified by unit tests.

---

## Phase 3: User Story 1 - Deep Hidden-Storage Audit & 5-Tier Impact Transparency (Priority: P1) 🎯 MVP

**Goal**: Scan hidden storage hotspots on `C:` concurrently in `< 15s` without hydrating cloud files or following junctions, and present categorized findings with full Vietnamese 5-tier Impact Assessments (`FR-001`–`FR-004`, `FR-014`, `FR-017`, `SC-001`, `SC-007`).

**Independent Test**: Run `nexusclean scan --drive <sandbox-or-C:> --json` or click **Quét Ổ Đĩa** in WPF UI; verify accurate byte totals, `< 2s` cancellation, zero file mutations, and complete Vietnamese `ImpactAssessment` details across all 5 tiers.

### Sandbox Tests for User Story 1

- [ ] T016 [P] [US1] Write sandboxed tests for `MultiThreadedDirectorySizer` verifying skipping of NTFS junctions/reparse loops, skipping of cloud-only `Offline`/`RecallOnDataAccess` attributes, and `< 2s` `CancellationToken` responsiveness in `tests/NexusClean.Infrastructure.Tests/FileSystem/MultiThreadedDirectorySizerTests.cs`
- [ ] T017 [P] [US1] Write unit tests for `AnalyzeDriveUseCase`, `EvaluateImpactUseCase`, and `DiskAnalysisOrchestrator` verifying 5-tier aggregation (`SafeReclaimableBytes`, `RelocatableBytes`, `AdminReclaimableBytes`) in `tests/NexusClean.Application.Tests/UseCases/AnalyzeDriveUseCaseTests.cs`

### Implementation for User Story 1

- [ ] T018 [P] [US1] Author expanded signature catalog `rules/default-signature-rules.json` preserving all 9 rules from `docs/03-SEED-SIGNATURE-RULES.json` and adding Brave, Cốc Cốc, `Notion\Partitions`, `.codex`/`.gemini` logs/tmp, `.cache`, and `pagefile.sys`/`hiberfil.sys` diagnostics (`FR-014`)
- [ ] T019 [P] [US1] Implement `MultiThreadedDirectorySizer` using `FileSystemEnumerable<long>` (`AttributesToSkip = ReparsePoint | Offline` + cloud recall filter + bounded `Parallel.ForEachAsync`) in `src/NexusClean.Infrastructure/FileSystem/MultiThreadedDirectorySizer.cs` (`FR-001`, `FR-002`)
- [ ] T020 [P] [US1] Implement `JsonSignatureDetectionRule`, `BrowserSelectiveCacheRule` (`^(Default|Profile \d+)$` + custom profile discovery restricted to `allowedCacheSubfolders`), and `DuplicateVersionExtensionRule` (parsing `.vscode\extensions\extensions.json` + platform-qualified SemVer + stale `.uuid` temp folders) in `src/NexusClean.Application/Rules/` (`FR-007`, `FR-008`)
- [ ] T021 [US1] Implement `EvaluateImpactUseCase`, `AnalyzeDriveUseCase`, and `DiskAnalysisOrchestrator.AnalyzeAsync` streaming `IProgress<ScanProgress>` in `src/NexusClean.Application/UseCases/` and `src/NexusClean.Application/Orchestration/DiskAnalysisOrchestrator.cs` (`FR-002`, `FR-003`, `FR-004`)
- [ ] T022 [P] [US1] Implement `nexusclean scan` CLI command (supporting `--drive`, `--rules`, `--json` output, and `Ctrl+C` cancellation) in `src/NexusClean.Cli/Commands/ScanCommand.cs` (`FR-017`)
- [ ] T023 [US1] Implement WPF Main Dashboard scan view, 4 summary capacity cards, categorized findings grid, and Vietnamese **Impact Inspector Drawer** (`MainDashboardViewModel.cs`, `ImpactInspectorViewModel.cs`, `MainWindow.xaml`) in `src/NexusClean.UI/` (`FR-004`, `FR-017`, `SC-007`)

**Checkpoint**: User Story 1 delivers a complete, fast, read-only Windows hidden-storage auditor with 5-tier Vietnamese Impact Analysis in both CLI and WPF UI.

---

## Phase 4: User Story 2 - Zero-Data-Loss Guarded Dry-Run & Selective Cache Cleanup (Priority: P1)

**Goal**: Enable non-destructive Dry-Run simulation (`SimulateCleanupUseCase`) and guarded cleanup execution (`ExecuteCleanupUseCase`) for `SAFE_CACHE_TIER1` and `REBUILD_ON_DEMAND` findings with 100% browser profile and latest-extension preservation (`FR-005`–`FR-010`, `FR-016`, `SC-002`–`SC-004`).

**Independent Test**: Populate sandbox with mock Chrome/Edge/Brave/Cốc Cốc profiles (`Login Data`, `Cookies`, `Cache\Cache_Data`) and multi-version `.vscode\extensions`, run Dry-Run (assert 0 deletions), then Execute (assert 100% cache/duplicate removal and 100% SHA-256 preservation of credentials and latest extensions).

### Sandbox Tests for User Story 2

- [ ] T024 [P] [US2] Write sandbox integration tests `BrowserProfileAndVscodeSandboxTests` verifying SHA-256 byte-for-byte preservation of all 10 protected browser credential/extension assets and latest VS Code extension versions in `tests/NexusClean.Infrastructure.Tests/Strategies/BrowserProfileAndVscodeSandboxTests.cs` (`SC-002`, `SC-003`, `SC-004`)

### Implementation for User Story 2

- [ ] T025 [P] [US2] Implement `BlockedNoOpStrategy`, `FullDirectoryDeleteStrategy`, and `DeleteUnlockedItemsStrategy` (permanent file deletion bypassing `$Recycle.Bin`, catching sharing violations cleanly) in `src/NexusClean.Infrastructure/Strategies/`
- [ ] T026 [P] [US2] Implement `SelectiveSubfolderPurgeStrategy` (double-checking `BrowserProfilePreservationGuard` before deleting each allowlisted cache subfolder) and `SemVerDuplicatePruneStrategy` in `src/NexusClean.Infrastructure/Strategies/` (`FR-006`, `FR-007`, `FR-008`)
- [ ] T027 [US2] Implement `SimulateCleanupUseCase` (`DryRun = true`, zero disk mutations) and `ExecuteCleanupUseCase` (`DryRun = false`, mandatory guard re-verification + `JsonlAuditLogRepository` recording) in `src/NexusClean.Application/UseCases/` (`FR-009`, `FR-010`, `FR-016`)
- [ ] T028 [P] [US2] Implement `nexusclean simulate` and `nexusclean clean` CLI commands (`--ids`, `--tier safe`, `--yes`, `--json`) in `src/NexusClean.Cli/Commands/CleanCommand.cs` (`FR-017`)
- [ ] T029 [US2] Wire two-step **Mô phỏng Dry-Run** → **Thực thi Dọn dẹp An toàn** workflow, tier-aware checkbox disabling (`IsEnabled = false` for `PROTECTED_NEVER_DELETE`, `RELOCATE_TO_SECONDARY_DRIVE`, `REQUIRES_ADMIN_ACTION`), and batch audit summary modal into `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs` and `src/NexusClean.UI/Views/MainWindow.xaml` (`FR-009`, `FR-010`)

**Checkpoint**: User Stories 1 & 2 complete a production-grade safe Windows cache cleaner with verified Dry-Run, Profile Guards, SemVer Extension Pruning, and Audit Logging.

---

## Phase 5: User Story 3 - Secondary Drive Relocation Advisor for Continuously Growing Data (Priority: P2)

**Goal**: Prevent blind deletion of growing user data (`ZaloData\media`, `pagefile.sys`) by evaluating secondary fixed drives (`D:`), enforcing a `+20%` capacity buffer check, and presenting step-by-step Vietnamese migration guidance (`FR-012`).

**Independent Test**: Run `nexusclean relocate-plan` and open the WPF Relocation Advisor on simulated multi-drive (`C:` + `D:`) and single-drive (`C:` only) volume snapshots; verify `+20%` free-space validation and non-destructive guidance.

### Sandbox Tests for User Story 3

- [ ] T030 [P] [US3] Write unit tests for `SecondaryDriveRelocateStrategy` and `PlanSecondaryRelocationUseCase` covering sufficient secondary space (`>= 1.2x`), insufficient space, and single-partition (`C:` only) fallback in `tests/NexusClean.Application.Tests/UseCases/PlanSecondaryRelocationUseCaseTests.cs`

### Implementation for User Story 3

- [ ] T031 [P] [US3] Implement `SecondaryDriveRelocateStrategy` and `IVolumeCapacityProvider` adapter inspecting fixed NTFS volumes (`DriveType.Fixed`, `IsReady`) in `src/NexusClean.Infrastructure/Strategies/SecondaryDriveRelocateStrategy.cs` (`FR-012`)
- [ ] T032 [US3] Implement `PlanSecondaryRelocationUseCase` generating `RelocationPlan` with Vietnamese step-by-step guides for Zalo PC, WSL2, and Windows Virtual Memory in `src/NexusClean.Application/UseCases/PlanSecondaryRelocationUseCase.cs`
- [ ] T033 [P] [US3] Implement `nexusclean relocate-plan` CLI command and WPF **Relocation Advisor Dialog** (`RelocationAdvisorViewModel.cs`, `RelocationAdvisorDialog.xaml`) in `src/NexusClean.Cli/Commands/RelocatePlanCommand.cs` and `src/NexusClean.UI/`

**Checkpoint**: Growing user media and system files are safely guided to secondary drives instead of destructively deleted.

---

## Phase 6: User Story 4 - Process-Lock Detection & Isolated Administrator Maintenance (Priority: P2)

**Goal**: Identify locking processes via Windows Restart Manager (`rstrtmgr.dll`) without freezing threads, and execute user-approved Administrator maintenance (`vssadmin` shadow resize >= 2 GB, `wsl --manage --move`) through an isolated parameterized UAC elevation boundary (`FR-011`, `FR-013`, `SC-005`).

**Independent Test**: Hold an active `FileStream(..., FileShare.None)` lock on a sandbox temp file during `nexusclean clean` (verify process name attribution + graceful skip without crash), and test `admin-exec` manifest validation + UAC cancellation (`Win32Exception 1223`) handling.

### Sandbox Tests for User Story 4

- [ ] T034 [P] [US4] Write sandbox integration tests holding live `FileShare.None` locks to verify `WindowsProcessInspector` Restart Manager attribution and zero-hang skip behavior in `tests/NexusClean.Infrastructure.Tests/OS/WindowsProcessInspectorTests.cs` (`SC-005`)
- [ ] T035 [P] [US4] Write contract & security validation tests for `AdminMaintenanceRequest` (rejecting `maxShadowSizeGb < 2`, invalid volume letters, or unapproved operations) in `tests/NexusClean.Infrastructure.Tests/OS/ElevatedAdminScriptRunnerTests.cs`

### Implementation for User Story 4

- [ ] T036 [P] [US4] Implement `WindowsProcessInspector` combining process-name snapshot matching and batched Win32 `rstrtmgr.dll` (`RmStartSession`, `RmRegisterResources`, `RmGetList`, `RmEndSession`) P/Invoke in `src/NexusClean.Infrastructure/OS/WindowsProcessInspector.cs` (`FR-011`)
- [ ] T037 [P] [US4] Implement `VssShadowStorageManager`, `ElevatedAdminScriptRunner` (`Verb = "runas"`), and `ElevatedAdminScriptStrategy` using strictly parameterized `ProcessStartInfo.ArgumentList` calls in `src/NexusClean.Infrastructure/OS/` and `src/NexusClean.Infrastructure/Strategies/ElevatedAdminScriptStrategy.cs` (`FR-013`)
- [ ] T038 [US4] Implement `nexusclean admin-exec` CLI worker command and WPF **Elevated Maintenance Queue** panel with command preview and UAC decline handling in `src/NexusClean.Cli/Commands/AdminExecCommand.cs` and `src/NexusClean.UI/`

**Checkpoint**: Active process locks and system-protected storage (`VSS`, `WSL2`) are handled safely without elevating the main desktop app.

---

## Phase 7: User Story 5 - Extensible Signature Catalog & Proactive Disk Growth Monitoring (Priority: P3)

**Goal**: Enable zero-recompile rule catalog extension with CLI/UI validation (`nexusclean rules validate`) and background hotspot monitoring (`IDiskGrowthWatcher`) raising `DiskGrowthAlert` notifications (`FR-014`, `FR-015`, `FR-018`, `SC-006`).

**Independent Test**: Add a custom app cache rule to a temporary JSON catalog, validate via `nexusclean rules validate`, scan without recompiling (`SC-006`), verify malformed/root-targeting JSON rules are rejected, and trigger a simulated growth delta inside a sandbox watched folder to assert `DiskGrowthAlert` emission.

### Sandbox Tests for User Story 5

- [ ] T039 [P] [US5] Write schema & hot-reload tests validating `docs/03-SEED-SIGNATURE-RULES.json`, `rules/default-signature-rules.json`, custom rule injection (`SC-006`), and rejection of malformed/unsafe rules in `tests/NexusClean.Infrastructure.Tests/Persistence/SignatureCatalogValidationTests.cs`
- [ ] T040 [P] [US5] Write observer tests for `DiskGrowthFileSystemWatcher` verifying threshold alert emission when simulated growth exceeds configured delta in `tests/NexusClean.Infrastructure.Tests/OS/DiskGrowthWatcherTests.cs`

### Implementation for User Story 5

- [ ] T041 [P] [US5] Implement `DiskGrowthFileSystemWatcher` (`IDiskGrowthWatcher`) polling drive free space and observing configured high-churn paths (`%TEMP%`, `ZaloData\media`, `*-updater`) with debounced delta calculation in `src/NexusClean.Infrastructure/OS/DiskGrowthFileSystemWatcher.cs` (`FR-018`)
- [ ] T042 [P] [US5] Implement `nexusclean rules validate` and `nexusclean watch` CLI commands in `src/NexusClean.Cli/Commands/RulesAndWatchCommands.cs` (`FR-015`, `FR-018`)
- [ ] T043 [US5] Add Rule Catalog reload/validation panel and Tray Growth Alert toggle in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs` and `src/NexusClean.UI/Views/MainWindow.xaml`

**Checkpoint**: All 5 User Stories (`P1`–`P3`) are independently functional, extensible, and covered by sandboxed automated tests.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Finalize Dependency Injection wiring, low-disk resilience, performance benchmark verification, and `quickstart.md` end-to-end validation.

- [ ] T044 [P] Centralize Clean Architecture Dependency Injection registration extensions (`AddNexusCleanApplication`, `AddNexusCleanInfrastructure`) shared by both `NexusClean.Cli` and `NexusClean.UI` in `src/NexusClean.Infrastructure/DependencyInjection.cs`
- [ ] T045 [P] Add near-zero free-disk (`< 100 MB` free) resilience test and audit log 5 MB rollover verification in `tests/NexusClean.Infrastructure.Tests/Persistence/JsonlAuditLogRepositoryTests.cs` (`FR-016`)
- [ ] T046 Execute all 6 end-to-end validation scenarios in `specs/001-intelligent-disk-cleanup/quickstart.md` and verify `< 15s` scan performance target (`SC-001`)

---

## Dependencies & Execution Order

### Phase Dependencies

1. **Phase 1 (Setup)**: Starts immediately; unblocks Phase 2.
2. **Phase 2 (Foundational)**: Depends on Phase 1; establishes Domain entities, 4-guard safety chain, JSON rule loader, and JSONL audit log. Blocks Phases 3–7.
3. **Phase 3 (`US1` - P1 Scan & 5-Tier Impact)**: Depends on Phase 2. Delivers standalone read-only MVP.
4. **Phase 4 (`US2` - P1 Dry-Run & Guarded Cleanup)**: Depends on Phase 2 and integrates seamlessly with `US1` findings.
5. **Phase 5 (`US3` - P2 Relocation Advisor)**: Depends on Phase 2 (`RelocationPlan`) and `US1` discovery; independent of `US4`/`US5`.
6. **Phase 6 (`US4` - P2 Lock Detection & Admin Queue)**: Depends on Phase 2; enhances `US1`/`US2` with `rstrtmgr.dll` attribution and isolated `admin-exec` worker.
7. **Phase 7 (`US5` - P3 Rule Validation & Growth Watcher)**: Depends on Phase 2; can run in parallel with `US3` and `US4`.
8. **Phase 8 (Polish)**: Runs after desired User Stories complete.

### Parallel Opportunities

- **Setup**: `T002`, `T003`, `T004`, `T005`, `T006` touch separate `.csproj` directories.
- **Foundational**: `T007`, `T008`, `T009`, `T013`, `T015` can be authored in parallel before wiring guards (`T010`–`T012`).
- **Within `US1`**: `T016`, `T017`, `T018`, `T019`, `T020` operate on independent layers/files in parallel.
- **Across Stories**: After Phase 4 (`US1` + `US2` core), `US3` (Relocation), `US4` (Restart Manager + UAC Queue), and `US5` (Growth Watcher) can proceed concurrently.

---

## Parallel Example: User Story 1

```bash
# Parallel Wave A (Tests + Rules + Sizer):
Task T016: "Write sandboxed tests for MultiThreadedDirectorySizer in tests/NexusClean.Infrastructure.Tests/FileSystem/MultiThreadedDirectorySizerTests.cs"
Task T018: "Author expanded signature catalog in rules/default-signature-rules.json"
Task T019: "Implement MultiThreadedDirectorySizer in src/NexusClean.Infrastructure/FileSystem/MultiThreadedDirectorySizer.cs"
Task T020: "Implement detection rules in src/NexusClean.Application/Rules/"

# Wave B (Use Case Orchestration):
Task T021: "Implement EvaluateImpactUseCase, AnalyzeDriveUseCase, and DiskAnalysisOrchestrator in src/NexusClean.Application/"

# Parallel Wave C (CLI + WPF Presentation):
Task T022: "Implement nexusclean scan CLI command in src/NexusClean.Cli/Commands/ScanCommand.cs"
Task T023: "Implement WPF Main Dashboard scan view in src/NexusClean.UI/"
```

---

## Implementation Strategy

### MVP First (`US1` + `US2` Core P1 Slice)
1. Complete **Phase 1 (Setup)** + **Phase 2 (Foundational Safety Guards)**.
2. Complete **Phase 3 (`US1`)** → Validate read-only drive `C:` audit and 5-tier Vietnamese Impact Inspector.
3. Complete **Phase 4 (`US2`)** → Validate Dry-Run simulation + guarded cleanup in sandbox, then safe Tier 1/2 cache cleanup.

### Incremental Delivery (`P2` → `P3`)
4. Add **Phase 5 (`US3`)** → Permanent relief for growing `ZaloData\media` via `D:` Relocation Advisor.
5. Add **Phase 6 (`US4`)** → Granular `rstrtmgr.dll` process lock attribution + isolated UAC `VSS`/`WSL2` maintenance queue.
6. Add **Phase 7 (`US5`)** + **Phase 8 (Polish)** → Custom rule hot-reload and proactive Disk Growth Watcher.
