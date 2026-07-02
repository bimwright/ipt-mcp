# Contributing to Bimwright Inventor MCP

Thanks for your interest. Bimwright Inventor MCP is an open-source (Apache-2.0) MCP gateway for
Autodesk Inventor 2022–2027. Open an issue before a large PR so we can agree on scope.

## Dev setup

### Prereqs

- Windows 10/11 (Autodesk Inventor is Windows-only).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — required for the server, tests,
  and the 2025/2026 add-ins.
- .NET Framework 4.8 Developer Pack — required to compile the `net48` add-ins (2022–2024).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — required to compile the 2027
  add-in (`net10.0-windows7.0`), even for a shape-only check.
- Visual Studio 2022+ or JetBrains Rider.
- One or more Autodesk Inventor installations (2022–2027) for add-in compile and runtime testing.

To compile and run the server + tests *only*, no Inventor installation is required — the server has
no Inventor reference and compiles only the API-agnostic shared source.

### Clone + build

```bash
git clone https://github.com/bimwright/ipt-mcp.git
cd ipt-mcp

# Server + tests (no Inventor required):
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# Legacy TFM compatibility check using an installed compatible interop reference:
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"

# Real 2027 compile (needs the .NET 10 SDK and installed Inventor 2027 interop):
dotnet build src/plugin-inv27 -c Debug
```

Every add-in compile needs an Inventor interop reference. Pass `/p:InventorInteropDir=...` if the
interop used for a compatibility check or real build is not at that project's default path
(`C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop`).

**Close every running Inventor before deploying add-in DLLs** — Inventor holds file locks on loaded
add-ins. The per-user bundle deploys to
`%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\`.

> Autodesk Inventor binaries and SDK/interop DLLs are **not** redistributed by this repo. Building
> the add-ins requires a local Inventor installation, the matching interop assemblies, or explicit
> MSBuild reference-path properties.

## Project layout

See [ARCHITECTURE.md](ARCHITECTURE.md) for the conceptual model. Quick reference:

| Path | What lives here |
|------|-----------------|
| `src/server/` | MCP server, tool registration, stdio entry points; **no Inventor reference** |
| `src/server/Tools/` | `[McpServerToolType]` classes, one per toolset/domain |
| `src/shared/Contracts/` | API-agnostic envelope/result/error/descriptor + response guard |
| `src/shared/Infrastructure/` | `CommandDispatcher`, `IInventorCommand`, `InventorCommandContext` |
| `src/shared/Transport/` | `ITransportServer`, TCP + Named-Pipe NDJSON transports, descriptor writer |
| `src/shared/Plugin/` | `InventorAddInServer`, `InventorStaDispatcher`, partial command registry |
| `src/shared/Handlers/` | one file per wire command (the Inventor API implementation) |
| `src/shared/Security/` | `SecretMasker`, `ErrorSanitizer` |
| `src/shared/ToolBaker/` | Roslyn-based self-evolution engine + safety policy |
| `src/plugin-inv22..24/` | net48 add-in shells (TCP transport) |
| `src/plugin-inv25..27/` | net8 / net10 add-in shells (Named Pipe transport) |
| `tests/Bimwright.Ipt.Tests/` | xUnit tests (pure .NET 8, no Inventor API) |

## Adding a new MCP tool

1. Write the handler in `src/shared/Handlers/<Domain>/<Verb><Noun>Handler.cs` implementing
   `IInventorCommand` (`Name`, `IsReadOnly`, `Execute`). Cast `ctx.Application` to
   `Inventor.Application`; never call the ROT / `GetActiveObject`. Return DTOs (anonymous objects or
   `JObject`) — never serialize Inventor API objects. Convert mm↔cm via
   `src/shared/Handlers/UnitConvert.cs` (Inventor's internal length unit is centimetres).
2. Register it in the matching partial registrar
   `src/shared/Plugin/InventorCommandRegistry.<Domain>.cs` (`add(new YourHandler());`).
3. Add an `[McpServerTool(Name = "inventor_<wire>")]` method on the owning toolset class under
   `src/server/Tools/`. The wire command sent to the add-in is the snake_case name minus the
   `inventor_` prefix.
4. Cover non-trivial logic with an xUnit test, including the registration/read-only snapshot.
5. Smoke test in at least one Inventor version before the PR (see
   [`docs/testing/manual-smoke.md`](docs/testing/manual-smoke.md)).

## Coding style

- Match the surrounding code; existing handlers are the authoritative reference.
- DTOs are anonymous objects or `JObject`s with lowercase JSON property names.
- Comments explain *why*, not *what*.
- Server tool registration lives once in `Program.cs`; add-in handler registration uses the
  per-domain partial registrars so no shared `Build()` list is edited concurrently.

## Commit + PR

- One logical change per commit. Commit messages start with a short scope prefix
  (e.g. `handlers:`, `transport:`, `docs:`).
- Open a PR against `main`. CI (server-only build + tests) must be green.
- Include a "Tested with" line: which Inventor year(s) you smoke-tested, if any.

## Code of Conduct

Be kind. Assume good faith. See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
([Contributor Covenant v2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/)).
