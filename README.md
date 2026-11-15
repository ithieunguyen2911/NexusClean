# NexusClean

> **Intelligent, Safety-First Windows Disk Audit, Cache Cleanup & Secondary-Drive Relocation System**  
> Designed with **4-Layer Clean Architecture (.NET 8 / C# 12)**, **SOLID Design Patterns**, and **GitHub Spec Kit** Spec-Driven Development.

---

## Why NexusClean?

Traditional Windows cleanup utilities either miss over **140+ GB** of hidden developer/app/system bloat inside `AppData`, `%USERPROFILE%\.dot-folders`, WSL2 virtual disks, and Volume Shadow Copies—or act as reckless "one-click wipers" that destroy browser login sessions, saved cookies, IDE extensions, and chat media.

Born from a real-world Windows `C:` (`274.52 GB`, `98%` full) forensic case study that safely reclaimed **+60 GB**, **NexusClean** enforces **5 Golden Safety Rules**:

1. **Hardcoded Root Protection (`RootSystemFolderGuard`)**: Unconditionally blocks direct deletion of `AppData\Local`, `AppData\Roaming`, `%USERPROFILE%`, `C:\Windows`, and `C:\Program Files`.
2. **100% Browser Profile Preservation (`BrowserProfilePreservationGuard`)**: Purges only transient cache subdirectories (`Cache\Cache_Data`, `Code Cache`, `GPUCache`, `Service Worker\CacheStorage`) across Chrome, Edge, Brave, and Cốc Cốc while keeping `Login Data`, `Cookies`, `Bookmarks`, `History`, `Preferences`, and `Extensions` 100% intact.
3. **5-Tier Vietnamese Impact Analysis**: Explains in plain Vietnamese what every item is, its risk score (`0–10`), side effects if cleaned, and guaranteed preserved assets *before* any action is taken.
4. **Relocate Over Blind Delete**: Guides continuously growing user data (`ZaloData\media`, `pagefile.sys`, active WSL2 `ext4.vhdx`) toward secondary volumes (`D:`) with a `+20%` capacity buffer check instead of destructive deletion.
5. **Process-Lock Awareness & Isolated Admin Queue**: Uses Windows Restart Manager (`rstrtmgr.dll`) to skip locked files gracefully and isolates privileged maintenance (`vssadmin` shadow storage cap, `wsl --manage --move`) into a parameterized UAC workflow with bounded JSONL audit logging.

---

## Architecture Overview

```mermaid
flowchart LR
    subgraph UI["Presentation Layer"]
        WPF["NexusClean.UI (WPF MVVM)"]
        CLI["NexusClean.Cli (CLI & JSON)"]
    end

    subgraph APP["Application Layer"]
        ORCH["DiskAnalysisOrchestrator"]
        UC["Analyze / Simulate / Execute / Relocate UseCases"]
    end

    subgraph DOM["Domain Layer (Pure C#)"]
        ENT["StorageFinding & ImpactAssessment"]
        GUARD["4-Link ISafetyGuard Chain"]
        INT["IDetectionRule & ICleanupStrategy"]
    end

    subgraph INF["Infrastructure Layer"]
        SIZER["MultiThreadedDirectorySizer"]
        LOCK["WindowsProcessInspector (rstrtmgr)"]
        STRAT["8 Cleanup Strategies & Elevated Runner"]
        JSON["JsonSignatureRuleProvider & Audit Log"]
    end

    UI --> APP
    APP --> DOM
    INF -. "Implements" .-> DOM
```

---

## Repository Structure

| Path | Description |
| :--- | :--- |
| [`docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md`](./docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md) | Real-world `C:` full-disk audit metrics, 4 disk-refill mechanisms, and 5 Golden Rules |
| [`docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`](./docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md) | 4-Layer OOD Clean Architecture, SOLID patterns, UML Class Diagram & 5-Tier Impact Matrix |
| [`docs/03-SEED-SIGNATURE-RULES.json`](./docs/03-SEED-SIGNATURE-RULES.json) | Seed declarative JSON detection & safety rules |
| [`docs/04-DOCS-AUDIT-AND-GAP-ANALYSIS.md`](./docs/04-DOCS-AUDIT-AND-GAP-ANALYSIS.md) | Technical audit & cross-document gap analysis of `docs/01..03` |
| [`.specify/memory/constitution.md`](./.specify/memory/constitution.md) | Non-negotiable project governance & safety constitution (`v1.0.0`) |
| [`specs/001-intelligent-disk-cleanup/`](./specs/001-intelligent-disk-cleanup/) | Full GitHub Spec Kit artifacts (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `checklists/`, `quickstart.md`, `tasks.md`) |
| [`.agents/skills/`](./.agents/skills/) | Workspace GitHub Spec Kit skills integrated for Google Antigravity (`agy`) |

---

## Quick Start — Hướng Dẫn Khởi Động Nhanh

### Yêu cầu môi trường
- **Hệ điều hành**: Windows 10 / Windows 11 (x64)
- **Runtime / SDK**: `.NET 8.0 SDK` (hoặc `.NET 9 / .NET 10 SDK` kèm `.NET 8 Desktop Runtime`)

### Cách 1: Khởi động Giao diện Desktop (WPF Dashboard — Khuyên dùng)

Mở **PowerShell** tại thư mục gốc dự án `NexusClean` và chạy:

```powershell
.\run_ui.ps1
```

Hoặc chạy trực tiếp bằng lệnh `.NET CLI`:

```powershell
dotnet run --project src/NexusClean.UI -c Release
```

**Quy trình sử dụng an toàn trên giao diện Desktop**:
1. Nhấn **1. Quét Ổ Đĩa** (chỉ đọc 100%, không chạm dữ liệu).
2. Nhấp vào từng mục để xem **Thanh Thanh Tra Tác Động 5 Cấp Độ** (Mức rủi ro `0–10`, Tác động nếu xóa, Cam kết dữ liệu giữ nguyên, Tiến trình đang giữ khóa).
3. Nhấn **2. Mô Phỏng Dry-Run** để xem trước chính xác dung lượng thu hồi mà chưa xóa bất kỳ tệp nào.
4. Nhấn **3. Dọn An Toàn Đã Chọn** (chỉ dọn Tier 1 & Tier 2; giữ nguyên 100% mật khẩu, cookie, dấu trang trình duyệt và extension mới nhất).
5. Với Zalo / Pagefile / WSL2 / Shadow Copy: dùng nút **Tư Vấn Dời Ổ D:** hoặc **Thực Thi Quyền Admin (UAC)**.

### Cách 2: Sử dụng Công cụ Dòng lệnh (`NexusClean.Cli`)

```powershell
# 1. Xem hướng dẫn các lệnh CLI
.\run_cli.ps1 --help

# 2. Quét kiểm toán ổ C: (chỉ đọc 100%, hỗ trợ xuất --json)
.\run_cli.ps1 scan --drive C:

# 3. Mô phỏng Dry-Run các mục bộ nhớ đệm an toàn (không xóa tệp thật)
.\run_cli.ps1 simulate --drive C: --tier safe

# 4. Lập kế hoạch di dời dữ liệu phình to (ZaloData, Pagefile) sang ổ phụ D:
.\run_cli.ps1 relocate-plan --drive C: --target D:

# 5. Kiểm tra tính hợp lệ & an toàn của bộ quy tắc JSON
.\run_cli.ps1 rules validate

# 6. Thực thi dọn dẹp an toàn Tier 1 + Tier 2 (ghi nhật ký kiểm toán vào %LOCALAPPDATA%\NexusClean\logs\audit.jsonl)
.\run_cli.ps1 clean --drive C: --tier safe --yes
```

### Chạy Bộ Kiểm Thử Tự Động Cô Lập (Sandbox xUnit Tests)

Toàn bộ kiểm thử tự động chạy cô lập tuyệt đối bên trong `%TEMP%\NexusClean.Tests\<guid>`:

```powershell
dotnet test NexusClean.sln -c Release
```

---

## Spec-Driven Development Workflow (GitHub Spec Kit)

This repository uses [GitHub Spec Kit](https://github.com/github/spec-kit) (`specify-cli` with Antigravity `agy` skills):

1. **Constitution** (`/speckit-constitution`) → [`.specify/memory/constitution.md`](./.specify/memory/constitution.md) *(Completed)*
2. **Specification** (`/speckit-specify`) → [`specs/001-intelligent-disk-cleanup/spec.md`](./specs/001-intelligent-disk-cleanup/spec.md) *(Completed)*
3. **Architecture & Plan** (`/speckit-plan`) → [`plan.md`](./specs/001-intelligent-disk-cleanup/plan.md), [`research.md`](./specs/001-intelligent-disk-cleanup/research.md), [`data-model.md`](./specs/001-intelligent-disk-cleanup/data-model.md), [`contracts/`](./specs/001-intelligent-disk-cleanup/contracts/) *(Completed)*
4. **Task Breakdown** (`/speckit-tasks`) → [`specs/001-intelligent-disk-cleanup/tasks.md`](./specs/001-intelligent-disk-cleanup/tasks.md) (`T001`–`T046`) *(Completed)*
5. **Consistency Analysis** (`/speckit-analyze`) → Verified 100% requirement coverage & 0 constitution violations *(Completed)*
6. **Implementation** (`/speckit-implement`) → Executed Phase 1 (`T001`) through Phase 8 (`T046`) with 100% passing sandbox tests *(Completed)*.
