# Phase 0 Research & Architectural Decisions: NexusClean

**Feature**: `001-intelligent-disk-cleanup`  
**Date**: 2026-11-15  
**Sources Analyzed**: `docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md`, `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`, `docs/03-SEED-SIGNATURE-RULES.json`

---

## R-01: Target .NET Runtime & Desktop UI Framework

- **Decision**: Target **`net8.0`** (LTS) for `NexusClean.Domain`, `NexusClean.Application`, `NexusClean.Infrastructure`, `NexusClean.Cli`, and all xUnit test projects; target **`net8.0-windows`** with native **WPF + MVVM (`CommunityToolkit.Mvvm`)** for `NexusClean.UI`.
- **Rationale**:
  1. Host environment inspection confirmed `Microsoft.NETCore.App 8.0.30` and `Microsoft.WindowsDesktop.App 8.0.30` runtimes are installed alongside `.NET SDK 9.0.203` and `10.0.400`, directly matching the `.NET 8+` requirement in `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`.
  2. Native WPF has zero external runtime or WebView2 disk cache overhead (ironic and counterproductive in a disk cleanup tool), < 400 ms startup time, native Windows dark/light vector styling, and zero commercial license barriers.
  3. Because `NexusClean.Domain` and `NexusClean.Application` are UI-agnostic, future WinUI 3 or DevExpress skins can bind to the exact same Use Cases without touching core code.
- **Alternatives Considered**:
  - *Blazor Hybrid / Electron*: Rejected — creates its own `EBWebView` / Chromium cache footprint (~150–400 MB) inside `%LOCALAPPDATA%`.
  - *DevExpress WPF*: Rejected for open-source GitHub repo baseline due to proprietary commercial NuGet license requirements on contributor/CI machines.
  - *WinUI 3 (Windows App SDK)*: Viable alternative, but requires MSIX packaging or Windows App Runtime bootstrapper deployment; WPF runs single-folder/portable effortlessly.

---

## R-02: High-Speed, Non-Hydrating Concurrent Directory Sizing

- **Decision**: Implement `MultiThreadedDirectorySizer` using `System.IO.Enumeration.FileSystemEnumerable<long>` with `EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline }` combined with Windows cloud-placeholder attribute filtering (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000`, `FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000`) and a bounded work-stealing `Parallel.ForEachAsync` queue (`MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)`).
- **Rationale**:
  1. Standard `DirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)` can follow junction loops (such as legacy `AppData\Local\Application Data`) or accidentally touch OneDrive Files On-Demand placeholders.
  2. Skipping `ReparsePoint` and cloud recall attributes guarantees zero unintended file downloads and prevents recursive directory cycles.
  3. `FileSystemEnumerable<T>` operates directly over Win32 `FindFirstFileEx` / `NtQueryDirectoryFile` buffers with minimal GC allocations, scanning 100,000+ cached files on NVMe SSD in 2–6 seconds without requiring Administrator privileges (unlike raw NTFS `$MFT` volume reads).
- **Alternatives Considered**:
  - *Raw NTFS MFT DeviceIoControl (`FSCTL_ENUM_USN_DATA`)*: Rejected for default mode because opening `\\.\C:` requires Administrator UAC on every launch, violating the standard-user scan requirement. Can be added later as an optional elevated turbo mode.

---

## R-03: Process-Lock Detection Without UI Freezes

- **Decision**: Implement a two-stage lock detector in `WindowsProcessInspector`:
  1. **Fast Process Snapshot Check**: Match active `Process.GetProcesses()` names against rule-declared `lockingProcesses` (e.g., `vmmemWSL`, `wslservice`, `Zalo`, `chrome`, `msedge`, `Code`).
  2. **Win32 Restart Manager API (`rstrtmgr.dll`)**: P/Invoke `RmStartSession`, `RmRegisterResources` (batched up to 64 candidate files per session), `RmGetList`, and `RmEndSession` for granular lock inspection, paired with graceful `IOException` (`HResult` `0x80070020` / `0x80070021`) catch-and-skip during `ExecuteAsync`.
- **Rationale**:
  - Calling `RmStartSession` per individual file across 50,000 temp files is slow, whereas batching candidate files + handling `SHARING_VIOLATION` atomically during deletion guarantees both accurate process attribution and sub-second execution without TOCTOU (Time-of-Check to Time-of-Use) crashes if an app locks a file mid-clean.
- **Alternatives Considered**:
  - *`NtQuerySystemInformation(SystemExtendedHandleInformation)`*: Rejected — undocumented kernel structure layouts vary across Windows builds and risk deadlocking on named pipe handles unless guarded by helper threads.

---

## R-04: Reconciling `CleanupActionType` & `ISafetyGuard` Naming Across `docs/02` and `docs/03`

- **Decision**: Standardize `CleanupActionType` in `NexusClean.Domain.Enums` to unify all 7 action strings present in `docs/03-SEED-SIGNATURE-RULES.json` plus the blocked state from `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`:
  1. `DeleteContents` → `FullDirectoryDeleteStrategy` (deletes children inside target folder or recreates empty cache dir; never deletes protected parent).
  2. `DeleteUnlockedItems` → `DeleteUnlockedItemsStrategy` (iterates files/subfolders, skips locked handles gracefully, logs skipped count).
  3. `SelectiveSubfolderPurge` → `SelectiveSubfolderPurgeStrategy` (iterates matched profile folders and purges ONLY `allowedCacheSubfolders`).
  4. `SemVerDuplicatePrune` → `SemVerDuplicatePruneStrategy` (prunes older duplicate extension versions & `.uuid` orphan temp dirs while keeping highest version + manifest-active version).
  5. `RecommendRelocateToSecondaryDrive` → `SecondaryDriveRelocateStrategy` (inspects secondary fixed drives, verifies capacity >= 1.2x source size, produces guided Vietnamese relocation plan; never deletes).
  6. `RelocateOrUnregisterAdmin` → `ElevatedAdminScriptStrategy` (queues parameterized `wsl --manage <Distro> --move <Target>` workflow).
  7. `ResizeShadowStorageAdmin` → `ElevatedAdminScriptStrategy` (queues parameterized `vssadmin resize shadowstorage /for=C: /on=C: /maxsize=5GB` workflow).
  8. `BlockedNoOp` → `BlockedNoOpStrategy` (enforced automatically whenever any `ISafetyGuard` returns `Block`).
- **Guard Chain Standardization**: Canonicalize the 4 Domain/Application guards in execution order:
  `RootSystemFolderGuard` → `BrowserProfilePreservationGuard` → `RunningProcessLockGuard` → `AdminElevationGuard`.
- **Rationale**: Eliminates the naming drift found during analysis of `docs/01`, `docs/02`, and `docs/03` while maintaining 100% compatibility with `docs/03-SEED-SIGNATURE-RULES.json`.

---

## R-05: Composite Signature Rule Handling & Expanded Case-Study Coverage

- **Decision**:
  1. Design `SignatureRuleDefinition` and `signature-rules.v1.schema.json` to natively support all three rule shapes in `docs/03-SEED-SIGNATURE-RULES.json`:
     - **Path-list rules** (`targetPaths`)
     - **Profile-scoped rules** (`basePaths` + `profileFolderRegex` + `allowedCacheSubfolders`)
     - **Composite IDE rules** (`targetPaths` for direct VSIX/Crashpad cache folders + `extensionRoot` with `pruneOlderDuplicateVersions` & `pruneDotUuidTempFolders`)
  2. Preserve `docs/03-SEED-SIGNATURE-RULES.json` untouched as the reference baseline and ship an enriched `rules/default-signature-rules.json` (schema-compatible `v1.1.0`) that adds the missing hotspots documented in `docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md`:
     - Brave (`%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data`) and Cốc Cốc (`%LOCALAPPDATA%\CocCoc\Browser\User Data`) in `browser.chromium.selective_cache` (fulfilling Golden Rule #2).
     - `app.notion.offline_cache` (`%APPDATA%\Notion\Partitions` — selective cache subfolders only, preserving Notion session DB).
     - `dev.ai_agents.temp_logs` (`%LOCALAPPDATA%\Codex\Cache`, `%USERPROFILE%\.codex\log`, `%USERPROFILE%\.gemini\tmp` — safe logs/temp only, never touching skills/configs/history).
     - `dev.dot_caches.userprofile` (`%USERPROFILE%\.cache`, `%USERPROFILE%\.nuget\packages` advisory).
     - `system.memory.pagefile_hiberfil` (`C:\pagefile.sys`, `C:\hiberfil.sys` — read-only diagnostic & relocation/hibernation sizing advisor under `REQUIRES_ADMIN_ACTION` / `RELOCATE_TO_SECONDARY_DRIVE`).
- **Rationale**: Directly closes the 42+ GB coverage gap identified between the narrative findings in `docs/01` and the 9 seed rules in `docs/03`.

---

## R-06: VS Code Duplicate Extension Pruning Algorithm (`SemVerDuplicatePruneStrategy`)

- **Decision**: Parse `.vscode\extensions\extensions.json` first (if present) to build an immutable allowlist of currently registered extension relative locations (`relativeLocation`), AND group subdirectories matching `^(?<id>[a-z0-9-]+\.[a-z0-9-]+)-(?<ver>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)(?:-(?<platform>win32-(?:x64|arm64)|universal))?$` (case-insensitive) by `(id, platform)`. Keep BOTH the highest SemVer version AND any folder referenced in `extensions.json`; prune only strictly older unreferenced versions plus stale `.([0-9a-f]{8}-[0-9a-f]{4}-...)` interrupted-install temp directories older than 24 hours.
- **Rationale**: Naive regex folder splitting breaks on platform-specific extensions like `ms-python.python-2026.4.0-win32-x64` or deletes an extension VS Code hasn't finished switching away from yet. Consulting `extensions.json` + age threshold on `.uuid` folders guarantees zero broken IDE extensions.

---

## R-07: Isolated Parameterized UAC Elevation (`ElevatedAdminScriptRunner`)

- **Decision**: Never execute arbitrary raw PowerShell/CMD text in elevated mode. Instead, serialize approved `AdminMaintenanceCommand` objects (containing strict enum `AdminOperationKind`: `ResizeVssShadowStorage` or `MoveWslDistribution`, validated drive letter `^[A-Z]:$`, bounded size `>= 2GB`, validated WSL distro name) to a temporary request file and launch `NexusClean.Cli.exe admin-exec --request <path> --output <reportPath>` with `ProcessStartInfo { Verb = "runas", UseShellExecute = true }`.
- **Rationale**: Prevents command injection, keeps the main GUI running safely in standard user context, handles UAC cancellation (`Win32Exception` error code `1223`) cleanly, and writes structured results back into the unified audit log.

---

## R-08: Bounded Local Audit Trail (`JsonlAuditLogRepository`)

- **Decision**: Persist every `CleanupBatchReport` (both `DryRun = true` and `DryRun = false`) as single-line UTF-8 JSON records in `%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`, rolling over at `5 MB` (keeping max 3 files = `15 MB` ceiling), and register `%LOCALAPPDATA%\NexusClean` as an excluded self-directory.
- **Rationale**: Works offline with zero database engine overhead (SQLite WAL files can fail to allocate on a disk with < 10 MB free), is human-inspectable, and supports instant history display in UI and CLI.
