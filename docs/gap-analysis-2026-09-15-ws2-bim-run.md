# Gap analysis — WS2 BIM-authoring run (2026-09-15)

> **Nguồn bằng chứng:** `C:\Users\Admin\AppData\Local\Bimwright\ipt-mcp-calls.jsonl` — 147 tool calls
> trong một session dựng model Clack WS2 (control valve) từ PDF manual, hoàn toàn qua ipt-mcp.
> Cross-ref pipeline: `D:\Workspace\ai-skills-registry\KEI-departments\R&D\pdf-to-bim-family-pipeline\`.
> Model artifacts: `D:\Workspace\DailyTask\2026.09\15\WS2_model\`.

## TL;DR

Typed tools chỉ phủ phần plumbing (document lifecycle, capture, targets). **Toàn bộ modeling thật
chạy qua `send_code`: 91/147 calls (62%).** Escape hatch hoạt động tốt nhưng chính nó có 3 vấn đề
ergonomics đáng sửa. Gap lớn nhất cho workflow BIM: **không có `export_sat`** (blocker trực tiếp
khi đưa geometry sang Revit) và **không có face/edge-level probe** (nền của mọi connection survey).

## A. Missing typed tools — buộc dùng send_code

| Capability thiếu | Bằng chứng run (call # trong jsonl) | Repo status |
|---|---|---|
| Sweep | Drain elbow: `SweepFeature` trong send_code | roadmap deferred ("Advanced features") |
| Loft | Lower taper/piston envelope: `LoftFeatures` | roadmap deferred |
| Derived component / shrinkwrap | #76–82: `DerivedPart` fail 2× (denylist) → `DerivedAssembly` qua iam tạm | **không có trong roadmap** — đây là đường làm "envelope" nhẹ cho BIM export |
| Combine/boolean (join/cut) | `CombineFeatures` ×9 calls | roadmap deferred |
| Work point | Cần cho connector anchors | roadmap deferred |
| Sketch text / emboss | Panel lettering `Label_WS2`, `Panel_CLACK` | không có trong roadmap |
| BIM connector authoring (part level) | Survey: model có 0 BIM connectors; không tool nào author connector/iMate mức part | **vắng hoàn toàn** |

## B. Export gaps

| Thiếu | Bằng chứng |
|---|---|
| **`export_sat`** | `src/server/Tools/ExportTools.cs:43–83` chỉ có step/stl/dxf. SAT là format duy nhất Revit `ShapeImporter` đọc tốt (ACIS ≤7.0). Run phải tự gọi `TranslatorAddIn` trong send_code (#79–82, #91). **Đề xuất: 1 handler bọc SAT translator — effort nhỏ, unblock cả pipeline BIM.** |
| (ghi chú) Inventor→DWG | Inventor xuất "Inventor DWG" = ASM custom entities, không phải AcDb3dSolid → Revit import rỗng. Giới hạn Autodesk, không phải bug repo — nhưng nên ghi vào docs để agent không đi đường này. |

## C. Missing inspection / audit tools

| Thiếu | Bằng chứng run |
|---|---|
| Feature list + health report | `HealthStatus` iterate bằng tay (#53, #64, #74). Roadmap có "model health" ở deferred Diagnostics — run này chứng minh nó là nhu cầu thật, không phải nice-to-have |
| Face/edge probe (BRep) | Connection survey cần: detect cạnh tròn đồng tâm trên mặt phẳng miệng ống, face normal có hiệu chỉnh `Face.IsParamReversed`. Hoàn toàn send_code. `measure_min_distance` hiện chỉ mức body |
| Floater/nearest-neighbor audit | `DistanceTo` loops viết tay (#75) — bắt được 6 floaters + 14 effectively-floating bodies |
| Per-body enumeration | `SurfaceBodies` iterate tay mọi lần |
| Tessellation/facet per face | `Face.CalculateFacets` COM marshaling lỗi (#87, `INVALID_ARGUMENT`); workaround duy nhất là `export_stl` mức document |

## D. Appearance / material

`inventor_set_material` chỉ set physical material theo tên (`src/server/Tools/DocumentTools.cs:51–54`).
Không có tool: tạo appearance asset, per-body/per-face appearance, RGB/gloss/texture.

**Bằng chứng:** 27/91 send_code calls cho appearance; #62 (09:52:37) **STA timeout 30s** khi
bulk-assign 60 bodies — timeout duy nhất của run.

## E. `send_code` ergonomics — escape hatch tự nó cần sửa

| Vấn đề | Bằng chứng (file:line) |
|---|---|
| Timeout cứng 30s, hậu quả mập mờ | `src/shared/Handlers/Code/SendCodeHandler.cs:27` `ExecutionTimeoutMilliseconds = 30000`. Error "execution cancelled after 30s" nhưng script **vẫn chạy tiếp** phía Inventor (STA dispatch đã đi) — caller không biết là đã rollback hay đang chạy ngầm. Đề xuất: timeout cấu hình được + báo rõ "script may still be running, verify state". |
| Không có return value | `SendCodeHandler.cs:98–103` chỉ trả `{ok, stdout, error}`. Script `return x` không echo — phải `Console.WriteLine`. Đề xuất: capture `script.RunAsync` ReturnValue vào `data["result"]` (serialize DTO-safe). |
| Denylist substring quá thô | `src/shared/ToolBaker/BakeCompilerPolicy.cs:19–40`: `IndexOf` case-insensitive trên raw token. #77/#78 bị reject vì `typeof(`/`GetType(` — dùng cho enum discovery hợp pháp (`DerivedComponentStyleEnum`), không phải sandbox escape. Chữ "socket" trong comment cũng trip. Đề xuất: strip comments/string literals trước khi scan, hoặc Roslyn-based token analysis (đã có Roslyn trong process). |
| Thông điệp lệch context | Error text "Baked tool source uses forbidden token: X" xuất hiện trên `send_code` call thường — do share `BakeCompilerPolicy`. Đề xuất: truyền context label vào `ValidateSource`. |
| `Console.SetOut` global mutation | `SendCodeHandler.cs:59–61, 140–143` — process-wide; 2 send_code concurrent sẽ trộn stdout. Đề xuất: serialize send_code bằng semaphore hoặc ghi nhận limitation. |

## F. Works well — không phải gap

Document lifecycle (22 calls, 0 fail thật), `capture_view` (19×, avg 144ms), target
discovery/switch, `export_stl`, `new_part`. 2 fails còn lại là client misuse
(`get_document_info` khi chưa có active doc; `get_iproperty` sai tên property set —
gợi ý thêm `list_iproperty_sets`).

## G. Đề xuất ưu tiên implement (theo tần suất đau)

| Pri | Tool / fix | Lý do |
|---|---|---|
| P0 | `inventor_export_sat` | Blocker trực tiếp của Revit bridge; bọc `TranslatorAddIn` "SAT" là xong |
| P0 | send_code: trả return value + timeout configurable + báo "may still be running" | Sửa cả 3 ergonomics issues bằng một pass |
| P1 | `inventor_sweep`, `inventor_loft` | ~15 send_code calls trong run; 2 feature types phổ biến nhất còn thiếu |
| P1 | `inventor_probe_brep` (face normals + edge loops + IsParamReversed) | Nền của connection survey — mọi BIM authoring đều cần |
| P1 | Denylist token-aware (bỏ trip trên comments/strings) | 2 false-positive blocks trong run |
| P2 | `inventor_derive_envelope` (part→iam→derive single solid) | Pattern lặp lại được cho "lightweight BIM export" |
| P2 | `inventor_list_features` + health report | Audit loop mọi run đều viết lại |
| P3 | `inventor_create_bim_connector` (part-level) | Dài hạn — author BIM ngay trong Inventor |
| P3 | `inventor_list_iproperty_sets` | Tránh fail do đoán tên property set |

## Appendix — failure log trích từ `ipt-mcp-calls.jsonl`

```text
05:04:46 send_code  INVALID_ARGUMENT: Baked tool source uses forbidden token: Socket
09:52:37 send_code  TIMEOUT: STA dispatch timed out            (bulk appearance; completed anyway)
10:25:56 send_code  INVALID_ARGUMENT: ...forbidden token: typeof(
10:26:11 send_code  INVALID_ARGUMENT: ...forbidden token: GetType(
10:40:53 send_code  INVALID_ARGUMENT (CalculateFacets COM marshaling)
--        get_document_info NO_DOCUMENT (client misuse — no active doc)
--        get_iproperty    INVALID_ARGUMENT: property set 'Summary Information' not found
```
