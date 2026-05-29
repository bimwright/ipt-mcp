<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phiên-bản-inventor-được-hỗ-trợ"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#bề-mặt-công-cụ"><img src="https://img.shields.io/badge/MCP-46%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · Tiếng Việt
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

> **Trạng thái: còn sớm.** Giai đoạn 1-3 đã xong và green (46 MCP tools; server + tests build mà không cần Inventor). Phần thân handler Inventor-API được viết theo API tài liệu nhưng mới chỉ compile-only cho tới khi có smoke run trên Inventor thật. Hãy test trên môi trường của bạn trước khi tin dùng cho production model.

---

## Cài đặt / Wire MCP client

Server là một tiến trình MCP stdio thuần. Trỏ client tới `Bimwright.Ipt.Server.exe` đã build (hoặc `dotnet run`), rồi load add-in bên trong Inventor.

Một entry `.mcp.json` (hoặc config client tương đương) điển hình:

```json
{
  "mcpServers": {
    "ipt-mcp": {
      "command": "D:\\path\\to\\src\\server\\bin\\Debug\\net8.0\\Bimwright.Ipt.Server.exe",
      "args": []
    }
  }
}
```

Discovery add-in là tự động: mỗi add-in Inventor đang chạy ghi một descriptor theo từng instance dưới `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`. Server scan các file này, bỏ những file dead/stale, và kết nối. Khi có thể mở nhiều Inventor cùng lúc, hãy gọi `inventor_list_available_targets` rồi `inventor_switch_target`.

---

## Build & phát triển

Binaries của Autodesk Inventor và Inventor SDK **không được phân phối lại** trong repo này (xem [Không phân phối lại](#không-phân-phối-lại)). Build **server và tests** chỉ cần .NET 8 SDK; build **add-in theo từng phiên bản** cần SDK tương ứng cùng với interop reference assembly của Inventor.

```bash
# Server + tests (chỉ server; KHÔNG cần Inventor — chạy trên mọi máy có .NET 8 SDK):
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# Kiểm tra SHAPE của project add-in mà không cài Inventor:
dotnet build src/plugin-inv24 -c Debug /p:SkipInventorReferenceCheck=true
dotnet build src/plugin-inv27 -c Debug /p:SkipInventorReferenceCheck=true   # cần .NET 10 SDK

# Compile add-in thật (chỉ trên máy có Inventor + SDK tương ứng): bỏ SkipInventorReferenceCheck.
# Truyền /p:InventorInteropDir=... nếu interop không nằm ở đường dẫn mặc định.
```

- **Server** explicit-include chỉ `shared/Contracts/*` + `shared/Security/*` (+ ToolBaker), nên compile được khi không có Inventor SDK.
- Mỗi **add-in** dùng `<Compile Include="..\shared\**\*.cs" />` để kéo mọi thứ, kể cả `Infrastructure`/`Plugin`/`Handlers` chạm vào API.
- Đường dẫn interop mặc định là `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`.
- Build add-in 2027 cần cài **.NET 10 SDK**.
- **Đóng Inventor trước khi deploy add-in DLL** vì Inventor sẽ lock DLL đã load.

---

## Bề mặt công cụ

Toàn bộ surface là **46 tools** khi bật mọi platform toolset. Mọi tên MCP đều có prefix `inventor_`. Các tool được nhóm theo toolset class; `--toolsets sketch,feature` và `--read-only` kiểm soát tool nào được đăng ký để agent yếu không nhìn thấy tool đã tắt.

Toolsets bật mặc định: `meta`, `query`, `document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `toolbaker`, `toolbaker_write`.
Tắt mặc định: `code` (escape hatch `send_code` — chỉ bật khi opt-in).

Mọi input độ dài tính bằng **mm**, góc tính bằng **độ**; add-in tự chuyển sang centimét/radian nội bộ của Inventor.

### meta (3) — tool target phía server, không round-trip tới add-in; vẫn hiện dưới `--read-only`

| Tool | Mô tả |
|---|---|
| `inventor_list_available_targets` | List các target add-in Inventor đang sống (năm, pid, transport, document đang hoạt động). |
| `inventor_get_current_target` | Báo target đang được server chọn, hoặc `NO_TARGET` nếu không có target sống. |
| `inventor_switch_target` | Chọn target theo descriptor id, năm hoặc session. Chỉ phía server. |

### query (3) — probe document/health read-only

| Tool | Mô tả |
|---|---|
| `inventor_health` | Probe add-in đang hoạt động: inventor_year, process_id, có document mở không, loại document. |
| `inventor_list_open_documents` | List mọi document đang mở: title, path, type, và cái nào active. |
| `inventor_get_document_info` | Lấy title, full path và document type của document đang active. |

### document (7) — vòng đời document (write)

| Tool | Mô tả |
|---|---|
| `inventor_new_part` | Tạo document part mới (.ipt); template path tùy chọn. |
| `inventor_new_assembly` | Tạo document assembly mới (.iam); template path tùy chọn. |
| `inventor_open_document` | Mở document có sẵn từ full path và đặt làm active. |
| `inventor_save_document` | Save document active, hoặc Save-As tới một path. |
| `inventor_close_document` | Đóng document active; `save=true` lưu trước. |
| `inventor_set_units` | Đặt đơn vị độ dài của document (mm, cm, m, in, ft). |
| `inventor_set_material` | Gán material cho part active theo tên. |

### parameters (4) — model & user parameters (write)

| Tool | Mô tả |
|---|---|
| `inventor_list_parameters` | List parameter (model + user): name, expression, value, unit, kind. |
| `inventor_get_parameter` | Lấy một parameter theo tên: expression, value, unit. |
| `inventor_set_parameter` | Set expression/value của parameter có sẵn, rồi update document. |
| `inventor_create_parameter` | Tạo user parameter mới (name, expression, unit). |

### properties (3) — iProperties & mass properties (write)

| Tool | Mô tả |
|---|---|
| `inventor_get_iproperty` | Lấy giá trị iProperty theo property-set và tên property. |
| `inventor_set_iproperty` | Set giá trị iProperty. |
| `inventor_get_mass_properties` | Khối lượng (g), thể tích (mm³), diện tích bề mặt (mm²), trọng tâm, bounding box. |

### sketch (9) — geometry & constraint sketch 2D (write)

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
| `inventor_close_sketch` | Kết thúc chỉnh sketch (thoát chế độ edit sketch). |

### feature (6) — solid & work feature (write)

| Tool | Mô tả |
|---|---|
| `inventor_extrude` | Extrude một sketch theo tên (distance, join/cut/intersect, direction). |
| `inventor_revolve` | Revolve một sketch quanh trục (góc, operation). |
| `inventor_fillet` | Thêm fillet cạnh bán kính cố định trên các edge model. |
| `inventor_chamfer` | Thêm chamfer cạnh khoảng cách đều trên các edge model. |
| `inventor_create_work_plane` | Tạo work plane (offset, three_points hoặc tangent). |
| `inventor_create_work_axis` | Tạo work axis (two_points, edge, plane_intersection, normal_to_face_through_point). |

### export (4) — capture view & export geometry (write)

| Tool | Mô tả |
|---|---|
| `inventor_capture_view` | Capture view active thành PNG base64 có giới hạn (không ghi file). |
| `inventor_export_step` | Export part/assembly active sang STEP (.stp/.step). |
| `inventor_export_stl` | Export part/assembly active sang STL (.stl). |
| `inventor_export_dxf` | Export DXF 2D; phải khai báo source (`sketch` hoặc `flat_pattern`). |

> Đường dẫn export phải là absolute và nằm dưới một output root được phép (user profile hoặc temp).

### code (1) — escape hatch opt-in (TẮT mặc định)

| Tool | Mô tả |
|---|---|
| `inventor_send_code` | **Nguy hiểm, chỉ opt-in.** Thực thi đoạn C# in-process trên `Inventor.Application`. Tắt trừ khi cả server và add-in đều opt-in (nếu không trả `SEND_CODE_DISABLED`); API bị cấm (file/process/network/environment) bị reject. |

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

## An toàn

Ngắn gọn: model của bạn ở lại trên máy bạn, và các tool write/nguy hiểm đều có gate.

- **Read-only mode.** `--read-only` (hoặc `BIMWRIGHT_INVENTOR_READ_ONLY=1`) loại bỏ mọi write-capable toolset (`document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `code`, `toolbaker_write`) nhưng giữ `meta` + `query` + read-only `toolbaker`, và **giữ `inventor_switch_target`**. `CommandDispatcher` của add-in là tuyến phòng thủ thứ hai: lệnh write dưới read-only trả về `READ_ONLY`.
- **send_code opt-in hai phía.** `inventor_send_code` **mặc định tắt**. Chỉ hiện khi **cả hai** gate được bật: server với `--enable-send-code` (hoặc `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) **và** tiến trình add-in với `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`. Nếu không, dispatcher trả `SEND_CODE_DISABLED`. API bị cấm (file/process/network/environment) bị reject.
- **Transport local, có xác thực.** TCP bind loopback; Named Pipe scoped local-machine. Mỗi descriptor theo session mang một auth token ngẫu nhiên.
- **Error đã sanitize.** Error trả về model được sanitize để tránh leak absolute path/secret.

**ToolBaker** biến workflow local lặp lại thành tool cá nhân đã verify: suggestion xuất hiện qua `inventor_list_bake_suggestions`, bạn chủ động accept bằng `inventor_accept_bake_suggestion` (validate → compile → apply → persist), và tool đã accept gọi được qua `inventor_list_baked_tools` / `inventor_run_baked_tool`. Bake database và audit log nằm local dưới `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`. Xem [docs/toolbaker.md](docs/toolbaker.md) và [SECURITY.md](SECURITY.md).

---

## Không phân phối lại

Dự án này **không** phân phối lại binaries của Autodesk Inventor hay Inventor SDK / interop DLL. Server và unit test được ship build và chạy mà không cần Inventor. Build add-in theo từng phiên bản cần một **bản cài Inventor local** hoặc **interop reference assembly** tương ứng (`Autodesk.Inventor.Interop.dll`), cung cấp qua đường dẫn shared-extensions mặc định của Autodesk hoặc một MSBuild property `/p:InventorInteropDir=...` rõ ràng. Chạy cổng kết nối với Inventor cần một bản cài Inventor có license.

---

## Giấy phép

[Apache-2.0](LICENSE). Xem [LICENSE](LICENSE).

Inventor và Autodesk là thương hiệu đã đăng ký của Autodesk, Inc. bimwright là dự án open-source độc lập, không liên kết, không được tài trợ và không được bảo chứng bởi Autodesk, Inc.
