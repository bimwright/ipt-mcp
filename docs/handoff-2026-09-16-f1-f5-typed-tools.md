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
- derive_envelope: `include_occurrences`-style per-occurrence pick cho .iam (hiện assembly
  derive include-all hoặc bounding-box toàn occurrence); `use_oriented_min_bounding_box` đã có
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
  (Inventor 2027, target `inventor-2027-52228` sau redeploy review-fix).
- Chưa push / chưa mở PR — chờ review này.
- `docs/superpowers/` gitignored: 3 mini-spec P2/P3 tồn tại local, cần đính kèm nếu reviewer
  muốn đọc design contract chi tiết.

---

## 9. Review findings vòng 1 → fixes (commit `fix(plugin,server): external review round 1 …` — tip của branch)

External review báo 6 findings (1 spec P1 + 5 P2). Tất cả đã fix + verify live trên
`inventor-2027-52228` (plugin inv27 DLL rebuild + redeploy):

| # | Finding | Fix | File |
|---|---|---|---|
| 1 | `derive_envelope` chạy được trong `batch_execute` — phá scope rollback (tạo+kích hoạt doc mới giữa transaction của doc cũ) | Thêm vào `BatchExecutor.BlockedCommands`; tool description liệt kê | `BatchExecutor.cs`, `FeatureTools.cs` |
| 2 | Derive fail để lại part rỗng active → retry không `source_path` nhắm nhầm doc | Capture `prevActive` trước `Documents.Add`; catch đóng doc mới (`Close(skipSave:true)`) + `prevActive.Activate()` khi `activate=true`; cleanup nuốt lỗi riêng, không che lỗi gốc | `DeriveEnvelopeHandler.cs` |
| 3 | Contract nhận `.iam` nhưng luôn `DerivedPartComponents` | Branch `.iam` thật qua `DerivedAssemblyComponents`/`DerivedAssemblyDefinition` (`DeriveStyle`, `UseOrientedMinimumBoundingBox`, `IncludeAllTopLevelParameters`, `InclusionOption`=`kDerivedBoundingBox`+`RemoveInternalVoids` cho envelope); `include_bodies` reject trên `.iam`; response thêm `source_type` + `occurrences` | `DeriveEnvelopeHandler.cs`, `ExportTools.cs` |
| 4 | `list_iproperty_sets` không giới hạn | `max_items` (default 200, tổng properties across sets), `truncated`, `properties_total`, per-set `properties_omitted`; server param `max_items` | `ListIPropertySetsHandler.cs`, `PropertyTools.cs` |
| 5 | Journal ghi raw secret (trái SECURITY.md) | `ServerLogger.MaskParams`: clone JToken, key tên credential → `***`, mọi string qua `SecretMasker.Mask`; finish `error`/`data_error` qua `ErrorSanitizer`; `SecretMasker` thêm key-value (`password="…"`, `api_key: '…'`, …) + `Bearer <tok≥8>` | `ServerLogger.cs`, `SecretMasker.cs` |
| 6 | Spill ghi trước khi dispatcher sanitize | `SanitizeErrorFields` public trên `ErrorSanitizer` (single source, dispatcher gọi lại); `AttachResults` sanitize từng result + `SecretMasker.Mask` serialized trước khi ghi file/preview | `ErrorSanitizer.cs`, `CommandDispatcher.cs`, `ResponseSpillWriter.cs` |

**Regression tests** (410/410 xanh, trước đây 404):

- `BatchExecutorTests`: `derive_envelope` vào blocked theory.
- `ResponseSpillTests`: `AttachResults_SanitizesErrorFieldsBeforeSpill` — path + fake token
  trong step error >64 KiB → vắng mặt ở file spill lẫn `results_preview`, `<path>` hiện diện;
  pad test cũ đổi sang `'.'` để không trip masker.
- `ServerLoggerTests`: `MaskParams*` (shape giữ nguyên, clone không mutate caller, nested/array),
  `FinishEntryMasksSecretsInErrorAndDataError`, `LogStartMasksSecretsInJournalLine` (đọc
  journal thật); `DataErrorIsTruncatedTo300Chars` đổi pad sang `'!'`.

**Live verify** (`revfix_smoke.py`, target `inventor-2027-52228`):

- batch chứa `derive_envelope` → step fail `"cannot run inside batch_execute"`, `rolled_back:true`.
- `include_bodies:["nope"]` → INVALID_ARGUMENT + doc info sau đó vẫn là source; retry bỏ
  `source_path` resolve đúng `WS2_reference.ipt` (không còn empty doc nuốt source).
- `.iam` derive → `source_type:"assembly"`, `occurrences:1`, `body_count:1`, saved;
  `.iam + include_bodies` reject trước khi tạo doc.
- `list_iproperty_sets max_items=5` → `truncated:true`, `properties_total`, per-set `properties_omitted`.
- `set_iproperty` value chứa `Bearer <28-char>` → journal start line mask sạch.
- batch 19×`probe_brep` + `create_sketch` plane=`C:\nonexistent\…` → spill 300 KB;
  file chứa `<path>`, không chứa raw path; preview sạch.

**Lưu ý cho reviewer:**

- `AttachStdout` (send_code stdout) cố ý không sanitize — stdout chưa từng qua sanitizer ở bất
  kỳ đâu (inline hay file đều cùng nội dung); thêm mask vào sẽ đổi nội dung inline nhìn thấy.
  Nếu muốn stdout spill cũng mask thì đó là quyết định policy riêng.
- `SecretMasker` key-value pattern giờ bắt `authorization`, `token`-family, `password`, `api_key`
  … — có thể over-mask một identifier trùng tên trong code string (chỉ ảnh hưởng journal/spill,
  không ảnh hưởng payload trả về).
- `ListIPropertySets` `max_items` đếm **properties** (không phải sets) — `count` vẫn là số
  property thật của set, `properties` là phần đã emit.
