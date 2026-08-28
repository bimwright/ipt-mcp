<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-inventor-versions"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#tool-surface"><img src="https://img.shields.io/badge/MCP-58%20or%2059%20tools-6C47FF" alt="MCP tools" /></a>
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

> **Status: verified.** Phases 1-3 are complete and green (58 MCP tools by default, or 59 with send_code; server + tests build with no Inventor installed), and the Inventor-API handlers have been exercised against a live Inventor session. As always, test against your own templates before trusting it on production models.

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

Deploys `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\` and `ipt-mcp.exe` under `%LOCALAPPDATA%\Bimwright\ipt-mcp\server\<version>\`. Restart Inventor. Point your MCP client at that `ipt-mcp.exe` path. Pin a year with `--target 2025` or `BIMWRIGHT_INVENTOR_TARGET=2025`.

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

The full surface is **58 tools** by default when all platform toolsets are enabled, or **59 tools** when inventor_send_code is enabled (opt-in). Every MCP-facing name is prefixed `inventor_`. Tools are grouped into toolset classes; `--toolsets sketch,feature` and `--read-only` gate which ones register so weak models never see disabled tools.

Default-on toolsets: `meta`, `query`, `document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `assembly_query`, `toolbaker`, `toolbaker_write`.
Off by default: `code` (the `send_code` escape hatch — opt-in only).

All length inputs are in **mm**, angles in **degrees**; the add-in converts to Inventor's internal centimetres/radians.

### meta (3) — server-side target tools, never round-trip to the add-in; stay exposed under `--read-only`

| Tool | Description |
|---|---|
| `inventor_list_available_targets` | List detected live Inventor add-in targets (year, pid, transport, active document). |
| `inventor_get_current_target` | Report the server's currently selected target, or `NO_TARGET` if none is live. |
| `inventor_switch_target` | Select the active target by descriptor id, Inventor year, process id, or pipe/session name. Server-side only. |

### query (3) — read-only document/health probes

| Tool | Description |
|---|---|
| `inventor_health` | Probe the active add-in: inventor_year, process_id, whether a document is open, active document type. |
| `inventor_list_open_documents` | List all open documents: title, path, type, and which is active. |
| `inventor_get_document_info` | Get the active document's title, full path, and document type. |

### document (7) — document lifecycle (write)

| Tool | Description |
|---|---|
| `inventor_new_part` | Create a new part document (.ipt); optional template path. |
| `inventor_new_assembly` | Create a new assembly document (.iam); optional template path. |
| `inventor_open_document` | Open an existing document from a full path and make it active. |
| `inventor_save_document` | Save the active document, or Save-As to a given path. |
| `inventor_close_document` | Close the active document; `save=true` saves first. |
| `inventor_set_units` | Set the active document's length unit (mm, cm, m, in, ft). |
| `inventor_set_material` | Assign a material to the active part by name. |

### parameters (4) — model & user parameters (write)

| Tool | Description |
|---|---|
| `inventor_list_parameters` | List parameters (model + user): name, expression, value, unit, kind. |
| `inventor_get_parameter` | Get one parameter by name: expression, value, unit. |
| `inventor_set_parameter` | Set an existing parameter's expression/value, then update the document. |
| `inventor_create_parameter` | Create a new user parameter (name, expression, unit). |

### properties (3) — iProperties & mass properties (write)

| Tool | Description |
|---|---|
| `inventor_get_iproperty` | Get an iProperty value by property-set and property name. |
| `inventor_set_iproperty` | Set an iProperty value. |
| `inventor_get_mass_properties` | Mass (g), volume (mm³), surface area (mm²), centre of mass, bounding box. |

### sketch (9) — 2D sketch geometry & constraints (write)

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
| `inventor_close_sketch` | Finish editing a sketch (exit sketch edit mode). |

### feature (9) — solid & work features (write)

| Tool | Description |
|---|---|
| `inventor_extrude` | Extrude a named sketch (distance, join/cut/intersect, direction). |
| `inventor_revolve` | Revolve a named sketch about an axis (angle, operation). |
| `inventor_fillet` | Add a constant-radius edge fillet over model edges. |
| `inventor_chamfer` | Add an equal-distance edge chamfer over model edges. |
| `inventor_create_work_plane` | Create a work plane (offset, three_points, or tangent). |
| `inventor_create_work_axis` | Create a work axis (two_points, edge, plane_intersection, normal_to_face_through_point). |
| `inventor_hole` | Drilled/counterbore/countersink holes on a deterministically-selected planar face; optional tapped-thread metadata. |
| `inventor_circular_pattern` | Circular-pattern part features around a named axis (count over an angle). |
| `inventor_rectangular_pattern` | Rectangular-pattern part features along one or two named axes. |

### export (6) — view capture & geometry export (write)

| Tool | Description |
|---|---|
| `inventor_capture_view` | Capture the active view as a bounded base64 PNG, or write to an output path. |
| `inventor_export_step` | Export the active part/assembly to STEP (.stp/.step). |
| `inventor_export_stl` | Export the active part/assembly to STL (.stl). |
| `inventor_export_dxf` | Export a 2D DXF; must declare source (`sketch` or `flat_pattern`). |
| `inventor_view_fit` | Zoom-fit the active view to the model extents (run before capture). |
| `inventor_set_view_orientation` | Set a standard camera orientation (iso/front/top/…) for multi-angle captures. |

> Export paths must be absolute and under an allowed output root (user profile or temp).

### assembly (3, write) — compose assemblies via relationships, not coordinates

| Tool | Description |
|---|---|
| `inventor_place_occurrence` | Place a component (.ipt/.iam) into the active assembly; optional initial pose + grounded. |
| `inventor_add_constraint` | Constrain two named refs (mate/flush/insert/angle); response carries `health` — always check it. |
| `inventor_create_imate` | Author a named iMate on the active part using a deterministic face selector. |

### assembly_query (5, read-only) — numeric self-check battery; survives `--read-only`

| Tool | Description |
|---|---|
| `inventor_list_interfaces` | List named interfaces (iMates, work features, origin geometry) of the doc or one occurrence. |
| `inventor_check_interference` | Run interference analysis; returns pair count and total/per-pair volumes. |
| `inventor_measure_min_distance` | Minimum 3D distance (mm) between two occurrences or named refs. |
| `inventor_get_assembly_bom` | BOM + occurrence tree with grounded flag and translation/rotation degrees of freedom. |
| `inventor_list_constraints` | Read back every constraint with type, `health`, suppressed flag and the two occurrence names. |

### code (1) — opt-in escape hatch (OFF by default)

| Tool | Description |
|---|---|
| `inventor_send_code` | **Dangerous, opt-in only.** Execute a C# snippet in-process against `Inventor.Application`. Disabled unless both server and add-in opt in (else `SEND_CODE_DISABLED`); banned APIs (file/process/network/environment) are rejected. |

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

## Safety

Short version: your model stays on your machine, and write/dangerous tools are gated.

- **Read-only mode.** `--read-only` (or `BIMWRIGHT_INVENTOR_READ_ONLY=1`) removes every write-capable toolset (`document`, `parameters`, `properties`, `sketch`, `feature`, `export`, `assembly`, `code`, `toolbaker_write`) but keeps `meta` + `query` + `assembly_query` + read-only `toolbaker`, and **keeps `inventor_switch_target` exposed**. The server sends read-only mode in each command envelope; the add-in also honors `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`. A write command under enforced read-only returns `READ_ONLY`.
- **send_code two-sided opt-in.** `inventor_send_code` is **disabled by default**. It is exposed only when **both** gates are set: the server with `--enable-send-code` (or `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) **and** the add-in process with `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`. Otherwise the dispatcher returns `SEND_CODE_DISABLED`. Banned APIs (file/process/network/environment) are rejected.
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

Hand-forged MCP gateways for the AEC toolchain — one architecture, predictable / auditable / reversible:

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Vietnamese-first BIM knowledge base

---

## License

[Apache-2.0](LICENSE). See [LICENSE](LICENSE).

Inventor and Autodesk are registered trademarks of Autodesk, Inc. bimwright is an independent open-source project and is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.
