# Contract: Windows 11 Fluent Design System & Anti-AI UX Specification

**Feature**: `002-native-win11-ui-redesign`  
**Target Assembly**: `NexusClean.UI`

---

## 1. Anti-AI Visual Design Rules (Mandatory Invariants)

1. **Zero Unicode Emojis**: No control, badge, header, tooltip, or status string may contain Unicode emoji characters (`🔍`, `⏹`, `✔`, `🛡`, `📜`, `🧪`, `🚀`, `📦`, `⚡`, etc.). Every icon MUST use a `14px`–`18px` monochrome vector `Path` bound to `Foreground`.
2. **Zero Neon Borders on Cards**: Containers MUST use `1px` neutral hairline borders (`BorderSubtleBrush`) and subtle `6px`–`8px` corner radii (`Windows 11 Fluent` standard)—never glowing green, amber, cyan, or purple card outlines.
3. **Sentence-Case Vietnamese Typography**: Replace screaming ALL-CAPS headers (`TRẠNG THÁI Ổ ĐĨA HỆ THỐNG`, `PHÂN TÍCH MỨC ĐỘ ẢNH HƯỞNG (IMPACT INSPECTOR)`) with calm, legible Vietnamese sentence-case hierarchy (`Segoe UI Variable Display` / `Segoe UI`).
4. **Single Primary CTA Per Screen Region**: Only two solid accent buttons exist across the entire window: **`Quét ổ đĩa`** in the top drive header and **`Dọn dẹp đã chọn`** in the bottom action bar. All other actions use neutral secondary outline or subtle ghost styles.

---

## 2. Color Token Contract (`Theme.Light.xaml` & `Theme.Dark.xaml`)

| Token Key | Windows 11 Light (Default) | Neutral Graphite Dark | Usage |
| :--- | :--- | :--- | :--- |
| `WindowBgBrush` | `#F3F3F3` | `#18181B` | Root window background (Mica-like warm neutral) |
| `SidebarBgBrush` | `#EBEBEB` | `#141417` | Left navigation rail (`216px`) |
| `CardBgBrush` | `#FFFFFF` | `#202024` | Content panels, storage card, inspector, table |
| `SubtleSurfaceBrush` | `#F8F9FA` | `#27272C` | Hover states, table header, inspector info blocks |
| `BorderSubtleBrush` | `#E2E4E8` | `#2E2E34` | `1px` hairline dividers and card borders |
| `BorderStrongBrush` | `#CBD0D6` | `#3F3F46` | Input field borders (`ComboBox`, `TextBox`, `CheckBox`) |
| `TextPrimaryBrush` | `#1A1A1A` | `#F4F4F5` | Titles, item names, primary values |
| `TextSecondaryBrush` | `#5C5F66` | `#A1A1AA` | Subtitles, descriptions, column headers |
| `TextMutedBrush` | `#868E96` | `#71717A` | Truncated filesystem paths, timestamps |
| `AccentPrimaryBrush` | `#0067C0` | `#3B82F6` | Primary CTA buttons, selected nav indicator, checkboxes |
| `AccentHoverBrush` | `#005BA8` | `#2563EB` | Primary button hover |
| `SelectionRowBgBrush` | `#EBF3FC` | `#1E293B` | Selected `DataGrid` row background (high legibility!) |
| `PillSafeBg` / `PillSafeFg` | `#E6F4EA` / `#137333` | `#132A1C` / `#6DD58C` | Tier 1 (`Xóa an toàn`) badge & preserved chip |
| `PillRebuildBg` / `PillRebuildFg` | `#E8F0FE` / `#1967D2` | `#172554` / `#93C5FD` | Tier 2 (`Tự tạo lại`) badge |
| `PillRelocateBg` / `PillRelocateFg` | `#FEF7E0` / `#B06000` | `#2E210B` / `#FCD34D` | Tier 3 (`Nên dời ổ`) badge & relocation card |
| `PillAdminBg` / `PillAdminFg` | `#F3E8FD` / `#681DA8` | `#27173A` / `#D8B4FE` | Tier 4 (`Quyền Admin`) badge & UAC card |

---

## 3. Microcopy Transformation Contract (Before vs. After)

| UI Location | Previous v1.0 ("AI-Like" Copy) | Redesigned v1.1 (Human-Crafted Copy) |
| :--- | :--- | :--- |
| Window Title | `NexusClean v1.0 — Kiểm Toán & Dọn Dẹp Ổ Đĩa Thông Minh An Toàn (Clean Architecture)` | `NexusClean` |
| Brand Subtitle | `NEXUSCLEAN • INTELLIGENT STORAGE GUARD` | `NexusClean` / `Quản lý dung lượng Windows` |
| Scan Status | `Hoàn tất kiểm toán ổ C:\ trong 9779 ms — Phát hiện 12 mục...` | `Đã phân tích ổ C: (9,8 giây) • 12 mục phát hiện` |
| Tier Badges | `TIER 1 • XÓA AN TOÀN 100%` / `TIER 2 • BỘ ĐỆM TỰ TẠO LẠI` | `Xóa an toàn` / `Tự tạo lại` / `Nên dời ổ` / `Quyền Admin` |
| Inspector Header | `PHÂN TÍCH MỨC ĐỘ ẢNH HƯỞNG (IMPACT INSPECTOR)` | `Chi tiết mục đã chọn` |
| Inspector Q1 | `1. Bản chất dữ liệu này là gì?` | `Mô tả dữ liệu` |
| Inspector Q2 | `2. Nếu dọn dẹp sẽ ảnh hưởng gì đến máy?` | `Ảnh hưởng khi dọn` |
| Inspector Q3 | `3. Trạng thái Khóa Tiến trình (Restart Manager):` | `Tiến trình đang sử dụng` |
| Preserved Header | `CAM KẾT DỮ LIỆU ĐƯỢC BẢO TOÀN 100%:` | `Dữ liệu được giữ nguyên` |
| Footer Buttons | `🧪 Bước 1: Mô phỏng Dọn dẹp (Dry-Run)` / `🚀 Bước 2: Thực thi Dọn dẹp An toàn` | `Xem trước (Dry-Run)` / `Dọn dẹp mục đã chọn` |
