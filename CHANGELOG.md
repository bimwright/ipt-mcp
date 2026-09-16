# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `inventor_send_code` returns the script's `ScriptState.ReturnValue` as `data.result` (JSON-safe: primitives, arrays, anonymous DTOs, `JToken` pass-through; Inventor API/COM objects are rejected with DTO guidance). `inventor_send_code` accepts an optional `timeout_ms` (default = `--timeout-ms`, clamped to 1..600000) carried on the command envelope — the add-in's `task.Wait` is the single owner of the TIMEOUT decision; the transport only adds a 5 s read grace. TIMEOUT responses now explain that the script may still be running on the STA thread and point at `inventor_health`. `inventor_health` reports `sta_busy` and `pending_commands` and answers even while the STA thread is jammed (2 s fast-path synthesized from queue counters, marked `answered_without_sta`).
- Call journal v2: `finish` lines now record `error_code`, `target_id`, `response_bytes`, `plugin_duration_ms` and the payload-level outcome (`data_ok`, `data_error`, `stdout_bytes`) of `send_code` / `run_baked_tool` and of meta tools whose payload carries `ok` (`success` stays envelope-level); calls that fail before reaching a target (`NO_TARGET`) are journaled too; `session_id` identifies each server start (`server-<utc>-<pid>`); the journal path can be overridden with env `BIMWRIGHT_INVENTOR_CALL_LOG` (the test suite redirects it to `%TEMP%`).
- **Response-size guard (add-in):** responses ≥64 KiB carry a top-level `size_warning` ("warning", "strong_warning" >256 KiB); above 700 KiB the call fails with `RESPONSE_TOO_LARGE` plus a command-specific narrowing hint (`max_rows`, `occurrence=<name>`, send-code spilling, …). The 5 MB transport fence is unchanged.
- **Output spilling:** `send_code` stdout and `run_baked_tool` results over 64 KiB are written to `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\` (24 h TTL, 50-file cap); the response keeps the first 8 KiB inline plus `stdout_truncated`/`stdout_file` (or `results_truncated`/`results_file`/`results_count`/`results_preview`).
- `inventor_capture_view` gains an `inline` parameter (default `false`).
- `inventor_extrude` accepts `operation=new_body`, a `name` for the created feature (with `new_body` the body is named `<name>_body` and echoed as `body_name` — Inventor shares one browser namespace, so the body cannot take the feature's name), `distance` as a number (mm) **or** an Inventor expression string (`"40 mm"`, `"plate_thk"`), and `affected_bodies` (`["1","body:2","BodyName"]`) scoping join/cut on multi-body parts. Fixed `volume_mm3` always returning 0 (`SurfaceBody.get_Volume` rejects precision 0.0).
- `inventor_create_work_plane` gains `type=fixed` (`AddFixed`): `origin`/`x_axis`/`y_axis` accept `{x,y,z}` or `[x,y,z]` (origin in mm, axes are direction vectors auto-normalized; zero/parallel axes rejected); optional `name` and `visible` apply to all work-plane types; `refs` stays required for the other types.
- `inventor_list_bodies` and `inventor_list_features` (read-only, `query` toolset — visible under `--read-only` and callable from baked tools): bodies report `id` (`body:N`), `name`, `volume_mm3`, `bbox_mm`, `face_count`, `created_by` (producing feature), `visible`; features report `name`, `type`, `health` (`include_health=false` omits), `suppressed`, `body_names`. Both take `max_items` (default 200) and return `total`+`truncated`. Tool surface is now 62 default / 63 with send_code.
- `inventor_export_sat` (export toolset): export the active part/assembly to ACIS SAT (.sat) — the Revit interop path. `acis_version` defaults to 7; the Inventor SAT translator's `Version` option supports ACIS 7.0 only, so other values are rejected rather than writing a file the consumer can't read. The export-section docs now show how to register a workspace output root via `BIMWRIGHT_INVENTOR_EXPORT_ROOT` (the C4 gap — allowed-root failures pointed at the policy but not at how to extend it).
- `inventor_set_camera` (export toolset): position the active view's camera explicitly — `eye`/`target` in mm (`{x,y,z}` or `[x,y,z]`), `up` direction, `perspective`, `extents_mm` `[width,height]`, optional `fit` (default false so explicit framing is kept). Rejects degenerate views (`eye`==`target`, zero-length or view-parallel `up`) and returns the resolved camera state for verification without a capture round-trip.
- Server-side `ToolResponse` serializes compactly above 4 KiB and indented at/below 4 KiB, so large payloads no longer pay indentation overhead on the wire.

### Fixed

- `send_code` source policy is token-aware instead of a raw case-insensitive substring scan: comments and string/char literals (regular, verbatim, interpolated, raw `"""…"""` incl. `$$"""` holes) are stripped before matching — interpolation holes are still scanned — and tokens match on word boundaries with C#'s case sensitivity. This removes the observed false positives (`VisibleSocket` in a body name, `GetType(`/`typeof(` metadata reads, lowercase `file.`/`process.` variables, banned text inside strings/comments) while still blocking file/process/network/environment APIs, ToolBaker re-entry, and invoke/load-style reflection (`GetMethod`/`GetProperty`/`GetField`/`GetMember`/`GetEvent`/`GetConstructor`, `Invoke`/`DynamicInvoke`/`BeginInvoke`, `Activator.`, `Assembly.Load*`, `Delegate.CreateDelegate`). Errors now name the calling surface (`send_code source uses forbidden token: X` vs `Baked tool …`).
- Removed the `send_code` handler's private 30 s `CancellationTokenSource` (a third, ineffective timeout layer that could not interrupt a synchronous script on the STA thread); `InventorStaDispatcher.InvokeAsync` no longer takes an unused `timeoutMs` parameter. Plugin-side `Err` responses now carry `InventorResponseMeta` (target id/year) and preserve the request id, so journal entries attribute TIMEOUT/UNAUTHORIZED correctly.

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
