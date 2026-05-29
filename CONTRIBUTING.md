# Contributing

> **Stub (Phase 1).** Finalized in Phase 4.

Thanks for your interest in inventor-mcp. Contributions are welcome under the project's
[Apache-2.0](LICENSE) license and [Code of Conduct](CODE_OF_CONDUCT.md).

## Quick start

```bash
dotnet build src/InventorMcp.sln -c Debug
dotnet test  tests/Bimwright.Inventor.Tests -c Debug
```

The server and tests build with no Inventor SDK installed. Add-in projects can be
shape-checked with `/p:SkipInventorReferenceCheck=true`.

## Guidelines

- Follow the existing `Bimwright.Inventor.*` namespace and file-per-handler conventions.
- Add or update xUnit tests for any behavior change; the server-only suite must stay green.
- Never serialize Inventor API objects — return DTOs.
- Report security issues per [SECURITY.md](SECURITY.md), not as public issues.
