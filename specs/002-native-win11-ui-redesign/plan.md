# Implementation Plan: Native Windows 11 Human-Crafted UI/UX Redesign

**Branch**: `002-native-win11-ui-redesign` | **Date**: 2026-11-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-native-win11-ui-redesign/spec.md`

## Summary

Redesign `NexusClean.UI` (`WPF / net8.0-windows`) from an AI-looking neon-on-slate dashboard into an authentic, calm, human-crafted **Windows 11 Desktop Utility** (inspired by *Windows 11 Settings → Storage*, *Microsoft PC Manager*, and *WizTree*). Key technical changes:
1. Replace 100% of Unicode emojis with resolution-independent monochrome XAML `Geometry` vector icons (`Themes/Icons.xaml`).
2. Replace the `#0F172A` neon-bordered palette with a token-driven dual theme system: **Windows 11 Fluent Light (Default)** and **Neutral Graphite Dark** (`Themes/Theme.Light.xaml`, `Themes/Theme.Dark.xaml`), plus cohesive `ControlTemplate`s (`Themes/Controls.xaml`) eliminating unstyled Win32 `ComboBox`, `CheckBox`, `TextBox`, `DataGrid`, and `ScrollBar` artifacts.
3. Restructure the window layout into a **Left Navigation Rail (`216px`)** + **Windows 11 Segmented Storage Breakdown Bar** + **Instant Search & Two-Line Findings Table (with mini WizTree size bars & semantic pills)** + **Resizable `GridSplitter` Inspector** + **In-Window `ContentDialog` Modal Overlay** (eliminating `MessageBox.Show`) with natural Vietnamese product copywriting.

## Technical Context

**Language/Version**: C# 12 on `.NET 8.0` (`TargetFramework=net8.0-windows`, `UseWPF=true`)

**Primary Dependencies**: Native WPF (`PresentationFramework`, `PresentationCore`, `WindowsBase`) + existing `CommunityToolkit.Mvvm 8.3.2` (zero new third-party UI NuGet packages required)

**Storage**: N/A (optional lightweight theme preference in `%LOCALAPPDATA%\NexusClean\ui-settings.json` or runtime memory; core scan/audit storage unchanged)

**Testing**: `xUnit` (`NexusClean.Domain.Tests`, `NexusClean.Application.Tests`, `NexusClean.Infrastructure.Tests` — 31 existing tests preserved + ViewModel filter/formatting verification)

**Target Platform**: Windows 10 & Windows 11 Desktop (x64, DPI-aware vector rendering, `MinWidth=1080`, `MinHeight=700`)

**Project Type**: Desktop Application (`WPF MVVM` Presentation Layer within 4-Layer Clean Architecture)

**Performance Goals**: `< 16 ms` (`60 FPS`) UI response on theme switching, category tab filtering, keyword search (`ICollectionView`), and `GridSplitter` resizing; zero extra disk I/O during UI filtering

**Constraints**: Strict Presentation-layer isolation (`src/NexusClean.UI` only); zero modifications to `Domain` safety guards or `Infrastructure` deletion engines; zero Unicode emojis; zero Win32 `MessageBox.Show` calls

**Scale/Scope**: 1 main desktop shell (`MainWindow.xaml`), 4 modular XAML resource dictionaries (`Icons.xaml`, `Theme.Light.xaml`, `Theme.Dark.xaml`, `Controls.xaml`), and refactored MVVM ViewModels (`MainDashboardViewModel`, `FindingItemViewModel`, `StorageBarBreakdownViewModel`, `ConfirmationModalViewModel`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Constitution Principle | Pre-Design Gate | Post-Design Gate | Verification Notes |
| :--- | :--- | :--- | :--- |
| **I. 4-Layer Clean Architecture & Domain Purity** | PASS | PASS | 100% of redesign resides in `src/NexusClean.UI`; `NexusClean.Domain` remains 0-NuGet pure C#. |
| **II. SOLID & Canonical OOD Patterns** | PASS | PASS | Preserves DI-injected `DiskAnalysisOrchestrator`, `IDiskGrowthWatcher`, and MVVM command separation. |
| **III. Zero-Data-Loss Safety Guards & Profile Preservation** | PASS | PASS | `CanSelectForStandardCleanup` checkbox lock and all 4 Domain guards remain untouched. |
| **IV. 5-Tier Impact Analysis & Relocation-Over-Deletion** | PASS | PASS | All 5 tiers, Vietnamese impact disclosures, preserved-asset chips, and Relocation steps remain prominently surfaced in cleaner typography. |
| **V. Process-Lock Awareness, Isolated Admin Flow & Auditability** | PASS | PASS | Restart Manager lock attribution, Dry-Run preview, UAC command preview modal, and JSONL audit folder shortcut are preserved and clearer. |
| **VI. Sandbox-Isolated Automated Testing** | PASS | PASS | All 31 sandbox xUnit tests remain green without touching host paths. |

## Project Structure

### Documentation (this feature)

```text
specs/002-native-win11-ui-redesign/
├── spec.md              # Visual audit & UI/UX feature specification
├── plan.md              # This implementation plan (/speckit-plan output)
├── research.md          # Phase 0 UI/UX & WPF architecture decisions
├── data-model.md        # Phase 1 Presentation ViewModel & theme state model
├── quickstart.md        # Phase 1 build & visual walkthrough verification guide
├── contracts/
│   └── win11-fluent-ui-ux-contract.md  # Tokens, control templates & microcopy contract
└── tasks.md             # Phase 2 output (/speckit-tasks — generated next)
```

### Source Code (repository root)

```text
src/
├── NexusClean.Domain/                 # UNCHANGED (Pure C# entities & 4 safety guards)
├── NexusClean.Application/            # UNCHANGED (UseCases & DiskAnalysisOrchestrator)
├── NexusClean.Infrastructure/         # UNCHANGED (Sizer, Restart Manager, 8 Strategies, JSONL Audit)
├── NexusClean.Cli/                    # UNCHANGED (CLI host)
└── NexusClean.UI/                     # TARGET OF FEATURE 002 REDESIGN
    ├── App.xaml                       # MergedDictionaries bootstrap (Light default + Icons + Controls)
    ├── App.xaml.cs                    # Runtime ThemeManager helper (Light <-> Neutral Graphite Dark)
    ├── Themes/
    │   ├── Icons.xaml                 # 18 monochrome vector PathGeometry definitions (zero emojis)
    │   ├── Theme.Light.xaml           # Windows 11 Fluent Light color & brush tokens
    │   ├── Theme.Dark.xaml            # Neutral Graphite Dark color & brush tokens
    │   └── Controls.xaml              # Custom styles/templates: Button, ComboBox, CheckBox, TextBox, DataGrid, ScrollBar, PillBadge
    ├── ViewModels/
    │   ├── MainDashboardViewModel.cs  # Filter tabs, instant search ICollectionView, StorageBar ratios, Modal state, natural Vi copy
    │   └── UiModels.cs                # StorageBarBreakdownViewModel, ConfirmationModalViewModel, FindingFilterCategory, BadgeTone
    └── Views/
        ├── MainWindow.xaml            # Left Nav Rail + Segmented Storage Bar + 2-Line Table + GridSplitter Inspector + Modal Overlay
        └── MainWindow.xaml.cs         # Clean code-behind (zero business logic)

tests/
├── NexusClean.Domain.Tests/           # Existing 18 guard & entity tests
├── NexusClean.Application.Tests/      # Existing 3 orchestrator & relocation tests
└── NexusClean.Infrastructure.Tests/   # Existing 10 sandbox cleanup, SemVer & Restart Manager tests
```

**Structure Decision**: Retain the existing 8-project Clean Architecture solution (`NexusClean.sln`) and modularize `src/NexusClean.UI` by introducing a dedicated `Themes/` folder (`Icons.xaml`, `Theme.Light.xaml`, `Theme.Dark.xaml`, `Controls.xaml`) and `ViewModels/UiModels.cs` so XAML markup stays clean, maintainable, and decoupled from color literals.

## Complexity Tracking

*Zero Constitution violations — table omitted as all 6 gates pass unconditionally.*
