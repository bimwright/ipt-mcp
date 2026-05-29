# ToolBaker

> **Stub (Phase 1).** Filled in Phase 4 (ported from `nwd-mcp/docs/toolbaker.md`).

ToolBaker is the self-evolution layer: it lets approved, compiled C# snippets be
registered as reusable read-only baked tools and run via `inventor_run_baked_tool`.

## Planned contents (Phase 4)

- Env vars: `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE`, `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE`.
- Persistence directory: `%LOCALAPPDATA%\Bimwright\inventor-mcp\baked`.
- Banned-API policy (`BakeCompilerPolicy`) and the dispatch deny-list
  (`send_code, batch_execute, run_baked_tool, accept/dismiss_bake_suggestion, list_baked_tools`).
- Allowed baked-tool commands = the read-only Inventor query commands.
