# Quickstart & End-to-End Validation Guide: NexusClean

**Feature**: `001-intelligent-disk-cleanup`  
**Date**: 2026-11-15  
**Contracts Referenced**: [domain-and-cli-contracts.md](./contracts/domain-and-cli-contracts.md), [signature-rules.v1.schema.json](./contracts/signature-rules.v1.schema.json), [data-model.md](./data-model.md)

---

## 1. Prerequisites

- **OS**: Windows 10 (21H2+) or Windows 11 x64
- **SDK**: `.NET SDK 8.0+` (verified compatible with installed `.NET SDK 9.0.203` / `10.0.400` and `Microsoft.WindowsDesktop.App 8.0.30`)
- **Repository Root**: `D:\AI_Learning_Repository\NexusClean`

---

## 2. Build & Automated Sandbox Test Suite

All automated tests run strictly inside temporary sandbox folders (`%TEMP%\NexusClean.Tests\<guid>`) and never mutate the host machine's real `AppData` or `C:\` system folders.

```powershell
# 1. Restore and build all 4 Clean Architecture layers + CLI + WPF UI
dotnet build NexusClean.sln -c Release

# 2. Run Domain, Guard, Strategy, SemVer Pruner, and Schema Contract tests
dotnet test NexusClean.sln -c Release --logger "console;verbosity=normal"
```

**Expected Outcome**:
- Zero compiler warnings in `NexusClean.Domain` and `NexusClean.Application`.
- 100% pass rate across guard boundary tests (`RootSystemFolderGuard`, `BrowserProfilePreservationGuard`, `RunningProcessLockGuard`, `AdminElevationGuard`) and strategy simulation/execution tests.

---

## 3. End-to-End Validation Scenarios

### Scenario A: Validate Seed & Default Signature Rules (`US5` / `FR-014` / `FR-015`)
```powershell
dotnet run --project src/NexusClean.Cli -- rules validate --file docs/03-SEED-SIGNATURE-RULES.json --json
dotnet run --project src/NexusClean.Cli -- rules validate --file rules/default-signature-rules.json --json
```
- **Expected**: Exit code `0`; JSON output reports `"isValid": true` with `9` valid baseline rules in `docs/03-SEED-SIGNATURE-RULES.json` and expanded coverage in `rules/default-signature-rules.json`.

### Scenario B: Read-Only Drive `C:` Audit & 5-Tier Impact Report (`US1` / `SC-001`)
```powershell
dotnet run --project src/NexusClean.Cli -- scan --drive C: --json
```
- **Expected**: Completes in `< 15 seconds` on NVMe SSD; outputs structured `DriveAnalysisReport` with `SafeReclaimableBytes`, `RelocatableBytes`, `AdminReclaimableBytes`, and Vietnamese `ImpactAssessment` (`SummaryVi`, `SideEffectIfDeleted`, `PreservedAssets`) for every discovered hotspot. Zero files modified.

### Scenario C: Non-Destructive Dry-Run Simulation (`US2` / `SC-004`)
```powershell
dotnet run --project src/NexusClean.Cli -- simulate --drive C: --tier safe --json
```
- **Expected**: Exit code `0`; outputs `CleanupBatchReport` with `"isDryRun": true`, exact projected byte recovery, count of currently locked files to skip, and appends a simulation entry to `%LOCALAPPDATA%\NexusClean\logs\audit.jsonl`.

### Scenario D: Browser Profile Preservation & SemVer Pruning Sandbox Verification (`US2` / `SC-002` / `SC-003`)
Run the dedicated integration test harness:
```powershell
dotnet test tests/NexusClean.Infrastructure.Tests --filter "FullyQualifiedName~BrowserProfileAndVscodeSandboxTests"
```
- **Expected**:
  - Mock Chrome/Edge/Brave/Cốc Cốc `Cache\Cache_Data` and `Code Cache\js` are emptied (`0 bytes`), while `Login Data`, `Cookies`, `Bookmarks`, `Preferences`, `History`, and `Extensions` match pre-clean SHA-256 hashes 100%.
  - Mock `.vscode\extensions` removes `ms-python.python-2026.2.0-win32-x64` and stale `.014fc366-temp`, while keeping `ms-python.python-2026.4.0-win32-x64` intact.
  - Mock rule targeting `%LOCALAPPDATA%` directly is intercepted by `RootSystemFolderGuard` with `Severity = Block` and `RiskScore = 10`.

### Scenario E: Secondary Drive Relocation Plan (`US3` / `FR-012`)
```powershell
dotnet run --project src/NexusClean.Cli -- relocate-plan --id <zaloFindingId> --target-drive D: --json
```
- **Expected**: Verifies `D:` free space against `SourceBytes * 1.20` and prints step-by-step Vietnamese migration instructions without deleting `ZaloData\media`.

### Scenario F: Launch Native WPF Desktop Dashboard (`US1`–`US5` / `FR-017`)
```powershell
dotnet run --project src/NexusClean.UI
```
- **Expected**: Opens the NexusClean Desktop Dashboard displaying 4 summary cards, categorized findings table, Vietnamese Impact Inspector drawer, Dry-Run button, Relocation Advisor dialog, and Elevated Maintenance Queue.
