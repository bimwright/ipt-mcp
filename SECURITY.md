# Security Policy

> **Stub (Phase 1).** Finalized in Phase 4.

## Reporting a vulnerability

Please report security issues privately (do not open a public issue). Contact the
maintainer and allow reasonable time for a fix before disclosure.

## Security model (summary)

- The transport (TCP loopback / Named Pipe) is authenticated with a per-session token
  carried in every command envelope; mismatches return `UNAUTHORIZED`.
- `inventor_send_code` (arbitrary C# via Roslyn) is **disabled by default** and requires
  an explicit opt-in on both the server (`--enable-send-code`) and the add-in
  (`BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`).
- ToolBaker compiles/runs only a constrained command surface; see [`docs/toolbaker.md`](docs/toolbaker.md).
- Error messages are sanitized (file paths and secrets stripped) before leaving the add-in.
