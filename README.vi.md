<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phiên-bản-inventor-được-hỗ-trợ"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#bề-mặt-công-cụ"><img src="https://img.shields.io/badge/MCP-84%20or%2088%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · Tiếng Việt · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a>
</p>

---

`ipt-mcp` là một cổng [Model Context Protocol](https://modelcontextprotocol.io) mã nguồn mở ([Apache-2.0](LICENSE)) cho phép Claude Code — và mọi client hỗ trợ MCP — điều khiển **Autodesk Inventor 2022-2027** ngay tại máy local.

Agent nói chuyện bằng MCP qua stdio. Server nói chuyện bằng NDJSON qua một kênh truyền local có xác thực (TCP hoặc Named Pipe) tới add-in Inventor chạy trong tiến trình theo từng phiên bản. Add-in đẩy mọi lệnh lên luồng STA của Inventor và làm việc với Inventor API.

Model của bạn vẫn nằm trên máy bạn.

---

## ipt-mcp là gì

Hai tiến trình, một kênh local:

- **`Bimwright.Ipt.Server.exe`** — MCP stdio server .NET 8, được Claude Code, Cursor, Cline, Codex hoặc MCP client khác launch qua stdio. Nó **không tham chiếu Inventor**; chỉ compile các file contract API-agnostic, nên build và chạy được trên bất kỳ máy nào có .NET 8 SDK.
- **`Bimwright.Ipt.Plugin.InvNN.dll`** — add-in `ApplicationAddInServer` load bên trong `Inventor.exe`, chạy listener TCP hoặc Named Pipe, và thực thi lệnh trên luồng UI (STA) chính của Inventor. Mỗi năm Inventor có một shell mỏng riêng, tất cả compile từ cùng source glob `src/shared/**`.

Khác với Revit, Inventor **không có** thứ tương đương `ExternalEvent`. Add-in đẩy công việc lên luồng STA thông qua một WinForms control message-only ẩn (`InventorStaDispatcher`). Xem [ARCHITECTURE.md](ARCHITECTURE.md) để biết thiết kế đầy đủ.

---

## Phiên bản Inventor được hỗ trợ

| Inventor | Target framework | Kênh truyền | Ghi chú |
|----------|------------------|-------------|---------|
| 2022 | `net48` (.NET Framework 4.8) | TCP | tham chiếu `System.Windows.Forms` trực tiếp |
| 2023 | `net48` (.NET Framework 4.8) | TCP | |
| 2024 | `net48` (.NET Framework 4.8) | TCP | |
| 2025 | `net8.0-windows7.0` | Named Pipe | `UseWindowsForms`, `EnableDynamicLoading` |
| 2026 | `net8.0-windows7.0` | Named Pipe | |
| 2027 | `net10.0-windows7.0` | Named Pipe | cần .NET 10 SDK; tôn trọng `UseInventorAssemblyContext` |

- MCP server là một tiến trình, **không phụ thuộc phiên bản Inventor** — nó chỉ forward các JSON envelope.
- TCP cho 2022-2024 (add-in net48); Named Pipe cho 2025-2027 — Named Pipe tránh prompt loopback-firewall trên Windows hiện đại.
- Inventor chuyển add-in desktop khỏi .NET Framework từ 2025: **.NET 8 cho 2025/2026, .NET 10 cho 2027**. (Add-in .NET 8 vẫn binary-compatible trên 2027, nhưng net10 là target native.)
- Dùng **năm dương lịch 4 chữ số** (2022..2027) ở mọi nơi — không dùng version code cũ.

> **Trạng thái: đã verify.** Giai đoạn 1-3 đã xong và green (84 MCP tools mặc định, hoặc 88 với send_code; server + tests build mà không cần Inventor), và phần thân handler Inventor-API đã được chạy thử trên một session Inventor thật. Như mọi khi, hãy test trên template của bạn trước khi tin dùng cho production model.

---

## Cài đặt / Wire MCP client

Tải [GitHub Releases](https://github.com/bimwright/ipt-mcp/releases/latest) (`IptMcp.Setup-*-win-x64.zip`). v0.1.0 gồm Inventor **2025** và **2027**. `install.ps1` trong ZIP; trỏ MCP vào `ipt-mcp.exe`. Không `dotnet tool install -g Bimwright.Ipt.Server`.

Discovery: `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`. `inventor_send_code` cần opt-in hai phía — xem [An toàn](#an-toàn).

---

## Build & phát triển

Binaries của Autodesk Inventor và Inventor SDK **không được phân phối lại** trong repo này (xem [Không phân phối lại](#không-phân-phối-lại)). Build **server và tests** chỉ cần .NET 8 SDK; build **add-in theo từng phiên bản** cần SDK tương ứng cùng với interop reference assembly của Inventor.

```bash
# Server + tests (chỉ server; KHÔNG cần Inventor — chạy trên mọi máy có .NET 8 SDK):
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# Kiểm tra tương thích TFM cũ dùng interop reference 2027 đã cài:
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"
dotnet build src/plugin-inv27 -c Debug   # compile interop 2027 thật; cần .NET 10 SDK

# Add-in theo từng phiên bản luôn cần một interop reference của Inventor. Với kiểm tra tương thích
# TFM cũ, trỏ InventorInteropDir vào một interop tương thích đã cài; bản release thật dùng đường
# dẫn mặc định của năm tương ứng.
```

- **Server** explicit-include chỉ `shared/Contracts/*` + `shared/Security/*` (+ ToolBaker), nên compile được khi không có Inventor SDK.
- Mỗi **add-in** dùng `<Compile Include="..\shared\**\*.cs" />` để kéo mọi thứ, kể cả `Infrastructure`/`Plugin`/`Handlers` chạm vào API.
- Đường dẫn interop mặc định là `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`.
- Build add-in 2027 cần cài **.NET 10 SDK**.
- **Đóng Inventor trước khi deploy add-in DLL** vì Inventor sẽ lock DLL đã load.

---

## Bề mặt công cụ

Toàn bộ surface là **84 công cụ** khi bật mọi platform toolset mặc định, hoặc **88 công cụ** khi bật toolset send_code (opt-in: `inventor_send_code` + 3 tool code module). Mọi tên MCP đều có prefix `inventor_`. Các tool được nhóm theo toolset class; `--toolsets sketch,feature` và `--read-only` kiểm soát tool nào được đăng ký để agent yếu không nhìn thấy tool đã tắt.

**Chọn document & occurrence.** Các tool cấp document nhận `document` tuỳ chọn (đường dẫn hoặc tên của document Inventor đang giữ trong bộ nhớ — cả part được assembly tham chiếu); không bao giờ tự mở/activate. Các tool assembly theo lô dùng chung selector `{names?: [glob], regex?, file?, path_contains?, leaf?, max_depth?, include_suppressed?, limit?}`; không khớp hoặc vượt `limit` là lỗi kèm gợi ý tên gần giống. Save/open/close/export chạy dưới `SilentOperation` mặc định để dialog ẩn không làm treo call; nếu vẫn timeout, `inventor_health` báo `modal_dialog {open, title}`.

Toolsets bật mặc định: `meta`, `query`, `document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `assembly_query`, `toolbaker`, `toolbaker_write`.
Tắt mặc định: `code` (escape hatch `send_code` — chỉ bật khi opt-in).

Mọi input độ dài tính bằng **mm**, góc tính bằng **độ**; add-in tự chuyển sang centimét/radian nội bộ của Inventor.

### meta (3) — tool target phía server, không round-trip tới add-in; vẫn hiện dưới `--read-only`

| Tool | Mô tả |
|---|---|
| `inventor_list_available_targets` | List các target add-in Inventor đang sống (năm, pid, transport, document đang hoạt động). |
| `inventor_get_current_target` | Báo target đang được server chọn, hoặc `NO_TARGET` nếu không có target sống. |
| `inventor_switch_target` | Chọn target theo descriptor id, năm hoặc session. Chỉ phía server. |

### query (7) — probe document/health/model read-only và báo kết quả công việc

| Tool | Mô tả |
|---|---|
| `inventor_health` | Probe add-in đang hoạt động: inventor_year, process_id, có document mở không, loại document. |
| `inventor_report_task_result` | Agent chủ động báo kết quả: `task_id`, `outcome` (`completed`/`failed`/`cancelled`), `summary` một dòng. Không sửa model. |
| `inventor_list_open_documents` | List mọi document đang mở: title, path, type, và cái nào active. |
| `inventor_get_document_info` | Lấy title, full path và document type của document đang active. |
| `inventor_list_bodies` | List các solid body của part: id (`body:N`), name, volume_mm3, bbox_mm, face_count, feature tạo ra (created_by), visible. |
| `inventor_list_features` | List các feature của part theo thứ tự tree: name, type, health, suppressed, body_names. |
| `inventor_probe_brep` | Survey B-rep của part tìm miệng port: mặt phẳng có cạnh tròn inner-loop — normal (đã hiệu chỉnh IsParamReversed), center_mm, port_diameter_mm, mọi đường tròn trên mặt. |

### document (10) — vòng đời document (write)

| Tool | Mô tả |
|---|---|
| `inventor_new_part` | Tạo document part mới (.ipt); template path tùy chọn. |
| `inventor_new_assembly` | Tạo document assembly mới (.iam); template path tùy chọn. |
| `inventor_open_document` | Mở document có sẵn từ full path và đặt làm active. |
| `inventor_save_document` | Save document active, hoặc Save-As tới một path. |
| `inventor_close_document` | Đóng document active; `save=true` lưu trước. |
| `inventor_set_units` | Đặt đơn vị độ dài của document (mm, cm, m, in, ft). |
| `inventor_set_material` | Gán material cho part active theo tên. |
| `inventor_save_all` | Update document gốc rồi lưu cùng mọi document tham chiếu đang dirty, chạy silent; báo từng file saved / read_only / error; có `dry_run`. |
| `inventor_open_documents` | Mở nhiều document trong một call (mặc định không mở cửa sổ, để sửa qua tham số `document`). |
| `inventor_close_documents` | Đóng document theo path/tên, hoặc mọi document đang hiển thị (`keep_active`); tuỳ chọn lưu trước. |

### parameters (4) — model & user parameters (write)

| Tool | Mô tả |
|---|---|
| `inventor_list_parameters` | List parameter (model + user): name, expression, value, unit, kind. |
| `inventor_get_parameter` | Lấy một parameter theo tên: expression, value, unit. |
| `inventor_set_parameter` | Set expression/value của parameter có sẵn, rồi update document. |
| `inventor_create_parameter` | Tạo user parameter mới (name, expression, unit). |

### properties (4) — iProperties & mass properties (write)

| Tool | Mô tả |
|---|---|
| `inventor_get_iproperty` | Lấy giá trị iProperty theo property-set và tên property. |
| `inventor_set_iproperty` | Set giá trị iProperty. |
| `inventor_get_mass_properties` | Khối lượng (g), thể tích (mm³), diện tích bề mặt (mm²), trọng tâm, bounding box. |
| `inventor_list_iproperty_sets` | Liệt kê các iProperty set (name, internal_name, tên property; tùy chọn value) — để khám phá set_name/prop_name cho get/set_iproperty. |

### sketch (10) — geometry & constraint sketch 2D (write)

| Tool | Mô tả |
|---|---|
| `inventor_create_sketch` | Tạo sketch 2D trên một mặt phẳng (XY/XZ/YZ hoặc tham chiếu face/work-plane). |
| `inventor_project_geometry` | Project edge/vertex model (theo edge id) vào sketch active. |
| `inventor_draw_line` | Vẽ line sketch từ (x1,y1) tới (x2,y2). |
| `inventor_draw_circle` | Vẽ circle sketch từ tâm + bán kính. |
| `inventor_draw_rectangle` | Vẽ rectangle sketch hai điểm. |
| `inventor_draw_arc` | Vẽ arc sketch (tâm, bán kính, góc bắt đầu/kết thúc). |
| `inventor_add_sketch_dimension` | Thêm dimension constraint điều khiển một sketch entity. |
| `inventor_add_sketch_constraint` | Thêm geometric constraint (coincident, parallel, tangent, …). |
| `inventor_draw_text` | Thêm text box fitted (position mm, tùy chọn font_size_mm; rotation_deg theo bội số 90). |
| `inventor_close_sketch` | Kết thúc chỉnh sketch (thoát chế độ edit sketch). |

### feature (16) — solid & work feature (write)

| Tool | Mô tả |
|---|---|
| `inventor_extrude` | Extrude một sketch theo tên (distance, join/cut/intersect, direction). |
| `inventor_revolve` | Revolve một sketch quanh trục (góc, operation). |
| `inventor_combine` | Boolean các solid body (base + tool bodies, join/cut/intersect, keep_tool_bodies). |
| `inventor_batch_execute` | Chạy tới 20 wire command trong một transaction (một undo, rollback khi lỗi). |
| `inventor_fillet` | Thêm fillet cạnh bán kính cố định — `edgeIds` hoặc selector `edges` `{kind:circular, radius_mm?, center_mm?, on_body?, adjacent_surface_types?}`; trả `matched_edges`. |
| `inventor_chamfer` | Thêm chamfer cạnh khoảng cách đều trên các edge model. |
| `inventor_create_work_plane` | Tạo work plane (offset, three_points, tangent hoặc fixed origin+axes). |
| `inventor_create_work_axis` | Tạo work axis (two_points, edge, plane_intersection, normal_to_face_through_point). |
| `inventor_create_work_point` | Tạo work point cố định tại {x,y,z}/[x,y,z] mm; construction point không đặt tên được (name_applied báo lại). |
| `inventor_hole` | Lỗ drilled/counterbore/countersink trên một planar face được chọn xác định; tùy chọn metadata luồng tapped-thread. |
| `inventor_circular_pattern` | Circular-pattern các part feature quanh một trục theo tên (count trên một góc). |
| `inventor_rectangular_pattern` | Rectangular-pattern các part feature dọc theo một hoặc hai trục theo tên. |
| `inventor_loft` | Loft một danh sách sketch profile có thứ tự ('SketchName' hoặc 'SketchName:N'), tùy chọn sketch centerline, closed/merge-tangent-faces. |
| `inventor_sweep` | Sweep một sketch profile dọc theo một sketch path (các đoạn liền kề tự nối chuỗi); orientation normal_to_path\|parallel. |
| `inventor_create_bim_connector` | Author một BIM pipe connector trên một circular port edge (ref từ `circles[].edge` của inventor_probe_brep); kind=pipe, tùy chọn metadata system/flow/connection. |
| `inventor_create_part` | Dựng cả part từ recipe JSON trong một call: sketch (rect / circle / polyline có cung bulge, vòng rỗng, trên plane gốc hoặc plane cố định) → extrude / hole / fillet / chamfer → material + iProperty → Save-As silent. Lỗi chỉ đúng đường dẫn trong recipe; hỏng thì part bị đóng không lưu; có `dry_run`. |

### export (10) — capture view & export geometry (write)

> `output_path` phải nằm dưới root được phép: user profile, `%TEMP%`, hoặc root bạn thêm — ví dụ đặt `BIMWRIGHT_INVENTOR_EXPORT_ROOT=D:\Inventor-Exports` trên máy chạy Inventor (rồi khởi động lại Inventor và cả MCP client/server session).

| Tool | Mô tả |
|---|---|
| `inventor_capture_view` | Capture view active ra file PNG (dưới `<export-root>\captures\` hoặc `output_path`); `inline=true` trả PNG base64 có giới hạn. |
| `inventor_export_step` | Export part/assembly active sang STEP (.stp/.step). |
| `inventor_export_stl` | Export part/assembly active sang STL (.stl). |
| `inventor_export_sat` | Export part/assembly sang ACIS SAT (.sat) — định dạng interop cho Revit; acis_version mặc định 7 (giá trị duy nhất được hỗ trợ). |
| `inventor_export_dxf` | Export DXF 2D; phải khai báo source (`sketch` hoặc `flat_pattern`). |
| `inventor_derive_envelope` | Tạo derived part từ part/assembly nguồn — đường envelope/interop: `derive_style`, chọn solid với `include_bodies`, chế độ gọn `bounding_box`; lưu .ipt dưới root được phép. |
| `inventor_view_fit` | Zoom-fit view active vào model extents (chạy trước khi capture). |
| `inventor_set_view_orientation` | Đặt một camera orientation chuẩn (iso/front/top/…) cho multi-angle capture. |
| `inventor_set_camera` | Đặt camera tường minh (eye/target mm, up, perspective, extents_mm, fit) — dùng trước capture_view khi orientation chuẩn không khớp. |
| `inventor_set_view_state` | Kích hoạt / tạo design view, bật tắt object visibility (work feature, sketch, …) và ẩn/hiện occurrence theo selector. `inventor_capture_view` nhận cùng các khoá này, cộng orientation/camera/fit và `shots` để chụp nhiều góc trong một call. |

> Đường dẫn export phải là absolute và nằm dưới một output root được phép (user profile hoặc temp).

### assembly (8, write) — dựng và sửa assembly

| Tool | Mô tả |
|---|---|
| `inventor_place_occurrence` | Đặt một component (.ipt/.iam) vào assembly active; tùy chọn pose ban đầu + grounded. |
| `inventor_add_constraint` | Constrain hai ref theo tên (mate/flush/insert/angle); response mang `health` — luôn kiểm tra. |
| `inventor_create_imate` | Author một iMate theo tên trên part active dùng một face selector xác định. |
| `inventor_place_occurrences` | Đặt nhiều component trong một bước undo; pose = origin+rotation, trục hoặc ma trận 4×4; `lock` = none / grounded / workplanes (3 fixed work plane ẩn flush với origin plane của part). |
| `inventor_delete_occurrences` | Xoá các occurrence cấp top khớp selector (kèm lock plane); có `dry_run`. |
| `inventor_set_occurrence_state` | Đặt visible / suppressed / grounded / pose / lock cho mọi occurrence khớp selector trong một bước undo. |
| `inventor_set_appearance` | Tô màu RGB hoặc gán appearance thư viện cho occurrence (selector) hoặc body của part. |
| `inventor_reset_appearance` | Gỡ appearance override. |

### assembly_query (6, read-only) — bộ pin self-check số; sống sót dưới `--read-only`

| Tool | Mô tả |
|---|---|
| `inventor_list_interfaces` | List các named interface (iMates, work feature, origin geometry) của doc hoặc một occurrence. |
| `inventor_check_interference` | Chạy interference analysis; trả về pair count và total/per-pair volume. |
| `inventor_measure_min_distance` | Khoảng cách 3D nhỏ nhất (mm) giữa hai occurrence hoặc named ref. |
| `inventor_get_assembly_bom` | BOM + occurrence tree với grounded flag và translation/rotation degrees of freedom. |
| `inventor_list_constraints` | Đọc lại mọi constraint với type, `health`, suppressed flag và hai occurrence name. |
| `inventor_list_occurrences` | Liệt kê occurrence theo selector với cột tuỳ chọn (path, file, bbox_mm, transform, visibility, material, appearance, mass, volume); inline hoặc ra file. `check_interference` nhận `set_a` × `set_b`; `measure_min_distance` nhận `pairs[]` hoặc tập × tập + `threshold_mm`. |

### code (4) — escape hatch opt-in (TẮT mặc định)

| Tool | Mô tả |
|---|---|
| `inventor_send_code` | **Nguy hiểm, chỉ opt-in.** Thực thi đoạn C# in-process trên `Inventor.Application`. Tắt trừ khi cả server và add-in đều opt-in (nếu không trả `SEND_CODE_DISABLED`); API bị cấm (file/process/network/environment) bị reject. |
| `inventor_save_code_module` | Lưu module C# helper dùng lại (chỉ khai báo); kiểm policy + dry-compile trong add-in trước khi lưu; có `requires`. |
| `inventor_list_code_modules` | Liệt kê module đã lưu: hash, mô tả, chữ ký hàm. |
| `inventor_delete_code_module` | Xoá module (bị từ chối nếu module khác đang require). |

### toolbaker (3, read-only) — thao tác hoàn toàn trên bake database phía server

| Tool | Mô tả |
|---|---|
| `inventor_list_baked_tools` | List mọi baked tool đã verify, compile, register. |
| `inventor_list_bake_suggestions` | List các ToolBaker suggestion active từ workflow lặp lại. |
| `inventor_create_bake_issue_draft` | Tạo GitHub issue draft cho một suggestion (không submit). |

### toolbaker_write (3, write) — chạy baked tool và quản lý vòng đời suggestion

| Tool | Mô tả |
|---|---|
| `inventor_run_baked_tool` | Thực thi baked tool đã register theo tên với JSON parameter. |
| `inventor_accept_bake_suggestion` | Accept suggestion: validate + compile + apply + persist thành baked tool. |
| `inventor_dismiss_bake_suggestion` | Dismiss hoặc snooze một suggestion active. |

---

## Thông báo toast

Khi Inventor hiển thị, kết quả tool cập nhật vào **một thẻ hoạt động nhỏ gọn**, hiển thị tool gần nhất và bộ đếm **Success / Failed / Capture** với hiệu ứng cuộn số. Capture là tập con của Success, bao gồm `inventor_capture_view` với `inline=true`, không cộng thêm vào tổng thao tác. Failed bao gồm soft-fail và batch rollback. Read và write đều dùng màu nhấn xanh dương; khi đã ghi nhận lỗi, màu nhấn của thẻ giữ màu đỏ. Bố cục ổn định khi số thay đổi, không có thẻ thumbnail, chồng thẻ theo ưu tiên hay hàng đợi toast từng tool. Bộ đếm chỉ tính trong vòng đời thẻ trên target, không đại diện cho toàn bộ công việc hoặc riêng từng MCP client. `inventor_health` không tạo toast. **Agent connected** chỉ là thông báo trạng thái, không tăng bộ đếm và không thay thẻ hoạt động hoặc báo cáo đang giữ.

Thẻ hoạt động tự mất **20 giây sau kết quả cuối**. Hover thực sự, nhận biết qua chuyển động con trỏ, tạm dừng thời hạn; khi con trỏ rời thẻ, thời hạn bắt đầu lại đủ 20 giây. Thẻ xuất hiện dưới con trỏ đứng yên không được tính là hover. Click thẻ mở **History** và đóng thẻ; **×** chỉ đóng thẻ. Footer luôn hiển thị **năm Inventor**, bất kể branding bật hay tắt.

Agent báo kết quả công việc bằng `inventor_report_task_result`: `task_id` riêng cho agent/công việc (1–80 ký tự), `outcome` (`completed`, `failed`, `cancelled`), `summary` trung thực một dòng (1–120 ký tự). Báo cáo **thay vào cùng vị trí thẻ**, ghi **Agent reported**, có thời hạn **8 giây**, tạm dừng khi hover thực sự và bắt đầu lại khi rời thẻ. Báo cáo không tăng bộ đếm; kết quả tool tiếp theo bắt đầu thẻ hoạt động mới. Không suy đoán hoàn thành từ khoảng im lặng hoặc một tool thành công. Lệnh báo cáo không đi qua hàng đợi lệnh Inventor và không gọi Inventor API, nên vẫn nhận được khi `send_code` đang giữ luồng chính. `toast_shown` cho biết thẻ đã được giữ để hiển thị, kể cả khi đang chờ Inventor hết minimize hoặc đóng modal dialog. Báo cáo tôn trọng nút Toasts và cần cập nhật cả server lẫn add-in.

Toast dùng **WPF dựng hoàn toàn bằng code trên UI thread STA riêng**, với cửa sổ **unowned, no-activate**, độc lập với STA chính của Inventor và không gọi Inventor COM trên luồng toast. Thông báo kết quả được post sau khi response đã trả về. Toast không giật focus, ẩn khi Inventor bị minimize hoặc có modal dialog, và **giữ topmost ngay cả khi ứng dụng khác ở foreground**. Palette auto lấy mẫu màn hình, không lấy mẫu chính thẻ đã vẽ; mẫu chỉ nằm trong bộ nhớ, không ghi đĩa hay log.

Bật/tắt toast từ ribbon: **Bimwright ▸ MCP → Toasts** (Status mở dialog chẩn đoán). Lựa chọn lưu vào `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` dưới key `enableToast`; `toastTheme` nhận `auto` (mặc định), `light` hoặc `dark`. Biến môi trường `BIMWRIGHT_INVENTOR_ENABLE_TOAST` và `BIMWRIGHT_INVENTOR_TOAST_THEME` override giá trị JSON. File config malformed sẽ được giữ nguyên — toggle từ chối ghi đè nó.

**Toast Brand** trên cùng ribbon **tắt mặc định, chỉ có hiệu lực trong phiên**, không lưu vào config. Khi bật, tiêu đề hoạt động có tiền tố `IPT-MCP - ` và wordmark BIMwright hiện bằng hiệu ứng wipe khi hover thực sự, rồi fade khi rời thẻ. **Không tự chạy wipe wordmark lúc thẻ xuất hiện**. Hiệu ứng tuân theo tùy chọn animation của Windows. Nhãn UI toast vẫn là tiếng Anh; bản dịch README không có nghĩa UI đã được bản địa hóa.

---

## An toàn

Ngắn gọn: model của bạn ở lại trên máy bạn, và các tool write/nguy hiểm đều có gate.

- **Read-only mode.** `--read-only` (hoặc `BIMWRIGHT_INVENTOR_READ_ONLY=1`) loại bỏ mọi write-capable toolset (`document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `code`, `toolbaker_write`) nhưng giữ `meta` + `query` + `assembly_query` + read-only `toolbaker`, và **giữ `inventor_switch_target`**. Server gửi read-only mode trong mỗi command envelope; add-in cũng tôn trọng `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`. `CommandDispatcher` của add-in là tuyến phòng thủ thứ hai: lệnh write dưới read-only trả về `READ_ONLY`.
- **send_code opt-in hai phía.** `inventor_send_code` **mặc định tắt**. Chỉ hiện khi **cả hai** gate được bật: server với `--enable-send-code` (hoặc `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) **và** tiến trình add-in với `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`. Nếu không, dispatcher trả `SEND_CODE_DISABLED`. API bị cấm (file/process/network/environment) bị reject.
- **Transport local, có xác thực.** TCP bind loopback; Named Pipe scoped local-machine. Mỗi descriptor theo session mang một auth token ngẫu nhiên.
- **Error đã sanitize.** Error trả về model được sanitize để tránh leak absolute path/secret.
- **Kiểm soát ToolBaker.** Mặc định ToolBaker được bật. Bạn có thể tắt hoàn toàn bằng cách truyền flag --disable-toolbaker lúc khởi động server hoặc set biến môi trường BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0.
- **Đường dẫn export được phép.** Các công cụ xuất tệp kiểm tra để đảm bảo output_path nằm trong thư mục an toàn (User Profile hoặc Temp). Bạn có thể đăng ký thêm thư mục cho phép bằng cách cấu hình biến môi trường BIMWRIGHT_INVENTOR_EXPORT_ROOT.

**ToolBaker** biến workflow local lặp lại thành tool cá nhân đã verify: suggestion xuất hiện qua `inventor_list_bake_suggestions`, bạn chủ động accept bằng `inventor_accept_bake_suggestion` (validate → compile → apply → persist), và tool đã accept gọi được qua `inventor_list_baked_tools` / `inventor_run_baked_tool`. Bake database và audit log nằm local dưới `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`. Xem [docs/toolbaker.md](docs/toolbaker.md) và [SECURITY.md](SECURITY.md).

---

## Không phân phối lại

Dự án này **không** phân phối lại binaries của Autodesk Inventor hay Inventor SDK / interop DLL. Server và unit test được ship build và chạy mà không cần Inventor. Build add-in theo từng phiên bản cần một **bản cài Inventor local** hoặc **interop reference assembly** tương ứng (`Autodesk.Inventor.Interop.dll`), cung cấp qua đường dẫn shared-extensions mặc định của Autodesk hoặc một MSBuild property `/p:InventorInteropDir=...` rõ ràng. Chạy cổng kết nối với Inventor cần một bản cài Inventor có license.

---

## Họ bimwright

Các công cụ mã nguồn mở kết nối trợ lý AI với ứng dụng BIM và CAD.

Tên **bimwright** ghép **BIM** với **wright**, một từ tiếng Anh cổ chỉ người thợ chế tạo hoặc xây dựng — như trong *shipwright* (thợ đóng tàu).

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Kho kiến thức BIM ưu tiên tiếng Việt

---

## Giấy phép

[Apache-2.0](LICENSE). Xem [LICENSE](LICENSE).

Inventor và Autodesk là thương hiệu đã đăng ký của Autodesk, Inc. bimwright là dự án open-source độc lập, không liên kết, không được tài trợ và không được bảo chứng bởi Autodesk, Inc.
