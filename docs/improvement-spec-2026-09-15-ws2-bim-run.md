# ipt-mcp — Audit run WS2 & Spec cải tiến (2026-09-15)

Date: 2026-09-15
Status: **spec chính thức** cho chu kỳ cải tiến ipt-mcp đầu tiên — owner đã chốt 3 quyết định mở (F2-d, F3-c, F3-d) ngày
2026-09-15; F4 chốt interface từng tool trước khi implement
Companion: [`gap-analysis-2026-09-15-ws2-bim-run.md`](gap-analysis-2026-09-15-ws2-bim-run.md) (gap doc) — tài liệu này
**đính chính** gap doc (phần E) và **thay** phần đề xuất của gap doc bằng spec có tiêu chí nghiệm thu (phần F).
Quy trình: owner implement **từng bước** theo phần F, Devin review theo checklist phần G.

Bằng chứng gốc:

- Call journal: `C:\Users\Admin\AppData\Local\Bimwright\ipt-mcp-calls.jsonl` — 294 dòng = 147 call
  (mỗi call 1 dòng `start` + 1 dòng `finish`), 04:08:41Z → 11:05:28Z (11:08 → 18:05 giờ máy, UTC+7).
- Artifacts: `D:\Workspace\DailyTask\2026.09\15\WS2_model\` (models, views, `scripts\`, `three_region_evidence\`,
  `appearance_evidence\`).
- Mã nguồn ipt-mcp tại commit `8a9ebd6` (branch `master`), rvt-mcp `src/shared/Infrastructure/ResponseSizeGuard.cs`.

Ký hiệu:

- `#n` = thứ tự call theo `timestamp` của dòng `start` (1..147).
- **[ĐO]** đo trực tiếp từ log hoặc code. **[SUY]** suy luận từ nội dung script / artifacts. **[GT]** giả thuyết, cần run có kiểm soát.

---

## A. Dữ liệu có đủ để cải tiến chưa?

| Mục owner hỏi | Kết luận | Lý do |
|---|---|---|
| 1. send_code: bao nhiêu, vì sao | **Đủ** | 91 script được log nguyên văn (124.6K chars) → phân loại được từng call |
| 2. Chất lượng tool | **Thiếu** | Log không lưu response, không lưu kích thước response; chỉ có latency + error string. Với send_code, cờ `success` không phản ánh script chạy được hay không (A2) |
| 3. Kiến trúc guard / tool có nặng không | **Thiếu** | Không có `response_bytes` → không đo được; phần D đánh giá bằng đọc code thay thế |

→ Bước 1 của spec (F1) là **sửa log để đo được**, trước khi sửa bất kỳ tool nào.

### A1. Log hiện ghi gì [ĐO]

`src/server/ServerLogger.cs:26-65`: `start` có `params` đầy đủ (kể cả `code`); `finish` chỉ có `success`,
`duration_ms`, `error`. `session_id` là hằng `"server"`. `PluginClient.SendAsync` (`src/server/PluginClient.cs:100-113`)
đặt `ok = true` khi envelope `result.Ok` — không nhìn vào `result.Data`.

### A2. `success=true` của send_code là mức envelope, không phải mức script [ĐO + SUY]

`SendCodeHandler` trả `InventorCommandResult.Success` với `data.ok=false` khi script lỗi compile hoặc ném exception
(`src/shared/Handlers/Code/SendCodeHandler.cs:106-139`). Chỉ TIMEOUT và denylist mới là `Fail`. Log ghi 86/91 OK, nhưng
đọc nội dung script thấy các **chuỗi retry** (cùng ý đồ, viết lại với sửa nhỏ):

| Chuỗi | Số lần | Ý đồ | Sửa gì giữa các lần |
|---|---|---|---|
| #13 → #14 → #15 | 3 | reference body (19 body) | denylist `Socket` → compile → thêm cast `(Inventor._Document)`, `(SketchEntity)` |
| #16 → #17 → #19 → #20 → #21 → #22 → #23 | 7 | housing loft | sửa token số-thành-chữ, sửa API loft, đổi cách lấy profile |
| #24 → #25 → #26 → #27 | 4 | injector rings | #27 đổi sang truyền tọa độ dạng chuỗi CSV + `double.Parse` |
| #31 → … → #37 | 7 | faceplate + combine | tên body đổi sau combine → tra qua `ExtrudeFeatures[n].SurfaceBodies[1]` |
| #129 → … → #134 | 6 | derived part → SAT | denylist, đường dẫn, API `DerivedPartComponents` |
| #137 → … → #142 | 6 | `Face.CalculateFacets` | thử nhiều overload, cuối cùng bỏ, dùng `export_stl` (#143) |

Ước tính **~27–30/91 script thất bại thật ở mức script (~30%)** [SUY] so với 5 fail trong log [ĐO]. Con số chính xác
chỉ có sau F1.

### A3. Lỗ hổng coverage: 3 giờ modeling nặng nhất KHÔNG đi qua ipt-mcp [ĐO]

Khoảng trống trong jsonl:

| Từ → đến (UTC) | Giờ máy | Độ dài | Việc gì (theo artifacts) |
|---|---|---|---|
| #9 04:09Z → #10 05:01Z | 11:09 → 12:01 | 52 min | khảo sát PDF (không phải việc của MCP) |
| #53 05:20Z → #54 06:11Z | 12:20 → 13:11 | 50 min | — |
| **#59 06:12Z → #60 09:21Z** | **13:12 → 16:21** | **189 min** | **`scripts\Invoke-WS2Refinement.ps1` + `WS2Refiner.cs` (43 KB) + `Invoke-WS2Audit.ps1` + `WS2Audit.cs` (15 KB)** |
| #126 10:04Z → #127 10:20Z | 17:04 → 17:20 | 15 min | — |
| #145 10:43Z → #146 11:05Z | 17:43 → 18:05 | 22 min | — |

`Invoke-WS2Refinement.ps1` nạp `Autodesk.Inventor.Interop.dll` (Inventor 2027) bằng `Add-Type`, `WS2Refiner.cs` dùng
`Marshal.GetActiveObject` — **đi thẳng COM, không qua add-in**. Trong file này: `SweepFeature` ×3, `LoftFeature` ×3,
`ExtrudeFeature` ×11, `FilletFeature` ×7, `TextBoxes` ×1. Các feature `Inlet_Boss`, `Cast_Lower_Taper`, `LCD_Bezel2`,
`d51/d53/d57` trong `WS2_refined.ipt` (15:59 giờ máy) không do call nào trong jsonl tạo ra.

Hệ quả:

1. Câu "hoàn toàn qua ipt-mcp" trong gap doc là **sai**.
2. Bằng chứng Sweep của gap doc đến từ `WS2Refiner.cs`, không phải send_code (send_code có **0** `SweepFeature`).
3. Đây là tín hiệu quan trọng nhất của run: **khi việc nặng tới, agent rời MCP.** Nguyên nhân khả dĩ [GT]: script 43 KB
   không thể chạy trong timeout cứng 30 s của send_code; send_code không trả return value; denylist chặn `System.IO`
   (WS2Refiner.cs dùng `System.IO`). Mục tiêu cải tiến phải là "agent không có lý do để rời MCP".

---

## B. Mục 1 — 91 call send_code

### B1. Phân loại theo lý do phải dùng send_code [SUY — đọc từng script]

| Nhóm | Số script | Tỷ lệ | Nội dung điển hình | Call tiêu biểu |
|---|---|---|---|---|
| Inspection / audit / probe | **36** | 40% | liệt kê feature + `HealthStatus` (18 script), body + `RangeBox`, dò edge/face theo hình học, dump JSON validation, kiểm tra sau reopen, đọc asset values, `CalculateFacets` | #42, #43, #53, #63, #64, #74, #75, #79, #81, #110, #124, #126, #137–#142 |
| Modeling feature | 29 | 32% | extrude `kNewBodyOperation` trên work plane fixed (16 script dùng `WorkPlanes.AddFixed`), loft (4), combine (9), fillet chọn cạnh bằng predicate (6), cut với `AffectedBodies`, suppress/delete feature, set parameter expression | #13–#27, #31–#39, #69, #73 |
| Camera | **10** | 11% | `Camera.Eye/Target/UpVector/SetExtents/Perspective` + `ApplyWithoutTransition` trước mỗi `capture_view` | — |
| Export / derive | 6 | 7% | `DerivedPartComponents` → `DerivedAssemblyComponents`, SAT qua `TranslatorAddIn`, DWG | #129–#134 |
| Appearance thuần | 5 | 5% | (`Appearance`/`Assets.Add` xuất hiện trong 23 script tổng, đa số lồng trong modeling) | #100–#121 |
| Metadata | 2 | 2% | đổi tên body, iProperties user-defined | — |
| Thăm dò API | 3 | 3% | reflection (bị chặn ×2), test return value (#136) | #130, #131, #136 |

Không có nhóm nào "một lần rồi thôi": 36 script inspection lặp cùng một vài mẫu (list feature + health, list body + bbox,
dò cạnh tròn). Đó là primitive của workflow, không phải logic riêng của WS2.

### B2. Vì sao typed tool sketch/feature/parameter có **0** lượt dùng [ĐO — so tool inventory với nhu cầu run]

| Typed tool hiện có | Run cần | Thiếu |
|---|---|---|
| `inventor_extrude` (`src/server/Tools/FeatureTools.cs:66-75`) | `kNewBodyOperation`, đặt tên feature/body, distance dạng expression (`"EST_TankNeckLength"`, `"15 mm"`), `AffectedBodies` khi cut | operation `new_body`; `name`; expression; `affected_bodies` |
| `inventor_create_work_plane` (`offset` / `three_points` / `tangent`) | plane fixed từ origin + 2 unit vector (100% trường hợp) | kind `fixed` |
| `inventor_fillet` (nhận `edge_ids`) | chọn cạnh theo bán kính / tâm / loại surface hai bên | **không tool nào trả edge id** → fillet typed vô dụng với agent |
| `inventor_set_view_orientation` (10 hướng chuẩn) | eye/target/up/extents theo ý đồ | `set_camera` |
| — | combine, loft, sweep, work point, sketch text | chưa có |
| — | list bodies / list features / health | chưa có (roadmap deferred) |
| — | gộp nhiều bước thành 1 call | không có `batch_execute` (rvt-mcp có) |

Độ hạt: một ring = `create_work_plane` + `create_sketch` + `draw_circle` ×2 + `close_sketch` + `extrude` = 6 round-trip;
19 body của #15 ≈ 114 call typed, so với 1 call send_code. Kết hợp với bảng thiếu ở trên → agent chọn send_code từ đầu
là hành vi hợp lý, không phải lười.

### B3. Ghi chú các lệnh "quá phức tạp vì tọa độ hình học hay vì lý do gì" (theo yêu cầu owner)

1. **Lỗi số-thành-chữ trong danh sách tham số dài** [ĐO]: 5 script (#13, #16, #24, #25, #26) chứa `forty: 40`,
   `thirty:30`, `seventy:70`, `sixty:64`, `eighty:80` chèn giữa các đối số vị trí (x, y, z, od, id, len…) → C# không
   compile. #27 agent tự đổi sang truyền tọa độ dạng **chuỗi CSV rồi `double.Parse`** để né. Kết luận: danh sách số vị
   trí dài trong code là điểm yếu của LLM; typed tool với tham số JSON có tên (`{"x":0,"y":-83,"z":88.9}`) loại bỏ
   hoàn toàn lớp lỗi này.
2. **Định danh body không ổn định** [ĐO]: tra body theo `Name` / `CreatedByFeature.Name`; sau `CombineFeatures` tên đổi
   → #35 fail, #37 tra qua `ExtrudeFeatures[n].SurfaceBodies[1]`. Cần `list_bodies` trả id ổn định trong phiên.
3. **Chọn cạnh fillet** (#39, #69, #73, #74) [ĐO]: lọc `Edge` theo `Circle.Radius`, `Center.X`, loại surface hai bên
   (`Cylinder`/`BSpline`/`Plane`), `Faces.Count==2`; #69 thử 3 bán kính trong loop vì fillet fail không ném exception rõ.
   → cần edge selector theo predicate hình học (mirror `FaceSelectorSpec`).
4. **Connection survey** (#124, #126) [ĐO]: normal mặt phải hiệu chỉnh `Face.IsParamReversed`; tìm cạnh tròn đồng tâm
   trên mặt phẳng miệng ống; `MeasureTools.GetMinimumDistance` cho seating. → `probe_brep` (gap doc đã ghi đúng).
5. **Work plane fixed** (16 script) [ĐO]: mọi profile đều dựng bằng `AddFixed(origin, xAxis, yAxis)` với vector đơn vị tự
   tính và mm→cm tự chia 10. Typed tool `fixed` + đơn vị mm ở boundary xoá cả hai nguồn lỗi.
6. **Camera** (10 script) [ĐO]: không phức tạp về hình học nhưng bị ép vào send_code chỉ vì `set_view_orientation` chỉ
   nhận hướng chuẩn. Gap doc **bỏ sót** nhóm này; đây là fix rẻ nhất trong danh sách.

### B4. Hiệu năng & kích thước script send_code [ĐO]

- Thời gian: <0.5 s: 54 · 0.5–2 s: 24 · 2–5 s: 8 · 5–15 s: 4 · ≥15 s: 1. Chậm nhất: #104 TIMEOUT 30,004 ms; các call
  thành công 11,981 / 11,811 / 10,058 / 7,619 ms. Tổng 135.8 s / 162.6 s toàn run.
- Kích thước: trung bình 1,370 chars; max 6,320 (#104); 5 script ≥ 4.5K; 33 script 1K–2.5K.
- #104 (bulk appearance 6.3K chars) vẫn hoàn tất trong Inventor sau khi caller nhận TIMEOUT → xem C3.

### B5. Nên ở lại send_code

Thăm dò API (#130, #131, #136), thử nghiệm `CalculateFacets`, kịch bản one-off ghép nhiều bước có điều kiện. Tỷ lệ
send_code hợp lý sau cải tiến [GT]: **< 30%** cho cùng loại việc (mục tiêu đo ở F5).

---

## C. Mục 2 — Chất lượng tool hiện tại

### C1. Typed tools: 56 call, 54 OK [ĐO]

| Tool | n | fail | avg | max | Nhận xét |
|---|---|---|---|---|---|
| `capture_view` | 19 | 0 | 144 ms | 264 ms | tốt; 19/19 dùng `output_path` (file mode) |
| `save_document` | 9 | 0 | 921 ms | 1,511 ms | bình thường |
| `get_document_info` | 6 | 1 | 22 ms | 68 ms | fail = `NO_DOCUMENT` khi chưa mở doc — client misuse |
| `open_document` | 4 | 0 | 468 ms | 912 ms | |
| `list_open_documents` | 3 | 0 | 24 ms | 29 ms | |
| `inventor_list_available_targets` | 3 | 0 | 99 ms | 131 ms | |
| `get_iproperty` | 2 | 1 | 116 ms | 117 ms | fail: `'Summary Information' not found` — tên đúng là `Inventor Summary Information`. **Không phải client misuse**: Description của chính tool (`PropertyTools.cs:22-23`) đưa ví dụ `"Summary Information"` → agent làm đúng theo hướng dẫn sai. Defect tài liệu tool; sửa Description + nhận alias (F4-L) |
| `close_document` | 2 | 0 | 1,432 ms | 2,356 ms | |
| `new_part` | 1 | 0 | **8,898 ms** | | đáng xem: template resolve? project? — [GT], đo lại ở F5 |
| `export_stl` | 1 | 0 | 225 ms | | fallback thành công cho tessellation |
| `set_view_orientation` | 1 | 0 | 759 ms | | |
| `get_assembly_bom`, `list_constraints`, `health`, `get_current_target`, `switch_target` | 1 mỗi | 0 | | | |

Không đánh giá được **nội dung** output (không log). Typed sketch/feature/parameter tool **0 lượt** → run này không kiểm
chứng chúng.

### C2. 5 fail send_code trong log [ĐO]

| # | Giờ (UTC) | Lỗi | Phân loại |
|---|---|---|---|
| #13 | 05:04:46 | `forbidden token: Socket` | **false positive** — `Socket` nằm trong chuỗi tên body `Drain_1p5NPT_VisibleSocket` |
| #104 | 09:52:37 | `TIMEOUT: STA dispatch timed out` (30,004 ms) | **defect hợp đồng** — script vẫn chạy xong trong Inventor |
| #130 | 10:25:56 | `forbidden token: typeof(` | false positive theo mục đích (thăm dò API), đúng theo policy hiện tại |
| #131 | 10:26:11 | `forbidden token: GetType(` | như trên |
| #140 | 10:40:53 | `forbidden token: GetType(` | `ex.GetType().Name` trong catch — **false positive** |

Gap doc appendix ghi 10:40:53 là "CalculateFacets COM marshaling" — **sai**; các fail CalculateFacets (#137–#142) đều
nằm trong envelope `success=true`.

### C3. Defect hợp đồng send_code [ĐO từ code]

1. **Ba lớp timeout 30 s xếp chồng**: server `_config.TimeoutMs` (`InventorMcpConfig.cs:15`, dùng ở
   `PluginClient.cs:178`), add-in `task.Wait(env.TimeoutMs)` (`InventorAddInServerBase.cs:91-95`), CTS trong handler
   (`SendCodeHandler.cs:27, 90-96`). CTS **không thể ngắt** script đồng bộ đang gọi COM → script chạy tiếp trên STA;
   mọi request sau xếp hàng phía sau; caller chỉ nhận `"STA dispatch timed out"` không có cảnh báo trạng thái.
2. **Return value bị nuốt**: `script.RunAsync(...).GetResult()` bỏ `ReturnValue`; agent phải `Console.WriteLine` JSON
   thủ công (#136 xác nhận).
3. **Denylist quét substring thô** (`BakeCompilerPolicy.ValidateSource`, `IndexOf` không phân biệt hoa thường trên toàn
   source) → chặn `Socket` trong string literal, `GetType(` trong catch. Thông điệp lỗi `"Baked tool source uses
   forbidden token"` dùng chung cho send_code — gây hiểu nhầm.
4. **Denylist đồng thời "thủng"**: `doc.SaveAs(...)`, `Documents.Open(...)`, `TranslatorAddIn.SaveCopyAs(...)` là API
   Inventor nên không bị chặn → send_code ghi file ra `D:\Workspace\...` tự do, bypass `ExportPathPolicy` mà typed
   export phải tuân. (Quyết định thiết kế cần owner chốt — F2-d.)
5. *(Đính chính nhận định trước của Devin)* `Console.SetOut` toàn cục **không** phải vấn đề thực tế: mọi lệnh đều tuần tự
   trên STA qua `InventorStaDispatcher`, không có hai script chạy đồng thời.

### C4. Phát hiện mới: `ExportPathPolicy` chặn thư mục làm việc [ĐO]

`src/shared/Contracts/ExportPathPolicy.cs:44-51`: allowed roots = user profile + `%TEMP%` + env
`BIMWRIGHT_INVENTOR_EXPORT_ROOT`. `D:\Workspace\DailyTask\...` không nằm trong đó → mọi export/capture typed trong run
bị ép vào `%TEMP%`, SAT/DWG phải đi send_code. **`export_sat` (P0 của gap doc) một mình không unblock pipeline** nếu
không kèm cấu hình export root — gap doc bỏ sót.

---

## D. Mục 3 — Kiến trúc rào cản output lớn

### D1. So sánh [ĐO từ code]

| | rvt-mcp (`ResponseSizeGuard.cs:16-26`) | ipt-mcp |
|---|---|---|
| Cảnh báo sớm | 64 KiB `warning` / 256 KiB `strong_warning`, kèm hint thu hẹp theo lệnh (`ResponseSizePolicyCatalog`) | **không có** |
| Ngưỡng từ chối | 700 KiB compact (headroom dưới trần 1 MiB vì server pretty-print) | **5 MB** ở add-in (`InventorAddInServerBase.cs:49`) và server (`InventorMcpConfig.cs:16`); `ResponseSizeGuard.cs:7-18` chỉ có 1 ngưỡng |
| Spill ra file | `output=inline\|file`; send_code / `run_baked_tool` auto-spill vào `%LOCALAPPDATA%\RvtMcp\spill\` (TTL 24 h, cap 50) | không có |
| Paging | `ResponsePaging` | chỉ `max_rows` trên `get_assembly_bom` |

Kết luận: guard 5 MB bảo vệ **transport**, không bảo vệ **context window** (5 MB JSON ≈ >1M token). ipt-mcp chưa có
"lớp rào cản" theo nghĩa owner mô tả.

### D2. Điểm nguy hiểm cụ thể [ĐO]

1. **`capture_view` không có `output_path`** trả base64 tới 3.5 MB **nằm trong JSON text** (`Task<string>`), không phải
   MCP image content (`src/shared/Handlers/Export/CaptureViewHandler.cs:25` `MaxBase64Bytes = 3_500_000`, `:78-90`). Run thoát vì agent luôn truyền
   `output_path` — nhờ thói quen, không nhờ kiến trúc.
2. **stdout của send_code không giới hạn** — đường duy nhất không có trần nào ngoài 5 MB.
3. Mọi wrapper server serialize `Formatting.Indented` (vd `CodeTools.cs:34`) → phình 20–40% trước khi tới agent.

### D3. Tool có nặng không? [SUY — không đo được]

| Loại | Ước lượng | Cơ sở |
|---|---|---|
| Typed document/feature/parameter | nhẹ (< 2 KB) | trả tên + số |
| `capture_view` file mode | nhẹ | trả path + kích thước |
| `get_assembly_bom` / `list_constraints` | trung bình, có `max_rows` | |
| send_code stdout dump validation (#42, #43, #79, #81, #110, #126) | vài chục KB mỗi lần | features + parameters + ~60 body + bbox |
| `CalculateFacets` (nếu thành công) | **rất nặng** (per-facet) | chỉ nên tồn tại dạng export ra file |

Trong run này nhiều khả năng không có response nào vượt 64 KiB, nhưng không có gì bảo đảm điều đó khi model lớn hơn.

---

## E. Đính chính gap doc (`gap-analysis-2026-09-15-ws2-bim-run.md`)

| Vị trí | Hiện ghi | Sửa thành |
|---|---|---|
| Header | "hoàn toàn qua ipt-mcp" | "phần qua ipt-mcp (147 call); phase refinement 13:12–16:21 chạy COM trực tiếp (`scripts\WS2Refiner.cs`, `WS2Audit.cs`) — xem improvement-spec A3" |
| A. Sweep | "`SweepFeature` trong send_code" | "`SweepFeature` ×3 trong `WS2Refiner.cs` (ngoài MCP); send_code: 0" |
| A. Loft | "`LoftFeatures`" | "1 ý đồ, 4 lần thử (#16–#23) trong send_code; 3 lần trong `WS2Refiner.cs`" |
| TL;DR / D | "86 ok" | "86 ok mức envelope; ~27–30 script fail thật (retry chains, spec A2)" |
| Appendix | "10:40:53 send_code INVALID_ARGUMENT (CalculateFacets COM marshaling)" | "10:40:53 = #140 denylist `GetType(`; CalculateFacets fail nằm trong `success=true`" |
| Appendix | "2 false positive" | "3 false positive rõ (#13 string literal, #140 catch) + 2 chặn đúng policy nhưng vô ích (#130, #131)" |
| A (thêm dòng) | — | camera control (10 call, 11%) |
| A (thêm dòng) | — | extrude `new_body` + work plane `fixed` + combine + `list_bodies`/`list_features` — gốc của việc typed tool 0 lượt |
| A (thêm dòng) | — | `batch_execute` (độ hạt) |
| B (thêm dòng) | — | `ExportPathPolicy` không cho `D:\Workspace` → export_sat cần kèm cấu hình root |
| E (thêm) | — | lỗi số-thành-chữ trong 5 script — luận cứ cho typed JSON params |
| E (bỏ) | — | không nêu `Console.SetOut` là vấn đề (C3.5) |
| Ưu tiên | P1 sweep/loft | P1 → `new_body` extrude + `fixed` plane + combine + list_bodies/features; sweep/loft xuống P2 |

`roadmap.md` § Field Evidence: thêm link tới tài liệu này cạnh gap doc.

---

## F. Spec cải tiến — thực hiện từng bước

Nguyên tắc thứ tự: **đo trước (F1)** → sửa hợp đồng escape hatch (F2) → rào cản output (F3) → typed tool theo tần suất
(F4) → run kiểm chứng (F5). Mỗi bước là một branch/commit riêng, review xong mới sang bước sau. F1–F3 chốt chi tiết
ngay tại đây; F4 chốt interface từng tool **trước khi implement** (owner báo, Devin bổ sung mini-spec).

### F1. Bước 1 — Call journal đo được (`ServerLogger` v2)

**Trạng thái: implemented + reviewed (2026-09-15)** — code của owner + addendum R1–R3 đã verify:
209/209 tests xanh, journal mặc định không bị test ghi, smoke không-Inventor OK. Còn chờ: live smoke
có Inventor (3 call cuối trong checklist nghiệm thu) + commit `feat/call-journal-v2`.

**Mục tiêu.** Mỗi dòng `finish` trả lời được 3 câu hỏi mà run WS2 không trả lời được:
(1) script send_code có chạy thành công thật không; (2) response nặng bao nhiêu byte; (3) thời gian nằm ở add-in hay ở
transport/queue.

**Phạm vi.** Chỉ server: `src/server/ServerLogger.cs`, `src/server/PluginClient.cs`, test. **Không đổi add-in** → không
cần đóng Inventor, không rebuild plugin. Không đổi format dòng `start` (trừ `session_id`). Không log nội dung response
(chỉ kích thước). `success` **giữ nguyên nghĩa** (mức envelope) để không phá dữ liệu cũ.

**Thay đổi 1 — trường mới trên dòng `finish`.**

| Trường | Kiểu | Lấy ở đâu | Ý nghĩa |
|---|---|---|---|
| `error_code` | string? | `result.Error.Code` (nhánh `!result.Ok`) hoặc `InventorGatewayException.Code` (nhánh catch); null khi OK | mã lỗi tách khỏi message để đếm được TIMEOUT / RESPONSE_TOO_LARGE / INVALID_ARGUMENT |
| `target_id` | string? | `result.Meta.TargetId` (fallback `target.TargetId` đã chọn ở đầu `SendAsync`) | Inventor instance nào xử lý call — hiện không ghi, nên không biết Inventor có restart giữa run hay không (P1 phase 2) |
| `response_bytes` | long? | `Encoding.UTF8.GetByteCount(response)` ngay sau `SendLineAsync` (`PluginClient.cs:100`); null khi không nhận được dòng nào | kích thước dòng NDJSON compact từ add-in (trước pretty-print). Delivered size ≈ 1.2–1.4× |
| `plugin_duration_ms` | long? | `result.Meta.DurationMs` (do `CommandDispatcher.cs:68` điền); null nếu không parse được | thời gian handler trên STA; `duration_ms − plugin_duration_ms` = transport + chờ queue |
| `data_ok` | bool? | `result.Data["ok"]` khi `Data` là `JObject` và `ok` là `JTokenType.Boolean`; ngược lại null | kết quả **mức script** (send_code, run_baked_tool); typed tool khác → null |
| `data_error` | string? | `result.Data["error"]` khi là string; **cắt còn 300 chars** (không thêm ký tự) | lỗi compile/runtime của script |
| `stdout_bytes` | int? | UTF-8 byte count của `result.Data["stdout"]` khi là string | độ lớn stdout — dữ liệu hiệu chỉnh ngưỡng spill ở F3 |

Các trường luôn xuất hiện (giá trị `null` khi không áp dụng) để script phân tích có key ổn định. Giữ `Formatting.None`.

**Thay đổi 2 — `session_id`.** Thay hằng `"server"` bằng id sinh **một lần khi process server khởi động**, format
`server-<yyyyMMddTHHmmssZ>-<pid>` (vd `server-20260915T040830Z-18244`). Dùng cho cả `start` và `finish`. Mục đích: tách
các lần khởi động server trong cùng ngày (câu hỏi "lỗ hổng 3 giờ có phải server restart?" hiện không trả lời được).

**Thay đổi 3 — override đường dẫn log.** Env `BIMWRIGHT_INVENTOR_CALL_LOG` = đường dẫn file đầy đủ; mặc định giữ
`%LOCALAPPDATA%\Bimwright\ipt-mcp-calls.jsonl`. Chỉ env (không CLI/JSON) vì `ServerLogger` là static và khởi tạo trước
`InventorMcpConfig` được đưa vào `PluginClient`; ghi rõ giới hạn này trong CLAUDE.md. Lợi ích: test không ghi vào log
thật; owner tách log theo run.

**Gợi ý seam để test được (không bắt buộc, nhưng review sẽ hỏi nếu không có).**

```csharp
// ServerLogger.cs — pure, không I/O
internal static (bool? ok, string? error, int? stdoutBytes) ExtractDataOutcome(JToken? data);
internal static string? TruncateError(string? s, int max = 300);
internal static string ResolveLogPath(string? envOverride);   // null → %LOCALAPPDATA%\Bimwright\ipt-mcp-calls.jsonl
internal static JObject BuildFinishEntry(string sessionId, string requestId, string tool, bool success,
    long durationMs, string? error, string? errorCode, string? targetId, long? responseBytes,
    long? pluginDurationMs, JToken? data);
```

`LogFinish` giữ chữ ký cũ + tham số tuỳ chọn (`string? errorCode = null, string? targetId = null,
long? responseBytes = null, long? pluginDurationMs = null, JToken? data = null`) để 2 call site trong
`MetaTools.cs:57,63` không phải đổi.

**Tests (xUnit, `tests/Bimwright.Ipt.Tests`, file mới `ServerLoggerTests.cs`; `InternalsVisibleTo` đã có).**

1. `ExtractDataOutcome({"ok":false,"stdout":"abc","error":"compile error: CS1002"})` → `(false, "compile error: CS1002", 3)`.
2. `ExtractDataOutcome({"name":"Extrusion1"})` → `(null, null, null)`; `ExtractDataOutcome(null)` → all null;
   `ExtractDataOutcome(JArray)` → all null.
3. `ExtractDataOutcome({"ok":true,"tool_name":"x","results":[]})` (shape của run_baked_tool) → `(true, null, null)`.
4. `TruncateError(new string('x', 1000))` → length 300; `TruncateError(null)` → null; chuỗi 300 chars giữ nguyên.
5. `stdout_bytes` đếm UTF-8: `"ống"` → 5.
6. `BuildFinishEntry(...)` với send_code data `ok=false` → `success=true`, `data_ok=false`, `data_error` đúng,
   `error=null`, `error_code=null`; với typed tool → `data_ok/data_error/stdout_bytes` là `null` **và có mặt** trong JSON.
7. `session_id` khớp `^server-\d{8}T\d{6}Z-\d+$` và **giống nhau** giữa hai entry liên tiếp.
8. Đường dẫn: `ResolveLogPath(env: null)` → `...\Bimwright\ipt-mcp-calls.jsonl`; `ResolveLogPath(env: "X:\a\b.jsonl")`
   → `X:\a\b.jsonl` (tách hàm để test không phụ thuộc static ctor).

**Tiêu chí nghiệm thu.**

- [ ] `dotnet build src/IptMcp.sln -c Debug` không thêm warning; `dotnet test tests/Bimwright.Ipt.Tests -c Debug` xanh.
- [ ] Smoke live (Inventor mở, server mới build, không cần rebuild add-in): gọi `inventor_get_document_info`,
      `inventor_send_code` với `code = "var x = ;"` (lỗi compile cố ý), `inventor_send_code` với
      `code = "Console.WriteLine(app.ActiveDocument.DisplayName);"`. 3 dòng `finish` phải có: dòng 1 `data_ok=null`;
      dòng 2 `success=true, data_ok=false, data_error` bắt đầu bằng `compile error`; dòng 3 `data_ok=true,
      stdout_bytes>0`; cả 3 có `response_bytes>0`, `plugin_duration_ms>=0`, `target_id` khớp
      `inventor_get_current_target`, cùng `session_id`.
- [ ] `CHANGELOG.md` `[Unreleased] > Added`: 1 dòng mô tả journal v2 + env `BIMWRIGHT_INVENTOR_CALL_LOG`.
- [ ] `CLAUDE.md` (ipt-mcp) § Config precedence: 1 dòng về `BIMWRIGHT_INVENTOR_CALL_LOG` (env-only).
- [ ] Không refactor gì ngoài phạm vi; `MetaTools.cs` không đổi (hoặc chỉ đổi nếu bắt buộc).

**Bằng chứng nộp khi review.** Commit trên branch `feat/call-journal-v2`; output `dotnet test`; 3 dòng jsonl từ smoke
live (dán vào scratch file `docs/testing/` hoặc mô tả commit). Công cụ: `scripts/mcp-smoke.ps1` (stdio MCP client tối
giản; chuỗi mặc định = đúng 4 call của smoke F1; `-EnableSendCode`; `-LogPath` mặc định ghi vào `%TEMP%`).

**F1 addendum — phát hiện từ review lần 1 (2026-09-15), bổ sung vào phạm vi F1:**

- **F1-R1. Call `NO_TARGET` biến mất khỏi journal.** `PluginClient.SendAsync` ném `NO_TARGET` (`:78-80`) *trước*
  `LogStart` → không có dòng `start`/`finish` nào. Bằng chứng live: `inventor_get_document_info` khi Inventor tắt → 0
  dòng. Yêu cầu: sinh `requestId` + `LogStart` trước khi resolve target; `NO_TARGET` đi qua cùng try/finally →
  `finish` có `success=false, error_code=NO_TARGET, target_id=null`. Test: `PluginClient` với descriptor dir rỗng →
  `SendAsync` ném `InventorGatewayException(NO_TARGET)` và journal test (F1-R2) có dòng finish tương ứng.
- **F1-R2. Test suite ghi vào journal thật.** Các wire test (`AssemblyToolWireTests`, `MetaToolsTests`, …) chạy qua
  `PluginClient` thật → mỗi lần `dotnet test` nối 30 dòng vào `%LOCALAPPDATA%\Bimwright\ipt-mcp-calls.jsonl` (đo được:
  7 lần chạy ngày 2026-09-15 = 210 dòng, sessions `server` ×3 và `server-20260915T15{2754,2913,3526,3657}Z-*`). Yêu
  cầu: `[ModuleInitializer]` trong test project đặt `BIMWRIGHT_INVENTOR_CALL_LOG` =
  `%TEMP%\ipt-mcp-tests\calls.jsonl` trước mọi static ctor. Nghiệm thu: chạy `dotnet test` → số dòng journal mặc định
  không đổi. Việc dọn 210 dòng rác khỏi journal thật (giữ 294 dòng đầu = run WS2, hoặc archive run WS2 sang
  `ipt-mcp-calls-2026-09-15-ws2.jsonl`) do owner quyết.
- **F1-R3 (tuỳ chọn, khuyến nghị).** Meta tool trả payload `{ok:false, error:{code:"NO_TARGET"}}` nhưng journal ghi
  `success=true, data_ok=null` (bằng chứng live `inventor_get_current_target`). `MetaTools.LoggedMeta` có thể
  `JToken.Parse(result)` và truyền làm `data` → `data_ok=false` qua `ExtractDataOutcome` sẵn có. 3 dòng.
- **Ghi chú chuyển sang F2:** response do add-in tạo bằng `Err()` (`InventorAddInServerBase.cs:140-141`, dùng
  `new InventorResponseMeta()`) có `DurationMs=0`, `TargetId=null` → `plugin_duration_ms=0` cho TIMEOUT/UNAUTHORIZED
  là "không đo", không phải 0 ms thật. Khi F2 sửa `HandleLine`, điền `Meta` trong `Err`.

### F2. Bước 2 — Hợp đồng send_code

**Phạm vi.** Add-in (`SendCodeHandler.cs`, `BakeCompilerPolicy.cs`, `InventorAddInServerBase.cs`, `health` handler) +
server (`CodeTools.cs`, `PluginClient.cs`). Cần đóng Inventor và rebuild add-in cho các năm đang dùng (2027 trong run).

- **F2-a. Return value.** Handler lấy `ScriptState.ReturnValue`; nếu `!= null`: `Marshal.IsComObject(v)` → trả
  `ok=false, error="return a DTO (anonymous object / primitives / arrays), not an Inventor API object"`; ngược lại
  `data.result = JToken.FromObject(v)`. `stdout` giữ nguyên. Test: script `return new { a = 1 };` → `data.result.a == 1`
  (unit-test được phần chuyển đổi bằng cách tách hàm `ToResultToken(object?)`).
- **F2-b. Một chủ sở hữu timeout.** Bỏ CTS 30 s trong handler (không ngắt được gì). `inventor_send_code` nhận
  `timeout_ms?` (mặc định `config.TimeoutMs`, trần 600,000); server đặt `env.TimeoutMs = timeout_ms` và chờ transport
  `timeout_ms + 5,000`; add-in `task.Wait(env.TimeoutMs)` là nơi duy nhất quyết định TIMEOUT. Thông điệp TIMEOUT mới:
  `"send_code exceeded {ms} ms. The script MAY STILL BE RUNNING on Inventor's STA thread and later commands will queue
  behind it. Call inventor_health to check sta_busy before retrying; do not resend the same script."`
  `health` bổ sung `sta_busy: bool`, `pending_commands: int` (đếm bằng `Interlocked` trong `InventorStaDispatcher`).
- **F2-c. Denylist token-aware.** `ValidateSource`: loại bỏ string literal (kể cả verbatim/interpolated) và comment trước
  khi quét; khớp identifier theo ranh giới từ (`\bSocket\b` không khớp `VisibleSocket`); **cho phép** `GetType(`,
  `typeof(` (chỉ đọc metadata), **giữ chặn** `GetMethod(`/`Invoke(`/`Activator.`/`Assembly.Load`/`System.IO`/`Process`/
  `Environment.`/`Socket` dạng type. Thông điệp: `"send_code source uses forbidden token: X"` khi gọi từ send_code (truyền
  nhãn context; ToolBaker giữ chuỗi cũ). Mở rộng `SourcePolicyTests.cs`: 3 false positive của run phải pass; 6 mẫu nguy
  hiểm phải vẫn bị chặn.
- **F2-d. Đường ghi file từ send_code — ĐÃ CHỐT: chấp nhận (owner, 2026-09-15).** send_code là trusted escape hatch
  (đã "DANGEROUS, opt-in" hai lớp); không cố chặn `SaveAs(`/`SaveCopyAs(` vì không enforce được (path tính động) và tạo
  cảm giác an toàn giả. Việc cần làm (docs-only): (1) `CodeTools.cs` Description thêm câu
  `"File writes made through the Inventor API (SaveAs, SaveCopyAs, translators) are NOT restricted by the export-root
  policy that typed export tools obey."`; (2) `SECURITY.md` + README § Security: 1 dòng nêu send_code bypass
  `ExportPathPolicy` by design và vì sao gate hai lớp là đủ; (3) `CLAUDE.md` § Read-only & opt-in gates: 1 dòng.
  Không đổi code policy.
- **F2-e.** stdout cap/spill → làm ở F3 (dùng chung cơ chế).

**Nghiệm thu F2.** Build 6 add-in (hoặc tối thiểu 2027 + `plugin-inv24` compat check theo CLAUDE.md); tests
`SourcePolicyTests` mở rộng xanh; smoke: script trả `new { a = 1 }` → `data.result`; script `Thread.Sleep(40000)` với
`timeout_ms=5000` → TIMEOUT message mới, `health.sta_busy=true` trong lúc chạy, `false` sau đó.

**Status F2 (2026-09-16, implement bởi Devin, chưa commit): code xong + verify đủ — chờ commit + review.**

- Verified: `dotnet test` **261/261** (209 cũ + 52 test F2); `plugin-inv27` build 0 lỗi (6 warning CA1416
  có sẵn từ trước); `plugin-inv24` compat build (net48, interop 2027) 0 warning/0 lỗi.
- Live smoke **2026-09-16** (Inventor 2027, `inventor-2027-34876`, add-in F2 đã deploy): toàn bộ tiêu
  chí nghiệm thu pass — `result:{a:1}`, reject API object, TIMEOUT 5004 ms kèm message mới,
  `sta_busy` true→false qua fast-path health, `pending_commands`, denylist `VisibleSocket`/`GetType(`
  pass còn `System.IO` vẫn chặn. Bằng chứng đầy đủ: `docs/testing/manual-smoke.md` bước 19.
- Không verify: build `plugin-inv22/23/25/26` — máy này thiếu interop các năm đó (build báo
  `Inventor interop not found`, không phải lỗi code).
- Môi trường: 9 process `Bimwright.Ipt.Server` mồ côi (từ 12–15/9) đã được kill theo quyết định của
  owner; `src/server` build lại bình thường cả Debug lẫn Release (Release là binary mà KEI `.mcp.json`
  trỏ tới). 261/261 tests chạy trên bin chuẩn.

**Addendum F2 — lệch spec tối thiểu (đều đã ghi trong code comment):**

1. F2-a: `Marshal.IsComObject` obsolete từ .NET 8 (SYSLIB0050) → `ScriptResultToken.IsApiObject` dùng
   `Type.IsCOMObject` + check namespace `Inventor.*` (bắt cả wrapper interop không phải RCW).
2. F2-b: `InventorStaDispatcher.InvokeAsync` bỏ param `timeoutMs` vốn **không được dùng** bên trong
   (nguồn gây nhầm "3 lớp timeout"). `HandleLine` giờ điền `Meta` (target_id/year) trong mọi `Err`
   — ghi chú chuyển giao F1 — và giữ `env.Id` + unwrap `AggregateException` ở nhánh catch.
3. F2-b: `health` cần trả `sta_busy` **trong lúc** STA kẹt → không thể xếp hàng chờ STA. `HandleLine`
   cho `health` fast-path: chờ tối đa `min(env.TimeoutMs, 2000)` rồi trả payload tổng hợp từ counter
   (`answered_without_sta: true`, `has_active_document: null`). Item health vẫn ở queue và chạy sau —
   vô hại. `pending_commands` ở cả hai đường đều trừ chính request health đang đếm.
4. F2-b: `PluginClient` — connect (pipe/tcp) dùng budget `max(timeoutMs, 5000)` để `timeout_ms=1`
   không làm chết handshake loopback; chỉ nhánh **read** chờ `timeoutMs + 5000` grace.
5. F2-c: mở `typeof`/`GetType` mà chỉ chặn `GetMethod(`/`Invoke(` vẫn hở reflection-động → denylist
   mở rộng cả họ invoke/load: `GetMethods?/GetPropert(y|ies)/GetFields?/GetMembers?/GetEvents?/
   GetConstructors?`, `DynamicInvoke`, `BeginInvoke`/`EndInvoke`, `Delegate.CreateDelegate`,
   `MethodInfo`/`PropertyInfo`/`FieldInfo`. `Assembly.` thu hẹp thành `Assembly.Load*` (Load/LoadFrom/
   LoadFile…) — đọc metadata của Assembly không còn bị chặn, nhất quán với việc cho phép `typeof`.
6. F2-c: matching **case-sensitive** (C# phân biệt hoa/thường; IgnoreCase chỉ sinh false positive như
   `file.`/`process.` là biến thường). Scanner xử lý: `//`, `/* */`, char literal, string thường/
   verbatim/interpolated (`$`, `@`, `$@`, `@$`), raw string `"""` kể cả `$$"""` với hole `{{…}}`;
   **hole interpolation vẫn được quét như code**.

### F3. Bước 3 — Rào cản output (port mẫu rvt-mcp)

- **F3-a. `ResponseSizeGuard.Evaluate`** 3 mức trên payload compact tại add-in: `warning` ≥ 64 KiB, `strong_warning` >
  256 KiB, **reject** > 700 KiB với `RESPONSE_TOO_LARGE` + hint thu hẹp theo lệnh (catalog nhỏ: mặc định generic; riêng
  cho `list_*`/`get_assembly_bom`/`send_code`). Trần 5 MB ở transport giữ làm hàng rào cuối. Warning gắn vào response:
  handler luôn trả JObject (convention hiện có) → `CommandDispatcher` thêm property `size_warning` ở top-level; server
  wrapper không cần đổi.
- **F3-b. Spill.** Thư mục `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\`, TTL 24 h, cap 50 file (copy chính sách rvt).
  send_code: stdout > 64 KiB → ghi file, inline 8 KiB đầu + `stdout_truncated=true, stdout_file=...`. `run_baked_tool`
  tương tự cho `results`.
- **F3-c. `capture_view` — ĐÃ CHỐT: file mode mặc định (owner, 2026-09-15).**
  - Không có `output_path` → add-in tự sinh path `<root>\captures\capture-<yyyyMMdd-HHmmss>-<seq>.png`, với `<root>` =
    `BIMWRIGHT_INVENTOR_EXPORT_ROOT` nếu đặt, ngược lại `%LOCALAPPDATA%\Bimwright\ipt-mcp\` (nằm trong user profile →
    hợp lệ theo `ExportPathPolicy`). Response: `{ path, width, height, bytes }` — không có `base64`.
  - Inline chỉ khi caller truyền `inline=true` **và** base64 ≤ 256 KiB; vượt → `INVALID_ARGUMENT` với hint
    `"use file mode (omit inline) or reduce width/height"`. Bỏ hằng `MaxBase64Bytes = 3_500_000`.
  - Cập nhật Description ở `ExportTools.cs:26` (hiện ghi "Default: returns a base64-encoded PNG inline") và
    `set_view_orientation` (`:97`, câu "capture_view (output_path mode)" → không cần nữa).
  - Breaking change cho client dựa vào inline mặc định → CHANGELOG § Changed + bump minor version.
  - Đặt hàm sinh path mặc định + quyết định inline/file vào `CaptureImagePolicy.cs` (API-agnostic, test project đã
    compile file này) để test không cần Inventor; handler chỉ gọi policy rồi ghi file.
  - Tests: mở rộng `CaptureImagePolicyTests.cs`: (1) không `output_path`, không `inline` → path sinh đúng pattern, nằm
    trong root; (2) `inline=true` + ảnh nhỏ → có `base64`; (3) `inline=true` + ảnh > 256 KiB → `INVALID_ARGUMENT` đúng
    hint; (4) `output_path` tường minh → hành vi cũ không đổi.
- **F3-d. Định dạng output ở wrapper server — ĐÃ CHỐT theo khuyến nghị Devin (owner, 2026-09-15): helper chung +
  ngưỡng 4 KiB.**

  *Hiện trạng [ĐO].* 34 điểm `JsonConvert.SerializeObject(data, Formatting.Indented)` rải trong `src/server/Tools/*.cs`
  (mỗi class một `Call` helper riêng, vd `CodeTools.cs:29-40`). Tests không assert định dạng indented → đổi không phá
  test. Không có điểm thoát chung nào cho tool output ở server.

  *Phân tích.*
  - Indented không có ích cho agent: LLM đọc JSON compact tốt (ít token nhiễu hơn). Lợi ích duy nhất là người đọc
    transcript.
  - Chi phí phụ thuộc hình dạng payload. DTO phẳng nhỏ: +20–30% byte nhưng tuyệt đối chỉ vài trăm byte. Mảng nhiều
    object (vd `list_bodies` 60 body × 6 field ≈ 360 dòng): +30–50% byte, +12–25% token (tokenizer gộp run-of-spaces,
    nhưng mỗi dòng vẫn tốn 1–2 token). → chi phí **chỉ đáng kể khi payload lớn**.
  - rvt-mcp giữ Indented và bù bằng cách hạ ngưỡng reject xuống 700 KiB. Cách đó bảo vệ trần nhưng vẫn trả phí token
    trên mọi response lớn — đúng chỗ cần tiết kiệm nhất.
  - "Luôn None" tiết kiệm không đáng kể ở ~90% response nhỏ mà mất readability. "Luôn Indented" trả phí ở chỗ đắt.
    → **Indented khi nhỏ, None khi lớn**, ngưỡng đặt tại 4 KiB compact: dưới đó phí tuyệt đối ≤ ~1 KiB (≤ ~150 token/call).

  *Thiết kế.*

  ```csharp
  // src/server/ToolResponse.cs — điểm thoát duy nhất của mọi tool output phía server
  internal static class ToolResponse
  {
      internal const int PrettyPrintMaxBytes = 4 * 1024;          // đo trên compact UTF-8

      internal static string Serialize(object data)
      {
          var compact = JsonConvert.SerializeObject(data, Formatting.None);
          return Encoding.UTF8.GetByteCount(compact) <= PrettyPrintMaxBytes
              ? JsonConvert.SerializeObject(data, Formatting.Indented)
              : compact;
      }

      internal static string Error(string code, string message)   // thay bản sao new { ok = false, error = {...} } trong mỗi catch
          => Serialize(new { ok = false, error = new { code, message } });
  }
  ```

  - 34 site → `ToolResponse.Serialize(data)`; nhánh `catch (InventorGatewayException ex)` → `ToolResponse.Error(ex.Code,
    ex.Message)`. Shape JSON lỗi **không đổi** (`{"ok":false,"error":{"code","message"}}`).
  - Lợi ích phụ (lý do chính để làm helper thay vì đổi hằng 34 chỗ): `ToolResponse.Serialize` là nơi duy nhất để sau
    này (a) gắn `size_warning` phía server nếu F3-a cần, (b) đo delivered bytes cho journal, (c) đổi ngưỡng một chỗ.
  - Liên hệ F2-a: stdout của send_code hiện là JSON do agent tự in, bị **mã hoá hai lần** trong response
    (`"stdout": "{\"features\":[...]}"`, +10–15% ký tự escape). `data.result` của F2-a trả JSON thật → bỏ lớp mã hoá
    này; thêm một lý do cho F2-a.

  *Tests (`ToolResponseTests.cs`).* (1) payload ~100 byte → output chứa newline (indented); (2) payload ~5 KiB (mảng 200
  object) → không có newline ngoài string literal; (3) ranh giới: compact đúng 4096 byte → indented, 4097 → compact;
  (4) `Error("TIMEOUT","x")` parse lại thành `{ok:false, error:{code:"TIMEOUT", message:"x"}}`.
  *Nghiệm thu.* Grep `Formatting.Indented` trong `src/server/Tools/` → **0** kết quả; `McpProtocolSmokeTests` vẫn xanh.
  Ngưỡng 4 KiB là khởi điểm; hiệu chỉnh sau F5 bằng phân bố `response_bytes` từ F1.
- **F3-e.** Ngưỡng 64/256/700 KiB và 4 KiB là **giá trị khởi điểm**; hiệu chỉnh sau F5 bằng `response_bytes`/
  `stdout_bytes` từ F1.

**Nghiệm thu F3.** `ResponseSizeGuardTests.cs` mở rộng: 3 mức + hint; test spill (TTL, cap); `CaptureImagePolicyTests`
+ `ToolResponseTests` theo trên; smoke: send_code in 100 KB → có file spill + `stdout_truncated`; `capture_view` không
`output_path` → trả path trong `captures\`, không base64; `inventor_list_parameters` trên part nhỏ → output indented.
Thứ tự implement gợi ý trong F3: **F3-d trước** (nhỏ, tạo điểm thoát chung) → F3-a → F3-b → F3-c.

### F4. Bước 4+ — Typed tools theo tần suất trong run

Xếp theo **số lần bị ép vào send_code / COM** (cột bằng chứng), không theo cảm tính. Mỗi tool: mini-spec riêng trước
khi implement (interface, DTO, tests, smoke). Quy ước chung: input mm, output mm; DTO không COM; id ổn định trong phiên;
mọi tool `list_*` có `max_items` + `truncated` + dùng guard F3.

| Ưu tiên | Tool | Bằng chứng | Interface phác thảo (chốt ở mini-spec) |
|---|---|---|---|
| P0 | `inventor_extrude` mở rộng | 24 script | thêm `operation: new_body`; `name`; `distance` nhận số **hoặc** expression string; `affected_bodies: [id\|name]` |
| P0 | `inventor_create_work_plane` kind `fixed` | 16 script | `origin{x,y,z}`, `x_axis{x,y,z}`, `y_axis{x,y,z}` (mm; vector tự chuẩn hoá), `name`, `visible` |
| P0 | `inventor_list_bodies` / `inventor_list_features` | 36 script inspection | bodies: `id, name, volume_mm3, bbox_mm, face_count, created_by`; features: `name, type, health, suppressed, body_names`; `include_health`, `max_items` |
| P0 | `inventor_set_camera` | 10 script | `eye, target, up` (mm), `perspective`, `fit: bool`, `extents_mm?` → dùng trước `capture_view` |
| P0 | `inventor_export_sat` **+ docs export root** | 6 script + C4 | `output_path`, `acis_version` (mặc định 7.0 cho Revit); README: cách set `BIMWRIGHT_INVENTOR_EXPORT_ROOT` |
| P1 | `inventor_combine` | 9 script | `base_body`, `tool_bodies[]`, `operation: join\|cut\|intersect`, `keep_tool_bodies` |
| P1 | `inventor_batch_execute` | độ hạt B2 | tối đa 20 sub-command, một Transaction, dừng ở lỗi đầu (mirror rvt) |
| P1 | `inventor_fillet` edge selector | 6 script | `edges: {kind: circular, radius_mm?, center?, on_body?, adjacent_surface_types?}` mirror `FaceSelectorSpec`; trả `matched_edges` |
| P1 | `inventor_probe_brep` | #124, #126 + survey | mặt phẳng miệng ống → `normal` (đã hiệu chỉnh `IsParamReversed`), cạnh tròn đồng tâm, `port_diameter_mm`, `center` |
| P2 | `inventor_loft`, `inventor_sweep` | 4 script + `WS2Refiner.cs` | profiles[] sketch names; sweep: path sketch |
| P2 | `inventor_derive_envelope` | 6 script | derived part từ doc hiện tại, `include_bodies`, `shrinkwrap` |
| P3 | sketch text, `inventor_create_bim_connector`, work point | gap doc | |
| P3 (nhỏ) | `get_iproperty`: sửa ví dụ sai trong Description (`PropertyTools.cs:23`), nhận alias `Summary Information` → `Inventor Summary Information`; `list_iproperty_sets` | #9 (lỗi do Description của tool) | |

### F5. Run kiểm chứng có kiểm soát

Sau F1–F3 và các tool P0: chạy lại **cùng bài WS2** (cùng PDF, cùng agent prompt) với log v2. Chỉ tiêu:

| Chỉ tiêu | WS2 2026-09-15 | Mục tiêu |
|---|---|---|
| Tỷ lệ send_code | 62% | < 30% |
| Script-level fail (`data_ok=false`) | ~30% [SUY] | < 10% |
| Call ra ngoài MCP (COM trực tiếp) | 1 phase 189 min | 0 |
| TIMEOUT không có trạng thái | 1 | 0 (mọi TIMEOUT kèm `sta_busy`) |
| Response ≥ 64 KiB không cảnh báo | không đo được | 0 |
| False positive denylist | 3 | 0 |

---

## G. Quy trình từng bước & checklist review

1. Owner đọc mục F của bước; nếu có điểm mơ hồ, hỏi trước khi code (Devin trả lời trong 1 lượt, cập nhật spec).
2. Owner implement trên branch `feat/<bước>`; commit message nêu ID bước (vd `F1`).
3. Owner chạy lệnh build/test của bước; thu bằng chứng theo mục "Bằng chứng nộp khi review".
4. Devin review theo checklist:
   - [ ] Đúng và đủ tiêu chí nghiệm thu của bước; không thiếu trường/tests đã liệt kê.
   - [ ] Quy ước CLAUDE.md: server không tham chiếu Inventor; handler trả DTO; mm ở boundary; wire name snake_case
         không prefix; MCP name `inventor_*`.
   - [ ] Không refactor ngoài phạm vi; không đổi nghĩa trường log cũ.
   - [ ] Tests mới có, chạy xanh, và test đúng hành vi (không test implementation detail).
   - [ ] Docs: CHANGELOG; tool count sync (README ×4, root `CLAUDE.md` bảng family, `.github/profile`) **khi** số tool đổi.
   - [ ] Bằng chứng smoke live khớp mô tả.
5. Kết luận review: **approve** hoặc danh sách rework gộp một lần. Sang bước kế tiếp chỉ sau approve.

---

## Phụ lục

### P1. Timeline 147 call theo phase (UTC / giờ máy)

| Phase | Call | UTC | Giờ máy | Nội dung |
|---|---|---|---|---|
| 0 | #1–#9 | 04:08–04:09 | 11:08–11:09 | targets, list docs, `get_document_info`, `get_iproperty` ×2 |
| 1 | #10–#53 | 05:01–05:20 | 12:01–12:20 | `new_part`, reference body, housing loft, rings, save `WS2_reference.ipt`, faceplate/combine, fillet, validation dump |
| 2 | #54–#59 | 06:11–06:12 | 13:11–13:12 | re-list targets + `switch_target` → `inventor-2027-70780` (Inventor có thể đã khởi động lại [SUY]; log không ghi target của từng call → F1 thêm `target_id`), `get_document_info`, `set_view_orientation`, `capture_view` iso |
| — | — | 06:12–09:21 | 13:12–16:21 | **COM trực tiếp** (`WS2Refiner.cs`, `WS2Audit.cs`) |
| 3 | #60–#126 | 09:21–10:04 | 16:21–17:04 | three-region evidence (#63–#88), appearance (#100–#121, TIMEOUT #104), connection survey (#124, #126) |
| 4 | #127–#145 | 10:20–10:43 | 17:20–17:43 | derived part/SAT (#129–#134), return-value test (#136), CalculateFacets (#137–#142), denylist (#130, #131, #140), `export_stl` (#143) |
| 5 | #146–#147 | 11:05 | 18:05 | kết thúc |

### P2. Lỗi số-thành-chữ [ĐO]

| Call | Token |
|---|---|
| #13 | `forty: 4`, `fifty:5`, `forty:4` |
| #16 | `thirty:3` |
| #24 | `seventy:7`, `eighty:8` |
| #25 | `sixty:6` |
| #26 | `thirty:3` |

### P3. Từ khoá API trong 91 script (số script chứa) [ĐO]

`ExtrudeFeature` 24 · `SurfaceBodies` 24 · `Appearance` 23 · `HealthStatus` 18 · `WorkPlanes.AddFixed` 16 · suppression
14 · camera 13 · `Assets.Add` 12 · `CombineFeature` 9 · `FilletFeature` 6 · `CalculateFacets` 6 · `TranslatorAddIn` 6 ·
`SaveAs` 6 · property sets 6 · `LoftFeature` 4 · `Documents.Add` 4 · attribute sets 4 · `DerivedPart` 3 ·
`DerivedAssembly` 3 · `MeasureTools` 2 · `IsParamReversed` 2 · `TextBoxes` 2 · BIM component 2 · work points 1 ·
`Documents.Open` 1 · `SweepFeature` **0** · transactions 34.

### P4. Tool typed có nhưng 0 lượt dùng trong run

Sketch: `create_sketch`, `project_geometry`, `draw_line`, `draw_circle`, `draw_rectangle`, `draw_arc`,
`add_sketch_dimension`, `add_sketch_constraint`, `close_sketch`. Feature: `extrude`, `revolve`, `fillet`, `chamfer`,
`create_work_plane`, `create_work_axis`, `hole`, `circular_pattern`, `rectangular_pattern`. Parameter: `list_parameters`,
`get_parameter`, `set_parameter`, `create_parameter`. Document/property: `new_assembly`, `set_units`, `set_material`,
`set_iproperty`, `get_mass_properties`. Export: `export_step`, `export_dxf`, `view_fit`. Assembly: `list_interfaces`,
`check_interference`, `measure_min_distance`, `place_occurrence`, `add_constraint`, `create_imate`. ToolBaker: toàn bộ.
(Inventory: 59 tool theo `Name = "inventor_*"` trong `src/server/Tools/`; run dùng 17 tên khác nhau.)
