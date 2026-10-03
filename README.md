<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-inventor-versions"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#tool-surface"><img src="https://img.shields.io/badge/MCP-111%20tools-6C47FF" alt="MCP tools" /></a>
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

> **Status: Phase 1 and Phase 2 supported scope accepted.** Phase 3 adds four tools validated by build, automated tests and native Inventor 2027 disposable fixtures; installed-client and release acceptance remain separate. See [Phase 3 behavior](docs/testing/drawing-phase3.md).

The final MCP output guard warns at 64/256 KiB and uses a 1 MiB UTF-8 budget. Oversized reads request narrowing; completed writes retain compact effects and must not be replayed. `--disable-output-guard` keeps the transport fence active. CLI/JSON/environment thresholds and the configurable 36-hour spill policy are documented in [drawing testing](docs/testing/drawing-phase1.md).

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

`inventor_send_code` is enabled by default. `--disable-send-code` hides code tools; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` disables execution in the host. Read-only mode excludes it.

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

Full mode exposes **111 tools** by default (`--toolsets all`); `--disable-send-code` exposes **107 tools**, and `--read-only` exposes **28 tools**. All MCP names start with `inventor_`. Toolset filters narrow this surface.

All 15 toolsets are enabled by default, including `code`. Use `--toolsets <csv>` to narrow the surface.

All length inputs are in **mm**, angles in **degrees**; the add-in converts to Inventor's internal centimetres/radians.

**Targeting a document.** Document-level tools (`get_document_info`, `list_bodies`, `list_features`, the iProperty, parameter, material and mass tools, `save_document`, `close_document`, and the assembly tools) take an optional `document`: a full path or a name of a document Inventor already has in memory — an open window *or* a part loaded as an assembly reference. It is matched by path, display name, file name, then a unique substring; it is never opened or activated automatically, and an ambiguous or unknown name returns the candidates. Omit it to act on the active document.

**Occurrence selectors.** The bulk assembly tools pick occurrences with one shared selector: `{names?: [glob], regex?, file?: glob, path_contains?, leaf?: false, max_depth?, include_suppressed?: false, limit?}` (or just a name / array of names). Globs use `*`/`?`; a name glob containing `/` matches the occurrence path (`SUB:1/PART:2`). Zero matches or more than `limit` is an error that lists the closest names — results are never silently truncated.

**Dialogs.** Save, open, close, export and the batch document tools run under `Application.SilentOperation` by default (`silent=true`), so Inventor answers its own prompts with their defaults instead of opening a hidden modal dialog that blocks the call. If a call still times out, the TIMEOUT message and `inventor_health` report `modal_dialog {open, title}` — probed without touching Inventor's main thread.


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
[Drawing annotations, tables and sheets](docs/testing/drawing-phase2.md) ·
[Generic live smoke record](docs/benchmarks/drawing-phase1-smoke.json) ·
[Declared read-only inventory](docs/testing/readonly-tools.json).

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

### assembly (9, write) — compose and edit assemblies

| Tool | Description |
|---|---|
| `inventor_create_design_view` | Copy an assembly design view; exact occurrence visibility/appearance settings, optional activation. Validated on an Inventor 2027 disposable fixture. |
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

### code (4) — C# scripts and code modules

| Tool | Description |
|---|---|
| `inventor_send_code` | `inventor_send_code` is enabled by default. `--disable-send-code` hides code tools; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` disables execution in the host. Read-only mode excludes it. |
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

When Inventor is visible, command results update **one compact activity card** with the latest tool and rolling **Success / Failed / Capture** counters. Captures are a subset of successes, including `inventor_capture_view` with `inline=true`, not extra operations to add to the total. Failures include soft script failures and rolled-back batches. Read and write activity use blue accents; a recorded failure keeps the card's accent red. The card is the same one rvt-mcp and dwg-mcp show, so the layout stays stable as numbers change: no priority stack or per-tool toast queue. A successful capture that saved an image also shows a **thumbnail** of it, centred in one fixed frame; clicking the thumbnail opens the file. Counts cover this card's lifetime on the Inventor target, not an inferred job or a particular MCP client. `inventor_health` never toasts. **Agent connected** is a status notification: it neither increments counters nor replaces an active activity card or task report.

The activity card expires **20 seconds after the last result by default**. Choose 10, 20, 30 or 60 seconds in **Status → Toast duration → Apply**; the choice is saved as `toastIdleSeconds`. A failed save displays an error and keeps the previous duration active. A real hover, detected by pointer movement, pauses expiry; leaving rearms the full selected interval. The new duration takes effect on the next result or pointer leave. A card appearing under a stationary cursor does not count as a hover. Click the card to open **History** and dismiss it; **×** only dismisses it. The card title always names the gateway and the **Inventor year** (`ipt-mcp 2027`), regardless of branding.

To report a whole job's outcome, the agent explicitly calls `inventor_report_task_result` with a unique agent/job `task_id` (1–80 chars), `outcome` (`completed`, `failed`, `cancelled`), and a truthful single-line `summary` (1–120 chars). The report **replaces the same shared slot**, says **Agent reported**, and has an **8-second** lifetime, paused by real hover and rearmed on leave. It does not increment activity counters; the next tool result starts a new activity card. Idle time and successful tool calls never imply task completion. The report bypasses the Inventor command queue (it touches no Inventor API), so it still lands while a long `send_code` holds the main thread. Its response carries `toast_shown`, indicating whether a card was retained for display; a card held while Inventor is minimized or behind a dialog counts and appears once the frame is usable. Reports respect the Toasts toggle and require the updated server **and** add-in.

Toasts use **code-only WPF on a dedicated STA UI thread**, with an **unowned, no-activate window** independent of Inventor's main STA and without Inventor COM calls on the toast thread. Command-result notifications are posted after the response is handed back. Toasts do not steal focus, hide while Inventor is minimized or a modal dialog is open, and **stay topmost even when another application is in the foreground**. The card uses one fixed light style, like rvt-mcp and dwg-mcp; there is no theme setting and nothing reads the screen behind it.

Toggle toasts from the ribbon: **Bimwright ▸ MCP → Toasts** (Status opens a diagnostic dialog). The choice persists in `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` under `enableToast`. The environment variable `BIMWRIGHT_INVENTOR_ENABLE_TOAST` overrides the JSON value. The retired `toastTheme` key and `BIMWRIGHT_INVENTOR_TOAST_THEME` are ignored. A malformed config file is left untouched — the toggle refuses to overwrite it.

**Toast Brand** on the same ribbon is **off by default**; the choice persists in the same file under `showBranding` and is restored at the next Inventor start. Enabling it reserves a wordmark row at the bottom of the card and reveals the BIMwright wordmark there with a wipe on real hover, fading it out on leave. There is **no automatic entrance wordmark wipe**. Motion follows the Windows animation preference. Toast UI labels remain English; translated READMEs do not imply UI localization.

## MCP Command History

Call-log files are disabled by default. Enable them with `--enable-call-log`; the plug-in records redacted calls in `%LOCALAPPDATA%\Bimwright\ipt-mcp\mcp-calls.jsonl`. In-memory History stays available when file logging is off.

`send_code` bodies are redacted to `{code_hash, code_length}` by default. `BIMWRIGHT_CACHE_SEND_CODE_BODIES=1` keeps bodies in memory for display and re-run; `BIMWRIGHT_PERSIST_SEND_CODE_BODIES=1` (+ optional `BIMWRIGHT_PERSIST_SEND_CODE_BODIES_TTL`, e.g. `4h`/`2d`, default 4 h) writes bake-redacted bodies to `send-code-journal.jsonl` so re-run can recover them — the window warns that a recovered body is redacted and may behave differently than the original.

---

## Safety

Short version: your model stays on your machine, and write/dangerous tools are gated.

- **Read-only mode.** `--read-only` keeps only tools annotated `ReadOnly = true`: no document or file writes. Parameter/property reads, view fit and code-module listing remain available. The add-in also enforces `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`.
- **send_code.** `inventor_send_code` is enabled by default. `--disable-send-code` hides code tools; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` disables execution in the host. Read-only mode excludes it.
- **Local, authenticated transport.** TCP binds loopback; Named Pipe is local-machine scoped. Each per-session descriptor carries a random auth token, but MCP meta tools never return it.
- **Sanitized errors.** Error messages returned to the model are sanitized to avoid leaking absolute paths/secrets.
- **ToolBaker controls.** ToolBaker is enabled by default. It can be completely disabled by passing the --disable-toolbaker CLI flag or setting BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0.
- **Allowed export paths.** File export tools validate that output_path points to a safe folder (within User Profile or Temp directory). You can define an additional allowed root folder by setting the BIMWRIGHT_INVENTOR_EXPORT_ROOT environment variable.
- **Secret masking in errors and journals.** Quoted key-value credentials and `Bearer` tokens are always masked. A heuristic also masks any 24+ character alphanumeric run as a possible secret; it is **on by default**. On a trusted machine where long identifiers (COM type names, part names) must stay readable, set `BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS=0` in the user environment (read by both the server and the add-in; restart Inventor and the MCP client).

**ToolBaker** turns repeated local workflows into personal, verified tools: suggestions surface through `inventor_list_bake_suggestions`, you explicitly accept one with `inventor_accept_bake_suggestion` (validate → compile → apply → persist), and accepted tools become callable through `inventor_list_baked_tools` / `inventor_run_baked_tool`. The bake database and audit log live locally under `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`. See [docs/toolbaker.md](docs/toolbaker.md) and [SECURITY.md](SECURITY.md).

---

## Not Redistributed

This project does **not** redistribute Autodesk Inventor binaries or the Inventor SDK / interop DLLs. The shipped server and unit tests build and run with no Inventor present. Building the per-version add-ins requires a **local Inventor installation** or the matching **interop reference assemblies** (`Autodesk.Inventor.Interop.dll`), supplied via the default Autodesk shared-extensions path or an explicit `/p:InventorInteropDir=...` MSBuild property. Running the gateway against Inventor requires a licensed Inventor install.

---

## The bimwright family

Open-source tools connecting AI assistants to BIM and CAD applications.

The name **bimwright** combines **BIM** with **wright**, an old word for a maker or builder—as in *shipwright*.

See [how the gateway names are chosen](https://github.com/bimwright/.github/blob/master/profile/README.md#naming).

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Vietnamese-first BIM knowledge base

---

## License

[Apache-2.0](LICENSE). See [LICENSE](LICENSE).

Inventor and Autodesk are registered trademarks of Autodesk, Inc. bimwright is an independent open-source project and is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.

## Permissions & auto mode

The allow list below is generated from the 28 read-only annotations and checked against the running server. Replace `ipt-mcp` with your exact client server ID. Do not use `mcp__ipt-mcp__*` or other server-wide wildcards. `inventor_send_code` has no annotations and no forced-interaction metadata; its exact allow rule is a separate user choice. Actual client permission persistence must be tested in that client.

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

[Benchmark records and verification scope](docs/benchmarks/README.md).

[v0.2.1 live benchmark: Inventor 2027, 111 tools](docs/benchmarks/v0.2.1-inventor-2027.md).
