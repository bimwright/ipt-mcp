# Inventor MCP — Phase 3 Review Gate (the 46-tool surface)

> Reviewer: Opus 4.8, MAX rigor. Branch `feat/inventor-mcp`.
> Date: 2026-05-29.
> Scope: Phase 3 commits 58cb7f2 (bootstrap), aa79231 (WS3-A), df832d4 (WS3-B), 121a229 (WS3-C).
> References: plan `D:\Projects\bimwright\docs\superpowers\plans\2026-05-29-inventor-mcp-implementation.md` (PHASE 3 + Frozen Integration Contracts); spec `D:\Projects\bimwright\docs\superpowers\specs\2026-05-29-inventor-mcp-design.md` (Phase 1 Tool Surface, Read-Only Mode, Toolset Registration, Handler Boundaries, Error Handling).

## Verdict: PASS (after fixes applied)

Baseline going in: server + inv25 real-compile + inv27 shape green; 86 tests pass; git clean — but the tool count was **45, not 46**. After applying the fixes below the surface is exactly **46** and the full suite (91 tests) is green.

---

## Findings

### F1 — BLOCKER (confirmed in brief): `inventor_health` MCP wrapper missing → count was 45, not 46. FIXED.
- The wire handler `health` exists and is read-only: `src/shared/Handlers/Core/HealthHandler.cs` (`Name => "health"`, `IsReadOnly => true`), registered via `AddCore` in `InventorCommandRegistry.Core.cs`.
- No server `[McpServerTool]` wrapper named `inventor_health` existed in any Tool class, so `health` was unreachable from MCP and the surface registered 45 tools.
- The spec "Core And Document Tools" table lists `inventor_health` (read-only) and the manual-smoke step 6 calls it.
- **Fix:** added a read-only `Health()` wrapper to `DocumentTools` (`src/server/Tools/DocumentTools.cs`) that does `Call("health", new JObject(), ct)`. Placed with the query (read-only) methods. `DocumentTools` is registered under the `query` toolset, so `inventor_health` correctly survives `--read-only` (query is not in `WriteCapable`) and the handler being `IsReadOnly=true` means the dispatcher allows it too. Count is now **46**.

### F2 — Wire-name ↔ handler-name diff (CRITICAL: compiler cannot catch). PASS, no mismatch.
I extracted every `Call("<wire>")` / `SendAsync("<wire>")` string in the server Tools and every `IInventorCommand.Name` in `src/shared/Handlers/**`, then diffed the two sets.

**Server wire strings that round-trip (38, after the health fix):**
`health` (NEW), list_open_documents, get_document_info, new_part, new_assembly, open_document, save_document, close_document, set_units, set_material, list_parameters, get_parameter, set_parameter, create_parameter, get_iproperty, set_iproperty, get_mass_properties, create_sketch, project_geometry, draw_line, draw_circle, draw_rectangle, draw_arc, add_sketch_dimension, add_sketch_constraint, close_sketch, extrude, revolve, fillet, chamfer, create_work_plane, create_work_axis, capture_view, export_step, export_stl, export_dxf, send_code, run_baked_tool, apply_bake.

**Handler `Name` values registered (39 distinct):** the 38 above plus `apply_bake` and `run_baked_tool` (both handlers exist). Every handler name is wired into `InventorCommandRegistry.Build` via its domain registrar.

**Diff result:**
- Server-wire − handler-names = ∅ (every wire string has a matching handler). No typo, no missing handler.
- handler-names − server-wire = ∅ for round-trip commands. (`apply_bake` is invoked server-side by `ToolBakerWriteTools.AcceptBakeSuggestion` via `SendAsync("apply_bake", …)` and `run_baked_tool` by `RunBakedTool`; both have handlers.)
- Server-side-only tools (no wire) are exactly the 3 meta tools (`inventor_list_available_targets`/`get_current_target`/`switch_target`) and the 3 read-only ToolBaker DB tools (`inventor_list_baked_tools`/`list_bake_suggestions`/`create_bake_issue_draft`) — matches the Frozen Contracts wire-name table.

A new test (`RegistrationCountTests.The_46_tools_match_the_frozen_phase1_surface`) pins the exact 46-name set so a future rename/typo fails CI.

### F3 — Read-only flags (`IsReadOnly`) vs spec tables. PASS.
All 39 handler `IsReadOnly` flags match the spec read-only column:
- Read-only (true): health, get_document_info, list_open_documents, list_parameters, get_parameter, get_iproperty, get_mass_properties, capture_view, export_step, export_stl, export_dxf.
- Write (false): new_part, new_assembly, open_document, save_document, close_document, set_units, set_material, set_parameter, create_parameter, set_iproperty, all 9 sketch, all 6 feature, send_code, run_baked_tool, apply_bake.
- The four export handlers are `IsReadOnly=true` (they do not mutate the doc), yet the `export` *toolset* is in `WriteCapable` (hidden under `--read-only`) because Phase 1 has no full output-path policy — exactly the spec's documented narrower behaviour. Consistent.

### F4 — Handler boundaries. PASS (spot-checked across all domains).
Verified `HealthHandler`, `ExtrudeHandler`, `GetMassPropertiesHandler`, `ExportDxfHandler`, `SendCodeHandler`, `EntityResolver`:
- All cast `ctx.Application` to `Inventor.Application` (`(Application)ctx.Application!`); none call ROT / `GetActiveObject`.
- All return DTOs (`JObject` / anonymous), never raw API objects.
- Unit conversion applied at the boundary via `UnitConvert` (mm/10→cm on input, cm*10→mm on output; mass kg→g ×1000, area cm²→mm² ×100, volume via `Cm3ToMm3`). Geometry handlers convert distances; `get_mass_properties` converts mass/volume/area/centre/bbox.
- Error codes correct: `NO_DOCUMENT` (no active doc), `WRONG_DOCUMENT_TYPE` (e.g. extrude/mass require a part), `INVALID_ARGUMENT` (missing/invalid params, `distance_mm <= 0`, bad `source`), `API_ERROR` (caught interop exceptions, sanitized in the dispatcher).
- All handlers are wrapped in `#if INVENTOR2022 || … || INVENTOR2027`, so the server/tests build without the Inventor API while the plugin glob compiles them.

### F5 — ToolBaker split / deny-list / compiler policy / persistence. PASS.
- 3 read-only tools in `ToolBakerTools` (`list_baked_tools`, `list_bake_suggestions`, `create_bake_issue_draft`); 3 write tools in `ToolBakerWriteTools` (`run_baked_tool`, `accept_bake_suggestion`, `dismiss_bake_suggestion`). Exact 3/3 split.
- `BakedToolDispatchAuthorizer`: deny-list = `send_code, batch_execute, run_baked_tool, apply_bake, accept_bake_suggestion, dismiss_bake_suggestion, list_baked_tools`; allow-list = the 7 read-only Inventor query commands (health, get_document_info, list_open_documents, list_parameters, get_parameter, get_iproperty, get_mass_properties). `IsAllowed` returns true only if not denied AND in the allow-list. Matches spec (no recursion/escalation).
- `BakeCompilerPolicy` rejects banned tokens (System.IO/Net/Diagnostics/Reflection, File./Directory./Process./Environment., Activator/Assembly/MethodInfo, `typeof(`/`GetType(`, Socket/HttpClient, and re-entry into the ToolBaker namespace).
- `src/shared/ToolBaker/*` is API-agnostic (no `Inventor` types) — the server compiles it explicitly and builds with no SDK.
- Persistence under `%LOCALAPPDATA%\Bimwright\inventor-mcp\baked` (`BakePaths.Root` → `InventorMcpConfig.BakeDirectory`).

### F6 — send_code OFF by default + dual gate. PASS.
- `code` not in `ToolsetFilter.DefaultOn`; `ToolsetFilter.Resolve` removes `code` unless `config.EnableSendCode`. Server gate = `--enable-send-code` / `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`.
- Plugin gate = `o.EnableSendCode` (from `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE`); `AddPlatform` only registers `SendCodeHandler` when enabled.
- `CommandDispatcher` returns `SEND_CODE_DISABLED` when `send_code` arrives without `ctx.EnableSendCode`; `SendCodeHandler` also re-checks (defense in depth) and runs `BakeCompilerPolicy.ValidateSource`. `--read-only` always drops `code` even when enabled (`WriteCapable`).

### F7 — Export read-only policy. PASS.
- `export` is in `ToolsetFilter.WriteCapable` → hidden under `--read-only` (documented narrower behaviour, no path policy in Phase 1).
- `ExportTools` wrappers run `TryRejectPath` (absolute rooted file path under user-profile/temp root) before round-tripping.
- `export_dxf` requires a declared source: wrapper rejects anything but `sketch`/`flat_pattern` and requires `sketch_name` for sketch; the handler re-validates and returns `WRONG_DOCUMENT_TYPE` (non-part, or flat_pattern on non-sheet-metal) / `INVALID_ARGUMENT` (missing sketch, no flat pattern).

### F8 — EntityResolver 1-based id convention. PASS.
`ParseIndex` rejects `idx < 1`; range checks are `1..count`; optional `body:N/` prefix defaults to 1; tolerates `"3"`, `"edge:3"`, `"body:1/edge:3"`, `"entity:5"`. Internally consistent and matches the documented convention the read-only query tools report back.

---

## Fixes applied (this review)
1. `src/server/Tools/DocumentTools.cs` — added the read-only `inventor_health` wrapper (`Call("health", …)`). [F1]
2. `tests/.../RegistrationCountTests.cs` — NEW. Asserts: (a) exactly 46 MCP tools register with `--toolsets all --enable-send-code` and no name collisions; (b) the exact 46-name set matches the frozen Phase-1 surface; (c) `inventor_health` present and read-only-survivable; (d) read-only registration keeps only `MetaTools` + `DocumentTools` (query) + `ToolBakerTools` and drops every write/export/code/toolbaker_write type; (e) default config (no send-code) registers 45. [count + read-only checklist item 1]
3. `tests/.../DocumentToolsTests.cs` — updated the per-type snapshot to include `inventor_health` (9 → 10) and assert it survives `--read-only`.

No wire-name mismatches were found, so no handler/wrapper string edits were needed beyond the health wrapper.

## Verification (final, all green)
- `dotnet build src/server -c Debug` → Build succeeded, 0 warnings, 0 errors.
- `dotnet build src/plugin-inv25 -c Debug` (real interop compile) → Build succeeded.
- `dotnet build src/plugin-inv27 -c Debug /p:SkipInventorReferenceCheck=true` (net10 shape) → Build succeeded.
- `dotnet test tests/Bimwright.Inventor.Tests -c Debug` → **Passed! Failed: 0, Passed: 91, Skipped: 0** (86 prior + 5 new count tests).
- Tool count with `--toolsets all --enable-send-code` = **46** (asserted by test). Default (no send-code) = 45. Read-only registers 3 types (meta + query + read-only toolbaker).

### The 46 breakdown
3 meta + 36 functional (incl. `inventor_health`) + 1 `send_code` + 6 ToolBaker = 46.
- meta (3): list_available_targets, get_current_target, switch_target.
- core+document (10): health, list_open_documents, get_document_info, new_part, new_assembly, open_document, save_document, close_document, set_units, set_material.
- parameters (4), properties (3), sketch (9), feature (6), export (4) = 26.
- code (1): send_code.
- toolbaker (6): list_baked_tools, list_bake_suggestions, create_bake_issue_draft, run_baked_tool, accept_bake_suggestion, dismiss_bake_suggestion.

(10 + 26 = 36 functional; 3 + 36 + 1 + 6 = 46.)
