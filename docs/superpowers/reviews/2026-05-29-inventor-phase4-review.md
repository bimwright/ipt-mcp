# Phase 4 (FINAL) Review Gate — inventor-mcp

> Reviewer: Opus 4.8, MAX rigor. Date: 2026-05-29. Branch: `feat/inventor-mcp`.
> Scope: WS4-A docs (README/README.vi/ARCHITECTURE/CLAUDE), WS4-B safety/ops docs + config
> (toolbaker.md, manual-smoke.md, roadmap.md, server.json, .mcp.json.example, SECURITY/CONTRIBUTING/CoC),
> WS4-C release verification (CHANGELOG, AddinManifestTests, scripts/package-bundle.ps1).
> Method: every doc claim cross-checked against the code under `src/`, then build + test + dry-run run.

## Verdict summary

- **Phase 4 GATE: PASS** (after one doc-accuracy fix applied).
- **Project (Phase-1 scope): COMPLETE for the server-only deliverable.** The only genuine remaining
  work is the conditional real-Inventor smoke run (spec Non-Goal until a runnable `Inventor.exe`),
  which is correctly documented as not-done everywhere.

## Verification evidence (run by the reviewer)

| Command | Result |
|---|---|
| `dotnet test tests/Bimwright.Inventor.Tests -c Debug` | **Passed! Failed: 0, Passed: 116, Skipped: 0** |
| `dotnet build src/server -c Debug` | **Build succeeded. 0 Warning(s), 0 Error(s)** |
| `dotnet build src/plugin-inv25 -c Debug /p:SkipInventorReferenceCheck=true` | **Build succeeded. 0/0** |
| `dotnet build src/plugin-inv27 -c Debug /p:SkipInventorReferenceCheck=true` | **Build succeeded. 0/0** (net10) |
| `pwsh scripts/package-bundle.ps1 -DryRun` | bundle dir absent before AND after → **writes nothing**; SDK FLAG emitted |
| `python -c json.load(server.json, .mcp.json.example)` | both valid JSON |

## Checklist findings

### 1. README tool list = the real 46 — PASS
Grepped `Name = "inventor_…"` across `src/server/Tools/*.cs`: exactly 46 tools.
Breakdown matches the README tables exactly: Meta 3, query 3 (health/list_open_documents/get_document_info),
document 7, parameters 4, properties 3, sketch 9, feature 6, export 4, code 1 (send_code),
toolbaker 3, toolbaker_write 3 = 46. No invented or missing tools. The version matrix
(net48 2022-24 TCP / net8.0-windows7.0 2025-26 Pipe / net10.0-windows7.0 2027 Pipe) is correct and
matches `Supported Inventor Versions`. The redistribution disclaimer ("Not Redistributed") is present.
README.vi.md is a faithful, complete mirror (same 46 count, same per-toolset tables, same matrix,
same "Không phân phối lại" disclaimer).

### 2. Env-var/flag/tool names in docs match code — PASS
Cross-checked against `InventorMcpConfig.cs`, `ToolsetFilter.cs`, `CommandDispatcher.cs`,
`BakeCompilerPolicy.cs`, `BakedToolDispatchAuthorizer.cs`:
- `BIMWRIGHT_INVENTOR_READ_ONLY`, `_ENABLE_SEND_CODE`, `_ENABLE_TOOLBAKER`, `_ENABLE_ADAPTIVE_BAKE`,
  `_TIMEOUT_MS`, `_MAX_RESPONSE_BYTES`, `_TARGET`, `_TOOLSETS`, plugin `_PLUGIN_ENABLE_SEND_CODE` —
  all present in code, all spelled correctly in docs (toolbaker.md, SECURITY.md, server.json).
- CLI flags `--read-only --enable-send-code --disable-toolbaker --enable-adaptive-bake --toolsets
  --target --timeout-ms --max-response-bytes --config` match `ApplyCli`.
- `WriteCapable` read-only removal set documented in README/CHANGELOG/CLAUDE == `ToolsetFilter.WriteCapable`
  (`document, parameters, properties, sketch, feature, export, code, toolbaker_write`).
- **Banned-API list:** the toolbaker.md table reproduces all 19 `BakeCompilerPolicy.ForbiddenTokens`
  exactly (System.IO, System.Net, System.Diagnostics, System.Reflection, File., Directory., Process.,
  Environment., Microsoft.Win32, Activator., Assembly., MethodInfo, PropertyInfo, FieldInfo, GetType(,
  typeof(, Socket, HttpClient, Bimwright.Inventor.Shared.ToolBaker). This is the broader (real) list,
  as required.
- **Dispatch deny/allow-list:** toolbaker.md allowed set (health, get_document_info,
  list_open_documents, list_parameters, get_parameter, get_iproperty, get_mass_properties) and denied
  set (send_code, batch_execute, run_baked_tool, apply_bake, accept_bake_suggestion,
  dismiss_bake_suggestion, list_baked_tools) == `BakedToolDispatchAuthorizer.Allowed`/`Denied` exactly.
- The `INVALID_ARGUMENT` claim for a banned token is correct: `SendCodeHandler` returns
  `INVALID_ARGUMENT` on a failed `BakeCompilerPolicy.ValidateSource`.

### 3. manual-smoke.md — PASS (after fix)
All 16 steps reference real `inventor_*` tools (list_available_targets, get_current_target, health,
new_part, create_sketch, draw_line/circle/rectangle, add_sketch_dimension, close_sketch, extrude,
list_parameters, get_mass_properties, export_step, export_stl, send_code, list_baked_tools,
switch_target). Expected results are plausible and match handler DTO shapes.
**FIX APPLIED (see Findings):** step 1 referenced a non-existent `scripts\install-bundle.ps1`.

### 4. No "production-ready" claim absent a smoke run — PASS
No doc claims production-readiness. README status banner = "early"; manual-smoke.md and CHANGELOG
explicitly say *do not claim production-ready before the real-Inventor smoke run*. All required
caveats are recorded:
- compile-only / no runnable Inventor (README, CLAUDE, manual-smoke, CHANGELOG).
- 2026/2027 interop-stub caveat (CHANGELOG "Known limitations": *"only the 2025-2027 interop
  assemblies, with the 2026/2027 ones being stubs"*).
- SupportedSoftwareVersion / PackageContents schema needs-SDK-verification FLAG
  (package-bundle.ps1 inline FLAG + end-of-run banner + generated XML comment; CHANGELOG known-limitations).

### 5. server.json + .mcp.json.example — PASS
Both valid JSON. server.json package `identifier` = `Bimwright.Inventor.Server` and name
`io.github.bimwright/inventor-mcp`; csproj `AssemblyName`/`PackageId` = `Bimwright.Inventor.Server`,
`ToolCommandName` = `bimwright-inventor` — internally consistent. .mcp.json.example points at
`…/src/server/bin/Debug/net8.0/Bimwright.Inventor.Server.exe` (matches the built artifact + README).

### 6. AddinManifestTests genuinely guard the bracket regression — PASS
`AddinManifestTests` asserts, per year 2022-2027: ClassId==ClientId; ClassId == the `[Guid]` on that
version's `InventorAddInServer.cs`; `<Assembly>` == `Bimwright.Inventor.Plugin.InvNN.dll`; all six
ClassIds distinct; and the GreaterThan/LessThan brackets isolate **exactly one** internal major =
year-1996 (the precise assertion that catches the Phase-2 bracket bug). Verified the inv25 manifest
brackets `28.. < v < 30..` → isolates 29 (= 2025-1996). All pass within the 116-test green run.

### 7. scripts/package-bundle.ps1 — PASS
`-DryRun` is provably safe: every build/copy/`Set-Content`/`New-Item` is guarded by `if ($DryRun)`
early-returns or branches that only print. Reviewer ran it: the bundle directory did not exist before
and still did not exist after. The Inventor-SDK schema FLAG is emitted (inline comments in the
generated XML + a yellow end-of-run banner enumerating the 4 verify-before-release items).

### 8. Whole-project coherence — PASS
- 46 tools consistent across README, README.vi, CHANGELOG, roadmap, server.json description, tests
  (`RegistrationCountTests`).
- Frozen contracts unchanged: error codes set in CLAUDE/CHANGELOG/SECURITY == `InventorErrorCodes`.
- Gates intact: read-only filter (`ToolsetFilter` + `CommandDispatcher` second-line `READ_ONLY`),
  send_code two-sided opt-in (`SEND_CODE_DISABLED`), toolbaker deny-list — all match docs.
- No leftover `Nwd`/`navisworks` **functional** identifiers: the only matches in `src/` are
  provenance comments ("Ported from nwd-mcp"); the only `.md` matches are deliberate contrast notes
  ("differs from nwd-mcp, where every Navisworks plug-in targets net48"). No `Nwd` types, namespaces,
  or wire names remain.
- No TODO/placeholder left as the sole content of a shipped doc.

## Findings

| # | Severity | File | Issue | Resolution |
|---|---|---|---|---|
| F1 | **Low (doc-accuracy)** | `docs/testing/manual-smoke.md` step 1 | Referenced `powershell -File .\scripts\install-bundle.ps1`, a script that does **not** exist. The only packaging script is `scripts/package-bundle.ps1`. A reader following the checklist would hit "file not found" at step 1. | **FIXED.** Rewrote step 1 to invoke `pwsh -File .\scripts\package-bundle.ps1 -Years 2025 -Configuration Release` (with a `-DryRun`-first hint), and corrected the "Expected" to say the per-version subfolder is under `Contents\` (matching the script's actual layout). |

No High/Medium findings. No code changes required — only the one doc fix above.

## Post-fix re-verification
- `dotnet test tests/Bimwright.Inventor.Tests -c Debug` → 116/116 (the fix is docs-only; suite
  unaffected, re-confirmed green before the fix and the fix touches no compiled file).
- Build/dry-run evidence above unchanged.
