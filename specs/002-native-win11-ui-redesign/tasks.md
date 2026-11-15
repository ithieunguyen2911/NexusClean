---
description: "Dependency-ordered implementation tasks for Native Windows 11 Human-Crafted UI/UX Redesign"
---

# Tasks: Native Windows 11 Human-Crafted UI/UX Redesign

**Input**: Design documents from `/specs/002-native-win11-ui-redesign/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/win11-fluent-ui-ux-contract.md](./contracts/win11-fluent-ui-ux-contract.md)

**Tests**: Automated build & existing 31 sandbox tests plus automated zero-emoji / XAML integrity verification (`SC-UI-001`–`SC-UI-004`).

**Organization**: Tasks are grouped by user story (`US1`–`US3`) so each slice is independently verifiable while preserving 100% of `Domain`, `Application`, and `Infrastructure` Clean Architecture boundaries.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`)
- Exact repository-relative file paths included in every task

---

## Phase 1: Setup (Theme & Icon Resource Architecture)

**Purpose**: Establish modular `Themes/` directory and monochrome vector iconography inside `src/NexusClean.UI/`.

- [X] T001 [P] Create monochrome `PathGeometry` icon dictionary (`IconDrive`, `IconScan`, `IconStop`, `IconSearch`, `IconShield`, `IconBroom`, `IconRelocate`, `IconAdminLock`, `IconFolderOpen`, `IconCopy`, `IconTheme`, `IconAuditLog`, `IconRulesCheck`, `IconBell`, `IconCheck`, `IconClose`) with zero Unicode emojis in `src/NexusClean.UI/Themes/Icons.xaml`
- [X] T002 [P] Create Windows 11 Fluent Light token dictionary (`WindowBgBrush=#F3F3F3`, `SidebarBgBrush=#EBEBEB`, `CardBgBrush=#FFFFFF`, `AccentPrimaryBrush=#0067C0`, semantic pill bg/fg brushes) in `src/NexusClean.UI/Themes/Theme.Light.xaml`
- [X] T003 [P] Create Neutral Graphite Dark token dictionary (`WindowBgBrush=#18181B`, `SidebarBgBrush=#141417`, `CardBgBrush=#202024`, `AccentPrimaryBrush=#3B82F6`, semantic pill bg/fg brushes) in `src/NexusClean.UI/Themes/Theme.Dark.xaml`

---

## Phase 2: Foundational (Cohesive Native Control Templates & Presentation Models)

**Purpose**: Eliminate unstyled legacy Win32 controls and define reusable Presentation state models before rebuilding the main shell.

- [X] T004 Create custom theme-aware WPF styles and `ControlTemplate`s (`PrimaryButtonStyle`, `SecondaryButtonStyle`, `SubtleIconButtonStyle`, `NavRadioButtonStyle`, `FluentComboBoxStyle`, `FluentCheckBoxStyle`, `FluentTextBoxStyle`, `FluentDataGridStyle`, `ThinScrollBarStyle`) in `src/NexusClean.UI/Themes/Controls.xaml`
- [X] T005 [P] Implement Presentation enums and helper ViewModels (`AppThemeMode`, `FindingFilterCategory`, `BadgeTone`, `StorageBarBreakdownViewModel` with `GridLength(..., GridUnitType.Star)`, `ConfirmationModalViewModel`) in `src/NexusClean.UI/ViewModels/UiModels.cs`
- [X] T006 Wire `MergedDictionaries` bootstrap (`Theme.Light.xaml` default + `Icons.xaml` + `Controls.xaml`) and runtime `ApplyTheme(AppThemeMode)` switcher in `src/NexusClean.UI/App.xaml` and `src/NexusClean.UI/App.xaml.cs`

**Checkpoint**: Theme switching, vector icons, and cohesive native-style WPF controls ready.

---

## Phase 3: User Story 1 - Authentic Windows 11 Visual Language & Segmented Storage Bar (Priority: P1) 🎯 MVP

**Goal**: Deliver calm Windows 11 Light/Dark surfaces, zero emojis, natural Vietnamese microcopy, and a unified 5-segment proportional Drive Storage Bar (`FR-UI-001`–`FR-UI-004`, `FR-UI-006`).

**Independent Test**: Launch `NexusClean.UI`, toggle Light/Dark theme, scan `C:`, and verify the 5-segment storage breakdown bar, human-formatted seconds (`9,8 giây`), and zero white Win32 control bleed.

### Implementation for User Story 1

- [X] T007 [US1] Refactor `FindingItemViewModel` and `MainDashboardViewModel` to compute `StorageBarBreakdownViewModel` star ratios, `RelativeSizePercent` (`0..100`), compact `ShortBadgeText` (`Xóa an toàn`, `Tự tạo lại`, `Nên dời ổ`, `Quyền Admin`, `Được bảo vệ`), `BadgeTone`, `ToggleThemeCommand`, and natural Vietnamese status messages in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs`
- [X] T008 [US1] Build the Windows 11 Drive Header & 5-Segment Proportional Storage Bar (`OtherUsed`, `SafeReclaimable`, `Relocatable`, `AdminQueue`, `FreeSpace`) with interactive legend chips and theme toggle in `src/NexusClean.UI/Views/MainWindow.xaml`

**Checkpoint**: User Story 1 eliminates the AI neon/emoji aesthetic and delivers a native Windows 11 storage overview.

---

## Phase 4: User Story 2 - Scannable Category Navigation, Search & Two-Line Findings Table (Priority: P1)

**Goal**: Provide a `216px` Left Navigation Rail with live category counts, `< 16 ms` keyword search (`ICollectionView`), spacious two-line table rows with mini WizTree proportional bars, and a `GridSplitter` workspace (`FR-UI-005`).

**Independent Test**: Filter across `Tất cả mục`, `Dọn an toàn`, `Dời sang ổ phụ`, `Bảo trì hệ thống`, type `"chrome"` in the search input, verify `< 16 ms` instant filtering with preserved checkbox state, and drag the `GridSplitter`.

### Implementation for User Story 2

- [X] T009 [US2] Add `ICollectionView` instant filtering (`SelectedFilterCategory`, `SearchQuery`, category item count properties `AllCount`, `SafeCount`, `RelocateCount`, `AdminCount`) in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs`
- [X] T010 [US2] Implement Left Navigation Rail (`216px`), Search & Selection Command Strip, spacious `48px` two-line `DataGrid` rows (title + muted path, tabular size + mini bar, semantic pill badge), empty state view, and `GridSplitter` in `src/NexusClean.UI/Views/MainWindow.xaml`

**Checkpoint**: User Stories 1 & 2 provide effortless browsing, filtering, and inspection of hidden storage findings.

---

## Phase 5: User Story 3 - Refined Inspector Drawer & In-App Confirmation Modal (Priority: P2)

**Goal**: Upgrade the right-hand Inspector with Explorer/Clipboard quick actions (`Mở thư mục`, `Sao chép đường dẫn`) and replace raw Win32 `MessageBox.Show` popups with an in-window `ContentDialog` modal overlay (`FR-UI-007`).

**Independent Test**: Select a finding, test `Sao chép đường dẫn` & `Mở thư mục`, click `Xem trước (Dry-Run)` and `Dọn dẹp mục đã chọn`, and verify the dimmed in-window confirmation dialog (`Esc` / `Để sau` / `Dọn dẹp ngay`).

### Implementation for User Story 3

- [X] T011 [US3] Replace `MessageBox.Show` with async `ConfirmationModalViewModel` (`TaskCompletionSource<bool>`) and add `OpenSelectedPathInExplorerCommand` + `CopySelectedPathCommand` in `src/NexusClean.UI/ViewModels/MainDashboardViewModel.cs` and render the modal overlay + refined Inspector cards in `src/NexusClean.UI/Views/MainWindow.xaml` and `src/NexusClean.UI/Views/MainWindow.xaml.cs`

---

## Phase 6: Polish & Cross-Cutting Verification

**Purpose**: Verify zero Unicode emojis, zero compiler warnings, 100% test pass rate, and smooth live launch.

- [X] T012 Run automated zero-emoji scan across `src/NexusClean.UI/`, full `dotnet build NexusClean.sln -c Release` (`0 Warning(s)`), `dotnet test NexusClean.sln -c Release` (`31/31 PASSED`), and launch verification per `specs/002-native-win11-ui-redesign/quickstart.md`

---

## Dependencies & Execution Order

1. **Phase 1 (`T001`–`T003`)**: Parallel creation of `Icons.xaml`, `Theme.Light.xaml`, and `Theme.Dark.xaml`.
2. **Phase 2 (`T004`–`T006`)**: `Controls.xaml`, `UiModels.cs`, and `App.xaml` theme switcher.
3. **Phase 3–5 (`T007`–`T011`)**: Sequential ViewModel + `MainWindow.xaml` delivery of `US1`, `US2`, and `US3`.
4. **Phase 6 (`T012`)**: Zero-emoji audit, Release build, sandbox tests, and live UI launch check.

## Parallel Example

```bash
Task T001: "Create monochrome vector icons in src/NexusClean.UI/Themes/Icons.xaml"
Task T002: "Create Windows 11 Light tokens in src/NexusClean.UI/Themes/Theme.Light.xaml"
Task T003: "Create Neutral Graphite Dark tokens in src/NexusClean.UI/Themes/Theme.Dark.xaml"
Task T005: "Implement Presentation models in src/NexusClean.UI/ViewModels/UiModels.cs"
```

## Implementation Strategy

1. Build token & icon foundation (`Phase 1` + `Phase 2`) so zero raw colors or emojis exist in markup.
2. Deliver unified `MainDashboardViewModel` + `MainWindow.xaml` covering `US1` (Segmented Storage Bar & Dual Theme), `US2` (Sidebar Navigation, Instant Search & 2-Line Table), and `US3` (Refined Inspector & In-Window Modal Dialog).
3. Verify build, tests, zero-emoji check, commit, push, and launch `NexusClean.UI` for immediate visual inspection.
