# Feature Specification: Native Windows 11 Human-Crafted UI/UX Redesign

**Feature Branch**: `002-native-win11-ui-redesign`  
**Created**: 2026-11-15  
**Status**: Draft  
**Input**: User request: "Update giao diện UI | UX nhìn cho nó bớt AI hơn giúp tôi" (accompanied by live screenshot of `NexusClean.UI` v1.0).

## Problem Diagnosis (Visual Audit of v1.0 Screenshot)

1. **Emoji Overload**: Toolbar and footer buttons rely on Unicode emojis (`🔍`, `⏹`, `✔`, `🛡`, `📜`, `🧪`, `🚀`, `📦`, `⚡`), immediately signaling an AI-generated prototype rather than a native Windows utility.
2. **Generic AI Dark-Slate + Neon Rainbow Borders**: `#0F172A` / `#1E293B` backgrounds paired with 4 competing neon KPI borders (cyan, bright green, amber, purple) and ALL-CAPS headers mimic generic AI web dashboards rather than Windows 11 desktop software.
3. **Unstyled Legacy WPF Controls**: Raw Win32/Aero `ComboBox` (`C:\`), harsh white `DataGrid` selection rows, cramped single-line table cells, left-aligned byte strings, and jarring `MessageBox.Show` popups break visual cohesion.
4. **Fragmented 5-Box Layout & Competing Primary Buttons**: 5 stacked bordered containers and 7 colored buttons compete for visual attention instead of guiding the user through a calm, clear workflow.
5. **Robotic / Verbose Prompt Copywriting**: Phrases such as `"NEXUSCLEAN • INTELLIGENT STORAGE GUARD"`, `"trong 9779 ms"`, `"1. Bản chất dữ liệu này là gì?"`, and `"TIER 1 • XÓA AN TOÀN 100%"` read like raw LLM output instead of natural Vietnamese desktop UX.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authentic Windows 11 Visual Language & Segmented Storage Bar (Priority: P1)

As a Windows 10/11 desktop user, I want NexusClean to look and feel like a native, human-crafted Windows utility (comparable to *Windows 11 Settings → Storage*, *Microsoft PC Manager*, and *WizTree*) with crisp Segoe vector icons, calm neutral surfaces (Light default + Dark toggle), and a unified proportional storage bar so the app feels trustworthy and effortless to read.

**Why this priority**: Directly resolves the visual "AI-generated dashboard" impression on first launch.

**Independent Test**: Launch `NexusClean.UI`, toggle between Light and Neutral Dark themes, run a `C:` scan, and verify zero emojis, zero neon rainbow card borders, styled native-feeling controls, and an accurate multi-segment proportional storage bar.

**Acceptance Scenarios**:

1. **Given** the user opens `NexusClean.UI`, **When** inspecting every toolbar, sidebar, table row, badge, and dialog, **Then** zero Unicode emojis (`🔍`, `🚀`, `🧪`, etc.) appear anywhere in the UI; all icons use monochrome vector `Path` or `Segoe Fluent Icons` / `Segoe MDL2 Assets`.
2. **Given** a completed drive scan, **When** viewing the top storage summary, **Then** a unified Windows 11-style proportional segmented bar displays Used Other, Safe Cache, Relocatable Data, Admin Queue, and Free Space with human-friendly units and seconds (`9,8 giây` instead of `9779 ms`).
3. **Given** the user clicks the Theme switch in the sidebar, **When** toggling between **Sáng (Windows Light)** and **Tối (Neutral Graphite)**, **Then** all surfaces, `ComboBox`, `CheckBox`, `TextBox`, `DataGrid`, `ScrollBar`, and badges update cohesively with zero white Win32 control bleed.

---

### User Story 2 - Scannable Category Navigation, Search & Two-Line Findings Table (Priority: P1)

As a user reviewing 10–30 detected storage items, I want a left navigation filter (`Tất cả`, `Dọn an toàn`, `Dời sang ổ phụ`, `Cần quyền Admin`), instant keyword search, and spacious two-line table rows with mini size bars and compact pill badges so I can find and verify items in seconds.

**Why this priority**: Eliminates cramped spreadsheet clutter and overcrowded top toolbars.

**Independent Test**: Scan `C:`, click each sidebar filter tab (`Tất cả`, `Dọn an toàn`, `Dời sang ổ phụ`, `Bảo trì Admin`), type `"chrome"` or `"zalo"` in the search box, and resize the detail pane with `GridSplitter`.

**Acceptance Scenarios**:

1. **Given** scan findings are loaded, **When** clicking sidebar filter tabs or typing in the search input, **Then** the findings list filters instantaneously (`< 16 ms`) without re-scanning the disk while preserving checkbox selection state.
2. **Given** findings are rendered in the list, **When** viewing each row, **Then** it displays a spacious `44px+` layout with checkbox, item title + muted path subtitle, right-aligned tabular byte size + proportional mini bar, and a subtle semantic pill badge (`An toàn`, `Bộ đệm`, `Nên dời ổ`, `Quyền Admin`, `Bảo vệ`).

---

### User Story 3 - Refined Inspector Drawer & In-App Confirmation Modal (Priority: P2)

As a safety-conscious user, I want a clean inspector panel with quick Explorer/Copy actions and an in-window confirmation dialog (replacing raw Win32 `MessageBox.Show`) so Dry-Run previews, Relocation steps, and UAC commands feel integrated and natural.

**Why this priority**: Completes the UX polish across inspection, Dry-Run confirmation, secondary-drive relocation, and UAC elevation.

**Independent Test**: Select a Tier 1/2 cache item, click `"Mở thư mục"` / `"Sao chép đường dẫn"`, click `"Xem trước (Dry-Run)"`, then click `"Dọn dẹp ..."` and verify the in-window modal overlay displays exact metrics without spawning legacy `MessageBox` windows.

**Acceptance Scenarios**:

1. **Given** any finding is selected, **When** viewing the right-hand Inspector, **Then** concise Vietnamese sections (`Tóm tắt`, `Ảnh hưởng sau khi dọn`, `Dữ liệu giữ nguyên`, `Tiến trình đang mở`) are displayed alongside `"Mở vị trí tệp"` and `"Sao chép đường dẫn"` actions.
2. **Given** the user initiates cleanup or UAC maintenance, **When** confirmation is requested, **Then** a styled in-window modal overlay (`ContentDialog` pattern) presents the command/Dry-Run summary with keyboard (`Esc` / `Enter`) support.

### Edge Cases

- **Missing `Segoe Fluent Icons` on older Windows 10 builds**: Icon helper or vector `Geometry` paths MUST render crisp monochrome SVG/XAML `Path` data so icons never degrade into tofu boxes (`□`) on Windows 10.
- **Empty Filter / Search State & Pre-Scan Empty State**: Before scanning (or when a filter/search matches 0 items), display a calm, centered empty state with a clear single action rather than a blank white/black grid.
- **Long Vietnamese Text & Deep `AppData` Paths**: Paths and titles MUST truncate gracefully with `TextTrimming="CharacterEllipsis"` and full path `ToolTip`, while resizing cleanly down to `1080x700` via `GridSplitter`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-UI-001 (Zero-Emoji Vector Iconography)**: Remove 100% of Unicode emojis from XAML and ViewModels; render all icons via resolution-independent monochrome XAML `Geometry` paths (`14–16px`).
- **FR-UI-002 (Dual Native Theme System — Light Default & Graphite Dark)**: Provide a token-driven `ResourceDictionary` supporting **Windows 11 Light** (`#F3F3F3` window base, `#FFFFFF` card surface, `#0067C0` primary accent) as default and **Neutral Graphite Dark** (`#18181B` / `#202024`, zero blue-neon tint) toggled at runtime.
- **FR-UI-003 (Custom Cohesive Control Templates)**: Provide full WPF `ControlTemplate`s and `Style`s for `Button` (Primary, Secondary Outline, Subtle Ghost), `ComboBox`, `CheckBox`, `TextBox` (search & inputs), `DataGrid` (header, row hover/selection, cell alignment), `ProgressBar`, and thin `ScrollBar`.
- **FR-UI-004 (Segmented Storage Breakdown Bar)**: Replace the 4 neon-bordered KPI boxes with a single Windows 11-style drive storage header card featuring a proportional multi-segment capacity bar (`Đã dùng khác`, `Dọn an toàn`, `Nên dời sang ổ phụ`, `Bảo trì Admin`, `Còn trống`) and interactive legend chips.
- **FR-UI-005 (Sidebar Navigation, Search & Resizable Split View)**: Organize workspace into a quiet Left Navigation Rail (`210px`) with tier count badges + utility switches, a center Findings Workspace with instant search/filter (`ICollectionView` or filtered observable projection), and a resizable `GridSplitter` Inspector panel.
- **FR-UI-006 (Natural Vietnamese Product Microcopy)**: Rewrite all robotic/ALL-CAPS headings, millisecond counters, and numbered Q&A prompts into concise, human-authored Vietnamese product copy while preserving 100% of Domain `ImpactAssessment` safety disclosures.
- **FR-UI-007 (In-Window Modal Confirmation & Explorer Quick Actions)**: Replace `MessageBox.Show` calls in `MainDashboardViewModel` with a MVVM-bound in-window modal dialog overlay (`ModalDialogViewModel`), and add `"Mở thư mục"` + `"Sao chép đường dẫn"` commands to the Inspector.
- **FR-UI-008 (Strict Clean Architecture Preservation)**: All changes MUST remain strictly inside `src/NexusClean.UI` (and UI ViewModel unit tests if added), with zero modifications that weaken `NexusClean.Domain`, `NexusClean.Application`, or `NexusClean.Infrastructure` safety invariants.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-UI-001**: **0 Unicode emojis** (`\p{So}` pictorial symbols) and **0 neon-bordered KPI cards** remain in `src/NexusClean.UI`.
- **SC-UI-002**: **100% of interactive controls** (`ComboBox`, `CheckBox`, `TextBox`, `DataGrid`, `ScrollBar`, confirmation dialogs) render with custom theme-aware styles in both Light and Dark modes with zero legacy Win32 white-box artifacts.
- **SC-UI-003**: Client-side category tab switching and keyword filtering update the displayed list in **`< 16 ms`** without re-triggering disk I/O or losing checkbox state.
- **SC-UI-004**: Full solution `dotnet build NexusClean.sln -c Release` succeeds with **`0 Warning(s), 0 Error(s)`** and **`100%` passing automated tests**.
