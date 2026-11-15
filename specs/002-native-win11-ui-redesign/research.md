# Phase 0 Research: Native Windows 11 Human-Crafted UI/UX Redesign

**Feature**: `002-native-win11-ui-redesign`  
**Date**: 2026-11-15  
**Scope**: `src/NexusClean.UI` Presentation Layer (`net8.0-windows`, WPF MVVM)

---

## 1. Visual Theme System: Pure WPF `DynamicResource` Tokens vs. Third-Party UI Frameworks

- **Decision**: Implement a lightweight, zero-bloat native WPF theme architecture inside `src/NexusClean.UI/Themes/` (`Theme.Light.xaml`, `Theme.Dark.xaml`, `Icons.xaml`, `Controls.xaml`) switched at runtime via `Application.Current.Resources.MergedDictionaries` + `DynamicResource`, defaulting to **Windows 11 Fluent Light** (`#F3F3F3` base / `#FFFFFF` card / `#0067C0` Windows Blue) with a 1-click toggle to **Neutral Graphite Dark** (`#18181B` base / `#222226` surface / `#4CC2FF` accent).
- **Rationale**:
  - Eliminates the #1 "AI-generated dashboard" telltale (`#0F172A` Tailwind slate-blue + neon cyan/green/amber/purple borders).
  - Real Windows system utilities (*Windows 11 Settings → Storage*, *WizTree*, *TreeSize*, *Microsoft PC Manager*) use calm neutral light/graphite surfaces with hairline `#E5E5E5` / `#2E2E34` borders and reserve color strictly for small status pills and the storage bar.
  - Zero third-party UI NuGet version conflicts on `.NET 8 / 9 / 10` SDKs, instant startup (`< 250 ms`), and 100% control over `ComboBox`, `DataGrid`, `CheckBox`, `TextBox`, and `ScrollBar` templates.
- **Alternatives Considered**:
  - *WPF-UI / ModernWpfUI NuGet*: Rejected because external theme libraries introduce version-specific XAML namespace coupling and occasionally conflict with custom high-density `DataGrid` row templates; pure XAML `ResourceDictionary` tokens achieve crisp Windows 11 aesthetics with zero external dependency risk.
  - *Dark-only theme*: Rejected because Windows system tools feel most authentic and legible in native Windows 11 Light mode during daytime desktop work, while developers still appreciate a clean Graphite Dark toggle.

---

## 2. Iconography: Embedded Monochrome Vector `Geometry` vs. Unicode Emojis / Font Glyphs

- **Decision**: Centralize 18 crisp `16x16` / `24x24` monochrome `Geometry` vector paths in `src/NexusClean.UI/Themes/Icons.xaml` (`IconDrive`, `IconScan`, `IconStop`, `IconSearch`, `IconShieldCheck`, `IconSparklesNone/Broom`, `IconArrowRightFolder`, `IconLock`, `IconFolderOpen`, `IconCopy`, `IconSunMoon`, `IconHistory`, `IconCheckCircle`, `IconAlertTriangle`, `IconSliders`) and ban all Unicode emojis (`🔍`, `🚀`, `🧪`, `🛡`, etc.) across XAML and C# ViewModels.
- **Rationale**:
  - Emojis inside buttons (`🧪 Bước 1`, `🚀 Bước 2`) are the single strongest visual signal of LLM-generated UIs.
  - Pure XAML `PathGeometry` renders identically on every Windows 10 and Windows 11 build (unlike `Segoe Fluent Icons`, which is absent on Windows 10 and renders `□` tofu glyphs) and automatically inherits button/text foreground brushes in both Light and Dark themes.
- **Alternatives Considered**:
  - *`Segoe Fluent Icons` font family*: Rejected as sole source due to missing font on Windows 10 machines; vector `Geometry` guarantees 100% cross-OS fidelity.

---

## 3. Storage Visualization: Windows 11 Proportional Segmented Bar vs. 4 KPI Boxes

- **Decision**: Replace the 4 separate neon-bordered KPI boxes with a single **Windows 11 Storage Breakdown Card** at the top of the main workspace containing:
  1. Drive selector (`ComboBox`), volume headline (`Ổ đĩa cục bộ (C:) • Đã dùng 235,07 GB / 274,52 GB • Còn trống 39,45 GB`), and primary **`Quét ổ đĩa`** / **`Dừng`** action button.
  2. A horizontal `12px` rounded **5-Segment Proportional Bar** (`Grid` with `GridLength(..., GridUnitType.Star)` columns computed in `MainDashboardViewModel`):
     - Segment 1: **Hệ điều hành & dữ liệu khác** (Neutral Slate `#64748B` / `#52525B`)
     - Segment 2: **Dọn an toàn (Tier 1 & 2)** (Windows Blue `#0067C0` / `#3B82F6`)
     - Segment 3: **Nên dời sang ổ phụ (Tier 3)** (Warm Amber `#D97706`)
     - Segment 4: **Bảo trì Admin (Tier 4)** (Muted Indigo `#6366F1`)
     - Segment 5: **Còn trống** (Subtle Track `#E5E7EB` / `#2E2E34`)
  3. Interactive clickable legend items beneath the bar that filter the table when clicked.
- **Rationale**:
  - Matches *Windows 11 Settings → System → Storage* and *macOS Storage / CleanMyPC* mental models.
  - Saves ~75px of vertical height so more findings fit on screen without scrolling, while turning abstract numbers into immediate spatial proportions.
- **Alternatives Considered**:
  - *Pie / Donut chart*: Rejected—harder to compare small cache slices against a 274 GB drive and wastes horizontal space.

---

## 4. Navigation, Instant Search & High-Density Two-Line Table UX

- **Decision**:
  - Introduce a calm **Left Navigation Rail (`216px`)** with:
    - Brand mark (`NexusClean` + subtle `v1.1` tag)
    - 4 Filter Views with live item count pills: `Tất cả mục`, `Dọn an toàn`, `Dời sang ổ phụ`, `Bảo trì hệ thống`
    - Quiet bottom utility section: `Cảnh báo đầy ổ` (toggle switch), `Kiểm tra bộ quy tắc`, `Nhật ký hoạt động`, `Giao diện: Sáng / Tối`
  - Bind the findings `DataGrid` / list to an `ICollectionView` (`CollectionViewSource.GetDefaultView(Findings)`) filtered by `(SelectedFilterTab, SearchQuery)` so filtering runs in `< 1 ms` in memory while keeping `FindingItemViewModel.IsSelected` intact across tab switches.
  - Upgrade each row (`MinHeight="48"`) to display:
    - Custom styled `CheckBox`
    - **Two-line primary cell**: Bold natural title (`DisplayName`) + muted `11.5px` truncated `TargetPath` (`CharacterEllipsis` + full tooltip)
    - **Right-aligned tabular size cell**: Bold formatted size (`4,71 GB`) + subtle `4px` proportional width bar (`SizeRatioPercent` relative to largest finding, like *WizTree*)
    - **Compact Semantic Pill Badge**: Soft tinted background + crisp label (`Xóa an toàn`, `Tự tạo lại`, `Nên dời ổ`, `Quyền Admin`, `Được bảo vệ`) instead of screaming `TIER 1 • XÓA AN TOÀN 100%` text.
- **Rationale**:
  - Eliminates top-toolbar button soup (previously 5 colored buttons crammed into one row) and makes paths immediately visible without clicking every single row.
- **Alternatives Considered**:
  - *Rebuilding `ObservableCollection` on each keystroke*: Rejected because clearing/re-adding items resets scroll position and selection focus; `ICollectionView.Refresh()` is native WPF best practice.

---

## 5. In-Window `ContentDialog` Confirmation vs. Win32 `MessageBox.Show`

- **Decision**: Implement an asynchronous MVVM dialog service/state on `MainDashboardViewModel` (`ActiveDialog` with `TaskCompletionSource<bool>`) rendered as a dimmed backdrop (`#66000000`) + centered `460px` Fluent dialog card inside `MainWindow.xaml` (supporting `IsDefault="True"` for Enter and `IsCancel="True"` for Esc).
- **Rationale**:
  - Win32 `MessageBox.Show` spawns an old Windows 95/Aero dialog box that clashes jarring with modern desktop apps and makes unit-testing ViewModel confirmation flows impossible without UI automation hooks.
- **Alternatives Considered**:
  - *Separate child `Window`*: Unnecessary window-management complexity compared to a crisp single-window overlay grid layer (`Panel.ZIndex="100"`).
