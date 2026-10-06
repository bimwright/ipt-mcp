# ipt-mcp

Open-source (Apache-2.0) MCP gateway that lets Claude Code (and any MCP-capable client) drive Autodesk Inventor 2022-2027.

## Architecture

Full C# stack. No TypeScript. Single language, single build system. Multi-version: one in-process add-in per Inventor 2022-2027, all compiled from the same `src/shared/**` source glob.

```
MCP client (Claude Code / Cursor / Cline / …) → stdio → C# MCP Server (.NET 8 console)
   → TCP (2022-2024) or Named Pipe (2025-2027) → per-version Inventor add-in → Inventor API
```

Two processes:
- **Bimwright.Ipt.Server.exe** — MCP server, separate process, stdio transport (`ModelContextProtocol` 1.1.0). Has NO Inventor reference; it only compiles the API-agnostic contract files.
- **Bimwright.Ipt.Plugin.InvNN.dll** — `ApplicationAddInServer` add-in, loads inside `Inventor.exe`, runs a TCP or Named-Pipe listener and marshals every command onto Inventor's STA thread.

Communication: newline-delimited JSON (NDJSON). Per-version discovery files are written to `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`.

Discovery / target descriptor (`TargetDescriptor`):
```json
{
  "target_id": "inventor-2025-12345",
  "inventor_year": 2025,
  "process_id": 12345,
  "host_app": "Inventor",
  "transport": "pipe",
  "port": 0,
  "pipe_name": "BimwrightInventor-12345",
  "auth_token": "…",
  "document_title": "Part1.ipt",
  "document_path": "C:\\…\\Part1.ipt",
  "last_heartbeat_utc": "2026-05-29T12:00:00Z"
}
```

The server scans these files (`TargetRegistry.List()`), dropping any whose `process_id` is dead, whose `last_heartbeat_utc` is older than the max age (~120 s), whose `host_app != "Inventor"`, or whose year is outside 2022-2027. Use 4-digit calendar years (2022..2027), never legacy version codes. Agents should call `inventor_list_available_targets` to enumerate live instances and `inventor_get_current_target` to inspect the pinned one rather than guessing.

## Project Structure

```
src/
├── IptMcp.sln                 # Solution (server + tests; 6 add-ins added in Phase 2)
├── server/                         # MCP server (console, net8.0). NO Inventor reference.
│   ├── Bimwright.Ipt.Server.csproj   # explicit-includes shared/Contracts + Security (+ ToolBaker)
│   ├── Program.cs                  # boot + ResolveToolTypesForRegistration → WithTools(...)
│   ├── InventorMcpConfig.cs        # CLI/env/JSON config (toolsets, read-only, send-code, …)
│   ├── ToolsetFilter.cs            # KnownToolsets / DefaultOn / WriteCapable + Resolve()
│   ├── PluginClient.cs             # transport client (tcp+pipe), target selection
│   └── Tools/                      # [McpServerToolType] classes, one per domain
│       ├── MetaTools.cs            # 3 server-side target tools (list/get/switch)
│       ├── QueryTools.cs  DocumentTools.cs
│       ├── ParameterTools.cs  PropertyTools.cs  SketchTools.cs  FeatureTools.cs
│       ├── ExportTools.cs  CodeTools.cs  ToolBakerTools.cs  ToolBakerWriteTools.cs
├── shared/
│   ├── Contracts/                  # API-AGNOSTIC — server explicit-includes; add-ins glob too
│   │   ├── InventorCommandEnvelope.cs  InventorCommandResult.cs  InventorErrorCodes.cs
│   │   ├── TargetDescriptor.cs  TargetRegistry.cs  ResponseSizeGuard.cs
│   ├── Security/                   # ErrorSanitizer, SecretMasker
│   ├── Infrastructure/             # PLUGIN-ONLY glob: IInventorCommand, InventorCommandContext,
│   │                               #   CommandDispatcher, IsExternalInit polyfill (net48)
│   ├── Transport/                  # ITransportServer, TcpTransportServer, PipeTransportServer, AuthToken
│   ├── Plugin/                     # InventorAddInServer, InventorStaDispatcher, registry, descriptor writer
│   └── Handlers/                   # one file per wire command (Phase 2-3)
├── plugin-inv22/ inv23/ inv24/     # net48, TCP transport
├── plugin-inv25/ inv26/            # net8.0-windows7.0, Named Pipe
└── plugin-inv27/                   # net10.0-windows7.0, Named Pipe
tests/Bimwright.Ipt.Tests/     # net8.0, xUnit. Server-only — no Inventor needed.
```

The **server** compiles only `shared/Contracts/*` + `shared/Security/*` (and ToolBaker) explicitly, so it builds with no Inventor SDK present. Each **add-in** uses `<Compile Include="..\shared\**\*.cs" />` to pull in everything, including the API-touching `Infrastructure`/`Plugin`/`Handlers`.

## Documentation Boundary

This is a public repository. Keep private design discussions, implementation plans,
handoffs, review transcripts, raw session logs and diagnostic captures in the
maintainer's separate private documentation checkout, never inside this repository
or its worktrees. Do not create junctions to private notes in the public tree.
Public docs describe the product, contributor procedures and reproducible tests.
Run `pwsh -NoProfile -File scripts/check-public-tree.ps1` before publishing changes.

## Build & Test

```bash
# Server + tests (server-only; no Inventor required, works on any machine with the .NET 8 SDK):
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# Legacy TFM compatibility check using the installed 2027 interop reference:
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"

# Real 2027 add-in compile (needs the .NET 10 SDK and installed Inventor 2027 interop):
dotnet build src/plugin-inv27 -c Debug
```

Inventor must be CLOSED before deploying add-in DLLs it would otherwise lock. The Inventor-API handlers have been smoke-tested against a live Inventor session; hold new handler bodies to the same verification bar before calling them done.

## Multi-Version Matrix

| Inventor year | TFM                    | Transport   | Notes                                   |
|---------------|------------------------|-------------|-----------------------------------------|
| 2022 / 2023 / 2024 | `net48`           | TCP         | references `System.Windows.Forms` directly |
| 2025 / 2026   | `net8.0-windows7.0`    | Named Pipe  | `<UseWindowsForms>true</UseWindowsForms>`, `EnableDynamicLoading` |
| 2027          | `net10.0-windows7.0`   | Named Pipe  | needs the .NET 10 SDK; `UseInventorAssemblyContext=0` honored |

- The MCP server is one process, unaffected by the Inventor version.
- Each add-in is a thin shell per year (`src/plugin-invNN/`) compiling the same `src/shared/**` glob.
- Version differences via `#if INVENTOR2022 … #endif` compile symbols (e.g. `InventorVersion.Year`).
- Interop HintPath family: `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`. `<EmbedInteropTypes>false</EmbedInteropTypes>`, `<Private>false</Private>`.

## Key Patterns

### Threading / STA marshalling (the novel piece)
Inventor has **no `ExternalEvent`** (unlike Revit). The add-in marshals every command onto Inventor's main STA thread via `InventorStaDispatcher`: a hidden message-only WinForms `Control` created during `Activate` (on the STA thread), whose handle is forced so `BeginInvoke` works.
- TCP / Named-Pipe listener runs on a background thread.
- Each request → `InventorStaDispatcher.InvokeAsync(work)` → `Control.BeginInvoke` → runs on the UI thread; `HandleLine` applies the `env.TimeoutMs` wait and is the single owner of TIMEOUT.
- `CommandDispatcher.Dispatch` runs **inside** `InvokeAsync`, so all `Inventor.Application` access is STA-bound.
- `InventorStaDispatcher.Stats` counts queued/executing work; `health` reports `sta_busy`/`pending_commands` and still answers (queue counters only) when the STA thread is jammed.
- The listener thread NEVER touches `_marshal` except via `BeginInvoke`.
- Shutdown (`Deactivate`): dispose transport, dispose dispatcher, null out `_app`, `GC.Collect()`.

### Command history (rvt-mcp parity)
`HandleLine` fans every authorized command outcome through `RecordOutcome` → plugin-side journal + in-memory session log + toast, covering the STA path, the STA-independent `report_task_result` path, timeouts, and dispatch errors.

- `src/shared/Logging/` (host-free — compiled into the test project):
  - `McpLogger` — `mcp-calls.jsonl` under `%LOCALAPPDATA%\Bimwright\ipt-mcp`, 5 MB rotation + `mcp-calls.version`, per-launch `session_id`, sanitized/redacted fields (`send_code` params collapse to `{code_hash, code_length}`). Writes are locked — they arrive from the listener thread AND the STA (re-run).
  - `McpSessionLog` — `ObservableCollection` capped at 1000 live entries; mutations marshal through the history UI thread (`HistoryHost.Post`) so the bound grid never sees a cross-thread write. `EntryAdded` drives the ribbon count. `send_code` bodies redact to hash/length unless `BIMWRIGHT_CACHE_SEND_CODE_BODIES=1`.
  - `SessionLogHistoryLoader` — rotated archives + current journal → `IsHistorical` rows (negative index, session tag); hides the newest `liveSessionCount` rows of the current session so evicted live rows return read-only without duplicating the tail.
  - `SendCodeJournal` + `SendCodeJournalGate` + `PersistSendCodeTtl` — opt-in (`BIMWRIGHT_PERSIST_SEND_CODE_BODIES`, TTL default 4 h) bake-redacted body persistence; `TryFindCodeByHash` recovers bodies for re-run. `RunMaintenance` purges at Activate.
  - `IptPrivacyConfig` — env-driven privacy flags (rvt-mcp uses a JSON config; same env names).
- `HistoryHost` (`src/shared/Views/`) owns a dedicated WPF STA thread (`Bimwright.Ipt.History`) — separate from the demand-driven toast thread because the session log marshals mutations through it from the first command. `HistoryWindow` is API-free: re-run is injected as `Func<string,string?,Task<InventorCommandResult>>` and marshals through `InventorStaDispatcher.InvokeAsync` (Inventor has no `ExternalEvent`). Kind filtering uses the handler's `IsReadOnly` captured on the entry (authoritative; rvt classifies by name).
- Ribbon: `Bimwright ▸ MCP → History (N)` — Inventor ribbon captions are creation-time only, so `BimwrightRibbon` recreates the `ButtonDefinition` + its bound `CommandControl`s in place on count change (coalesced via a pending flag + STA post).

### Commands / handlers
- `IInventorCommand { string Name; bool IsReadOnly; InventorCommandResult Execute(InventorCommandContext, JObject); }`.
- Wire command names are snake_case and unprefixed (`extrude`); the MCP tool name is `inventor_extrude`. The 3 meta tools and the server-side ToolBaker DB tools have NO wire command.
- Handlers cast `ctx.Application` to `Inventor.Application`. They NEVER call the ROT / `GetActiveObject`, and NEVER serialize API objects — always return DTOs (anonymous objects / `JObject`).
- Registration: partial registrars (`InventorCommandRegistry.<Domain>.cs`), one file per workstream, so nobody edits a shared `Build()` list.
- **Units:** the Inventor API uses internal **centimetres**. Every mm input → cm (`mm/10`); every length output → mm (`cm*10`). Centralized in `shared/Handlers/UnitConvert.cs`.
- **Assembly refs resolve iMate → work feature → origin by NAME** (`AssemblyRefResolver`; occurrence-scope entities are wrapped in assembly-context proxies via `CreateGeometryProxy`); face selection is deterministic (`FaceSelectorSpec`/`FaceSelector`, no face indexes); constraint responses carry `health` and callers MUST check it (a solver-sick constraint does not throw).

### MCP tools / registration
- All MCP-facing names prefixed `inventor_`. Tools live in `[McpServerToolType]` classes grouped by domain.
- `Program.ResolveToolTypesForRegistration(cfg)` maps toolset → type; `query` and `document` are separate tool classes so read-only filtering is by type.
- Progressive disclosure: `--toolsets sketch,feature` and `--read-only` gate which tools register, so weak models never see disabled tools.
- `ServerInstructions.Text` is keyword-dense (part/sketch/extrude/parameter/iproperty/export) so MCP Tool Search can discover the surface.

### Read-only and execution switches
- `code` is on by default. `--disable-send-code` hides all four code tools; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` is the host kill switch. `--read-only` keeps only annotated read tools (28), with no script execution. Treat send_code as full local host trust.
- `--read-only` removes every `WriteCapable` toolset (`document, parameters, properties, sketch, feature, export, assembly, code, toolbaker_write, drawing`) but keeps `meta` + `query` + `assembly_query` + `drawing_query` + read-only `toolbaker`, and KEEPS `inventor_switch_target` exposed. The server also sends read-only state in each envelope; the add-in can be hard-locked with `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`.
- `CommandDispatcher` is the second line of defense: write command under read-only → `READ_ONLY`; `send_code` without the gate → `SEND_CODE_DISABLED`; unknown command → `INVALID_ARGUMENT`; oversized response → `RESPONSE_TOO_LARGE`; handler throw or handler-returned error → sanitized `API_ERROR`.

### Config precedence
`InventorMcpConfig.Load(args)`: JSON file (`--config`) < environment (`BIMWRIGHT_INVENTOR_*`) < CLI flags. Descriptor dir defaults to `%LOCALAPPDATA%\Bimwright\ipt-mcp`.
The call journal path is **env-only**: `BIMWRIGHT_INVENTOR_CALL_LOG` (full file path; default `%LOCALAPPDATA%\Bimwright\ipt-mcp-calls.jsonl`) — `ServerLogger` is static and initializes before config loads, so there is no CLI/JSON equivalent.

## Error Codes (`InventorErrorCodes`)
`NO_TARGET, TARGET_UNAVAILABLE, NO_DOCUMENT, WRONG_DOCUMENT_TYPE, INVALID_ARGUMENT, UNSUPPORTED_HOST, API_ERROR, TIMEOUT, RESPONSE_TOO_LARGE, READ_ONLY, SEND_CODE_DISABLED, UNAUTHORIZED`.

## Decision Log
- **C# server over TypeScript** — single language, direct API access patterns shared with rvt-mcp / nwd-mcp.
- **STA via hidden WinForms control** — Inventor lacks `ExternalEvent`; `Control.BeginInvoke` is the reliable UI-thread marshaller. Created on the STA thread during `Activate`.
- **TCP ≤2024 / Named Pipe ≥2025** — Named Pipe avoids the loopback-firewall prompt on modern Windows; net48 add-ins keep TCP.
- **DTO mapping mandatory** — Inventor COM objects are not serializable (circular refs, interop).
- **Internal centimetres** — Inventor's API length unit is cm; convert at the handler boundary.
- **Server has no Inventor reference** — keeps the gateway buildable / testable on any machine; the API-agnostic contract files are the only shared source the server compiles.
- **Per-version unique ClientId GUID** on each `InventorAddInServer` (matches the `.addin`), so only the matching year loads.
```

Drawing Phase 1 adds 11 tools (99 all-enabled / 95 code-off / 20 read-only). The drawing implementation uses shared wire validation, registry/STA dispatch, persistent AttributeSets, atomic batches, per-call timeout, metadata catalog and truthful compact write responses. Inventor 2027 is the first host; older add-ins return UNSUPPORTED_HOST. See docs/testing/drawing-phase1.md for acceptance gaps and the disposable live fixture.
