# Lộ trình toast & brand — ipt-mcp

**Ngày khảo sát:** 2026-09-25 · **Baseline:** `f12d522` + working tree tại thời điểm khảo sát.
**Trạng thái:** kế hoạch đề xuất, chưa triển khai. Chủ repo yêu cầu kiểm tra và viết lộ trình; không phải phê duyệt deploy hay thay đổi sản phẩm.

Mục tiêu: giữ toast Inventor đang hoạt động tốt, đồng bộ nhận diện/interaction với RVT, không đánh mất feed chống spam và kiến trúc Inventor-native.
Lộ trình đối ứng: [rvt-mcp](../../rvt-mcp/docs/toast-brand-roadmap.md) trong monorepo; khi đọc repo độc lập, xem `bimwright/rvt-mcp`, cùng đường dẫn `docs/toast-brand-roadmap.md`.

## 1. Kết quả kiểm tra — không làm lại phần đã đúng

### Brand effect

Nguồn: [ToastWindow.cs](../src/shared/Views/Toast/ToastWindow.cs), [ToastPalette.cs](../src/shared/Views/Toast/Model/ToastPalette.cs), [BrandAssets.cs](../src/shared/Views/BrandAssets.cs).

| Thành phần | IPT hiện tại | Đối chiếu RVT |
|---|---|---|
| Wordmark | `BIM` + `wright`, Segoe UI, 10 DIP, SemiBold | Cùng kiểu chữ chính; RVT còn có font fallback |
| Cấu trúc | Hai lớp chữ trùng vị trí: lớp brand + lớp glint sáng, mỗi lớp có opacity mask | Giống; đây là ánh sáng chạy **trong nét chữ**, không phải gạch sáng bên dưới |
| Alpha | Ban đầu 0.3 → đỉnh 1.0 → ổn định 0.8 | Giống; byte alpha thực tế 76 / 255 / 204 |
| Nhịp | Chờ 1300 ms, quét 800 ms, Quadratic EaseInOut | Giống; không lặp vô hạn |
| Hướng | RelativeTransform X từ -0.75 đến +0.75 | Giống, quét trái → phải |
| Brand mask | `(offset, alpha)`: `(0,.8), (.32,.8), (.44,1), (.58,.3), (1,.3)` | Giống |
| Shine mask | `(0,0), (.36,0), (.44,1), (.52,0), (1,0)` | Giống; hai đỉnh cùng nằm ở .44 |
| Hover | Chỉ pause lifetime, **không replay brand** | RVT gọi `WipeBrand(150)` mỗi MouseEnter |
| Retained-card update | `UpdateModel` đổi nội dung, không replay brand | Giữ nguyên: cập nhật feed không được gây nhấp nháy |
| Retheme | Đổi brush cả hai lớp, không replay | Giữ nguyên; dark dùng màu brand sáng hơn có chủ đích |
| Suppression | Pause lifetime nhưng **không pause animation brand** | WPF probe xác nhận brand vẫn chạy hết khi IPT ẩn |
| Nguồn chữ | Hard-code `BIM`, `wright`, tooltip ngay trong window | Chưa dùng `BrandAssets` dù file này đã tồn tại |

**Không nhầm `0.55` với độ sáng khác:** IPT dùng `Blend(brand, white, 0.55)` = 55% brand + 45% trắng; RVT dùng `Lighten(brand, 0.45)` = cùng công thức. Khác biệt nhỏ là làm tròn: RVT ép byte, IPT round-away-from-zero.

- Light glint RVT: `#7995B3` / `#A3C192`; IPT: `#7995B4` / `#A3C292` — chỉ lệch 1 ở vài kênh RGB, không phải lỗi nhịp/độ sáng.
- Dark IPT: brand `#5B9BD5` / `#86C55C`, glint `#A5C8E8` / `#BCDFA5`. Không ép dùng navy của light trên nền tối.
- Hiệu ứng **cả card** khác effect brand: IPT fade-in 160 ms, fade-out 200 ms; RVT slide + scale + fade-in 280 ms, fade-out 220 ms. Không đổi các giá trị này chỉ vì nhầm rằng brand chưa đồng bộ.

### Phần toast khác cần giữ hoặc cải thiện

- Đang có tối đa **3 retained cards**, refresh ≤2 lần/giây, gom hoạt động/lỗi trùng và ưu tiên kết quả quan trọng. RVT hiện vẫn là tối đa 4 toast riêng lẻ.
- Có `inventor_report_task_result`; không suy diễn “task hoàn thành” từ idle hoặc các lệnh thành công.
- Theme `auto/light/dark`, anchor theo vùng đồ họa; Home fallback dưới ribbon. Đây là khác biệt có chủ đích, không phải thiếu parity.
- Chưa có nút ×; category và duration nằm trên header thay vì bố cục RVT. Chuỗi toast còn tiếng Anh cố định; chưa có thông báo client-connected tương đương.
- Hover dùng **thời gian còn lại**; tốt hơn việc khởi động lại nguyên timeout. Giữ nguyên hành vi này.
- Cấu hình riêng: `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json`, `enableToast`, `toastTheme`; env `BIMWRIGHT_INVENTOR_ENABLE_TOAST`, `BIMWRIGHT_INVENTOR_TOAST_THEME`. Không đổi sang env/config của RVT.

## 2. Bằng chứng và giới hạn

- Audit source + WPF .NET 10 độc lập, compile trực tiếp hai production windows; không load Inventor/Revit, không deploy. HWND thử nghiệm trong suốt, ngoài màn hình, không activate.
- PASS: hai mask giống nhau; giữ offset -0.75 trong delay; quét đồng bộ; kết thúc +0.75; synthetic MouseEnter replay ở RVT nhưng không ở IPT; IPT update/retheme không replay; IPT animation hoàn tất khi bị hide và không replay khi restore.
- Probe màu xác nhận các mã RGB ở trên. Hover được kích bằng routed event, **không** chứng minh hit-testing/focus trong Inventor.
- Lần thử chưa tạo HWND không làm WPF animation clock tiến: đó là hạn chế harness, không phải lỗi sản phẩm. Kết quả timing chỉ lấy từ probe offscreen HWND sau đó.
- Harness/log cục bộ: `%TEMP%\toast-brand-audit-709f629bd45f44919904ddccf55169f9\` (`Program.cs`, `BrandAudit.csproj`, `results.txt`), không phải test đã commit.
- Đã chạy lại **161/161** test `Bimwright.Ipt.Toast.Tests` trong sandbox ở lượt kiểm tra trước cùng phiên. Suite này chỉ gồm model host-free, **không kiểm thử WPF effect**.
- Bằng chứng live cũ và các mục còn mở: [smart-toasts.md](testing/smart-toasts.md). Chưa xác nhận DLL đang nạp, burst 100-call, mixed-DPI hay hover/modal bằng Inventor live trong phiên này.

## 3. Hợp đồng đồng bộ đề xuất — `toast-brand-v1`

Hai repo triển khai độc lập cùng hợp đồng; không thêm ProjectReference, source-link hay package dùng chung.

1. Giữ 10 DIP SemiBold, hai lớp mask và toàn bộ timing/alpha ở §1. Một pass khi card xuất hiện lần đầu.
2. Hover vào card: replay sau **150 ms**, thời lượng vẫn **800 ms**, không tự loop. Clock mới thay clock cũ, không xếp hàng nhiều pass. Không replay khi card đã closing.
3. Update nội dung, số đếm, di chuyển/reflow và retheme **không** replay brand.
4. Giữ palette thích nghi của IPT; chấp nhận sai khác rounding ≤1 RGB so với RVT. Không chỉnh 0.55 thành 0.45 trong `Blend`.
5. Lấy cả hai lớp chữ và tooltip từ `BrandAssets`; đổi brand ở một chỗ phải đổi cả hai lớp.
6. Bổ sung ở P1: nếu hệ thống tắt animation, dùng brand ổn định alpha 0.8, không chạy wipe/glint; thông báo vẫn hoạt động bình thường.
7. Lifecycle ẩn/hiện ở P1: card chưa hiện không chạy trước; card đã hiện thì pause/resume phase khi host bị minimize/modal, không phát lại từ đầu. **Đây là thay đổi đề xuất**, không phải hành vi hiện có đã PASS.

## 4. Lộ trình thực hiện

### P0 — Brand parity nhỏ, có thể giao độc lập

**Phụ thuộc:** chốt hợp đồng §3 trước khi sửa. Không phụ thuộc refactor RVT.

- [ ] **IPT-01 — Test nền và tokens.** Thêm thông số brand host-free nội bộ (`Model/BrandMotion.cs`, tên đề xuất) và tests cho delay, duration, alpha, offsets. Thêm WPF regression executable riêng (tên đề xuất `tests/Bimwright.Ipt.Toast.Wpf.Tests`); không đưa WPF/Inventor vào suite model hiện tại.
- [ ] **IPT-02 — Single source of truth.** Thay các literal wordmark/tooltip trong `ToastWindow.cs` bằng `BrandAssets`. Giữ hai TextBlock cùng font, kích thước, vị trí để glint không lệch nét.
- [ ] **IPT-03 — Hover replay.** Cho `WipeBrand` nhận delay; MouseEnter vừa pause lifetime vừa gọi replay 150 ms, có guard closing/disposed. Giữ `ToastCountdown` và MouseLeave resume thời gian còn lại.
- [ ] **IPT-04 — Khóa anti-flicker.** Test actual window: update card, retheme, reflow không reset sweep; hover liên tục không tích lũy clock; đóng giữa sweep an toàn.

**Files chính:** `src/shared/Views/Toast/ToastWindow.cs`, `Model/BrandMotion.cs` (mới), `src/shared/Views/BrandAssets.cs` (chỉ đổi nếu cần), hai nhóm test kể trên.
**Exit:** effect đầu vào không thay đổi; hover có replay như RVT; mọi chuỗi brand lấy từ một nguồn; tests model + WPF pass; không thay feed/thread/transport trong P0.

### P1 — Hoàn thiện UX và lifecycle, từng lát nhỏ

Sau P0; các mục sau là commit/lát triển khai riêng, không gộp thành rewrite toast.

- [ ] **IPT-05 — Visibility và motion.** Kiểm thử rồi thêm pause/resume animation khi suppression; lifetime và brand phải quản lý riêng, không dùng một timer thay cả hai. Xử lý hover/modal còn mở trong `smart-toasts.md`. Đọc setting animation của Windows, settle ngay khi disabled. Retheme giữa sweep phải đổi cả hai lớp cùng lúc.
- [ ] **IPT-06 — Nút đóng + bố cục.** Thêm × có tooltip/accessible name; click × chỉ đóng, không mở ảnh qua event bubbling. Tách category khỏi title, đưa duration về footer cạnh brand theo RVT. Giới hạn wrap/height cho nội dung dài. Giữ anchor, outline, shadow và theme Inventor; không ép giống pixel toàn card nếu làm giảm tương phản.
- [ ] **IPT-07 — Bản địa hóa.** Thiết kế catalog/fallback nội bộ cho toast, ribbon toggle và Status; không kéo dependency RVT. Có test placeholder, thiếu key fallback và chuỗi dài. Chọn locale được hỗ trợ trước khi dịch; không tự nhận đã parity toàn bộ localization RVT.
- [ ] **IPT-08 — Báo kết nối.** `ITransportServer` có `IsClientConnected` nhưng chưa có hook toast một-lần. Thiết kế tín hiệu **kết nối đã xác thực**, một card mỗi session/reconnect, không mỗi command/health. Không hiển thị auth token, pipe secret hay dữ liệu client. Snapshot host phải có từ STA; không đọc Inventor COM trên listener/toast thread. Card kết nối không chiếm mất lỗi/task result đang quan trọng; tôn trọng toggle và suppression.
- [ ] **IPT-09 — Cập nhật tài liệu.** Đồng bộ README, `docs/roadmap.md`, `docs/testing/smart-toasts.md`: phân biệt thông tin lịch sử 4-card với feed hiện tại 3-card; ghi chính xác build/live/deployed, không lấy test model thay evidence UI.

**Files liên quan:** `ToastWindow.cs`, `ToastHost.cs`, `ToastNotifier.cs`, `Model/ToastCountdown.cs`, `Model/ToastFeed.cs`, `Model/ToastContent.cs`, `Plugin/InventorAddInServerBase.cs`, `Plugin/BimwrightRibbon.cs`, `Transport/*` nếu thêm tín hiệu authenticated session.
**Exit:** các interaction đúng khi có ảnh, modal, hover, retheme và toggle; không tăng spam, không làm chậm/đổi kết quả MCP; code-only WPF và dedicated STA/unowned/no-activate được giữ.

### P2 — Chốt chất lượng, không mở rộng API vô cớ

- [ ] **IPT-10 — Burst/soak.** 100 read calls; vẫn ≤3 card, không phát lại hàng chục toast sau khi lệnh trả xong; brand không quét lại ở từng revision. Kiểm tra thêm errors lặp, snapshot và task summaries cạnh tranh slot.
- [ ] **IPT-11 — Ma trận runtime.** Build cả 2022–2027; live theo ba runtime tier: 2024 (.NET Framework), 2025 hoặc 2026 (.NET 8), 2027 (.NET 10). Năm chưa có host phải ghi compile-only/not tested. Giữ các mục mixed-DPI, detached view, invisible host và load context là chưa đạt cho đến khi có bằng chứng.
- [ ] **IPT-12 — Release gate.** Review source → unit/WPF → compile → xin phép deploy khi Inventor đã đóng → mở lại kiểm tra build identity → live acceptance → cập nhật evidence. Không publish/release tự động từ roadmap.

## 5. Ma trận nghiệm thu tối thiểu

| Tình huống | Kết quả cần đạt |
|---|---|
| Card mới, light/dark | Mờ → đỉnh sáng → ổn định; không loop; hai lớp trùng nét |
| Hover sau settle / trong sweep / enter-leave nhanh | Replay 150/800 ms; không queue clock, không crash, không kéo dài lifetime nguyên 3–9s |
| Update cùng retained card | Nội dung/count đổi nhưng không tạo HWND hoặc replay brand |
| Retheme giữa sweep | Đổi cả base/glint, giữ phase, không lấy ảnh toast làm backdrop |
| Minimize/modal trước và trong sweep | P1: pause/resume visible-time phase, không chạy sớm hoặc restart khi restore |
| Motion disabled | Brand ổn định, không animation; nội dung vẫn đọc được |
| Capture + click × / click ảnh | × chỉ đóng; click ảnh mới mở file hợp lệ |
| 100% / 150% / 200% DPI | Không tách hai lớp chữ, không clip brand/×; không đổi DPI awareness toàn host |
| Toggle off, close, Deactivate giữa sweep | Không còn window/timer hoạt động của card; MCP response không đổi |

## 6. Kiểm thử an toàn và hand-off

- Test file-write/delete/replace chỉ chạy trong sandbox, redirect `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `TEMP`, `TMP` trước khi start process; assert API nhận fixture path tồn tại. Kiểm tra cả đường dẫn thực tế mà `.NET Environment.GetFolderPath` trả về; nếu vẫn trỏ profile thật thì dừng, không giả định env redirect là đủ.
- Build artifacts/cache ghi vào sandbox khi audit; không dùng installer/uninstaller hay xóa profile để tạo trạng thái test. Không reset ribbon của người dùng để thử nghiệm.
- `dotnet test tests/Bimwright.Ipt.Toast.Tests -c Debug` là suite model; `dotnet test tests/Bimwright.Ipt.Tests -c Debug` gồm source-policy/server. Chỉ chạy sau khi dựng sandbox phù hợp. Các source-policy tests tự tìm repo bằng ancestor của output: nếu chuyển output ra temp, dùng sandbox copy của source hoặc một repo-root seam đã kiểm chứng, không bỏ test chỉ vì discovery thất bại.
- WPF suite mới phải được chạy bằng executable trên Windows/STA và ghi riêng kết quả; test actual production window, không chỉ dò chuỗi source.
- Lưu evidence: commit/source hash, DLL/build identity, năm host, DPI/theme, các mốc trước quét/giữa quét/sau quét/hover, kết quả burst và focus. Chỉ capture vùng fixture/brand hoặc model dùng thử; không đưa màn hình/tên tài liệu thật vào repo.
- Mỗi lát có thể review/revert riêng bằng thay đổi source có chủ đích; deploy lại bản DLL trước chỉ khi owner cho phép và host đã đóng. **Không uninstall để rollback; không đụng settings, logs, ToolBaker hay captures của người dùng.**

**Thứ tự đề xuất:** IPT-01 → 02 → 03 → 04; nghiệm thu P0 trước, rồi 05–09; cuối cùng 10–12. Có thể dừng sau P0 nếu mục tiêu trước mắt chỉ là đồng bộ effect brand.
