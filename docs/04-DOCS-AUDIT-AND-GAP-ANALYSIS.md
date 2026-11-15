# Báo Cáo Phân Tích Chuyên Sâu Bộ Tài Liệu Gốc (`docs/01..03`) & Đối Chiếu Thiết Kế Spec Kit

- **Dự án**: `NexusClean` (`D:\AI_Learning_Repository\NexusClean`)
- **Ngày phân tích**: 15/11/2026
- **Phạm vi tài liệu được kiểm toán**:
  1. `docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md` (Ngữ cảnh thực nghiệm kiểm toán ổ `C:` `274.52 GB` & 5 Nguyên tắc vàng)
  2. `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md` (Đặc tả OOD, Clean Architecture 4 tầng, UML, Ma trận 5 cấp độ tác động)
  3. `docs/03-SEED-SIGNATURE-RULES.json` (9 quy tắc nhận diện khởi tạo định dạng JSON `v1.0.0`)

---

## 1. Đánh Giá Tổng Quan Điểm Mạnh Của Bộ Tài Liệu Gốc

Bộ 3 tài liệu gốc có chất lượng định hướng rất cao và bám sát thực tiễn vận hành Windows:
1. **Số liệu thực chứng rõ ràng (`docs/01`)**: Chỉ ra chính xác nghịch lý phổ biến trên Windows — thư mục người dùng hiển thị (`Desktop` + `Downloads` + `Documents`) chỉ chiếm **4.72 GB**, trong khi **> 142 GB** nằm ẩn trong `AppData\Local` (`53.25 GB`), `AppData\Roaming` (`32.51 GB`), `%USERPROFILE%\.*` (`21.94 GB`) và gốc `C:\` (`Drivers`, `pagefile.sys`, `hiberfil.sys`, `System Volume Information`).
2. **Giải thích đúng 4 cơ chế "tự đầy lại"**: Shadow Copy (*Copy-on-Write*), `pagefile.sys` tự phình theo RAM, ứng dụng nhắn tin/AI Agent ghi liên tục (`ZaloData\media`, `.codex`, `.gemini`), và Auto-Updater/IDE lưu trùng phiên bản cũ (`CachedExtensionVSIXs`, `*-updater`, `.vscode\extensions`).
3. **Kiến trúc phần mềm chuẩn mực (`docs/02`)**: Phân tách Clean Architecture 4 tầng (`UI` → `Application` → `Domain` ← `Infrastructure`) kết hợp 4 Design Patterns kinh điển (*Strategy*, *Chain of Responsibility*, *Command + Dry-Run*, *Observer*) giúp tách biệt hoàn toàn logic nghiệp vụ khỏi giao diện và hệ điều hành.
4. **Tư duy An toàn lấy Người dùng làm trung tâm (`docs/01` & `docs/03`)**: Ma trận 5 cấp độ `ImpactLevel` cùng thông điệp tiếng Việt (`summaryVi`, `sideEffectIfDeleted`, `preservedAssets`) giải quyết triệt để nỗi sợ mất mật khẩu trình duyệt hoặc hỏng phần mềm khi dọn ổ đĩa.

---

## 2. Phát Hiện 7 Điểm Lệch / Khoảng Trống Kỹ Thuật Giữa `docs/01`, `docs/02` và `docs/03`

Qua quá trình đối chiếu chéo từng trường dữ liệu, kiểu liệt kê (Enum) và kịch bản thực tế, nhóm phân tích phát hiện **7 vấn đề kỹ thuật quan trọng** cần chuẩn hóa trước khi viết code để tránh lỗi kiến trúc ở giai đoạn thực thi:

| Mã | Mức độ | Vị trí phát hiện | Mô tả chi tiết điểm lệch / khoảng trống | Giải pháp đã chuẩn hóa vào bộ Spec Kit (`specs/001-...`) |
| :---: | :---: | :--- | :--- | :--- |
| **G-01** | **HIGH** | `docs/02` §4–6 vs `docs/03` `actionType` | **Lệch tên & thiếu giá trị Enum `ActionType` / `CleanupActionType`**: Sơ đồ UML (`docs/02` §4) gọi là `ActionType`, cấu trúc thư mục (§6) gọi là `CleanupActionType.cs` và chỉ liệt kê 4–6 Strategy; trong khi `docs/03` sử dụng **7 giá trị chuỗi khác nhau** (`DeleteContents`, `DeleteUnlockedItems`, `SelectiveSubfolderPurge`, `SemVerDuplicatePrune`, `RecommendRelocateToSecondaryDrive`, `RelocateOrUnregisterAdmin`, `ResizeShadowStorageAdmin`) cộng thêm trạng thái khóa `BlockedNoOp`. | Chuẩn hóa `CleanupActionType` gồm đủ **8 giá trị** khớp 100% với `docs/03` và ánh xạ 1-1 tới các lớp `ICleanupStrategy` tại `data-model.md` §1.2 & `contracts/signature-rules.v1.schema.json`. |
| **G-02** | **HIGH** | `docs/03` L50–L90 (`browser.*` & `ide.vscode.*`) | **Cấu trúc JSON Rule không đồng nhất (Polymorphic / Composite Schema)**: Các rule thường dùng `targetPaths`; riêng `browser.chromium.selective_cache` không có `targetPaths` mà dùng `basePaths` + `profileFolderRegex` + `allowedCacheSubfolders`; còn `ide.vscode.cached_vsix_and_orphans` vừa có `targetPaths` (cần xóa nội dung) vừa có `extensionRoot` + `pruneOlderDuplicateVersions` + `pruneDotUuidTempFolders` (cần lọc theo SemVer). Nếu viết bộ đọc JSON đơn giản chỉ đọc `targetPaths` sẽ bỏ sót toàn bộ Chrome/Edge và `.vscode\extensions`! | Thiết kế JSON Schema chính thức (`anyOf` tại `contracts/signature-rules.v1.schema.json`) và tách rõ 3 bộ Rule Adapter (`JsonSignatureDetectionRule`, `BrowserSelectiveCacheRule`, `DuplicateVersionExtensionRule`) trong `plan.md` & `research.md` R-05. |
| **G-03** | **HIGH** | `docs/01` §1.1 & §2 vs `docs/03` | **Khoảng trống độ phủ ~42+ GB từ Case Study chưa có trong `03-SEED-SIGNATURE-RULES.json`**: `docs/01` nêu rõ Nguyên tắc vàng #2 áp dụng cho **Brave & Cốc Cốc** và chỉ ra các ổ ngốn dung lượng lớn gồm `Notion\Partitions` (`9.94 GB`), `Codex` (`5.74 GB`), `.gemini`/`.codex`, `.cache`/`.rustup`, `pagefile.sys` (`17.55 GB`), `hiberfil.sys` (`4.69 GB`), nhưng `docs/03` mới chỉ khai báo Chrome/Edge và 9 rule cơ bản. | Giữ nguyên tệp gốc `docs/03-SEED-SIGNATURE-RULES.json` làm chuẩn đối chiếu, đồng thời thiết kế `rules/default-signature-rules.json` (`T018`) mở rộng đầy đủ Brave, Cốc Cốc, Notion, AI Agent logs/tmp, `.cache` và chẩn đoán `pagefile.sys`/`hiberfil.sys` (`FR-014`). |
| **G-04** | **MEDIUM** | `docs/01` §2 vs `docs/02` §3 & §6 | **Chưa đồng nhất tên các lớp `ISafetyGuard` và thiếu `AdminElevationGuard` trong cây thư mục**: `docs/01` gọi là `SafetyGuard` / `ProtectedProfileGuard`; `docs/02` §3 gọi là `RootSystemFolderGuard`, `BrowserProfilePreservationGuard`, `RunningProcessLockGuard`, `AdminElevationGuard`; còn `docs/02` §6 lại đặt tên `ProtectedBrowserProfileGuard.cs`, `RootAppDataGuard.cs` và thiếu tệp `AdminElevationGuard.cs`. | Chuẩn hóa chuỗi 4 Guard cố định trong `Domain/Guards/`: `RootSystemFolderGuard` → `BrowserProfilePreservationGuard` → `RunningProcessLockGuard` → `AdminElevationGuard`, kèm danh sách khóa cứng bất biến (không thể bị ghi đè bởi JSON ngoài) tại `Constitution` §III & `T010`–`T011`. |
| **G-05** | **MEDIUM** | `docs/03` L81–L84 (`SemVerDuplicatePrune`) | **Rủi ro xóa nhầm Extension VS Code có hậu tố kiến trúc hoặc đang chờ cập nhật**: Thư mục trong `.vscode\extensions` thực tế có dạng `ms-python.python-2026.4.0-win32-x64` hoặc bản pre-release, đồng thời VS Code quản lý phiên bản đang kích hoạt qua tệp `extensions.json`. Nếu chỉ tách chuỗi đơn giản có thể xóa nhầm bản đang dùng. | Đặc tả thuật toán `SemVerDuplicatePruneStrategy` (`research.md` R-06, `spec.md` Edge Cases, `T020`) bắt buộc đọc `extensions.json` làm allowlist + phân tích SemVer có hậu tố `platform` + chỉ dọn thư mục tạm `.uuid` mồ côi quá 24 giờ. |
| **G-06** | **HIGH** | `docs/02` §2 (`MultiThreadedDirectorySizer`) | **Nguy cơ kích hoạt tải ngược OneDrive (Cloud Hydration) và vòng lặp Junction NTFS**: Khi quét `%USERPROFILE%` hoặc `%LOCALAPPDATA%`, nếu bộ đếm dung lượng chạm vào tệp đám mây OneDrive (`RecallOnDataAccess`) hoặc Junction (`AppData\Local\Application Data`), quá trình quét sẽ tự tải dữ liệu từ mây về làm đầy cứng ổ `C:` hoặc lặp vô hạn. | Bắt buộc dùng `FileSystemEnumerable<long>` với cờ `AttributesToSkip = ReparsePoint \| Offline` kết hợp lọc cờ Win32 `RecallOnDataAccess` (`FR-001`, `research.md` R-02, `T016`, `T019`). |
| **G-07** | **HIGH** | `docs/02` §2 (`ElevatedAdminScriptRunner`) | **Rủi ro bảo mật nếu truyền chuỗi script tự do lên quyền Administrator (UAC)**: Thực thi chuỗi lệnh tùy ý qua UAC dễ gây lỗi cú pháp đường dẫn có khoảng trắng (như `C:\Users\HUU HIEU\...`) hoặc rủi ro tiêm lệnh (Command Injection). | Thiết kế giao thức IPC tham số hóa `nexusclean admin-exec --request <manifest.json>` (`contracts/domain-and-cli-contracts.md` §3, `T035`, `T037`), cấm hoàn toàn chuỗi lệnh tự do và khóa ngưỡng tối thiểu Shadow Copy `>= 2 GB`. |

---

## 3. Đánh Giá Tương Thích Môi Trường Máy Phát Triển Hiện Tại

Qua kiểm tra trực tiếp môi trường tại `D:\AI_Learning_Repository\NexusClean`:
- **SDK hiện có**: `.NET SDK 9.0.203` và `.NET SDK 10.0.400`.
- **Runtime hiện có**: `Microsoft.NETCore.App 8.0.30` và `Microsoft.WindowsDesktop.App 8.0.30` đã được cài sẵn.
- **Kết luận kỹ thuật**: Việc nhắm mục tiêu **`net8.0` / `net8.0-windows` (WPF MVVM)** theo đúng `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md` hoàn toàn biên dịch và chạy mượt trực tiếp bằng bộ SDK hiện tại mà không cần cài thêm bất kỳ thành phần phụ thuộc hay giấy phép thương mại nào.

---

## 4. Lộ Trình Thực Thi Khuyến Nghị Với GitHub Spec Kit

Toàn bộ kết quả phân tích trên đã được chuyển hóa thành hệ thống đặc tả chính thức trong thư mục [`specs/001-intelligent-disk-cleanup/`](../specs/001-intelligent-disk-cleanup/):

1. **Hiến pháp dự án (`Constitution`)**: [`.specify/memory/constitution.md`](../.specify/memory/constitution.md) — Khóa cứng 6 nguyên lý bất biến (4-Layer Clean Architecture, SOLID, Safety Guards, 5-Tier Impact & Relocation, Process-Lock & Isolated Admin, Sandbox Testing).
2. **Đặc tả nghiệp vụ (`Spec`)**: [`specs/001-intelligent-disk-cleanup/spec.md`](../specs/001-intelligent-disk-cleanup/spec.md) — 5 User Stories (`P1`–`P3`), 18 Functional Requirements (`FR-001`..`FR-018`), 7 Measurable Success Criteria (`SC-001`..`SC-007`).
3. **Kế hoạch & Thiết kế chi tiết (`Plan`)**: [`plan.md`](../specs/001-intelligent-disk-cleanup/plan.md), [`research.md`](../specs/001-intelligent-disk-cleanup/research.md), [`data-model.md`](../specs/001-intelligent-disk-cleanup/data-model.md), [`contracts/`](../specs/001-intelligent-disk-cleanup/contracts/), [`quickstart.md`](../specs/001-intelligent-disk-cleanup/quickstart.md).
4. **Danh sách công việc (`Tasks`)**: [`specs/001-intelligent-disk-cleanup/tasks.md`](../specs/001-intelligent-disk-cleanup/tasks.md) — **46 đầu việc (`T001`–`T046`)** chia làm 8 Phase rõ ràng, sẵn sàng kích hoạt bằng lệnh `/speckit-implement`.
