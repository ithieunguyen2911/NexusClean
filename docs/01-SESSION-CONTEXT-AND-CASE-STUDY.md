# Nhật Ký Ngữ Cảnh & Case Study Thực Tế Khởi Nguồn Dự Án NexusClean

- **Dự án đích**: `NexusClean` (`D:\AI_Learning_Repository\NexusClean`)
- **Mã phiên hội thoại gốc trên Antigravity**: `aa018769-2743-40f1-abb2-df18982169b8`
- **Ngày tổng hợp**: 15/11/2026

---

## 1. Bối Cảnh Bài Toán Thực Tế

Dự án **NexusClean** ra đời từ nhu cầu thực tế khi xử lý tình trạng ổ đĩa hệ thống `C:` (`274.52 GB`) bị đầy `98%` (chỉ còn `5.81 GB` trống) và liên tục bị đầy trở lại dù người dùng đã xóa thủ công nhiều lần trong các thư mục thông thường (`Downloads`, `Desktop`, `Documents`).

### 1.1. Phát hiện cốt lõi từ quá trình kiểm toán 100% ổ `C:`
1. **Dữ liệu hiển thị thông thường rất nhỏ**: Tổng `Desktop` + `Downloads` + `Documents` chỉ chiếm **4.72 GB**.
2. **Hơn 142 GB nằm ẩn trong các thư mục hệ thống & ứng dụng**:
   - `AppData\Local` (**53.25 GB**): Cache trình duyệt Chrome/Edge (`9.28 GB`), ứng dụng Codex (`5.74 GB`), máy ảo WSL `ext4.vhdx` (`5.38 GB`), trình duyệt test `ms-playwright` (`3.73 GB`), `%TEMP%` (`3.04 GB`), cache gói lập trình `uv`/`npm`/`pnpm`/`NuGet`/`pip` (`3.02 GB`), bộ cài cập nhật Electron `*-updater` (`1.87 GB`).
   - `AppData\Roaming` (**32.51 GB**): Kho ảnh/video tự tải của `ZaloData\media` (`9.19 GB` / `10.18 GB`), cache offline `Notion\Partitions` (`9.94 GB`), gói cài cũ `Code\CachedExtensionVSIXs` (`2.81 GB`).
   - Các thư mục `.dot` ẩn tại `%USERPROFILE%` (**21.94 GB**): `.vscode\extensions` (`6.87 GB` chứa nhiều phiên bản extension cũ bị lưu trùng và thư mục tạm lỗi `.014fc366-...`), `.gemini`, `.codex`, `.cache`, `.rustup`, `.nuget`.
   - Gốc ổ `C:\`: Bộ cài cũ `C:\Drivers` (`6.68 GB`), `pagefile.sys` (`17.55 GB`), `hiberfil.sys` (`4.69 GB`), và `System Volume Information` (`~12.76 GB` chứa 7 bản Shadow Copy).

### 1.2. Bốn cơ chế khiến ổ đĩa tự đầy lại sau khi xóa thủ công
1. **Shadow Copy (System Restore)** giữ lại các block dữ liệu vừa xóa theo cơ chế *Copy-on-Write*.
2. **Bộ nhớ ảo `pagefile.sys`** tự phình to chiếm phần dung lượng vừa giải phóng khi nhiều tiến trình ngầm ngốn RAM.
3. **Ứng dụng nhắn tin & AI Agent ghi liên tục** (`ZaloData\media`, `%TEMP%`, `.codex`, `.gemini`).
4. **Auto-Updater & IDE lưu trùng phiên bản cũ** (`CachedExtensionVSIXs`, `*-updater`, nhiều bản extension song song).

---

## 2. Kết Quả Thực Nghiệm & Quy Tắc An Toàn Cốt Lõi Cho NexusClean

Từ phiên dọn dẹp thực tế đã giải phóng trực tiếp **+36.75 GB** (nâng dung lượng trống ổ `C:` từ `5.81 GB` ban đầu lên **48.76 GB**, và lên trên **60 GB** sau khi thu gọn WSL + ShadowStorage), **NexusClean** bắt buộc tuân thủ 5 nguyên tắc vàng:

1. **Cấm tuyệt đối xóa thư mục gốc `AppData\Local` hoặc `AppData\Roaming`**: Phải khóa cứng bằng `SafetyGuard` vì chứa phần mềm đang cài đặt và dữ liệu tài khoản.
2. **Bảo toàn 100% Profile Trình duyệt (`ProtectedProfileGuard`)**: Khi dọn Chrome/Edge/Brave/Cốc Cốc, chỉ được phép xóa các thư mục đệm con (`Cache\Cache_Data`, `Code Cache`, `GPUCache`, `Service Worker\CacheStorage`); tuyệt đối không đụng vào `Login Data`, `Cookies`, `Bookmarks`, `Preferences`, `History`, `Extensions`.
3. **Phân tích Mức độ Ảnh hưởng (Impact Analysis) trước khi xóa**: Mọi mục phát hiện được đều phải trả lời rõ ràng cho người dùng: *"Xóa mục này có ảnh hưởng gì đến máy không? Cái gì mất, cái gì được giữ nguyên?"*
4. **Phân biệt giữa "Nên Xóa" và "Nên Di Chuyển sang ổ D:"**: Với dữ liệu người dùng sinh trưởng hằng ngày như `ZaloData\media` hoặc `pagefile.sys`, phần mềm phải khuyến nghị **Relocate (Di chuyển sang ổ phụ)** thay vì xóa mù quáng.
5. **Nhận diện Khóa Tiến trình & Quyền Administrator**: Phát hiện trước các tệp đang bị khóa bởi dịch vụ hệ thống (như `vmmemWSL` / `WSLService` giữ khóa `ext4.vhdx`, hoặc `System Volume Information` cần UAC Admin) để gom vào luồng thực thi quyền cao riêng biệt thay vì treo ứng dụng.

---

## 3. Danh Mục Tài Liệu Đính Kèm Trong Dự Án

- `docs/01-SESSION-CONTEXT-AND-CASE-STUDY.md` *(Tệp này)*: Ngữ cảnh bài toán thực tế, số liệu chẩn đoán và các quy tắc nghiệp vụ cốt lõi.
- `docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md`: Bản thiết kế kiến trúc hướng đối tượng (Clean Architecture 4 tầng, SOLID, Design Patterns, UML Class Diagram, C#/.NET 8 Interfaces).
- `docs/03-SEED-SIGNATURE-RULES.json`: Kho quy tắc nhận diện (Data-Driven Rules) khởi tạo sẵn cho Engine của **NexusClean**.
