# inventor-mcp

> **Status: Phase 1 (scaffold + runtime skeleton).** Not production-ready. This README is a stub; the full overview, supported-versions table, install/dev instructions, and the complete Phase-1 tool list are written in Phase 4.

Open-source ([Apache-2.0](LICENSE)) [Model Context Protocol](https://modelcontextprotocol.io) gateway that lets Claude Code and any MCP-capable client drive **Autodesk Inventor 2022-2027**.

## Overview

A .NET 8 MCP stdio server talks NDJSON over a local authenticated transport (TCP for 2022-2024, Named Pipe for 2025-2027) to a per-version in-process Inventor add-in. The add-in marshals every command onto Inventor's STA thread and returns a JSON envelope.

See [`CLAUDE.md`](CLAUDE.md) for the architecture cheat-sheet and [`ARCHITECTURE.md`](ARCHITECTURE.md) for the design.

## Supported versions

| Inventor | Runtime | Transport |
|----------|---------|-----------|
| 2022-2024 | .NET Framework 4.8 | TCP |
| 2025-2026 | .NET 8 | Named Pipe |
| 2027 | .NET 10 | Named Pipe |

## Build & test (server-only, no Inventor required)

```bash
dotnet build src/InventorMcp.sln -c Debug
dotnet test  tests/Bimwright.Inventor.Tests -c Debug
```

## Safety

- `inventor_send_code` is **disabled by default** — opt-in via `--enable-send-code` plus a plug-in env flag.
- `--read-only` hides every write-capable tool.
- See [`docs/toolbaker.md`](docs/toolbaker.md) and [`SECURITY.md`](SECURITY.md).

## Not redistributed

This project does **not** redistribute Autodesk binaries or the Inventor SDK. You must have a licensed Inventor install to build the add-ins or run the gateway against Inventor.

## License

[Apache-2.0](LICENSE) © Khoa Le.
