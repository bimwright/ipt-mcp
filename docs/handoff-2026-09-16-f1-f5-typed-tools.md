# Handoff — review `feat/f4-typed-tools` trước merge

2026-09-16 · Branch: `feat/f4-typed-tools` (43 commits ahead of `main`, merge-base `3302bf4`)
· Phạm vi: toàn bộ chu kỳ cải tiến ipt-mcp theo `docs/improvement-spec-2026-09-15-ws2-bim-run.md`
(F1 journal v2 → F2 send_code contract → F3 output guardrails → F4 typed tools → F5 rerun kiểm chứng)
· Diff tổng: 157 files, +11.720/−2.165 · Tests: **404/404 xanh** · Tool surface cuối: **72 default / 73 với send_code**

Người review nên đọc file này cùng improvement-spec (source of truth cho chỉ tiêu) và các
mini-spec trong `docs/superpowers/specs/` (gitignored — đính kèm trong PR nếu cần).

---

## 1. Commit map theo phase

| Phase | Commits | Nội dung |
|---|---|---|
| F1 journal v2 | `ac4e95c`, `ad6f3cc` | JSONL call journal: `error_code`, `target_id`, `response_bytes`, `plugin_duration_ms`, `data_ok`/`data_error` payload-level, `session_id`; env-only path `BIMWRIGHT_INVENTOR_CALL_LOG` |
| F2 send_code contract | `e024603` | Opt-in 2 phía (server `--enable-send-code` + plugin `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`); denylist token-aware (strip comments/strings trước khi scan); TIMEOUT do add-in `task.Wait` sở hữu duy nhất |
| F3 output guardrails | `3597dc4`, `850f1fc` | Response-size guard (≥64 KiB `size_warning`, >700 KiB `RESPONSE_TOO_LARGE`); output spilling 64 KiB → `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\`; `ExportPathPolicy` (capture/export chỉ ghi dưới allowed root + `BIMWRIGHT_INVENTOR_EXPORT_ROOT`); atomic capture reservation |
| F4 P0 | `f2bad45`, `867e87e`, `f7946ee`, `f2c14a0` | extrude v2 (`affected_bodies`, `new_body`, `name`, expression distance) · fixed work plane · `list_bodies`/`list_features` · `set_camera` · `export_sat` |
| F4 P1 | `deed410`, `a866472`, `5c8408b`, `5867921` | `combine` · `batch_execute` (1 transaction, rollback) · fillet edge selector + circle-profile fix · `probe_brep` |
| F5 bug fixes | `d8c96be` | 3 bug tìm được trong rerun (xem §4) |
| F4 P2 | `696c36a`, `526f7b5` | `derive_envelope` · `loft` + `sweep` |
| F4 P3 | `ff73ee5`, `6912643` | `list_iproperty_sets` + iProperty alias fix · `draw_text` + `create_work_point` + `create_bim_connector` + probe `circles[].edge` |

Lưu ý: branch còn chứa các commit trước đó của cùng line phát triển (assembly tools, capture file
mode, …) — phần đó đã qua các vòng review riêng; phạm vi review mới là F1→F5 (17 commit đầu bảng).

## 2. Kết quả F5 — chỉ tiêu spec

Rerun có kiểm soát cùng bài WS2 (journal `WS2_f5\ipt-mcp-calls.jsonl`, report
`WS2_f5\F5-report.md`):

| Chỉ tiêu | Run-1 | Mục tiêu | F5 | |
|---|---:|---:|---:|---|
| send_code rate | 62% | <30% | **18.8%** (9/48) | ✅ |
| script `data_ok=false` | ~30% | <10% | **22%** (2/9) | ❌ |
| direct COM ngoài MCP | 189 min | 0 | **0** | ✅ |
| TIMEOUT không status | 1 | 0 | **0** | ✅ |
| response ≥64 KiB không cảnh báo | ? | 0 | **0** | ✅ |
| denylist false-positive | 3 | 0 | **0** | ✅ |

Miss duy nhất: cả 2 compile-error đều ở bước derive envelope — đúng tool chưa tồn tại lúc đó;
đã đóng bằng `inventor_derive_envelope` (P2-1). Nếu rerun lại kỳ vọng script-fail ≈ 0.

## 3. Live verification đã chạy (tất cả trên Inventor 2027, add-in deployed)

- `derive_envelope`: default 9→9 bodies, `include_bodies` 2→2, `bounding_box+single_no_seams`→1,
  unknown body → INVALID_ARGUMENT kèm tên thật. (`inventor-2027-39104`)
- `loft`: frustum volume **khớp tuyệt đối** công thức giải tích (29321.53 & 3518.58 mm³);
  `sweep` path 3-segment tự chain (`path_entity_count=3`), volume 1446.35 = 25 mm² × 57.85 mm.
  (`inventor-2027-72672`)
- `list_iproperty_sets`: 4 sets đầy đủ; alias `"Summary Information"` → canonical.
  (`inventor-2027-63280`)
- `draw_text`/`create_work_point`/`create_bim_connector`: full pass + error paths;
  annulus tube → probe `circles[].edge` → connector `conn_in`. (`inventor-2027-88488`)

Build matrix verify: `plugin-inv24` (net48, compat check với 2027 interop) + `plugin-inv27`
(net10) đều 0 lỗi sau mỗi lần thêm handler. Plugin inv22/23/25/26 chưa build lại — cùng shared
source nên rủi ro thấp, nhưng reviewer có thể muốn một lượt build đủ 6 shell trước merge.

## 4. Bug thật tìm được & đã fix (`d8c96be`)

1. **Substring routing**: work plane tên chứa "face"/"vertex" (`wp_faceplate`) bị route nhầm
   sang face/vertex index parser → rollback cả batch. Fix bằng `EntityResolver.IsEntityRef`
   (strict `prefix:N` / `body:B/prefix:N`) áp cho 3 call sites. Đã verify live.
2. **Export false-positive**: `SaveCopyAs` fail ngầm khi parent dir thiếu → `exported:true`
   giả. Giờ tạo parent dir + verify `File.Exists` — cover cả step/stl. Verify live.
3. **`extrude volume_mm3`**: báo tổng part volume → giờ báo volume body feature tạo/ảnh hưởng
   (`feature.SurfaceBodies` last, fallback part total). Verify live (1570.8 = π·10²·5).

## 5. API quirks của Inventor cần reviewer biết (đều đã ghi trong mini-spec/CHANGELOG)

- `LoftDefinition.LoftType` **read-only** — centerline loft set qua `def.Centerline`.
- `SweepFeatures.CreateSweepDefinition` overload 4-arg tồn tại nhưng reflection dump ban đầu
  bị cắt — E_FAIL của `Add` thực chất là geometry (profile plane chứa path / self-intersect),
  handler giờ wrap kèm hint thay vì trả "Unspecified error" trần.
- `TextBox.Rotation` **chỉ chấp nhận bội số π/2** (scan 13 giá trị: 0/±π/2/π/3π/2/2π OK —
  1.57 fail nhưng 1.5708 pass → tolerance ~1e-3). `rotation_deg` validate multiple-of-90
  phía client + snap về quadrant chính xác.
- `WorkPoint.Name` setter **silently no-op trên construction points** (`Construction`
  read-only post-create; object không implement `PartFeature` → E_NOINTERFACE). Response có
  `name_applied` để không nuốt intent.
- `BIMPipeConnectorDefinition`/`BIMConnectorDefinition` là **sibling COM interfaces** —
  `Connectors.Add` cần explicit cast (QI cùng underlying object).
- SurfaceBody naming share 1 browser namespace với feature — `new_body` đặt `<name>_body`.

## 6. Deferred có chủ đích (đã ghi trong spec + CHANGELOG — không phải sót)

- loft: guide rails, section conditions, area-graph sections
- sweep: guide rail/surface, section twist, `affected_bodies`
- derive_envelope: `DerivedAssembly` path cho .iam nguồn (v1 đã cover part+assembly qua
  component-derive), `use_oriented_min_bounding_box` đã có
- bim_connector: kind `duct|conduit|cable_tray|electrical`, connector links
- work_point: ref-driven variants (AddByTwoLines/ThreePlanes/Point/Centroid/…)
- draw_text: `AddByRectangle` boxed text, justification, styles object
- **Regen `mcps/ipt-mcp/tools/*.json`** — chờ đến release (đã defer ở P0-3 review)

## 7. Điểm nên review kỹ (risk areas)

1. **`EntityResolver.IsEntityRef`** — thay đổi routing chung; đã grep toàn `src/` không còn
   substring-matching sót lại, nhưng reviewer nên xác nhận không call site nào cần hành vi cũ.
2. **`batch_execute` rollback semantics** — Abort transaction trên lỗi; file đã ghi bởi
   export/capture sub-command không được xóa (ghi trong docs).
3. **`send_code` denylist tokenization** — strip comments/strings rồi word-boundary match;
   rủi ro là quá lỏng (bypass qua obfuscation) hoặc quá chặt (false positive). F5 đo được 0
   false-positive nhưng bypass-resistance chưa có adversarial test.
4. **`ResponseSizeGuard` thresholds** (64 KiB warning / 700 KiB fail) — chọn theo MCP client
   thực tế; reviewer nên sanity-check con số.
5. **`probe_brep` edge refs** — dựa trên RCW identity (`ReferenceEquals` giữa `face.EdgeLoops`
   edges và `body.Edges`). Đã verify live trên 2027; chưa verify trên interop 2022-2024.
6. **Multi-version**: inv22/23/25/26 chưa build lại sau các handler mới (xem §3).

## 8. Trạng thái hiện tại

- `git status` sạch; mọi thứ đã commit trên `feat/f4-typed-tools`.
- Server Release exe + plugin inv27 DLL đang deployed và chạy được trên máy dev
  (Inventor 2027, target `inventor-2027-88488`).
- Chưa push / chưa mở PR — chờ review này.
- `docs/superpowers/` gitignored: 3 mini-spec P2/P3 tồn tại local, cần đính kèm nếu reviewer
  muốn đọc design contract chi tiết.
