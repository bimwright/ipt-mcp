# Architecture

`ipt-mcp` is a full C# MCP stack — no TypeScript bridge, no Python sidecar, no IPC format hop. One language across the server, the per-version add-in shells, the transport, the handlers, ToolBaker, and the tests. The authoritative agent-facing cheat-sheet is [CLAUDE.md](CLAUDE.md); this document is the design deep-dive.

## Two processes, one local pipe

```text
MCP client (Claude Code / Cursor / Cline / …)
        │  stdio (MCP)
        ▼
Bimwright.Ipt.Server        (.NET 8 console, NO Inventor reference)
        │  TCP (2022-2024)  OR  Named Pipe (2025-2027) — NDJSON + token auth
        ▼
Bimwright.Ipt.Plugin.InvNN  (ApplicationAddInServer add-in, one per Inventor year)
        │  InventorStaDispatcher.InvokeAsync → Control.BeginInvoke (STA thread)
        ▼
Inventor API  (Inventor.Application / Document)
```

- **Server** is an MCP stdio server. It translates each `tools/call` into a JSON command envelope and forwards it over a local transport to whichever Inventor add-in is running. It holds **no Inventor reference** — it compiles only the API-agnostic contract files, so it builds and runs (and its tests pass) on any machine with the .NET 8 SDK.
- **Add-in** is an in-process `ApplicationAddInServer` loaded by Inventor. It runs a transport listener on a background thread, enqueues requests, and marshals each one onto Inventor's main STA thread. One thin shell per Inventor year (`src/plugin-invNN/`), all compiling the same `src/shared/**` source glob.

The version split is explicit at the edge. The server is version-agnostic; all Inventor-API coupling lives in the add-in.

## STA marshalling (the novel piece)

Inventor has **no `ExternalEvent`** — the mechanism `rvt-mcp` relies on to hop work onto the Revit UI thread. Inventor's API is STA-bound and must be touched only from the application's main thread, and `SynchronizationContext.Current` may be null inside the add-in. So `ipt-mcp` builds its own marshaller, `InventorStaDispatcher`:

- During the add-in's `Activate` (which runs on Inventor's STA thread), the dispatcher creates a **hidden, message-only WinForms `Control`** and forces its window handle to be created so `BeginInvoke` works.
- The TCP / Named-Pipe **listener runs on a background thread** and never touches the Inventor API directly.
- Each request is dispatched via `InventorStaDispatcher.InvokeAsync(work, timeoutMs)`, which posts the work through `Control.BeginInvoke` so it executes on the STA (UI) thread, and awaits the result with a timeout.
- `CommandDispatcher.Dispatch` runs **inside** `InvokeAsync`, so every `Inventor.Application` access is STA-bound by construction. The listener thread only ever reaches the marshalled state via `BeginInvoke`.
- On shutdown (`Deactivate`): dispose the transport, dispose the dispatcher, null out the cached `Application`, and `GC.Collect()` to release the COM references.

Contrast with `rvt-mcp`: Revit gives you `ExternalEvent.Raise()` + an event handler invoked on the UI thread. Inventor gives you neither, so the hidden-control message pump is the reliable substitute.

## Target framework matrix

| Inventor year | TFM | Transport | Notes |
|---------------|-----|-----------|-------|
| 2022 / 2023 / 2024 | `net48` | TCP | references `System.Windows.Forms` directly |
| 2025 / 2026 | `net8.0-windows7.0` | Named Pipe | `<UseWindowsForms>true</UseWindowsForms>`, `EnableDynamicLoading` |
| 2027 | `net10.0-windows7.0` | Named Pipe | needs the .NET 10 SDK; `UseInventorAssemblyContext=0` honored in the `.addin` |

- Inventor moved desktop add-in development off .NET Framework starting in 2025: **.NET 8 for 2025/2026, .NET 10 for 2027.** This differs from `nwd-mcp`, where every Navisworks 2022-2027 plug-in targets `net48`.
- .NET 8 add-ins remain *binary-compatible* on Inventor 2027 (they load without recompilation), so net8 is a viable fallback — but net10 is 2027's native target.
- Interop hint-path family: `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`, with `<EmbedInteropTypes>false</EmbedInteropTypes>` and `<Private>false</Private>`.
- Year-specific API drift lives behind `#if INVENTOR2022 … #endif` compile symbols (e.g. `InventorVersion.Year`). Each shell defines its own `DefineConstants`.

## The shared-glob partition

`src/shared/` is a **source-only folder** (no standalone csproj). What makes this project distinctive is that the server and the add-ins compile *different subsets* of it:

```text
src/shared/
├── Contracts/         API-AGNOSTIC. Compiled by BOTH server and add-ins.
│                      InventorCommandEnvelope, InventorCommandResult, InventorErrorCodes,
│                      TargetDescriptor, TargetRegistry, ResponseSizeGuard.
├── Security/          API-AGNOSTIC. Compiled by BOTH. ErrorSanitizer, SecretMasker.
├── Transport/         API-FREE. ITransportServer, TcpTransportServer, PipeTransportServer,
│                      TransportFactory, TargetDescriptorWriter, AuthToken.
├── Infrastructure/    PLUGIN-ONLY. IInventorCommand, InventorCommandContext, CommandDispatcher,
│                      IsExternalInit polyfill (net48).
├── Plugin/            PLUGIN-ONLY. InventorAddInServerBase, InventorStaDispatcher,
│                      InventorCommandRegistry.*, descriptor writer wiring.
└── Handlers/          PLUGIN-ONLY. One file per wire command (touches the Inventor API).
```

- **Server csproj** uses *explicit* `<Compile Include="..\shared\Contracts\*.cs" />` + `Security` (+ the server-side ToolBaker sources). It never pulls in `Infrastructure` / `Plugin` / `Handlers`, which is why it builds with **no Inventor SDK present**.
- **Each add-in csproj** uses a broad `<Compile Include="..\shared\**\*.cs" />` glob, pulling in everything — including the API-touching `Infrastructure` / `Plugin` / `Handlers`.
- The same partition keeps the **test project** Inventor-free: it links only the API-agnostic / API-free shared files (contracts, transport, descriptor writer, sanitizers), so the 91 tests run on any machine.

## Command envelope and error codes

The server and add-in exchange newline-delimited JSON. Request envelope (`InventorCommandEnvelope`):

```json
{
  "id": "<guid>",
  "command": "extrude",
  "params": { "sketch_name": "Sketch1", "distance_mm": 25, "operation": "join" },
  "timeout_ms": 30000,
  "auth_token": "…"
}
```

Response (`InventorCommandResult`):

```json
{
  "id": "<guid>",
  "ok": true,
  "data": { "...": "DTO — never a serialized Inventor COM object" },
  "error": null,
  "meta": { "target_id": "inventor-2025-12345", "inventor_year": 2025, "duration_ms": 42, "read_only_enforced": null }
}
```

Wire command names are **snake_case and unprefixed** (`extrude`); the MCP tool name is the prefixed `inventor_extrude`. The 3 `meta` tools and the server-side ToolBaker database tools have **no wire command** — they operate purely on the server's view of the registry / bake DB.

Canonical error codes (`InventorErrorCodes`) returned in `error.code`:

`NO_TARGET`, `TARGET_UNAVAILABLE`, `NO_DOCUMENT`, `WRONG_DOCUMENT_TYPE`, `INVALID_ARGUMENT`, `UNSUPPORTED_HOST`, `API_ERROR`, `TIMEOUT`, `RESPONSE_TOO_LARGE`, `READ_ONLY`, `SEND_CODE_DISABLED`, `UNAUTHORIZED`.

The `CommandDispatcher` is the add-in-side line of defense: a write command under `--read-only` → `READ_ONLY`; `send_code` without both opt-in gates → `SEND_CODE_DISABLED`; an unknown command → `INVALID_ARGUMENT`; an oversized response → `RESPONSE_TOO_LARGE`; any handler throw → a sanitized `API_ERROR`.

## Handler contract and units

- `IInventorCommand { string Name; bool IsReadOnly; InventorCommandResult Execute(InventorCommandContext, JObject); }`.
- Handlers cast `ctx.Application` to `Inventor.Application`. They **never** call the ROT / `GetActiveObject`, and **never** serialize Inventor COM objects — they always return DTOs (anonymous objects / `JObject`), because the COM objects are not serializable (circular refs, interop).
- Registration uses partial registrars (`InventorCommandRegistry.<Domain>.cs`), one file per domain, so no one edits a shared `Build()` list.
- **Units:** the Inventor API's internal length unit is **centimetres**. Every mm input is converted to cm (`mm/10`) and every length output back to mm (`cm*10`), centralized at the handler boundary. Angles convert degrees ↔ radians the same way.

## Target discovery

Each running add-in writes a per-instance descriptor to `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json` and keeps it warm with a background heartbeat (default 30s, well inside the registry's ~120s staleness window). The descriptor (`TargetDescriptor`):

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

- For **TCP** targets (2022-2024) `transport` is `tcp` and `port` carries the OS-assigned bound port; `pipe_name` is null.
- For **Named-Pipe** targets (2025-2027) `transport` is `pipe`, `port` is `0`, and `pipe_name` is `BimwrightInventor-<pid>` (built by `TransportFactory`).
- The server's `TargetRegistry.List()` scans these files and drops any whose `process_id` is dead, whose `last_heartbeat_utc` is older than the max age, whose `host_app != "Inventor"`, or whose year is outside 2022-2027.
- Agents enumerate live instances with `inventor_list_available_targets`, inspect the pinned one with `inventor_get_current_target`, and switch with `inventor_switch_target` — using 4-digit calendar years, never legacy version codes. On `Deactivate` the writer deletes its descriptor so the target stops being advertised.

## Request lifecycle

1. MCP client sends `tools/call` over stdio.
2. The server tool wrapper serializes typed parameters into a command envelope and picks the connection (current/pinned target, or auto-detect from the registry).
3. The server writes the NDJSON envelope over TCP/Pipe (with the auth token).
4. The add-in's listener thread enqueues the request and posts it through `InventorStaDispatcher.InvokeAsync`.
5. On the STA thread: the `CommandDispatcher` validates (read-only / send-code gates, unknown command), dispatches to the handler, sanitizes any leaking paths in errors, and applies the response-size guard.
6. The result envelope travels back up the same pipe.
7. The server resolves the pending request; the MCP tool method returns the DTO JSON.

Default per-request timeout is 30s. The listener cancels pending requests on add-in shutdown.

## Progressive disclosure & gates

Tools are grouped into `[McpServerToolType]` classes by domain. `Program.ResolveToolTypesForRegistration(cfg)` maps each enabled toolset to its tool type and de-dups (`DocumentTools` maps to both `query` and `document`), then registers only those, so disabled tools never appear in `tools/list`. `ToolsetFilter.Resolve` handles defaults, the `all` shortcut, silent dropping of unknown names, the `code`/`toolbaker` gates, and `--read-only` post-processing (strips every `WriteCapable` toolset). `ServerInstructions.Text` is keyword-dense (part/sketch/extrude/parameter/iproperty/export) so MCP Tool Search can discover the surface.

## Configuration precedence

`InventorMcpConfig.Load(args)` applies three layers, later wins: JSON file (`--config <path>`) < environment (`BIMWRIGHT_INVENTOR_*`) < CLI flags (`--read-only`, `--enable-send-code`, `--toolsets`, `--target`, `--timeout-ms`, …). The descriptor directory defaults to `%LOCALAPPDATA%\Bimwright\ipt-mcp`; the bake database to its `baked\` subfolder.

## Why this shape

- **Full C#, one stack.** No language hop between server, add-in, handlers, ToolBaker, and tests.
- **Process split, not thread split.** The server lives outside Inventor, so it can start before Inventor and outlive an add-in crash, and it can be updated without touching the add-in.
- **Server has no Inventor reference.** The API-agnostic contract subset is the only shared source the server compiles, keeping the gateway buildable and testable on any machine.
- **Source glob, not shared DLL.** Each add-in shell compiles `src/shared/**` directly, so version-specific `#if` branches produce distinct binaries — no runtime version sniffing.
- **STA via hidden WinForms control.** Inventor lacks `ExternalEvent`; `Control.BeginInvoke` from a handle created on the STA thread is the reliable UI-thread marshaller.
- **Per-version unique ClientId GUID** on each add-in (matching its `.addin`), so only the matching year loads.
