# Feature Specification: NexusClean — Intelligent & Safe Windows Disk Cleanup System

**Feature Branch**: `001-intelligent-disk-cleanup`

**Created**: 2026-11-15

**Status**: Approved for Planning

**Input**: User description: "Xây dựng phần mềm dọn dẹp ổ đĩa thông minh NexusClean dựa trên hồ sơ nghiên cứu thực tế và đặc tả kiến trúc tại docs/ (01-SESSION-CONTEXT-AND-CASE-STUDY.md, 02-OOD-CLEAN-ARCHITECTURE-SPEC.md, 03-SEED-SIGNATURE-RULES.json): giải quyết triệt để tình trạng ổ C: tự đầy lại do dữ liệu ẩn trong AppData/UserProfile/System, đảm bảo an toàn tuyệt đối cho thư mục gốc và 100% Profile trình duyệt, phân tích mức độ ảnh hưởng 5 cấp độ trước khi xóa, mô phỏng Dry-Run, khuyến nghị di chuyển dữ liệu sinh trưởng sang ổ phụ, xử lý khóa tiến trình & quyền quản trị viên, và cảnh báo tăng trưởng dung lượng chủ động."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deep Hidden-Storage Audit & 5-Tier Impact Transparency (Priority: P1)

A Windows PC user whose system drive (`C:`) is critically full (e.g., 98% used with less than 6 GB free despite `Desktop`, `Downloads`, and `Documents` containing under 5 GB) launches a scan on drive `C:`. Instead of listing opaque folder trees or risking dangerous manual deletion inside hidden system directories, the user receives a categorized breakdown of hidden space consumers across application data, developer caches, browser caches, obsolete app updaters, messaging media, virtual disks, and system restore storage—paired with a clear Vietnamese 5-tier Impact Assessment explaining exactly what happens if each item is cleaned and what personal data stays untouched.

**Why this priority**: Without accurate hidden-storage discovery and transparent impact explanations, users either fail to find the 100+ GB of hidden bloat or accidentally break installed software and lose saved logins. This is the core value proposition of NexusClean.

**Independent Test**: Can be independently tested by running a read-only drive scan against a populated test directory layout containing sample browser caches, developer caches, app updaters, messaging folders, and protected system folders, verifying that every finding displays accurate byte size, category, risk score (0–10), 5-tier classification, side effects if deleted, and preserved user assets without modifying a single file on disk.

**Acceptance Scenarios**:

1. **Given** a target Windows drive containing hidden storage consumers across user application data, hidden dot-folders, and root installer folders, **When** the user initiates a drive analysis, **Then** the system scans all registered signature locations concurrently, displays live scan progress, and produces a ranked report showing total reclaimable space grouped by impact tier and category.
2. **Given** a completed drive analysis report, **When** the user inspects any detected item, **Then** the system displays its Vietnamese summary, risk score (`0` to `10`), assigned impact tier (`Safe Cache Tier 1`, `Rebuild on Demand`, `Relocate to Secondary Drive`, `Requires Admin Action`, or `Protected — Never Delete`), side effects if cleaned, and the explicit list of user assets guaranteed to be preserved.
3. **Given** an in-progress drive analysis, **When** the user cancels the scan, **Then** the system halts directory traversal within 2 seconds without corrupting state or leaving partial temporary artifacts.

---

### User Story 2 - Zero-Data-Loss Guarded Dry-Run & Selective Cache Cleanup (Priority: P1)

After reviewing scan results, the user selects safe caches (`Safe Cache Tier 1` and `Rebuild on Demand`)—such as unpacked driver installers, temporary files, Electron auto-updater leftovers, package manager caches, duplicate older IDE extension versions, and web browser caches—runs a Dry-Run simulation to preview exact space recovery, and then executes cleanup with 100% preservation of browser logins, cookies, bookmarks, history, extensions, and latest IDE extension versions.

**Why this priority**: Reclaiming tens of gigabytes safely is the primary actionable outcome. Dry-run verification plus mandatory safety guards guarantee zero credential or application loss.

**Independent Test**: Can be independently tested in an isolated sandbox environment containing mock browser profiles (`Default`, `Profile 1` with both cache subfolders and credential files `Login Data`, `Cookies`, `Bookmarks`, `History`) and multi-version IDE extension folders (`publisher.ext-1.0.0`, `publisher.ext-1.2.0`, `.uuid-temp`), verifying that Dry-Run deletes nothing while Execution removes 100% of allowlisted caches and old duplicates while preserving 100% of profile credentials, settings, and latest extension versions.

**Acceptance Scenarios**:

1. **Given** one or more selected cleanup findings, **When** the user triggers **Dry-Run Simulation**, **Then** the system calculates and displays exact projected bytes to reclaim, file counts, and any currently locked files to be skipped, while making zero modifications to the filesystem.
2. **Given** browser profiles for Chrome, Edge, Brave, or Cốc Cốc containing both transient cache directories and sensitive profile files (`Login Data`, `Cookies`, `Bookmarks`, `Preferences`, `History`, `Extensions`), **When** selective browser cleanup executes, **Then** only allowlisted transient cache subdirectories are purged and 100% of user login sessions, bookmarks, cookies, history, and extensions remain byte-for-byte intact.
3. **Given** an IDE extensions directory containing multiple installed versions of the same extension alongside orphaned temporary dot-UUID folders, **When** duplicate version pruning executes, **Then** only older semantic versions and orphaned temporary folders are removed while the highest version of every extension and user configuration files remain intact.
4. **Given** any attempt (via user custom path or malformed rule) to target a protected root folder (`AppData\Local`, `AppData\Roaming`, user profile root, `Windows`, `Program Files`) or a critical credential file, **Then** the safety guard chain unconditionally blocks deletion, marks the item `Protected — Never Delete` with risk score `10`, and disables destructive action.
5. **Given** a completed cleanup execution batch, **When** the operation finishes, **Then** the system records an immutable audit log entry detailing timestamp, cleaned items, actual bytes reclaimed, skipped locked files, and any non-fatal system warnings.

---

### User Story 3 - Secondary Drive Relocation Advisor for Continuously Growing Data (Priority: P2)

When the scan detects large, continuously growing user media or workspace stores on drive `C:`—such as messaging media (`ZaloData\media`) or virtual memory pagefiles—that would otherwise refill drive `C:` days after deletion, the user is guided away from destructive deletion and provided clear, actionable Secondary Drive Relocation guidance (or safe assisted relocation where supported) toward a secondary volume (such as `D:`).

**Why this priority**: Real-world case study auditing proved that deleting messaging media both destroys user work history and fails to stop drive `C:` from refilling. Guiding relocation solves the root cause permanently.

**Independent Test**: Can be independently tested by scanning a fixture containing a populated messaging media folder when a secondary drive exists (and when no secondary drive exists), verifying that the item is classified as `Relocate to Secondary Drive` rather than selected for automatic deletion, displays step-by-step Vietnamese relocation instructions, and checks secondary drive free space before recommending a destination.

**Acceptance Scenarios**:

1. **Given** a detected continuously growing user data store (such as `ZaloData\media`), **When** scan results are presented, **Then** the item is excluded from default "One-Click Safe Delete" selections, classified under `Relocate to Secondary Drive`, and accompanied by clear Vietnamese instructions explaining how to relocate storage to a secondary volume without losing chat media.
2. **Given** a machine with a secondary drive (`D:` or another local fixed volume with sufficient free capacity), **When** the user opens the Relocation Advisor for a relocatable item, **Then** the system verifies available free space on the target drive (requiring at least source size plus a 20% safety buffer) and presents the exact target path and migration steps.
3. **Given** a machine with only a single partition (`C:`) and no secondary fixed drive, **When** a `Relocate to Secondary Drive` item is inspected, **Then** the system informs the user that no secondary drive was detected and offers safe archival/selective age-based review advice without enabling blind bulk deletion.

---

### User Story 4 - Process-Lock Detection & Isolated Administrator Maintenance (Priority: P2)

When space is held by locked or system-protected mechanisms that cause drives to silently refill—such as Volume Shadow Copies (`System Volume Information`), Linux virtual disks (`ext4.vhdx` locked by virtualization services), or files actively held open by running applications—the user sees which processes hold locks and can queue system-level maintenance tasks into a dedicated, user-confirmed Administrator elevation workflow without freezing the main application.

**Why this priority**: Shadow copies and uncompacted virtual disks accounted for ~18+ GB in the baseline case study, while locked files routinely hang naive cleanup utilities.

**Independent Test**: Can be independently tested by holding an open file handle on a test cache file during cleanup (verifying graceful skip and reporting of the locking process) and verifying in non-elevated mode that Administrator-only findings (`System Volume Information`, virtual disk relocation/compaction) are separated into an explicit elevated action queue with dry-run command preview.

**Acceptance Scenarios**:

1. **Given** temporary or cache files currently held open by a running application, **When** scan or cleanup runs, **Then** the system detects the active lock, identifies the locking process name where accessible, skips locked files cleanly without hanging or crashing, and reports the skipped byte total in the batch summary.
2. **Given** system-protected storage consumers (`System Volume Information` shadow copies or virtual machine disks locked by background services) while the app runs under standard user permissions, **When** the user views cleanup options, **Then** standard user-space cleanup remains immediately usable while Administrator-required items are grouped in a separate **Elevated Maintenance Queue** showing the exact system effect (e.g., capping Shadow Copy storage to 5 GB while preserving the most recent restore point).
3. **Given** user approval to execute queued Administrator maintenance, **When** elevation is granted, **Then** only the explicitly approved maintenance commands execute, and if the user declines the elevation prompt, the application returns cleanly with no crash and logs the cancelled status.

---

### User Story 5 - Extensible Signature Catalog & Proactive Disk Growth Monitoring (Priority: P3)

Power users and maintainers can extend NexusClean to recognize new applications or AI/developer caches by adding entries to an external signature catalog file without rebuilding the core software, and users can enable lightweight background growth monitoring on high-churn folders (`%TEMP%`, messaging media, auto-updaters, AI agent caches) to receive early alerts before drive `C:` drops into critical low-space territory again.

**Why this priority**: New Electron apps, IDEs, and AI agents appear constantly. Data-driven extensibility and proactive growth alerts prevent recurring "98% full" emergencies over the long term.

**Independent Test**: Can be independently tested by adding a new custom cache signature entry to the external rule catalog file (verifying immediate recognition on the next scan without code changes, and rejection of any invalid or unsafe rule targeting protected roots) and simulating rapid file growth inside a watched sandbox folder to verify alert generation.

**Acceptance Scenarios**:

1. **Given** a valid new application cache rule added to the signature catalog file, **When** the user reloads rules or runs a new scan, **Then** the new target is automatically scanned, classified, guarded, and included in impact reports without modifying program binaries.
2. **Given** a malformed signature catalog file or a custom rule attempting to target a protected system/profile path, **When** the catalog loads, **Then** the system rejects the invalid/unsafe rule with a clear diagnostic message while continuing to enforce all hardcoded safety guards.
3. **Given** proactive disk growth monitoring is enabled with configurable thresholds, **When** free space on drive `C:` falls below the warning threshold (default: 15 GB) or a watched hotspot folder grows by more than the configured delta (default: +2 GB within 24 hours), **Then** the system raises a non-intrusive notification identifying the specific folder driving the growth and its recommended action.

---

### Edge Cases

- **Active Browser or IDE During Cleanup**: When Chrome, Edge, or VS Code is actively running while the user cleans browser caches or cached extensions, locked cache files are skipped gracefully without corrupting open tabs or active workspaces, and the UI recommends closing the browser/IDE if maximum reclamation is desired.
- **Reparse Points, Junctions & Symbolic Links**: Directory sizing and cleanup strategies MUST NOT follow filesystem junctions, symbolic links, or OneDrive cloud-only placeholders into unintended target directories or trigger accidental hydration of cloud-only files.
- **Zero-Byte or Missing Signature Paths**: Most machines only have a subset of applications installed; missing target paths MUST be silently skipped in milliseconds without generating error noise.
- **Custom Browser Profile Names**: Users may have profiles beyond `Default` and `Profile 1` (e.g., `Profile 12`, `Guest Profile`, `System Profile`); profile matching MUST safely guard credential files in ALL subdirectories of `User Data`, even if a folder name is non-standard.
- **Pre-Release & Non-SemVer Extension Folders**: IDE extension folders may include pre-release tags (`1.2.0-preview.1`), target-platform suffixes (`ms-python.python-2026.4.0-win32-x64`), or `extensions.json` manifest references; duplicate pruning MUST parse platform-qualified versions accurately and never delete a version actively referenced as the current enabled version.
- **Critically Full Disk (< 100 MB Free)**: Drive analysis, dry-run simulation, and audit logging MUST function reliably even when drive `C:` has near-zero free space left, bounding audit log size via automatic log rotation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST scan registered storage signature locations across local Windows drives (defaulting to system drive `C:`) and compute accurate on-disk byte sizes without hydrating cloud-only placeholder files or traversing directory junctions/reparse loops.
- **FR-002**: System MUST support concurrent directory sizing with live progress reporting (current area scanned, items discovered, cumulative reclaimable bytes) and responsive user cancellation within 2 seconds.
- **FR-003**: System MUST classify every detected `StorageFinding` into exactly one of five impact tiers: `SAFE_CACHE_TIER1` (Risk 0–1), `REBUILD_ON_DEMAND` (Risk 2–3), `RELOCATE_TO_SECONDARY_DRIVE` (Risk 4–6), `REQUIRES_ADMIN_ACTION` (Risk 5–7), or `PROTECTED_NEVER_DELETE` (Risk 10).
- **FR-004**: System MUST present a complete Vietnamese Impact Assessment for every finding prior to any cleanup action, explicitly displaying: summary (`SummaryVi`), side effects if deleted (`SideEffectIfDeleted`), list of preserved user assets (`PreservedAssets`), risk score, lock status, and administrator requirement.
- **FR-005**: System MUST enforce an unconditional root-directory safety guard blocking any deletion targeting `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`, or any drive root, regardless of external rule configuration.
- **FR-006**: System MUST enforce 100% browser profile credential and state preservation across Chromium-based browsers (`Chrome`, `Edge`, `Brave`, `Cốc Cốc`), guaranteeing that `Login Data`, `Login Data For Account`, `Cookies`, `Bookmarks`, `Preferences`, `Secure Preferences`, `Web Data`, `History`, `Extension Cookies`, and `Extensions` are never deleted or modified.
- **FR-007**: System MUST restrict browser cleanup exclusively to allowlisted transient cache subdirectories (`Cache\Cache_Data`, `Code Cache\js`, `Code Cache\wasm`, `GPUCache`, `Service Worker\CacheStorage`) within discovered browser profiles.
- **FR-008**: System MUST prune duplicate older IDE extension versions and orphaned temporary dot-UUID folders while preserving the highest installed semantic version (accounting for platform-specific suffixes and active manifest entries) and all user IDE settings.
- **FR-009**: System MUST provide a non-destructive **Dry-Run Simulation** mode for all cleanup selections that reports projected bytes reclaimed, affected item counts, and locked/skipped items without altering any file on disk.
- **FR-010**: System MUST pre-select only `SAFE_CACHE_TIER1` and `REBUILD_ON_DEMAND` items for standard cleanup workflows, and MUST never pre-select `RELOCATE_TO_SECONDARY_DRIVE`, `REQUIRES_ADMIN_ACTION`, or `PROTECTED_NEVER_DELETE` items for automatic deletion.
- **FR-011**: System MUST detect files or directories locked by active processes prior to or during cleanup, skip locked items without hanging or aborting the rest of the batch, and surface locking process names when available.
- **FR-012**: System MUST provide a Secondary Drive Relocation Advisor for continuously growing user/system stores (including `ZaloData\media`, WSL virtual disks, and virtual memory pagefile guidance), checking available capacity on secondary fixed drives before recommending target paths.
- **FR-013**: System MUST isolate operations requiring Administrator privileges (such as resizing Volume Shadow Copy storage via system utilities or relocating/managing WSL virtual disks) into a dedicated confirmation and elevation workflow separate from standard user-space cleanup.
- **FR-014**: System MUST load storage detection rules from an external, human-readable signature catalog initialized with at least all baseline signatures from the case study (driver installers, system temp, Chromium browser caches for Chrome/Edge/Brave/Cốc Cốc, VS Code cached VSIXs & duplicate extensions, Electron/Postman auto-updaters, developer package caches `uv`/`pip`/`npm`/`pnpm`/`NuGet`/`Playwright`/`.cache`, AI & productivity caches `Notion\Partitions`/`.codex`/`.gemini`, Zalo media, WSL2 virtual disks, VSS shadow storage, and `pagefile.sys`/`hiberfil.sys` diagnostics).
- **FR-015**: System MUST validate all external signature catalog files against strict schema and path-safety rules on startup, rejecting invalid or unsafe rules with actionable error messages while keeping valid rules operational.
- **FR-016**: System MUST write an immutable, size-bounded local audit log entry for every executed Simulation and Cleanup batch, recording timestamp, rule identifier, target path, action executed, bytes reclaimed, skipped locked items, and errors.
- **FR-017**: System MUST provide both an interactive Windows Desktop graphical interface and a command-line interface sharing the exact same underlying analysis, guard, simulation, and cleanup engine.
- **FR-018**: System MUST offer an optional Disk Growth Watcher capable of monitoring configured high-churn directories and drive free-space thresholds and emitting proactive growth alerts identifying the responsible directory.

### Key Entities

- **StorageFinding**: A discovered disk space consumer bound to a specific detection rule, holding its resolved filesystem path, category, calculated byte size, last-modified timestamp, and computed `ImpactAssessment`.
- **ImpactAssessment**: The safety and user-impact profile of a `StorageFinding`, containing its `ImpactLevel` (5 tiers), `RiskScore` (0–10), Vietnamese explanation (`SummaryVi`), deletion consequence (`SideEffectIfDeleted`), recommended action (`RecommendedAction`), guaranteed preserved assets (`PreservedAssets`), process-lock state, and administrator privilege flag.
- **SignatureRule**: A declarative catalog entry describing how to locate a specific application or system storage pattern, its cleanup action type, default impact metadata, optional profile/subfolder filters, and locking process hints.
- **GuardVerdict**: The pass/warn/block decision produced by each safety guard (`RootSystemFolderGuard`, `BrowserProfilePreservationGuard`, `RunningProcessLockGuard`, `AdminElevationGuard`) before any strategy is allowed to mutate disk state.
- **CleanupBatchReport**: The auditable outcome of a Dry-Run Simulation or Cleanup Execution batch, aggregating per-finding results, total bytes reclaimed (or projected), skipped locked items, duration, and any warnings.
- **DiskGrowthAlert**: A proactive notification event emitted when free drive capacity drops below threshold or a watched hotspot folder exceeds its configured growth rate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can complete a full signature-based hidden-storage scan of system drive `C:` and view categorized reclaimable space with Vietnamese impact explanations in under 15 seconds on a standard NVMe/SSD Windows PC.
- **SC-002**: 100% of browser login sessions, cookies, bookmarks, history, preferences, and installed extensions across Chrome, Edge, Brave, and Cốc Cốc remain intact after executing browser cache cleanup across all test profiles.
- **SC-003**: 100% of destructive attempts targeting protected system roots (`AppData\Local`, `AppData\Roaming`, user profile root, `Windows`, `Program Files`) or protected credential filenames are blocked with zero files deleted.
- **SC-004**: Dry-Run Simulation estimates match actual unlocked cleanup byte reclamation within 99% accuracy while performing zero filesystem writes or deletions inside scanned directories.
- **SC-005**: Cleanup execution encountering actively locked files completes 100% of unlocked selections without application freezes, crashes, or unhandled errors, reporting every skipped locked item clearly to the user.
- **SC-006**: Adding a new application cache signature via the external rule catalog enables detection, impact display, and guarded cleanup on the next scan with zero source-code edits or recompilation.
- **SC-007**: Non-technical users can distinguish between items that are "100% Safe to Delete", "Safe Cache (Rebuilds on Demand)", "Relocate to Drive D: Instead of Deleting", and "Requires Administrator Action" directly from the primary scan screen without consulting external documentation.

## Assumptions

- **Target Operating System**: NexusClean targets Windows 10 (21H2+) and Windows 11 x64 desktop/laptop environments formatted with NTFS, matching the real-world `C:` full-disk case study.
- **Execution Privileges**: The application launches by default under standard desktop user permissions so everyday scanning and user-profile cache cleanup work without UAC friction; UAC elevation is requested on demand solely for queued system-level maintenance tasks.
- **Deletion Permanence for Transient Caches**: Transient cache purges (`SAFE_CACHE_TIER1` and `REBUILD_ON_DEMAND`) perform permanent deletion (`Delete`) rather than moving gigabytes of cache files into `C:\$Recycle.Bin` (which would fail to free space on an already 98%-full `C:` drive!).
- **Relocation Scope in v1**: For third-party apps with proprietary internal settings (such as Zalo PC), v1 prioritizes safe pre-flight capacity checks + guided step-by-step relocation instructions (and native OS commands like `wsl --manage --move` where officially supported by the vendor/OS) rather than undocumented registry/symlink hacks that could break app updates.
- **Language Localization**: Primary user-facing impact explanations and UI labels prioritize clear Vietnamese (`vi-VN`) with clean technical terminology (`Profile`, `Cache`, `Dry-Run`, `Shadow Copy`, `WSL2`) matching the user's case study documentation.
