# Architecture

> **Stub (Phase 1).** Expanded in Phase 4 with diagrams. The authoritative cheat-sheet is [`CLAUDE.md`](CLAUDE.md); the source design lives in `docs/superpowers/specs/2026-05-29-inventor-mcp-design.md`.

## Processes

```
MCP client → stdio → Bimwright.Inventor.Server (.NET 8, no Inventor reference)
   → TCP (2022-2024) | Named Pipe (2025-2027) → Inventor add-in (ApplicationAddInServer) → Inventor API
```

- **Server** compiles only the API-agnostic `shared/Contracts/*` + `shared/Security/*` (+ ToolBaker), so it builds with no Inventor SDK present.
- **Add-in** globs all of `shared/**` (incl. `Infrastructure`, `Transport`, `Plugin`, `Handlers`), runs the transport listener, and marshals commands onto the STA thread.

## Target discovery

Per-version JSON descriptors in `%LOCALAPPDATA%\Bimwright\inventor-mcp\`. `TargetRegistry.List()` returns only live Inventor targets (drops dead PID / stale heartbeat / non-Inventor host / out-of-range year).

## STA marshalling

Inventor has no `ExternalEvent`. `InventorStaDispatcher` wraps a hidden message-only WinForms control created on the STA thread during `Activate`; `Control.BeginInvoke` runs each command on Inventor's UI thread.

## TFM matrix

| Year | TFM | Transport |
|------|-----|-----------|
| 2022-2024 | net48 | TCP |
| 2025-2026 | net8.0-windows7.0 | Named Pipe |
| 2027 | net10.0-windows7.0 | Named Pipe |

## Shared-glob partition

- Server: explicit `<Compile Include="..\shared\Contracts\*.cs" />` + `Security` (+ ToolBaker).
- Add-ins: `<Compile Include="..\shared\**\*.cs" />`.
