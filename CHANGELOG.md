# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `inventor_report_task_result` (`query`, read-only): the agent reports one job's outcome explicitly — `task_id` (1–80 characters, unique per agent/job), `outcome` (`completed`, `failed`, or `cancelled`), and a single-line `summary` (1–120 characters; control and Unicode format characters such as bidi overrides are rejected). The call does not modify the model and is STA-independent — it answers on the listener thread, so the report still lands while the STA is jammed behind a timed-out `send_code`. Its toast is labelled **Agent reported**, coloured by outcome (`failed` renders as an error card; `cancelled` gets a neutral icon, neither a tick nor an error mark), and the response carries `toast_shown` so the agent knows whether a card was actually retained for display — a card held while Inventor is minimized or behind a modal dialog counts as retained and appears once the frame is usable. Idle time and a successful tool call do not imply the job is finished. Tool surface is now 73 default / 74 with send_code.
- **Toast notifications in Inventor.** While Inventor is visible, command results update at most three retained cards, at most twice a second, instead of queueing one toast per tool. Routine successes share an activity card with a count and the latest tool; reads are blue (`MCP · Query`), writes green (`MCP · Modified`), failures red (`MCP · Failed`, including soft failures such as `send_code` returning `ok:false`, a rolled-back `batch_execute`, or a `failed` task report). Identical errors share a card and show an occurrence count. Failures outrank explicit task summaries (`completed`/`cancelled`), which outrank snapshots or exports, which outrank routine activity. When the three slots are full, the lowest-priority oldest card is replaced — a task report may evict even an error, since a report exists to be seen — while routine arrivals are dropped and not replayed later. The count covers that card's lifetime on the Inventor target. `inventor_capture_view` cards keep a clickable thumbnail. Cards age out a few seconds after their last displayed update (hover pauses). `inventor_health` never toasts. Toasts run on a dedicated UI thread woken on demand by feed changes, are posted only after the command response is handed back, never steal focus, hide while Inventor is minimized or blocked by a modal dialog, and stay topmost over other applications. Card colours follow an auto palette sampled from pixels beside the toast — a painted card is never its own backdrop (in-memory only — nothing is written or logged). The BIMwright wordmark rests dimmed, then a lit front wipes left→right once ~1.3 s after the card appears — after the reader's eye has had time to arrive. Ribbon control: **Bimwright ▸ MCP → Toasts** toggle (persisted to `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` as `enableToast`, default on; `toastTheme` = `auto`/`light`/`dark`; env `BIMWRIGHT_INVENTOR_ENABLE_TOAST` / `BIMWRIGHT_INVENTOR_TOAST_THEME` override) and **Status** diagnostic dialog.
- **MCP Command History in Inventor (rvt-mcp parity).** Every authorized wire command is journaled to `%LOCALAPPDATA%\Bimwright\ipt-mcp\mcp-calls.jsonl` — one JSON line per call (UTC timestamp, per-launch `session_id`, tool, success, duration, sanitized error, redacted params, capped result), rotated at 5 MB with a format-version marker — and appended to a bounded in-memory session log (1000 entries, oldest evicted). **Bimwright ▸ MCP → History (N)** shows the live session count — Inventor has no settable ribbon caption, so the button definition and its bound controls are recreated in place on each count change — and opens **BIMwright · MCP Command History**, a WPF window on its own UI thread (same pattern as the toast host, so it stays responsive while a modal holds Inventor's STA). The window offers search, success/failure and read/write kind filters, a detail pane (WHAT/INPUT/OUTPUT, JSON pretty-print, numbered code view), Clear Session (drops live rows, keeps loaded history), Open logs, and Load past sessions — rotated archives plus the current journal merge back as read-only rows tagged by session and deduplicated against the live tail. Re-run re-executes a live entry through `InventorStaDispatcher` and diffs numeric result fields against the original; historical and params-truncated entries are view-only. Privacy matches rvt-mcp: `send_code` bodies are redacted to `{code_hash, code_length}` unless `BIMWRIGHT_CACHE_SEND_CODE_BODIES=1`, and `BIMWRIGHT_PERSIST_SEND_CODE_BODIES` (+ `BIMWRIGHT_PERSIST_SEND_CODE_BODIES_TTL`, default 4 h) keeps bake-redacted bodies in `send-code-journal.jsonl` so re-run can recover them — the window warns that a recovered body is redacted and may behave differently.

## [0.2.0] - 2026-09-16

### Added

- `inventor_draw_text` (sketch toolset, write): add a fitted text box to a sketch — `text` + `position` [x,y] mm, `sketch_name` optional (defaults to the most recent sketch), `font_size_mm` wraps the text in a `<StyleOverride FontSize='N mm'>` tag, `rotation_deg` is validated client-side to a multiple of 90 because Inventor's `TextBox.Rotation` rejects arbitrary angles (quadrant rotations only — verified live: 0/±π/2/π/3π/2/2π accepted, everything else E_INVALIDARG). Tool surface is now 72 default / 73 with send_code.
- `inventor_create_work_point` (feature toolset, write): create a fixed work point at `position` (`{x,y,z}` or `[x,y,z]`, mm) via `WorkPoints.AddFixed`; `construction`, `name`, `visible` optional. Note: Inventor silently refuses to name construction work points — the response reports `name_applied` so a dropped name can't go unnoticed. Reference-driven work points (by planes/lines/centroid/…) are deferred — no run evidence.
- `inventor_create_bim_connector` (feature toolset, write): author a BIM pipe connector on a circular port edge — `geometry` is an edge ref (`body:N/edge:M`, e.g. from `inventor_probe_brep`'s `circles[].edge`), `kind=pipe` only for now (duct/conduit/cable-tray/electrical deferred). Optional `nominal_diameter_mm`, `system_type`, `flow_direction`, `connection_type`, `description`, `name`. Returns `connector_name`.
- `inventor_probe_brep` now reports a resolvable `edge` ref (`body:B/edge:N`) per circle in `circles[]`, so its output feeds `create_bim_connector`'s `geometry` and `fillet`'s `edgeIds` directly.
- `inventor_list_iproperty_sets` (properties toolset, read-only): list the active document's iProperty sets — each set's `name`, `internal_name`, `count` and property `name`s (plus `display_name` when it differs); `include_values` adds each property's value as a string capped at 200 chars. Discovery companion for `get_iproperty`/`set_iproperty`. Tool surface is now 69 default / 70 with send_code.
- `inventor_loft` (feature toolset, write): loft an ordered list of sketch profiles into a feature — `profiles` are sketch names in loft order (`"SketchName"` or `"SketchName:N"` for the Nth profile of a multi-profile sketch, ≥2), `operation=join|cut|intersect|new_body`, optional `centerline` sketch (its first curve flips the definition to a centerline loft — `LoftType` is read-only), `merge_tangent_faces` (default true), `closed` (periodic loft), `name` (with `new_body` the body is named `<name>_body`). Returns `feature_name`, `section_count`, `body_name?`, and `volume_mm3` of the feature body. Guide rails, section conditions and area-graph sections are not yet exposed.
- `inventor_sweep` (feature toolset, write): sweep a sketch profile along a sketch path — `profile` resolves like loft (`"SketchName[:N]"`), `path` is a sketch name whose first non-construction curve seeds `SweepFeatures.CreatePath` (connected segments chain automatically — `path_entity_count` in the response reports the resolved segment count), `operation` as above, `orientation=normal_to_path|parallel`, `name`. Guide rail/surface and section-twist sweep types are not yet exposed. Tool surface is now 68 default / 69 with send_code.
- `inventor_derive_envelope` (export toolset, write): create a new part document containing a derived-component feature from a source part/assembly — the envelope/interop path. `source_path` defaults to the active document (must have been saved); `output_path` is an absolute .ipt under an allowed root; `derive_style=multiple|single_seams|single_no_seams`; `include_bodies` (names or `body:N`) selects source solids via `DerivedPartEntity.IncludeEntity`; `bounding_box=true` derives every solid as its bounding box (`kDerivedBoundingBox` — the lightweight-envelope mode); `include_parameters` carries source parameters; `use_oriented_min_bounding_box`; `activate=false` creates the document hidden. Returns the document title, saved path, solids total/included, and `body_count`. Tool surface is now 66 default / 67 with send_code.
- `inventor_send_code` returns the script's `ScriptState.ReturnValue` as `data.result` (JSON-safe: primitives, arrays, anonymous DTOs, `JToken` pass-through; Inventor API/COM objects are rejected with DTO guidance). `inventor_send_code` accepts an optional `timeout_ms` (default = `--timeout-ms`, clamped to 1..600000) carried on the command envelope — the add-in's `task.Wait` is the single owner of the TIMEOUT decision; the transport only adds a 5 s read grace. TIMEOUT responses now explain that the script may still be running on the STA thread and point at `inventor_health`. `inventor_health` reports `sta_busy` and `pending_commands` and answers even while the STA thread is jammed (2 s fast-path synthesized from queue counters, marked `answered_without_sta`).
- Call journal v2: `finish` lines now record `error_code`, `target_id`, `response_bytes`, `plugin_duration_ms` and the payload-level outcome (`data_ok`, `data_error`, `stdout_bytes`) of `send_code` / `run_baked_tool` and of meta tools whose payload carries `ok` (`success` stays envelope-level); calls that fail before reaching a target (`NO_TARGET`) are journaled too; `session_id` identifies each server start (`server-<utc>-<pid>`); the journal path can be overridden with env `BIMWRIGHT_INVENTOR_CALL_LOG` (the test suite redirects it to `%TEMP%`).
- **Response-size guard (add-in):** responses ≥64 KiB carry a top-level `size_warning` ("warning", "strong_warning" >256 KiB); above 700 KiB the call fails with `RESPONSE_TOO_LARGE` plus a command-specific narrowing hint (`max_rows`, `occurrence=<name>`, send-code spilling, …). The 5 MB transport fence is unchanged.
- **Output spilling:** `send_code` stdout and `run_baked_tool` results over 64 KiB are written to `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\` (24 h TTL, 50-file cap); the response keeps the first 8 KiB inline plus `stdout_truncated`/`stdout_file` (or `results_truncated`/`results_file`/`results_count`/`results_preview`).
- `inventor_capture_view` gains an `inline` parameter (default `false`).
- `inventor_extrude` accepts `operation=new_body`, a `name` for the created feature (with `new_body` the body is named `<name>_body` and echoed as `body_name` — Inventor shares one browser namespace, so the body cannot take the feature's name), `distance` as a number (mm) **or** an Inventor expression string (`"40 mm"`, `"plate_thk"`), and `affected_bodies` (`["1","body:2","BodyName"]`) scoping join/cut on multi-body parts. Fixed `volume_mm3` always returning 0 (`SurfaceBody.get_Volume` rejects precision 0.0).
- `inventor_create_work_plane` gains `type=fixed` (`AddFixed`): `origin`/`x_axis`/`y_axis` accept `{x,y,z}` or `[x,y,z]` (origin in mm, axes are direction vectors auto-normalized; zero/parallel axes rejected); optional `name` and `visible` apply to all work-plane types; `refs` stays required for the other types.
- `inventor_list_bodies` and `inventor_list_features` (read-only, `query` toolset — visible under `--read-only` and callable from baked tools): bodies report `id` (`body:N`), `name`, `volume_mm3`, `bbox_mm`, `face_count`, `created_by` (producing feature), `visible`; features report `name`, `type`, `health` (`include_health=false` omits), `suppressed`, `body_names`. Both take `max_items` (default 200) and return `total`+`truncated`. Tool surface is now 62 default / 63 with send_code.
- `inventor_export_sat` (export toolset): export the active part/assembly to ACIS SAT (.sat) — the Revit interop path. `acis_version` defaults to 7; the Inventor SAT translator's `Version` option supports ACIS 7.0 only, so other values are rejected rather than writing a file the consumer can't read. The export-section docs now show how to register a workspace output root via `BIMWRIGHT_INVENTOR_EXPORT_ROOT` (the C4 gap — allowed-root failures pointed at the policy but not at how to extend it).
- `inventor_batch_execute` (feature toolset): run up to 20 wire commands inside one Inventor transaction — a single undo step, rolled back when the batch stops at an error (`continue_on_error` overrides). Sub-commands are `{command, params}` using unprefixed wire names (`extrude`, `create_work_plane`, `list_bodies`, …); `send_code`/`run_baked_tool`/`apply_bake`/`batch_execute` **and the document-lifecycle commands** (`new_part`/`new_assembly`/`open_document`/`close_document`/`save_document`) are blocked inside — matching is case-insensitive, mirroring the command registry — and each sub-command is re-checked against the read-only gate. The batch call gets a 120 s budget (20 steps share it); oversized `results` spill to a local file like `run_baked_tool`. Rollback covers the model only — files written by export/capture sub-commands are not removed. Returns per-step `{index, ok, data|error}` plus `executed` and `rolled_back`. Tool surface is now 64 default / 65 with send_code.
- `inventor_fillet` gains an edge **selector**: `edges` accepts either an array of edge ids (same as `edgeIds`, both still work) or `{kind:'circular', radius_mm?, radius_tol_mm?, center_mm?, center_tol_mm?, on_body?, adjacent_surface_types?}` — circular edges are filtered by radius, circle-center proximity, body (`"1"`/`"body:N"`/name), and the surface types of BOTH adjacent faces (`plane|cylinder|cone|torus|sphere|bspline|…`). Response adds `matched_edges` (`{edge, radius_mm, center_mm, adjacent}` per edge, capped at 200 rows + `matched_edges_truncated`) so callers verify what was filleted; note positional `edge:N` ids can shift after a feature adds edges — prefer the selector or re-list. Selector numbers must be finite (net48 `IsNaN`/`IsInfinity` guards — an infinite tolerance would silently disable its filter); the same hardening was applied to the existing `FaceSelectorSpec` used by assembly mates.
- `inventor_close_sketch` forces `Profiles.AddForSolid()` when `UpdateProfiles` leaves `Profiles.Count==0` — a lone closed curve (single circle/ellipse) never populated the collection, so `draw_circle` → `close_sketch` → `extrude` failed with an empty-profile error.
- `inventor_probe_brep` (read-only, `query` toolset): B-rep port survey — planar faces carrying an inner-loop circular edge (hole/pipe mouths) report `normal` (IsParamReversed-corrected), `center_mm`, `port_diameter_mm` (2 × smallest inner radius), and every full-circle edge on the face (`circles[]` with `inner_loop`), so concentric flange rims are visible. `body` scopes to one body, `min/max_diameter_mm` filter, `max_items` + `truncated`. Tool surface is now 65 default / 66 with send_code.
- `inventor_combine` (feature toolset): boolean solid bodies — `base_body` + `tool_bodies[]` (each resolves `"1"`/`"body:N"`/name), `operation=join|cut|intersect` (`new_body` rejected — it belongs to extrude), `keep_tool_bodies` (default false), optional `name` for the feature. Returns `body_names` + `volume_mm3` of the result — body names can change across a combine, so callers should use the response rather than assuming pre-combine names survive. Tool surface is now 63 default / 64 with send_code.
- `inventor_set_camera` (export toolset): position the active view's camera explicitly — `eye`/`target` in mm (`{x,y,z}` or `[x,y,z]`), `up` direction, `perspective`, `extents_mm` `[width,height]`, optional `fit` (default false so explicit framing is kept). Rejects degenerate views (`eye`==`target`, zero-length or view-parallel `up`) and returns the resolved camera state for verification without a capture round-trip.
- Server-side `ToolResponse` serializes compactly above 4 KiB and indented at/below 4 KiB, so large payloads no longer pay indentation overhead on the wire.

### Fixed

- `inventor_get_iproperty`'s tool description showed `"Summary Information"` as an example `set_name`, but the real set is `"Inventor Summary Information"` — a caller following the example got `'Summary Information' not found` (run-1 call #9). The description now names real sets, and `PropertyAccess.FindSet` retries with the `"Inventor "` prefix so the short form resolves too (also applies to `set_iproperty`).
- Plane/point reference routing no longer matches by substring: a work plane named e.g. `wp_faceplate` (or any name merely containing "face"/"vertex") was routed to the face/vertex index parser and failed with `not a valid 1-based index`. `create_sketch`, `ResolvePlaneRef`, and `ResolvePointRef` now route through `EntityResolver.IsEntityRef`, which requires the strict `prefix:N` / `body:B/prefix:N` form — everything else resolves by work-feature name. Found by the F5 rerun (`wp_faceplate` rolled back a 20-step batch).
- `export_sat`/`export_step`/`export_stl` could report `exported:true` without writing a file: the Inventor translators return silently on a missing parent directory. `ExportSupport.SaveCopyAs` now creates the parent directory and throws when the output file does not exist after the call — a silent export failure can no longer masquerade as success. Found by the F5 rerun.
- `inventor_extrude`'s `volume_mm3` reported total part volume; it now reports the volume of the body the feature produced or last affected (`feature.SurfaceBodies`' last entry), falling back to the part total when the feature exposes no bodies.
- `send_code` source policy is token-aware instead of a raw case-insensitive substring scan: comments and string/char literals (regular, verbatim, interpolated, raw `"""…"""` incl. `$$"""` holes) are stripped before matching — interpolation holes are still scanned — and tokens match on word boundaries with C#'s case sensitivity. This removes the observed false positives (`VisibleSocket` in a body name, `GetType(`/`typeof(` metadata reads, lowercase `file.`/`process.` variables, banned text inside strings/comments) while still blocking file/process/network/environment APIs, ToolBaker re-entry, and invoke/load-style reflection (`GetMethod`/`GetProperty`/`GetField`/`GetMember`/`GetEvent`/`GetConstructor`, `Invoke`/`DynamicInvoke`/`BeginInvoke`, `Activator.`, `Assembly.Load*`, `Delegate.CreateDelegate`). Errors now name the calling surface (`send_code source uses forbidden token: X` vs `Baked tool …`).
- Removed the `send_code` handler's private 30 s `CancellationTokenSource` (a third, ineffective timeout layer that could not interrupt a synchronous script on the STA thread); `InventorStaDispatcher.InvokeAsync` no longer takes an unused `timeoutMs` parameter. Plugin-side `Err` responses now carry `InventorResponseMeta` (target id/year) and preserve the request id, so journal entries attribute TIMEOUT/UNAUTHORIZED correctly.
- `derive_envelope` can no longer run inside `inventor_batch_execute`: it creates and activates a new document mid-transaction, so later steps would edit the new document while the transaction belongs to the old one — it's blocked alongside the other document-lifecycle commands (external review finding).
- A failed `derive_envelope` no longer leaves a half-built empty part active: the handler captures the active document before creating the derived one and, on any post-creation failure, closes the new document (skip-save) and re-activates the original — a retry without `source_path` resolves the real source again.
- `derive_envelope` actually supports `.iam` sources now via `DerivedAssemblyComponents` (per-occurrence inclusion, `bounding_box` → `kDerivedBoundingBox` + `RemoveInternalVoids`); `include_bodies` is rejected for `.iam` with a clear error, and the response carries `source_type` + `occurrences`.
- `inventor_list_iproperty_sets` gained `max_items` (default 200, counting total properties across sets) plus `truncated`/`properties_total` and per-set `properties_omitted`, so a large custom-property set can be narrowed instead of tripping `RESPONSE_TOO_LARGE`.
- The call journal no longer persists raw secrets: `ServerLogger` deep-masks params before writing (credential-named keys replaced wholesale, every string value swept by `SecretMasker`), and `error`/`data_error` on finish lines pass through `ErrorSanitizer`. `SecretMasker` additionally covers `Bearer <token>` and key=value/key:"value" secrets (`password`, `api_key`, `authorization`, token family) in code strings — honoring the SECURITY.md promise.
- Spill files and `results_preview` are sanitized BEFORE being written: `ResponseSpillWriter.AttachResults` runs the same error/message-field sanitization the dispatcher applies (now shared as `ErrorSanitizer.SanitizeErrorFields`) plus a `SecretMasker` pass over the serialized payload — previously a batch step error could persist raw paths/secrets to the 24 h spill file.

### Changed

- **BREAKING — `inventor_capture_view` defaults to file mode.** With no `output_path` it now writes `<export-root>\captures\capture-<yyyyMMdd-HHmmss>-<seq>.png` (root = `BIMWRIGHT_INVENTOR_EXPORT_ROOT`, else `%LOCALAPPDATA%\Bimwright\ipt-mcp`) and returns `{path,width,height,bytes}` — no base64. Pass `inline=true` for the previous base64 response; inline payloads over 256 KiB are rejected with `INVALID_ARGUMENT` (use file mode or reduce width/height). Explicit `output_path` is unchanged.

## [0.1.0] - 2026-08-28

First GitHub Release. Client setup ZIP: `IptMcp.Setup-v0.1.0-win-x64.zip` (self-contained `ipt-mcp.exe`). **Plugin years in this ZIP:** Inventor **2025** and **2027**. Source still supports 2022–2027; other years need a local Inventor interop build (shape-only DLLs are not shipped).

### Added

- Assembly-oriented tools (interference check, minimum distance, BOM, assembly-level mass properties) and ToolBaker allow-list for five read-only assembly queries. Documented surface: **58** tools by default, **59** with `send_code`.
- Japanese and Simplified Chinese README mirrors.

### Fixed

- Live feature and iMate runtime paths; handler response/error contracts aligned with the assembly spec.

### Changed

- Install path and locale README parity; custom export root and `--disable-toolbaker` documented.
- Inventor-API handlers exercised against a live Inventor session (supersedes the older “not validated against a running Inventor” limitation below).

## [0.1.0] - Initial public surface

First public surface of `bimwright/ipt-mcp` — a local MCP gateway that lets Claude Code
(and any MCP-capable client) drive Autodesk Inventor 2022-2027 through an in-process add-in.

### Added

- **MCP server** (`Bimwright.Ipt.Server`, .NET 8 console, stdio transport,
  `ModelContextProtocol` 1.1.0). Single process, independent of the Inventor version; compiles
  only the API-agnostic shared contract files, so it builds and tests on any machine with the
  .NET 8 SDK — no Inventor required.
- **Six per-version in-process add-ins** (`Bimwright.Ipt.Plugin.InvNN`), one per
  Inventor 2022-2027, all compiled from the same `src/shared/**` source glob:
  - 2022 / 2023 / 2024 → `net48`, **TCP** transport.
  - 2025 / 2026 → `net8.0-windows7.0`, **Named Pipe** transport.
  - 2027 → `net10.0-windows7.0`, **Named Pipe** transport (`UseInventorAssemblyContext=0`).
  - Each add-in implements `ApplicationAddInServer`, carries a unique `[Guid]` ClientId that
    matches its registry-free `.addin` manifest, and isolates exactly its own Inventor year via
    the `SupportedSoftwareVersion` brackets (internal major = calendar year − 1996).
- **STA marshalling** via `InventorStaDispatcher` — a hidden message-only WinForms control
  created on Inventor's main thread during `Activate` (Inventor has no `ExternalEvent`). Every
  command runs on the UI thread through `Control.BeginInvoke`.
- **Transport + target discovery**: `ITransportServer` with `TcpTransportServer` and
  `PipeTransportServer`, NDJSON framing, auth-token verification, response-size guard, and
  per-instance discovery descriptors at
  `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`. The server enumerates live
  targets and drops dead/stale ones (dead PID, expired heartbeat, wrong host app, out-of-range year).
- **46 Phase-1 tools** (MCP names prefixed `inventor_`):
  - 3 server-side **meta/target** tools (`list_available_targets`, `get_current_target`,
    `switch_target`).
  - **Query/document**: list open documents, document info, new part/assembly, open/save/close,
    set units, set material.
  - **Parameters / iProperties / mass**: list/get/set/create parameter, get/set iProperty,
    mass properties.
  - **Sketch**: create sketch, project geometry, draw line/circle/rectangle/arc, sketch
    dimension/constraint, close sketch.
  - **Feature / work geometry**: extrude, revolve, fillet, chamfer, work plane, work axis.
  - **View / export**: capture view (bounded base64 PNG), export STEP / STL / DXF.
  - `inventor_send_code` (Roslyn C# scripting against `Inventor.Application`) — opt-in only.
  - 6 **ToolBaker** tools (3 read-only + 3 write) for proposing/accepting compiled query tools.
- **Read-only mode** (`--read-only`): removes every write-capable toolset
  (`document, parameters, properties, sketch, feature, export, code, toolbaker_write`) while
  keeping `meta`, `query`, read-only `toolbaker`, and `inventor_switch_target`. The add-in
  `CommandDispatcher` enforces it as a second line of defense (`READ_ONLY`).
- **send_code opt-in gate**: OFF by default; requires `--enable-send-code` on the server **and**
  `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1` in the add-in. Otherwise `SEND_CODE_DISABLED`.
- **Progressive disclosure**: `--toolsets a,b` and a keyword-dense `ServerInstructions` so weak
  models and MCP Tool Search only see the enabled surface.
- **Units boundary**: every length input is converted mm→cm and every length output cm→mm
  (Inventor's API uses internal centimetres), centralized in `shared/Handlers/UnitConvert.cs`.
- **Error model** (`InventorErrorCodes`): `NO_TARGET, TARGET_UNAVAILABLE, NO_DOCUMENT,
  WRONG_DOCUMENT_TYPE, INVALID_ARGUMENT, UNSUPPORTED_HOST, API_ERROR, TIMEOUT,
  RESPONSE_TOO_LARGE, READ_ONLY, SEND_CODE_DISABLED, UNAUTHORIZED`.
- **Server-only test suite** (xUnit, .NET 8): registration / read-only / toolset filtering,
  envelope round-trip, response-size guard, target-registry stale-cleanup, transport selection,
  TFM-split matrix, send-code-disabled, ToolBaker dispatch authorization, DTO validation, and
  `.addin` manifest invariants (ClassId==ClientId, assembly name, single-version brackets).
- **Packaging**: `scripts/package-bundle.ps1` assembles the per-user, registry-free
  `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\` layout (a `PackageContents.xml`
  entry point plus per-version subfolders with the built DLL + `.addin`). Supports `-DryRun`.

### Known limitations

- **Not validated against a running Inventor.** This build box has no runnable `Inventor.exe`
  (and only the 2025-2027 interop assemblies, with the 2026/2027 ones being stubs). All
  Inventor-API handler bodies are compile-only until the manual smoke run on a real Inventor box
  (`docs/testing/manual-smoke.md`). Do not treat this as production-ready before that passes.
- The generated `PackageContents.xml` and the `.addin` element schema are **best-effort** and
  must be verified against the installed Inventor SDK before release (see the packaging script's
  output and the FLAG comments in `scripts/package-bundle.ps1`).
- Autodesk Inventor binaries / SDK / interop assemblies are **not** redistributed with this repo.

[Unreleased]: https://github.com/bimwright/ipt-mcp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/bimwright/ipt-mcp/releases/tag/v0.1.0
