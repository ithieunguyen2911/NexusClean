# Quickstart & Visual Verification Guide: Native Windows 11 UI/UX Redesign

**Feature**: `002-native-win11-ui-redesign`

---

## Prerequisites

- Windows 10 or Windows 11 (x64)
- `.NET 8.0` Desktop Runtime + `.NET SDK` (`8.0+`)

---

## 1. Build & Automated Regression Check

Verify zero compiler warnings across all 8 projects and 100% sandbox test pass rate:

```powershell
cd D:\AI_Learning_Repository\NexusClean
dotnet build NexusClean.sln -c Release
dotnet test NexusClean.sln -c Release
```

**Expected**: `0 Warning(s), 0 Error(s)`, all Domain/Application/Infrastructure/UI tests pass.

---

## 2. Launch Redesigned Desktop UI

```powershell
.\run_ui.ps1
```

---

## 3. End-to-End UX & Visual Acceptance Walkthrough

1. **First-Impression & Theme Check (`SC-UI-001`, `SC-UI-002`)**:
   - Confirm window opens in clean **Windows 11 Light** theme (`#F3F3F3` base, `#FFFFFF` cards, crisp vector icons, zero Unicode emojis, styled `ComboBox` drive picker).
   - Click **`Giao diện: Sáng / Tối`** in the bottom-left sidebar and verify seamless switch to **Neutral Graphite Dark** with zero white legacy Win32 control backgrounds, then toggle back as desired.
2. **Drive Scan & Windows 11 Segmented Storage Bar (`FR-UI-004`)**:
   - Click **`Quét ổ đĩa`** (read-only).
   - Verify the proportional 5-segment storage bar populates smoothly (`Đã dùng khác`, `Dọn an toàn`, `Nên dời sang ổ phụ`, `Bảo trì Admin`, `Còn trống`) and scan time displays in human seconds (`~9,8 giây`).
3. **Instant Sidebar Filter & Keyword Search (`SC-UI-003`)**:
   - Click sidebar views: **`Tất cả mục`**, **`Dọn an toàn`**, **`Dời sang ổ phụ`**, **`Bảo trì hệ thống`**.
   - Type `"chrome"` or `"vscode"` in the search bar; verify `< 16 ms` instant filtering and preserved checkbox selections.
   - Verify each row displays a two-line title + muted path, right-aligned tabular size + mini proportional bar, and compact semantic pill badge.
4. **Refined Inspector & Quick Explorer Actions (`FR-UI-007`)**:
   - Select any row; drag the vertical `GridSplitter` to test responsive resizing.
   - Click **`Sao chép đường dẫn`** and **`Mở thư mục`** in the Inspector header.
5. **In-Window Modal Confirmation (`FR-UI-007`)**:
   - Click **`Xem trước (Dry-Run)`** to verify zero-deletion byte simulation in the bottom bar.
   - Click **`Dọn dẹp mục đã chọn`** and confirm that a clean **in-window modal overlay** (not a Win32 `MessageBox`) displays the Dry-Run summary and dismisses cleanly on **`Để sau`** or **`Esc`**.
