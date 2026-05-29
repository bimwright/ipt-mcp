# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - Unreleased

First public surface of `bimwright/inventor-mcp` — a local MCP gateway that lets Claude Code
(and any MCP-capable client) drive Autodesk Inventor 2022-2027 through an in-process add-in.

### Added

- **MCP server** (`Bimwright.Inventor.Server`, .NET 8 console, stdio transport,
  `ModelContextProtocol` 1.1.0). Single process, independent of the Inventor version; compiles
  only the API-agnostic shared contract files, so it builds and tests on any machine with the
  .NET 8 SDK — no Inventor required.
- **Six per-version in-process add-ins** (`Bimwright.Inventor.Plugin.InvNN`), one per
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
  `%LOCALAPPDATA%\Bimwright\inventor-mcp\inventor-<year>-<pid>.json`. The server enumerates live
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
  `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Inventor.bundle\` layout (a `PackageContents.xml`
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

[Unreleased]: https://github.com/bimwright/inventor-mcp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/bimwright/inventor-mcp/releases/tag/v0.1.0
