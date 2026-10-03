<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phiên-bản-inventor-được-hỗ-trợ"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#bề-mặt-công-cụ"><img src="https://img.shields.io/badge/MCP-111%20tools-6C47FF" alt="MCP tools" /></a>
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

> **Trạng thái: Drawing Phase 1 đang triển khai.** Đã thêm 11 tool drawing cho Inventor 2027; test không cần host và workflow handler trên fixture thật đã qua. Acceptance đầy đủ và release gate còn pending. Xem [kiểm thử drawing](docs/testing/drawing-phase1.md).

Guard chung đo UTF-8 tại kết quả MCP cuối: cảnh báo 64/256 KiB, budget 1 MiB. Read quá lớn yêu cầu thu hẹp; write đã chạy giữ kết quả rút gọn, không chạy lại để lấy chi tiết. `--disable-output-guard` vẫn giữ giới hạn transport. Cấu hình ngưỡng CLI/JSON/env và spill mặc định 36 giờ tại [tài liệu kiểm thử](docs/testing/drawing-phase1.md).

---

## Cài đặt / Wire MCP client

Tải [GitHub Releases](https://github.com/bimwright/ipt-mcp/releases/latest) (`IptMcp.Setup-*-win-x64.zip`). v0.1.0 gồm Inventor **2025** và **2027**. `install.ps1` trong ZIP; trỏ MCP vào `ipt-mcp.exe`. Không `dotnet tool install -g Bimwright.Ipt.Server`.

`inventor_send_code` mặc định bật. `--disable-send-code` ẩn nhóm code; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` chặn thực thi ở host. Chế độ read-only luôn loại bỏ thực thi code.

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

Chế độ đầy đủ mặc định có **111 công cụ** (`--toolsets all`); `--disable-send-code` còn **107 công cụ**, và `--read-only` còn **28 công cụ**. Tên MCP đều bắt đầu bằng `inventor_`; có thể thu hẹp bằng bộ lọc toolset.

**Chọn document & occurrence.** Các tool cấp document nhận `document` tuỳ chọn (đường dẫn hoặc tên của document Inventor đang giữ trong bộ nhớ — cả part được assembly tham chiếu); không bao giờ tự mở/activate. Các tool assembly theo lô dùng chung selector `{names?: [glob], regex?, file?, path_contains?, leaf?, max_depth?, include_suppressed?, limit?}`; không khớp hoặc vượt `limit` là lỗi kèm gợi ý tên gần giống. Save/open/close/export chạy dưới `SilentOperation` mặc định để dialog ẩn không làm treo call; nếu vẫn timeout, `inventor_health` báo `modal_dialog {open, title}`.

Mặc định bật cả 15 toolset, gồm `code`. Dùng `--toolsets <csv>` để thu hẹp.

Mọi input độ dài tính bằng **mm**, góc tính bằng **độ**; add-in tự chuyển sang centimét/radian nội bộ của Inventor.


### drawing_query (2) / drawing (20) — Inventor 2027

Earlier Inventor 2027 fixture results: [Phase 3 tool behavior](docs/testing/drawing-phase3.md). These records predate v0.2.1 runtime changes; live acceptance must be renewed.

`inventor_get_drawing_info` and `inventor_find_view_geometry` are read-only. Drawing writes:
`inventor_new_drawing`, `inventor_add_sheet`, `inventor_set_title_block`,
`inventor_add_drawing_view`, `inventor_add_section_view`, `inventor_edit_drawing_view`,
`inventor_add_drawing_dimension`, `inventor_add_balloon`, `inventor_export_drawing`,
`inventor_capture_sheet`, `inventor_add_drawing_note`, `inventor_add_drawing_table`,
`inventor_add_drawing_symbol`, `inventor_edit_drawing_annotation`, `inventor_delete_drawing_items`,
`inventor_edit_drawing_table`, `inventor_set_drawing_styles`, `inventor_edit_sheet`, `inventor_sketch_on_view`, `inventor_hide_view_edges`. Captures write PNG files and are excluded from read-only.

[Drawing checks and current limitations](docs/testing/drawing-phase1.md) ·
[Generic live smoke record](docs/benchmarks/drawing-phase1-smoke.json) ·
[Declared read-only inventory](docs/testing/readonly-tools.json).

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

### assembly (9, write) — dựng và sửa assembly

| Tool | Mô tả |
|---|---|
| `inventor_create_design_view` | Copy an assembly design view; exact occurrence visibility/appearance settings, optional activation. Validated on an Inventor 2027 disposable fixture. |
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

### code (4) — C# scripts and code modules

| Tool | Mô tả |
|---|---|
| `inventor_send_code` | `inventor_send_code` mặc định bật. `--disable-send-code` ẩn nhóm code; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` chặn thực thi ở host. Chế độ read-only luôn loại bỏ thực thi code. |
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

Khi Inventor hiển thị, kết quả tool cập nhật vào **một thẻ hoạt động nhỏ gọn**, hiển thị tool gần nhất và bộ đếm **Success / Failed / Capture** với hiệu ứng cuộn số. Capture là tập con của Success, bao gồm `inventor_capture_view` với `inline=true`, không cộng thêm vào tổng thao tác. Failed bao gồm soft-fail và batch rollback. Read và write đều dùng màu nhấn xanh dương; khi đã ghi nhận lỗi, màu nhấn của thẻ giữ màu đỏ. Thẻ này giống thẻ của rvt-mcp và dwg-mcp nên bố cục ổn định khi số thay đổi, không có chồng thẻ theo ưu tiên hay hàng đợi toast từng tool. Capture thành công có lưu ảnh sẽ hiện thêm **thumbnail** của ảnh đó, căn giữa trong một khung cố định; bấm thumbnail để mở file. Bộ đếm chỉ tính trong vòng đời thẻ trên target, không đại diện cho toàn bộ công việc hoặc riêng từng MCP client. `inventor_health` không tạo toast. **Agent connected** chỉ là thông báo trạng thái, không tăng bộ đếm và không thay thẻ hoạt động hoặc báo cáo đang giữ.

Thẻ hoạt động mặc định tự mất **20 giây sau kết quả cuối**. Chọn 10, 20, 30 hoặc 60 giây tại **Status → Toast duration → Apply**; lựa chọn được lưu dưới key `toastIdleSeconds`. Nếu lưu lỗi, dialog hiện thông báo và giữ thời gian cũ. Hover thực sự, nhận biết qua chuyển động con trỏ, tạm dừng thời hạn; khi con trỏ rời thẻ, thời hạn bắt đầu lại đủ khoảng đã chọn. Thời gian mới có hiệu lực từ kết quả hoặc lần rời chuột tiếp theo. Thẻ xuất hiện dưới con trỏ đứng yên không được tính là hover. Click thẻ mở **History** và đóng thẻ; **×** chỉ đóng thẻ. Tiêu đề thẻ luôn nêu tên gateway và **năm Inventor** (`ipt-mcp 2027`), bất kể branding bật hay tắt.

Agent báo kết quả công việc bằng `inventor_report_task_result`: `task_id` riêng cho agent/công việc (1–80 ký tự), `outcome` (`completed`, `failed`, `cancelled`), `summary` trung thực một dòng (1–120 ký tự). Báo cáo **thay vào cùng vị trí thẻ**, ghi **Agent reported**, có thời hạn **8 giây**, tạm dừng khi hover thực sự và bắt đầu lại khi rời thẻ. Báo cáo không tăng bộ đếm; kết quả tool tiếp theo bắt đầu thẻ hoạt động mới. Không suy đoán hoàn thành từ khoảng im lặng hoặc một tool thành công. Lệnh báo cáo không đi qua hàng đợi lệnh Inventor và không gọi Inventor API, nên vẫn nhận được khi `send_code` đang giữ luồng chính. `toast_shown` cho biết thẻ đã được giữ để hiển thị, kể cả khi đang chờ Inventor hết minimize hoặc đóng modal dialog. Báo cáo tôn trọng nút Toasts và cần cập nhật cả server lẫn add-in.

Toast dùng **WPF dựng hoàn toàn bằng code trên UI thread STA riêng**, với cửa sổ **unowned, no-activate**, độc lập với STA chính của Inventor và không gọi Inventor COM trên luồng toast. Thông báo kết quả được post sau khi response đã trả về. Toast không giật focus, ẩn khi Inventor bị minimize hoặc có modal dialog, và **giữ topmost ngay cả khi ứng dụng khác ở foreground**. Thẻ dùng một kiểu sáng cố định như rvt-mcp và dwg-mcp; không có cài đặt theme và không có gì đọc màn hình phía sau thẻ.

Bật/tắt toast từ ribbon: **Bimwright ▸ MCP → Toasts** (Status mở dialog chẩn đoán). Lựa chọn lưu vào `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` dưới key `enableToast`. Biến môi trường `BIMWRIGHT_INVENTOR_ENABLE_TOAST` override giá trị JSON. Key `toastTheme` và `BIMWRIGHT_INVENTOR_TOAST_THEME` đã bị loại bỏ và được bỏ qua. File config malformed sẽ được giữ nguyên — toggle từ chối ghi đè nó.

**Toast Brand** trên cùng ribbon **tắt mặc định**; lựa chọn được lưu vào cùng file config dưới key `showBranding` và được khôi phục ở lần mở Inventor tiếp theo. Khi bật, thẻ dành một hàng wordmark ở cuối và wordmark BIMwright hiện ở đó bằng hiệu ứng wipe khi hover thực sự, rồi fade khi rời thẻ. **Không tự chạy wipe wordmark lúc thẻ xuất hiện**. Hiệu ứng tuân theo tùy chọn animation của Windows. Nhãn UI toast vẫn là tiếng Anh; bản dịch README không có nghĩa UI đã được bản địa hóa.

---

## An toàn

Ngắn gọn: model của bạn ở lại trên máy bạn, và các tool write/nguy hiểm đều có gate.

- **Read-only mode.** `--read-only` chỉ giữ tool có annotation `ReadOnly = true`, không ghi document hoặc file. Vẫn có truy vấn parameter/property, view fit và danh sách code module. Add-in cũng áp dụng `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`.
- **send_code.** `inventor_send_code` mặc định bật. `--disable-send-code` ẩn nhóm code; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` chặn thực thi ở host. Chế độ read-only luôn loại bỏ thực thi code.
- **Transport local, có xác thực.** TCP bind loopback; Named Pipe scoped local-machine. Mỗi descriptor theo session mang một auth token ngẫu nhiên.
- **Error đã sanitize.** Error trả về model được sanitize để tránh leak absolute path/secret.
- **Kiểm soát ToolBaker.** Mặc định ToolBaker được bật. Bạn có thể tắt hoàn toàn bằng cách truyền flag --disable-toolbaker lúc khởi động server hoặc set biến môi trường BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0.
- **Đường dẫn export được phép.** Các công cụ xuất tệp kiểm tra để đảm bảo output_path nằm trong thư mục an toàn (User Profile hoặc Temp). Bạn có thể đăng ký thêm thư mục cho phép bằng cách cấu hình biến môi trường BIMWRIGHT_INVENTOR_EXPORT_ROOT.
- **Che thông tin bí mật trong lỗi và nhật ký.** Cặp khóa–giá trị chứa thông tin đăng nhập và token `Bearer` luôn bị che. Một heuristic che thêm mọi chuỗi chữ-số dài từ 24 ký tự; mặc định **bật**. Trên máy tin cậy cần đọc rõ tên dài (tên kiểu COM, tên part), đặt `BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS=0` trong biến môi trường người dùng (cả server và add-in đều đọc; khởi động lại Inventor và MCP client).

**ToolBaker** biến workflow local lặp lại thành tool cá nhân đã verify: suggestion xuất hiện qua `inventor_list_bake_suggestions`, bạn chủ động accept bằng `inventor_accept_bake_suggestion` (validate → compile → apply → persist), và tool đã accept gọi được qua `inventor_list_baked_tools` / `inventor_run_baked_tool`. Bake database và audit log nằm local dưới `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`. Xem [docs/toolbaker.md](docs/toolbaker.md) và [SECURITY.md](SECURITY.md).

---

## Không phân phối lại

Dự án này **không** phân phối lại binaries của Autodesk Inventor hay Inventor SDK / interop DLL. Server và unit test được ship build và chạy mà không cần Inventor. Build add-in theo từng phiên bản cần một **bản cài Inventor local** hoặc **interop reference assembly** tương ứng (`Autodesk.Inventor.Interop.dll`), cung cấp qua đường dẫn shared-extensions mặc định của Autodesk hoặc một MSBuild property `/p:InventorInteropDir=...` rõ ràng. Chạy cổng kết nối với Inventor cần một bản cài Inventor có license.

---

## Họ bimwright

Các công cụ mã nguồn mở kết nối trợ lý AI với ứng dụng BIM và CAD.

Tên **bimwright** ghép **BIM** với **wright**, một từ tiếng Anh cổ chỉ người thợ chế tạo hoặc xây dựng — như trong *shipwright* (thợ đóng tàu).

Xem [cách đặt tên các gateway](https://github.com/bimwright/.github/blob/master/profile/README.vi.md#cách-đặt-tên).

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Kho kiến thức BIM ưu tiên tiếng Việt

---

## Giấy phép

[Apache-2.0](LICENSE). Xem [LICENSE](LICENSE).

Inventor và Autodesk là thương hiệu đã đăng ký của Autodesk, Inc. bimwright là dự án open-source độc lập, không liên kết, không được tài trợ và không được bảo chứng bởi Autodesk, Inc.

## Quyền & auto mode (Permissions & auto mode)

Allow list dưới đây được sinh từ 28 annotation read-only và có test đối chiếu server đang chạy. Thay `ipt-mcp` bằng đúng server ID trong client. Không dùng wildcard `mcp__ipt-mcp__*` hoặc wildcard cho toàn server. `inventor_send_code` không gắn annotation hay metadata bắt buộc hỏi mỗi lần; cho phép riêng tool này là lựa chọn của người dùng. Cần kiểm tra việc lưu quyền trên client thực tế.

<!-- BEGIN GENERATED READONLY -->
```json
{
  "permissions": {
    "allow": [
      "mcp__ipt-mcp__inventor_check_interference",
      "mcp__ipt-mcp__inventor_create_bake_issue_draft",
      "mcp__ipt-mcp__inventor_find_view_geometry",
      "mcp__ipt-mcp__inventor_get_assembly_bom",
      "mcp__ipt-mcp__inventor_get_current_target",
      "mcp__ipt-mcp__inventor_get_document_info",
      "mcp__ipt-mcp__inventor_get_drawing_info",
      "mcp__ipt-mcp__inventor_get_iproperty",
      "mcp__ipt-mcp__inventor_get_mass_properties",
      "mcp__ipt-mcp__inventor_get_parameter",
      "mcp__ipt-mcp__inventor_health",
      "mcp__ipt-mcp__inventor_list_available_targets",
      "mcp__ipt-mcp__inventor_list_bake_suggestions",
      "mcp__ipt-mcp__inventor_list_baked_tools",
      "mcp__ipt-mcp__inventor_list_bodies",
      "mcp__ipt-mcp__inventor_list_code_modules",
      "mcp__ipt-mcp__inventor_list_constraints",
      "mcp__ipt-mcp__inventor_list_features",
      "mcp__ipt-mcp__inventor_list_interfaces",
      "mcp__ipt-mcp__inventor_list_iproperty_sets",
      "mcp__ipt-mcp__inventor_list_occurrences",
      "mcp__ipt-mcp__inventor_list_open_documents",
      "mcp__ipt-mcp__inventor_list_parameters",
      "mcp__ipt-mcp__inventor_measure_min_distance",
      "mcp__ipt-mcp__inventor_probe_brep",
      "mcp__ipt-mcp__inventor_report_task_result",
      "mcp__ipt-mcp__inventor_switch_target",
      "mcp__ipt-mcp__inventor_view_fit"
    ]
  }
}
```
<!-- END GENERATED READONLY -->

### Runtime settings (v0.2.1)

| Setting | Default | CLI / JSON |
|---|---|---|
| send_code | on | `--enable-send-code` / `--disable-send-code`; `enableSendCode` |
| Call-log files | off | `--enable-call-log` / `--disable-call-log`; `enableCallLog` |
| Toolsets | all | `--toolsets all`; `toolsets` |
| Read-only | off | `--read-only`; `readOnly` |
| Response guard | on | `--enable-output-guard` / `--disable-output-guard`; `enableOutputGuard` |
| Warning / strong warning / budget | 65536 / 262144 / 1048576 bytes | `--output-warning-bytes`, `--output-strong-warning-bytes`, `--output-budget-bytes`; `outputWarningBytes`, `outputStrongWarningBytes`, `outputBudgetBytes` |
| Transport cap | 5000000 bytes | `--max-response-bytes`; `maxResponseBytes` |
| Spill retention | 36 hours | `--spill-retention-hours`; `spillRetentionHours` (invalid values: 36) |

CLI overrides environment, which overrides `--config` JSON. The server's logging switch reaches the plug-in; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_CALL_LOG=1` can veto it. History re-runs do not persist call logs. In-memory History is independent. Body caching (`BIMWRIGHT_CACHE_SEND_CODE_BODIES=1`) and body journaling (`BIMWRIGHT_PERSIST_SEND_CODE_BODIES=1`, TTL default 4 hours) are separate opt-ins; journaling also requires call logging. Enabled call logs keep source length/hash, never source bodies. Saved code modules are explicit user-requested storage; credential-like source values are rejected before compilation/storage, without rewriting the code.

`send_code` provides `app` and nullable `doc`, accepts a script body with `return` and optional helper declarations, and imports `System`, `System.Collections.Generic`, `System.Linq`, `Inventor`. Writes to the active document share one undo transaction; errors abort it and warnings are returned. New/closed documents, other documents and external files are outside that rollback scope. Oversized script output includes a file, preview, schema and `mutation_applied: null`; read the file and do not re-run the script. Spill files live under `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill` and fresh files are never evicted by a count cap.

[Bản ghi benchmark và phạm vi kiểm chứng](docs/benchmarks/README.md).

[Benchmark live v0.2.1: Inventor 2027, 111 công cụ](docs/benchmarks/v0.2.1-inventor-2027.md).
