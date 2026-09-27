<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-inventor-versions"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#tool-surface"><img src="https://img.shields.io/badge/MCP-84%20or%2088%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  English · <a href="README.vi.md">Tiếng Việt</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a>
</p>

---

`ipt-mcp` is an open-source ([Apache-2.0](LICENSE)) [Model Context Protocol](https://modelcontextprotocol.io) gateway that lets Claude Code — and any MCP-capable client — drive **Autodesk Inventor 2022-2027** locally.

The agent speaks MCP over stdio. The server speaks NDJSON over a local authenticated transport (TCP or Named Pipe) to a per-version in-process Inventor add-in. The add-in marshals every command onto Inventor's STA thread and talks to the Inventor API.

Your model stays on your machine.

---

## What ipt-mcp Is

Two processes, one local pipe:

- **`Bimwright.Ipt.Server.exe`** — a .NET 8 MCP stdio server launched by Claude Code, Cursor, Cline, Codex, or another stdio MCP client. It has **no Inventor reference**; it only compiles the API-agnostic contract files, so it builds and runs on any machine with the .NET 8 SDK.
- **`Bimwright.Ipt.Plugin.InvNN.dll`** — an `ApplicationAddInServer` add-in that loads inside `Inventor.exe`, runs a TCP or Named-Pipe listener, and executes commands on Inventor's main UI (STA) thread. One thin shell per Inventor year, all compiled from the same `src/shared/**` source glob.

Unlike Revit, Inventor has **no `ExternalEvent`** equivalent. The add-in marshals work onto the STA thread through a hidden message-only WinForms control (`InventorStaDispatcher`). See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design.

---

## Supported Inventor Versions

| Inventor | Target framework | Transport | Notes |
|----------|------------------|-----------|-------|
| 2022 | `net48` (.NET Framework 4.8) | TCP | references `System.Windows.Forms` directly |
| 2023 | `net48` (.NET Framework 4.8) | TCP | |
| 2024 | `net48` (.NET Framework 4.8) | TCP | |
| 2025 | `net8.0-windows7.0` | Named Pipe | `UseWindowsForms`, `EnableDynamicLoading` |
| 2026 | `net8.0-windows7.0` | Named Pipe | |
| 2027 | `net10.0-windows7.0` | Named Pipe | needs the .NET 10 SDK; honors `UseInventorAssemblyContext` |

- The MCP server is one process, **unaffected by the Inventor version** — it just forwards JSON envelopes.
- TCP for 2022-2024 (net48 add-ins); Named Pipe for 2025-2027 — Named Pipe avoids the loopback-firewall prompt on modern Windows.
- Inventor moved desktop add-in development off .NET Framework starting in 2025: **.NET 8 for 2025/2026, .NET 10 for 2027**. (.NET 8 add-ins remain binary-compatible on 2027, but net10 is the native target.)
- Use **4-digit calendar years** (2022..2027) everywhere — never legacy version codes.

> **Status: verified.** Phases 1-3 are complete and green (84 MCP tools by default, or 88 with send_code; server + tests build with no Inventor installed), and the Inventor-API handlers have been exercised against a live Inventor session. As always, test against your own templates before trusting it on production models.

---

## Install / Wire an MCP Client

Download the client setup ZIP from [GitHub Releases](https://github.com/bimwright/ipt-mcp/releases/latest). It includes a self-contained MCP server and Inventor add-in years compiled against **real** interop (see `manifest.json`). The v0.1.0 ZIP ships **2025** and **2027**. Other years: build locally — do not ship `SkipInventorReferenceCheck` shape-only DLLs.

```powershell
$tag = (Invoke-RestMethod https://api.github.com/repos/bimwright/ipt-mcp/releases/latest).tag_name
$zip = "$env:TEMP\IptMcp.Setup-$tag-win-x64.zip"
$dir = "$env:TEMP\IptMcp.Setup-$tag-win-x64"
Invoke-WebRequest "https://github.com/bimwright/ipt-mcp/releases/download/$tag/IptMcp.Setup-$tag-win-x64.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath $dir -Force

powershell -ExecutionPolicy Bypass -File "$dir\install.ps1" -WhatIf
powershell -ExecutionPolicy Bypass -File "$dir\install.ps1"
```

Deploys `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\` and `ipt-mcp.exe` to the fixed path `%LOCALAPPDATA%\Bimwright\ipt-mcp\server\current\ipt-mcp.exe`. The installer rolls back on failure, verifies the installed bundle against the package, smoke-checks the server with `--help`, and never edits MCP client configs — point your client at that fixed `ipt-mcp.exe` path yourself (see `AGENTS.md` in the ZIP for agent-driven wiring). Updates keep the path, so clients only need a restart; `-PruneOldServers` retires legacy `server\<version>\` copies once clients are repointed. `uninstall.ps1` runs the full sweep (personal data kept unless `-Purge`; `install.ps1 -Uninstall` removes the bundle only). Pin a year with `--target 2025` or `BIMWRIGHT_INVENTOR_TARGET=2025`.

Do **not** `dotnet tool install -g Bimwright.Ipt.Server` — that is not the supported client install.

**Developer:** `pwsh scripts/package-bundle.ps1` on a box with Inventor interop (skip years without SDK). Close Inventor before rebuilds.

Add-in discovery is automatic: each running add-in writes `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`. With more than one Inventor open, call `inventor_list_available_targets` then `inventor_switch_target`.

`inventor_send_code` stays **off** unless both server (`--enable-send-code` / `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) and plugin (`BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`) opt in — see [Safety](#safety).

---

## Build & Develop

Autodesk Inventor binaries and the Inventor SDK are **not redistributed** in this repo (see [Not Redistributed](#not-redistributed)). Building the **server and tests** needs only the .NET 8 SDK; building a **per-version add-in** needs the matching SDK plus the Inventor interop reference assembly.

```bash
# Server + tests (server-only; NO Inventor required — works on any machine with the .NET 8 SDK):
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# Legacy TFM compatibility check using the installed 2027 interop reference:
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"
dotnet build src/plugin-inv27 -c Debug   # real 2027 interop compile; needs the .NET 10 SDK

# A per-version add-in always needs an Inventor interop reference. For a legacy TFM compatibility
# check, point InventorInteropDir at an installed compatible interop; real release builds use the
# matching year's default path.
```

- The **server** explicit-includes only `shared/Contracts/*` + `shared/Security/*` (+ ToolBaker), so it compiles with no Inventor SDK present.
- Each **add-in** uses `<Compile Include="..\shared\**\*.cs" />` to pull in everything, including the API-touching `Infrastructure`/`Plugin`/`Handlers`.
- The default interop hint path is `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`.
- Building add-in 2027 needs the **.NET 10 SDK** installed.
- **Close Inventor before deploying add-in DLLs** it would otherwise lock.

---

## Tool Surface

The full surface is **84 tools** by default when all platform toolsets are enabled, or **88 tools** when the send_code toolset is enabled (opt-in: `inventor_send_code` + 3 code-module tools). Every MCP-facing name is prefixed `inventor_`. Tools are grouped into toolset classes; `--toolsets sketch,feature` and `--read-only` gate which ones register so weak models never see disabled tools.

Default-on toolsets: `meta`, `query`, `document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `assembly_query`, `toolbaker`, `toolbaker_write`.
Off by default: `code` (the `send_code` escape hatch — opt-in only).

All length inputs are in **mm**, angles in **degrees**; the add-in converts to Inventor's internal centimetres/radians.

**Targeting a document.** Document-level tools (`get_document_info`, `list_bodies`, `list_features`, the iProperty, parameter, material and mass tools, `save_document`, `close_document`, and the assembly tools) take an optional `document`: a full path or a name of a document Inventor already has in memory — an open window *or* a part loaded as an assembly reference. It is matched by path, display name, file name, then a unique substring; it is never opened or activated automatically, and an ambiguous or unknown name returns the candidates. Omit it to act on the active document.

**Occurrence selectors.** The bulk assembly tools pick occurrences with one shared selector: `{names?: [glob], regex?, file?: glob, path_contains?, leaf?: false, max_depth?, include_suppressed?: false, limit?}` (or just a name / array of names). Globs use `*`/`?`; a name glob containing `/` matches the occurrence path (`SUB:1/PART:2`). Zero matches or more than `limit` is an error that lists the closest names — results are never silently truncated.

**Dialogs.** Save, open, close, export and the batch document tools run under `Application.SilentOperation` by default (`silent=true`), so Inventor answers its own prompts with their defaults instead of opening a hidden modal dialog that blocks the call. If a call still times out, the TIMEOUT message and `inventor_health` report `modal_dialog {open, title}` — probed without touching Inventor's main thread.

### meta (3) — server-side target tools, never round-trip to the add-in; stay exposed under `--read-only`

| Tool | Description |
|---|---|
| `inventor_list_available_targets` | List detected live Inventor add-in targets (year, pid, transport, active document). |
| `inventor_get_current_target` | Report the server's currently selected target, or `NO_TARGET` if none is live. |
| `inventor_switch_target` | Select the active target by descriptor id, Inventor year, process id, or pipe/session name. Server-side only. |

### query (7) — read-only document/health/model probes and agent outcome reports

| Tool | Description |
|---|---|
| `inventor_health` | Probe the active add-in: inventor_year, process_id, whether a document is open, active document type, STA queue, and `modal_dialog {open, title}` when a dialog is blocking Inventor. |
| `inventor_report_task_result` | Explicit agent-reported task outcome: `task_id`, `outcome` (`completed`/`failed`/`cancelled`), single-line `summary`. No model changes. |
| `inventor_list_open_documents` | List documents in memory: title, path, type, is_active, dirty, visible; optional `filter` / `dirty_only`. |
| `inventor_get_document_info` | Title, full path, type and dirty flag of the active or a targeted document; `references=true` adds its file references (with missing flags). |
| `inventor_list_bodies` | List the part's solid bodies: id (`body:N`), name, volume_mm3, bbox_mm, face_count, created_by feature, visible. |
| `inventor_list_features` | List the part's features in tree order: name, type, health, suppressed, body_names. |
| `inventor_probe_brep` | Survey the part's B-rep for port mouths: planar faces with inner-loop circular edges — normal (IsParamReversed-corrected), center_mm, port_diameter_mm, all circles on the face. |

### document (10) — document lifecycle (write)

| Tool | Description |
|---|---|
| `inventor_new_part` | Create a new part document (.ipt); optional template path. |
| `inventor_new_assembly` | Create a new assembly document (.iam); optional template path. |
| `inventor_open_document` | Open an existing document from a full path and make it active. |
| `inventor_save_document` | Save the active document, or Save-As to a given path. |
| `inventor_close_document` | Close the active document; `save=true` saves first. |
| `inventor_set_units` | Set the active document's length unit (mm, cm, m, in, ft). |
| `inventor_set_material` | Assign a material to the active part by name. |
| `inventor_save_all` | Update a root document and save it with every dirty referenced document, silently; per-file saved / read_only / error; `dry_run`. |
| `inventor_open_documents` | Open several documents in one call (default without windows, for `document`-targeted edits). |
| `inventor_close_documents` | Close documents by path/name, or all visible ones (`keep_active`); optional save. |

### parameters (4) — model & user parameters (write)

| Tool | Description |
|---|---|
| `inventor_list_parameters` | List parameters (model + user): name, expression, value, unit, kind. |
| `inventor_get_parameter` | Get one parameter by name: expression, value, unit. |
| `inventor_set_parameter` | Set an existing parameter's expression/value, then update the document. |
| `inventor_create_parameter` | Create a new user parameter (name, expression, unit). |

### properties (4) — iProperties & mass properties (write)

| Tool | Description |
|---|---|
| `inventor_get_iproperty` | Get an iProperty value by property-set and property name. |
| `inventor_set_iproperty` | Set an iProperty value. |
| `inventor_get_mass_properties` | Mass (g), volume (mm³), surface area (mm²), centre of mass, bounding box. |
| `inventor_list_iproperty_sets` | List iProperty sets (name, internal_name, property names; optional values) — discovery for get/set_iproperty. |

### sketch (10) — 2D sketch geometry & constraints (write)

| Tool | Description |
|---|---|
| `inventor_create_sketch` | Create a 2D sketch on a plane (XY/XZ/YZ or a face/work-plane reference). |
| `inventor_project_geometry` | Project model edges/vertices (by edge ids) into the active sketch. |
| `inventor_draw_line` | Draw a sketch line from (x1,y1) to (x2,y2). |
| `inventor_draw_circle` | Draw a sketch circle from centre + radius. |
| `inventor_draw_rectangle` | Draw a two-point sketch rectangle. |
| `inventor_draw_arc` | Draw a sketch arc (centre, radius, start/end angle). |
| `inventor_add_sketch_dimension` | Add a driving dimension constraint to a sketch entity. |
| `inventor_add_sketch_constraint` | Add a geometric constraint (coincident, parallel, tangent, …). |
| `inventor_draw_text` | Add a fitted text box (position mm, optional font_size_mm; rotation_deg in multiples of 90). |
| `inventor_close_sketch` | Finish editing a sketch (exit sketch edit mode). |

### feature (16) — solid & work features (write)

| Tool | Description |
|---|---|
| `inventor_create_part` | Build a whole part from a JSON recipe in one call: sketches (rect / circle / polyline with arc bulges, inner voids, on origin or fixed planes) → extrude / hole / fillet / chamfer → material + iProperties → silent Save-As. Errors name the recipe path (`features[2].extrude.distance`); a failing build closes the part unsaved; `dry_run` validates only. |
| `inventor_extrude` | Extrude a named sketch (distance, join/cut/intersect, direction). |
| `inventor_revolve` | Revolve a named sketch about an axis (angle, operation). |
| `inventor_combine` | Boolean solid bodies (base + tool bodies, join/cut/intersect, keep_tool_bodies). |
| `inventor_batch_execute` | Run up to 20 wire commands in one transaction (single undo, rollback on error). |
| `inventor_fillet` | Add a constant-radius edge fillet — `edgeIds` or an `edges` selector `{kind:circular, radius_mm?, center_mm?, on_body?, adjacent_surface_types?}`; returns `matched_edges`. |
| `inventor_chamfer` | Add an equal-distance edge chamfer over model edges. |
| `inventor_create_work_plane` | Create a work plane (offset, three_points, tangent, or fixed origin+axes). |
| `inventor_create_work_axis` | Create a work axis (two_points, edge, plane_intersection, normal_to_face_through_point). |
| `inventor_create_work_point` | Create a fixed work point at {x,y,z}/[x,y,z] mm; construction points can't be named (name_applied reports it). |
| `inventor_hole` | Drilled/counterbore/countersink holes on a deterministically-selected planar face; optional tapped-thread metadata. |
| `inventor_circular_pattern` | Circular-pattern part features around a named axis (count over an angle). |
| `inventor_rectangular_pattern` | Rectangular-pattern part features along one or two named axes. |
| `inventor_loft` | Loft an ordered list of sketch profiles ('SketchName' or 'SketchName:N'), optional centerline sketch, closed/merge-tangent-faces. |
| `inventor_sweep` | Sweep a sketch profile along a sketch path (connected segments chain); orientation normal_to_path\|parallel. |
| `inventor_create_bim_connector` | Author a BIM pipe connector on a circular port edge (ref from inventor_probe_brep `circles[].edge`); kind=pipe, optional system/flow/connection metadata. |

### export (10) — view capture & geometry export (write)

> `output_path` must sit under an allowed root: the user profile, `%TEMP%`, or a root you add — e.g. set `BIMWRIGHT_INVENTOR_EXPORT_ROOT=D:\Inventor-Exports` on the Inventor machine (restart Inventor and your MCP client/server session afterwards).

| Tool | Description |
|---|---|
| `inventor_capture_view` | Capture the active view to a PNG file (under `<export-root>\captures\` or `output_path`); `inline=true` returns a bounded base64 PNG instead. Can set up the view in the same call (design view, object/occurrence visibility, orientation or camera, fit) and take several `shots` at once. |
| `inventor_set_view_state` | Activate or create a design view, toggle object visibility (work features, sketches, …) and show/hide occurrences by selector. |
| `inventor_export_step` | Export the active part/assembly to STEP (.stp/.step). |
| `inventor_export_stl` | Export the active part/assembly to STL (.stl). |
| `inventor_export_sat` | Export the active part/assembly to ACIS SAT (.sat) — the Revit interop format; acis_version defaults to 7 (the only supported value). |
| `inventor_export_dxf` | Export a 2D DXF; must declare source (`sketch` or `flat_pattern`). |
| `inventor_derive_envelope` | Create a derived part from a source part/assembly — the envelope/interop path: `derive_style`, `include_bodies` per-solid selection, `bounding_box` lightweight mode; saves an .ipt under an allowed root. |
| `inventor_view_fit` | Zoom-fit the active view to the model extents (run before capture). |
| `inventor_set_view_orientation` | Set a standard camera orientation (iso/front/top/…) for multi-angle captures. |
| `inventor_set_camera` | Position the camera explicitly (eye/target in mm, up, perspective, extents_mm, fit) — use before capture_view when standard orientations don't fit. |

> Export paths must be absolute and under an allowed output root (user profile or temp).

### assembly (8, write) — compose and edit assemblies

| Tool | Description |
|---|---|
| `inventor_place_occurrence` | Place a component (.ipt/.iam) into the active assembly; optional initial pose + grounded. |
| `inventor_add_constraint` | Constrain two named refs (mate/flush/insert/angle); response carries `health` — always check it. |
| `inventor_create_imate` | Author a named iMate on the active part using a deterministic face selector. |
| `inventor_place_occurrences` | Place many components in one undo step; pose as origin+rotation, axes or a 4×4 matrix; `lock` = none / grounded / workplanes (three hidden fixed work planes flush-constrained to the part's origin planes). |
| `inventor_delete_occurrences` | Delete the top-level occurrences a selector matches (with their lock planes); `dry_run`. |
| `inventor_set_occurrence_state` | Set visible / suppressed / grounded / pose / lock on every selector match in one undo step; `dry_run`. |
| `inventor_set_appearance` | Colour occurrences (selector) or part bodies by RGB, or apply a library appearance by name. |
| `inventor_reset_appearance` | Remove appearance overrides from occurrences or part bodies. |

### assembly_query (6, read-only) — numeric self-check battery; survives `--read-only`

| Tool | Description |
|---|---|
| `inventor_list_interfaces` | List named interfaces (iMates, work features, origin geometry) of the doc or one occurrence. |
| `inventor_list_occurrences` | Selector-filtered occurrence report with chosen fields (path, file, bbox_mm, transform, visibility, grounded, material, appearance, mass, volume); inline or to a file. |
| `inventor_check_interference` | Run interference analysis — legacy occurrence names, or `set_a` × `set_b` selectors with a bounding-box prefilter; pairs sorted by volume, capped by `max_pairs`. |
| `inventor_measure_min_distance` | Minimum 3D distance (mm) between two occurrences or named refs — or a batch: `pairs[]` or `set_a` × `set_b` with `threshold_mm` (clearance checks). |
| `inventor_get_assembly_bom` | BOM + occurrence tree with grounded flag and translation/rotation degrees of freedom. |
| `inventor_list_constraints` | Read back every constraint with type, `health`, suppressed flag and the two occurrence names. |

### code (4) — opt-in escape hatch (OFF by default)

| Tool | Description |
|---|---|
| `inventor_send_code` | **Dangerous, opt-in only.** Execute a C# snippet in-process against `Inventor.Application`. Disabled unless both server and add-in opt in (else `SEND_CODE_DISABLED`); banned APIs are rejected (fully-qualified `System.IO.Path` string helpers are allowed). Returns the script's `result` (spilled to a file above 64 KiB) + captured `stdout`; `modules` loads saved helper modules; `silent=true` runs under SilentOperation; `timeout_ms` overrides the per-call timeout. Failures carry `diagnostics[{source, line, code, message, hint}]` or a runtime `location` + `hint`. |
| `inventor_save_code_module` | Save a reusable C# helper module (declarations only); policy-checked and dry-compiled in the add-in before it is stored; `requires` other modules. |
| `inventor_list_code_modules` | List saved modules with hash, description and declared signatures. |
| `inventor_delete_code_module` | Delete a saved module (refused while another module requires it). |

### toolbaker (3, read-only) — operate purely on the server-side bake database

| Tool | Description |
|---|---|
| `inventor_list_baked_tools` | List all verified, compiled, registered baked tools. |
| `inventor_list_bake_suggestions` | List active ToolBaker suggestions from recurrent workflows. |
| `inventor_create_bake_issue_draft` | Create a GitHub issue draft for a suggestion (without submitting). |

### toolbaker_write (3, write) — run baked tools and manage the suggestion lifecycle

| Tool | Description |
|---|---|
| `inventor_run_baked_tool` | Execute a registered baked tool by name with JSON parameters. |
| `inventor_accept_bake_suggestion` | Accept a suggestion: validate + compile + apply + persist as a baked tool. |
| `inventor_dismiss_bake_suggestion` | Dismiss or snooze an active suggestion. |

---

## Toast Notifications

When Inventor is visible, command results update **at most three retained cards**, at most **twice per second**, rather than queueing one toast per tool. Routine successes share an activity card with a count and latest tool; reads are blue, writes green, failures red (including soft script failures, rolled-back batches, and `failed` task reports). Repeated identical errors share a card with an occurrence count. Failures take priority over task summaries (`completed`/`cancelled`), snapshots/exports, and routine activity; when full, the lowest-priority oldest card is replaced — a task report may evict even an error, since a report exists to be seen — while routine arrivals are dropped, not replayed later. Capture cards retain their clickable thumbnail. Cards age out a few seconds after their last displayed update (hover pauses). `inventor_health` never toasts.

Activity counts cover the retained card's lifetime on this Inventor target, not an inferred job or a particular MCP client. To report a whole job's outcome, the agent explicitly calls `inventor_report_task_result` with a unique agent/job `task_id` (1–80 chars), `outcome` (`completed`, `failed`, `cancelled`), and a truthful single-line `summary` (1–120 chars). These cards say **Agent reported**; idle time and successful tool calls never imply task completion. The report bypasses the Inventor command queue (it touches no Inventor API), so it still lands while a long `send_code` holds the main thread, and its response carries `toast_shown` telling the agent whether a card was actually retained for display (a card held while Inventor is minimized or behind a dialog counts, and appears once the frame is usable). Reports respect the Toasts toggle. Requires the updated server **and** add-in. See [smart-toast verification](docs/testing/smart-toasts.md) for build/live-test status.

Toasts run on a dedicated UI thread, never steal focus, and never block a command — they are posted only after the response is handed back. They hide while Inventor is minimized or a modal dialog is open, and stay topmost even when another application has focus (that is the point: proof the agent is still running). The card colour follows an auto palette sampled from the pixels behind the toast; the sample lives in memory only — nothing is written to disk or logged.

Toggle toasts from the ribbon: **Bimwright ▸ MCP → Toasts** (Status opens a diagnostic dialog). The choice persists in `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` under `enableToast`; `toastTheme` accepts `auto` (default), `light`, or `dark`. Environment variables `BIMWRIGHT_INVENTOR_ENABLE_TOAST` and `BIMWRIGHT_INVENTOR_TOAST_THEME` override the JSON values. A malformed config file is left untouched — the toggle refuses to overwrite it.

## MCP Command History

Every wire command is journaled to `%LOCALAPPDATA%\Bimwright\ipt-mcp\mcp-calls.jsonl` (one JSON line per call — timestamp, per-launch `session_id`, tool, success, duration, sanitized error, redacted params, capped result; rotated at 5 MB) and appended to a bounded in-memory session log (1000 entries, oldest evicted). **Bimwright ▸ MCP → History (N)** shows the live session count and opens **BIMwright · MCP Command History**: search, success/failure and read/write kind filters, a detail pane (WHAT/INPUT/OUTPUT, JSON pretty-print, numbered code view), Clear Session, Open logs, and **Load past sessions** — rotated archives plus the current journal merge back as read-only rows tagged by session and deduplicated against the live tail. **Re-run** re-executes a live entry on Inventor's STA thread and diffs numeric result fields; historical and params-truncated entries are view-only.

`send_code` bodies are redacted to `{code_hash, code_length}` by default. `BIMWRIGHT_CACHE_SEND_CODE_BODIES=1` keeps bodies in memory for display and re-run; `BIMWRIGHT_PERSIST_SEND_CODE_BODIES=1` (+ optional `BIMWRIGHT_PERSIST_SEND_CODE_BODIES_TTL`, e.g. `4h`/`2d`, default 4 h) writes bake-redacted bodies to `send-code-journal.jsonl` so re-run can recover them — the window warns that a recovered body is redacted and may behave differently than the original.

---

## Safety

Short version: your model stays on your machine, and write/dangerous tools are gated.

- **Read-only mode.** `--read-only` (or `BIMWRIGHT_INVENTOR_READ_ONLY=1`) removes every write-capable toolset (`document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `code`, `toolbaker_write`) but keeps `meta` + `query` + `assembly_query` + read-only `toolbaker`, and **keeps `inventor_switch_target` exposed**. The server sends read-only mode in each command envelope; the add-in also honors `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`. A write command under enforced read-only returns `READ_ONLY`.
- **send_code two-sided opt-in.** `inventor_send_code` is **disabled by default**. It is exposed only when **both** gates are set: the server with `--enable-send-code` (or `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) **and** the add-in process with `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`. Otherwise the dispatcher returns `SEND_CODE_DISABLED`. Banned APIs (file/process/network/environment/dynamic-invocation) are rejected by a best-effort source scan — note that file writes made *through the Inventor API* (`SaveAs`, `SaveCopyAs`, translators) are **not** constrained by the export-root policy below; the two-sided opt-in is the trust boundary.
- **Local, authenticated transport.** TCP binds loopback; Named Pipe is local-machine scoped. Each per-session descriptor carries a random auth token, but MCP meta tools never return it.
- **Sanitized errors.** Error messages returned to the model are sanitized to avoid leaking absolute paths/secrets.
- **ToolBaker controls.** ToolBaker is enabled by default. It can be completely disabled by passing the --disable-toolbaker CLI flag or setting BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0.
- **Allowed export paths.** File export tools validate that output_path points to a safe folder (within User Profile or Temp directory). You can define an additional allowed root folder by setting the BIMWRIGHT_INVENTOR_EXPORT_ROOT environment variable.

**ToolBaker** turns repeated local workflows into personal, verified tools: suggestions surface through `inventor_list_bake_suggestions`, you explicitly accept one with `inventor_accept_bake_suggestion` (validate → compile → apply → persist), and accepted tools become callable through `inventor_list_baked_tools` / `inventor_run_baked_tool`. The bake database and audit log live locally under `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`. See [docs/toolbaker.md](docs/toolbaker.md) and [SECURITY.md](SECURITY.md).

---

## Not Redistributed

This project does **not** redistribute Autodesk Inventor binaries or the Inventor SDK / interop DLLs. The shipped server and unit tests build and run with no Inventor present. Building the per-version add-ins requires a **local Inventor installation** or the matching **interop reference assemblies** (`Autodesk.Inventor.Interop.dll`), supplied via the default Autodesk shared-extensions path or an explicit `/p:InventorInteropDir=...` MSBuild property. Running the gateway against Inventor requires a licensed Inventor install.

---

## The bimwright family

Open-source tools connecting AI assistants to BIM and CAD applications.

The name **bimwright** combines **BIM** with **wright**, an old word for a maker or builder—as in *shipwright*.

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Vietnamese-first BIM knowledge base

---

## License

[Apache-2.0](LICENSE). See [LICENSE](LICENSE).

Inventor and Autodesk are registered trademarks of Autodesk, Inc. bimwright is an independent open-source project and is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.
