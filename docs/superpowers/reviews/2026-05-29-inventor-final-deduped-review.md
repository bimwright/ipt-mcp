# Inventor MCP Final Deduped Post-Implementation Review

Date: 2026-05-29

Repo: `D:\Projects\bimwright\inventor-mcp`

Branch observed: `feat/inventor-mcp`

## Scope

This report deduplicates and verifies the three final review reports in:

- `docs/superpowers/reviews/2026-05-29-inventor-adversarial-post-implementation-review-gpt55.md`
- `docs/superpowers/reviews/2026-05-29-inventor-final-post-implementation-gemini-review.md`
- `docs/superpowers/reviews/2026-05-29-inventor-final-post-implementation-qwen-review.md`

I also checked the current source tree directly and reran the requested build/test commands that can run on this machine. Known environment limits were not reported as defects: no runnable `Inventor.exe`, no 2022-2024 interop DLLs, and 2026/2027 interops are 2025-level stubs.

## Overall Verdict

**DO-NOT-SHIP** for the Phase-1 scope.

The implementation has the correct broad shape: 46 MCP tools are registered, all tool names are `inventor_`-prefixed, server wire names and add-in handler names are symmetric for the round-trip commands, STA marshalling is present for normal dispatcher calls, TFM/addin version isolation is in place, and the server/test projects build without Inventor API references.

However, the current code still has ship-blocking policy/security defects:

- `--read-only` still exposes mutating document tools.
- The add-in's claimed read-only second line of defense is hard-coded off.
- Meta tools leak the per-session `auth_token`, undermining transport authentication.

The SHIP verdicts in the Gemini and Qwen reports are therefore rejected.

## Deduped Findings Summary

| ID | Severity | Finding | Status | Source reports |
| --- | --- | --- | --- | --- |
| B1 | BLOCKER | `--read-only` still exposes document write tools | Confirmed | GPT55; missed by Gemini/Qwen |
| B2 | BLOCKER | Add-in read-only enforcement is hard-coded off | Confirmed | GPT55; missed by Gemini/Qwen |
| B3 | BLOCKER | Auth token leaks through meta target tools | Confirmed | Independent; missed by all three |
| M1 | MAJOR | Plugin-disabled `send_code` returns `INVALID_ARGUMENT`, not `SEND_CODE_DISABLED` | Confirmed | GPT55; missed by Gemini/Qwen |
| M2 | MAJOR | `send_code` runs user code on a worker thread with `Inventor.Application` | Confirmed | GPT55; missed by Gemini/Qwen |
| M3 | MAJOR | Parameter handlers return raw Inventor internal values | Confirmed | GPT55/Qwen |
| M4 | MAJOR | Sketch/feature handlers conflate no document with wrong document type | Confirmed | GPT55 |
| M5 | MAJOR | Handler-returned errors bypass sanitizer and can expose paths/stacks | Confirmed | GPT55; strengthened here |
| M6 | MAJOR | Export path policy exists only in server wrappers, not add-in handlers | Confirmed | Independent |
| M7 | MAJOR | `inventor_dismiss_bake_suggestion` is a seventh server-only tool, contrary to the stated wire model | Confirmed | GPT55 |
| M8 | MAJOR | `inventor_switch_target` claims year/session matching but only matches exact target id | Confirmed | Independent |
| m1 | MINOR | Server-side response read is unbounded | Confirmed | Qwen |
| m2 | MINOR | Registry filters stale/dead descriptors but does not clean descriptor files | Confirmed/wording-dependent | GPT55 |
| m3 | MINOR | Tests are green but miss the actual failing policy paths | Confirmed | GPT55/Qwen; strengthened here |
| m4 | MINOR | Mass-properties area conversion is inline instead of using a helper | Confirmed but cleanup-only | Qwen |

## Confirmed BLOCKER Findings

### B1 - `--read-only` still exposes document write tools

Severity: **BLOCKER**

Evidence:

- `src/server/Program.cs:39` registers `DocumentTools` for `query`.
- `src/server/Program.cs:40` also registers the same `DocumentTools` type for `document`.
- `src/server/ToolsetFilter.cs:44` removes write-capable toolsets under `ReadOnly`, which removes `document` but keeps `query`.
- `src/server/Tools/DocumentTools.cs:42` exposes `inventor_new_part`.
- `src/server/Tools/DocumentTools.cs:52` exposes `inventor_open_document`.
- `src/server/Tools/DocumentTools.cs:57` exposes `inventor_save_document`.
- `src/server/Tools/DocumentTools.cs:67` exposes `inventor_set_units`.
- `src/server/Tools/DocumentTools.cs:72` exposes `inventor_set_material`.

Impact:

Because registration is type-based, keeping the read-only `query` toolset also keeps the mutating document methods on the same class. This directly violates the `--read-only` contract.

Recommended fix:

Split `DocumentTools` into a read-only query tool class and a write-capable document tool class, or add method-level registration filtering. Add a test that asserts these names are absent under `ReadOnly=true`: `inventor_new_part`, `inventor_new_assembly`, `inventor_open_document`, `inventor_save_document`, `inventor_close_document`, `inventor_set_units`, `inventor_set_material`.

### B2 - Add-in read-only enforcement is hard-coded off

Severity: **BLOCKER**

Evidence:

- `src/shared/Infrastructure/CommandDispatcher.cs:38` checks `if (!cmd.IsReadOnly && ctx.ReadOnly)`.
- `src/shared/Infrastructure/CommandDispatcher.cs:39` returns `READ_ONLY` only when `ctx.ReadOnly` is true.
- `src/shared/Plugin/InventorAddInServerBase.cs:81` sets `ReadOnly = false`.
- `src/shared/Contracts/InventorCommandEnvelope.cs:13` has `auth_token`, but there is no read-only field or server-supplied read-only mode.

Impact:

The documented "second line of defense" does not exist in practice. Any authenticated direct envelope, baked-tool path, or future server-side registration mistake can reach write handlers because the add-in context is always writable.

Recommended fix:

Introduce a real add-in read-only option and set `InventorCommandContext.ReadOnly` from it. Cover it with integration-style dispatcher tests that use the real command registry, not only fake commands.

### B3 - Auth token leaks through meta target tools

Severity: **BLOCKER**

Evidence:

- `src/shared/Contracts/TargetDescriptor.cs:15` serializes `[JsonProperty("auth_token")] public string AuthToken`.
- `src/server/Tools/MetaTools.cs:22` serializes `_client.ListTargets()` directly.
- `src/server/Tools/MetaTools.cs:31` serializes the current `TargetDescriptor` directly.
- `src/server/PluginClient.cs:73` uses `target.AuthToken` as the transport credential.

Impact:

Any MCP client that can call `inventor_list_available_targets` or `inventor_get_current_target` can read the active transport token. This collapses the local auth boundary and compounds the read-only/export-path defects because a client can then speak to the add-in transport directly.

Recommended fix:

Never return `TargetDescriptor` directly from MCP meta tools. Return a public DTO that omits or masks `auth_token`; add tests asserting serialized meta output never contains `auth_token` or the token value.

## Confirmed MAJOR Findings

### M1 - Plugin-disabled `send_code` returns the wrong error

Severity: **MAJOR**

Evidence:

- `src/shared/Plugin/InventorCommandRegistry.Platform.cs:22` registers `SendCodeHandler` only when `o.EnableSendCode`.
- `src/shared/Plugin/InventorCommandRegistry.Platform.cs:23` adds `new SendCodeHandler()`.
- `src/shared/Infrastructure/CommandDispatcher.cs:35` checks the command map first.
- `src/shared/Infrastructure/CommandDispatcher.cs:36` returns `INVALID_ARGUMENT` for an unknown command.
- `src/shared/Infrastructure/CommandDispatcher.cs:41-43` can return `SEND_CODE_DISABLED`, but only after a `send_code` command is already registered.
- `README.md:221` and `docs/toolbaker.md:41` claim missing opt-in returns `SEND_CODE_DISABLED`.

Impact:

When the server exposes `inventor_send_code` but the plugin-side env var is absent, the add-in registry has no `send_code` handler. The dispatcher therefore returns `INVALID_ARGUMENT` instead of the specified `SEND_CODE_DISABLED`.

Recommended fix:

Either always register a gated `SendCodeHandler`, or special-case `env.Command == "send_code"` before the unknown-command branch. Add a real-registry test for server-enabled/plugin-disabled `send_code`.

### M2 - `send_code` violates STA marshalling

Severity: **MAJOR**

Evidence:

- Normal dispatch is marshalled: `src/shared/Plugin/InventorAddInServerBase.cs:90` calls `_sta!.InvokeAsync(() => dispatcher.Dispatch(ctx, env), env.TimeoutMs)`.
- `src/shared/Handlers/Code/SendCodeHandler.cs:81` creates `var worker = new Thread(() =>`.
- `src/shared/Handlers/Code/SendCodeHandler.cs:85` runs `CSharpScript.EvaluateAsync(...)` on that worker.
- The script globals expose `app` as `Inventor.Application` before this worker is started.

Impact:

Normal typed handlers run through the STA dispatcher, but user C# snippets execute on a background worker while still holding `Inventor.Application`. Any snippet touching the Inventor API can violate COM/STA requirements.

Recommended fix:

Do not expose `Inventor.Application` to worker-thread code. Run snippets that touch Inventor on the STA dispatcher, or restrict `send_code` to a safe non-API execution mode. Revisit timeout/cancellation semantics without pushing COM access off the STA thread.

### M3 - Parameter handlers return raw internal Inventor values

Severity: **MAJOR**

Evidence:

- `src/shared/Handlers/Parameters/CreateParameterHandler.cs:54` reads `prm.Value`.
- `src/shared/Handlers/Parameters/CreateParameterHandler.cs:60` returns that value.
- `src/shared/Handlers/Parameters/GetParameterHandler.cs:40` reads `prm.Value`.
- `src/shared/Handlers/Parameters/GetParameterHandler.cs:46` returns that value.
- `src/shared/Handlers/Parameters/ListParametersHandler.cs:37` reads `prm.Value`.
- `src/shared/Handlers/Parameters/ListParametersHandler.cs:43` returns that value.
- `src/shared/Handlers/Parameters/SetParameterHandler.cs:53` reads `prm.Value`.
- `src/shared/Handlers/Parameters/SetParameterHandler.cs:59` returns that value.
- `src/shared/Handlers/UnitConvert.cs:6-9` states that handler boundary values should be converted to mm/mm3/degrees.

Impact:

Inventor parameter values are internal API values. Returning them under a generic `value` field violates the boundary-unit contract and will confuse callers that expect MCP outputs in mm/degrees.

Recommended fix:

Return explicit unit-normalized fields, for example `value_mm`, `value_deg`, or a typed `evaluated` object based on the parameter unit. If raw values are needed for debugging, name them `internal_value` and document them as internal.

### M4 - No active document is reported as wrong document type in many sketch/feature handlers

Severity: **MAJOR**

Evidence:

- `src/shared/Handlers/Sketch/CreateSketchHandler.cs:23-24` returns `WRONG_DOCUMENT_TYPE` when `app.ActiveDocument is not PartDocument`.
- `src/shared/Handlers/Sketch/DrawLineHandler.cs:22-23` uses the same pattern.
- `src/shared/Handlers/Sketch/DrawCircleHandler.cs:21-22` uses the same pattern.
- `src/shared/Handlers/Sketch/AddSketchDimensionHandler.cs:25-26` uses the same pattern.
- `src/shared/Handlers/Feature/ExtrudeHandler.cs:24-25` uses the same pattern.
- `src/shared/Handlers/Feature/FilletHandler.cs:24-25` uses the same pattern.
- `src/shared/Handlers/Feature/CreateWorkPlaneHandler.cs:27-28` uses the same pattern.

Impact:

The public error taxonomy distinguishes `NO_DOCUMENT` from `WRONG_DOCUMENT_TYPE`. These handlers do not distinguish a missing active document from an active non-part document.

Recommended fix:

Add a shared helper that first resolves active document safely and returns `NO_DOCUMENT` for null/no active document, then `WRONG_DOCUMENT_TYPE` for non-part documents. Use it across sketch and feature handlers.

### M5 - Handler-returned errors bypass central sanitization

Severity: **MAJOR**

Evidence:

- `src/shared/Infrastructure/CommandDispatcher.cs:54-57` sanitizes thrown exceptions only.
- `src/shared/Handlers/HandlerBase.cs:26-27` passes handler-provided failure messages through directly.
- `src/shared/Handlers/Document/OpenDocumentHandler.cs:28` returns `"file does not exist: " + path`.
- `src/shared/Handlers/Document/OpenDocumentHandler.cs:37` returns `ex.Message`.
- `src/shared/Handlers/Document/SaveDocumentHandler.cs:50` returns `ex.Message`.
- `src/shared/Handlers/Export/ExportStepHandler.cs:46` and `src/shared/Handlers/Export/ExportStepHandler.cs:50` return raw exception messages.
- `src/shared/Handlers/Export/ExportStlHandler.cs:45` and `src/shared/Handlers/Export/ExportStlHandler.cs:49` return raw exception messages.
- `src/shared/Handlers/Export/ExportDxfHandler.cs:96` returns raw exception messages.
- `src/shared/Handlers/Code/SendCodeHandler.cs:155` and `src/shared/Handlers/Code/SendCodeHandler.cs:165` return exception type, message, and stack trace in the data payload.
- `README.md:223` claims sanitized errors avoid absolute path/secret leakage.

Impact:

The sanitizer does not cover errors that handlers intentionally return as `InventorCommandResult.Fail`, nor `send_code` data payloads. Absolute paths, exception text, and stack traces can be exposed to the model.

Recommended fix:

Normalize and sanitize all outbound error messages at the dispatcher boundary, including handler-returned failures. Treat `send_code` exception payloads as errors that must be sanitized or gated to a debug-only mode.

### M6 - Export path policy exists only in server wrappers

Severity: **MAJOR**

Evidence:

- Server wrapper policy exists: `src/server/Tools/ExportTools.cs:39`, `src/server/Tools/ExportTools.cs:47`, and `src/server/Tools/ExportTools.cs:59` call `TryRejectPath`.
- `src/server/Tools/ExportTools.cs:89-130` implements the allowed-root path policy.
- Add-in handlers accept raw `output_path`: `src/shared/Handlers/Export/ExportStepHandler.cs:25`, `src/shared/Handlers/Export/ExportStlHandler.cs:24`, and `src/shared/Handlers/Export/ExportDxfHandler.cs:30`.
- Add-in handlers write directly: `src/shared/Handlers/Export/ExportStepHandler.cs:42`, `src/shared/Handlers/Export/ExportStlHandler.cs:41`, `src/shared/Handlers/Export/ExportDxfHandler.cs:62`, and `src/shared/Handlers/Export/ExportDxfHandler.cs:83`.

Impact:

The path policy can be bypassed by any direct authenticated transport call. This is especially serious because B3 leaks the auth token through meta tools.

Recommended fix:

Move the path validation into shared code and enforce it in the add-in export handlers as well as in server wrappers.

### M7 - `inventor_dismiss_bake_suggestion` is a seventh server-only tool

Severity: **MAJOR**

Evidence:

- Tool count/wire extraction from current source: `count=46`, `server_wire_count=39`, `handler_count=39`.
- With 46 total tools and 39 round-trip handlers, the current code has 7 server-only tools.
- `src/server/Tools/ToolBakerWriteTools.cs:81` defines `inventor_dismiss_bake_suggestion`.
- `docs/toolbaker.md:103` documents `inventor_dismiss_bake_suggestion` as not round-tripping.

Impact:

The expected Phase-1 wire model says the only server-side-only tools are the 3 meta tools plus the 3 read-only ToolBaker database tools. The implementation has one additional server-only write-capable ToolBaker tool.

Recommended fix:

Resolve the contract explicitly. Either implement a matching add-in handler/wire command for dismiss, or update the spec and expected-facts language to state that `dismiss_bake_suggestion` is intentionally server-side-only.

### M8 - `inventor_switch_target` claims year/session matching but only matches exact target id

Severity: **MAJOR**

Evidence:

- `src/server/Tools/MetaTools.cs:35` describes selection by descriptor id, year, or session.
- `README.md:117` repeats "descriptor id, year, or session".
- `src/server/PluginClient.cs:53-55` accepts a string but only matches `t.TargetId == targetId`.

Impact:

The tool description shown to the model overclaims behavior. A call using a year or session value as documented will fail even when a matching target exists.

Recommended fix:

Either implement year/session/process matching, or narrow the tool description and README to exact descriptor id only.

## Confirmed MINOR Findings

### m1 - Server-side response read is unbounded

Severity: **MINOR**

Evidence:

- Add-in TCP input is bounded: `src/shared/Transport/TcpTransportServer.cs:34` sets `MaxLineBytes = 1024 * 1024`, and `src/shared/Transport/TcpTransportServer.cs:116` calls `ReadLineBounded`.
- Add-in pipe input is bounded: `src/shared/Transport/PipeTransportServer.cs:32` sets `MaxLineBytes = 1024 * 1024`, and `src/shared/Transport/PipeTransportServer.cs:125` calls `ReadLineBounded`.
- Server response read is unbounded: `src/server/PluginClient.cs:136` calls `reader.ReadLineAsync()`.

Impact:

A malformed or compromised add-in transport can make the server read an arbitrarily large line before deserialization. The add-in's response-size guard helps for honest handlers, but the client read path is still not bounded.

Recommended fix:

Use a bounded line reader on the server side too, with a cap consistent with the dispatcher response-size limit.

### m2 - Registry filters stale/dead descriptors but does not clean descriptor files

Severity: **MINOR**

Evidence:

- `src/shared/Contracts/TargetRegistry.cs:21` enumerates descriptor JSON files.
- `src/shared/Contracts/TargetRegistry.cs:29` skips stale descriptors.
- `src/shared/Contracts/TargetRegistry.cs:30` skips dead-PID descriptors.
- No delete/quarantine of stale/dead descriptor files occurs in `TargetRegistry.List()`.
- `README.md:73` says the server "drops dead/stale ones"; this is true for returned results, not for filesystem cleanup.

Impact:

If "cleanup" means removing stale files, it is not implemented. If "drops" means filtering from the live list, the code is acceptable but the wording should stay precise.

Recommended fix:

Either implement best-effort file cleanup for stale/dead descriptors or document that stale/dead descriptors are filtered but left on disk.

### m3 - Tests are green but miss the actual failing policy paths

Severity: **MINOR**

Evidence:

- Tests pass: 116 passed.
- `tests/Bimwright.Inventor.Tests/RegistrationCountTests.cs:104-113` asserts read-only registration keeps `DocumentTools` by type, not that write tool names are absent.
- `tests/Bimwright.Inventor.Tests/DocumentToolsTests.cs:61-68` checks that read-only query names remain, but does not assert document write names are absent.
- `tests/Bimwright.Inventor.Tests/DispatcherTests.cs:6` is active via `HAS_INVENTOR_DISPATCHER`, and `tests/Bimwright.Inventor.Tests/Bimwright.Inventor.Tests.csproj:19` defines that constant.
- `tests/Bimwright.Inventor.Tests/DispatcherTests.cs:67-71` tests `SEND_CODE_DISABLED` with a fake registered `send_code` command, so it misses the real plugin registry path where `send_code` is absent.

Impact:

The 116-test green result is real, but it does not cover the high-risk paths now failing in source.

Recommended fix:

Add behavioral tests over resolved MCP tool names and real command registry construction. Cover read-only absent write-name assertions, plugin-disabled `send_code`, meta tool token redaction, and export handler path-policy enforcement.

### m4 - Mass-properties area conversion is inline

Severity: **MINOR**

Evidence:

- `src/shared/Handlers/Properties/GetMassPropertiesHandler.cs:78` uses `UnitConvert.Cm3ToMm3(volumeCm3)`.
- `src/shared/Handlers/Properties/GetMassPropertiesHandler.cs:79` converts area with `areaCm2 * 100.0`.
- `src/shared/Handlers/UnitConvert.cs:17-23` has helpers for volume and angle, but no area helper.

Impact:

This is not a functional bug: cm2 to mm2 is correctly `* 100`. It is a maintainability gap in an otherwise centralized unit-conversion pattern.

Recommended fix:

Add `Cm2ToMm2` and use it for consistency.

## Rejected Or Adjusted Findings

- **Gemini SHIP verdict**: rejected. It claims read-only, token auth, and dispatcher safety are sound, but B1-B3 contradict that directly.
- **Qwen SHIP verdict**: rejected. It correctly noticed some minor gaps, but it missed the read-only and auth-token blockers.
- **Full solution build failure**: not a defect in this environment. It fails only because 2022-2024 interop DLLs are absent, which was an explicit known limit.
- **TFM/addin bracket bug**: verified fixed. The six TFMs match the expected matrix and the `.addin` version brackets isolate one internal major each.
- **GUID casing**: not included as a finding. The net8/net10 `[Guid]` attributes are lower-case strings while `.addin` XML is upper-case, but GUID comparison is case-insensitive and tests cover equality.
- **`LoadOnStartUp` inconsistency**: not included as a ship-affecting finding. Net8/net10 manifests include both `LoadOnStartUp` and `LoadAutomatically`; net48 manifests use `LoadAutomatically`.
- **MCP attributes lacking read-only metadata**: folded into B1 as root cause. The actionable fix is method-level filtering or class splitting, not a separate ship finding.
- **Area conversion helper absence**: kept only as minor cleanup, not a correctness issue.

## Verified Positive Facts

These checks passed or matched the spec:

- Tool count is exactly 46.
- Every MCP-facing tool name is prefixed `inventor_`.
- Round-trip server wire names and add-in handler names are symmetric: 39 server wire commands and 39 handler names, no diff in either direction.
- Server build passes.
- Test suite passes: 116 passed, 0 skipped.
- `plugin-inv25` builds against the real available 2025 interop.
- `plugin-inv27` builds under net10 with the available stub interop.
- Normal typed command dispatch is marshalled through `InventorStaDispatcher`.
- `InventorCommandContext.Application` is typed `object` at `src/shared/Infrastructure/InventorCommandContext.cs:28`.
- Server and net8 test project have no Inventor/WinForms API references; searches only found descriptive text, not `using Inventor`, Autodesk interop references, or WinForms references.
- `src/shared/Contracts`, `src/shared/Security`, and `src/shared/ToolBaker` are API-agnostic; search found no Inventor/WinForms references there.
- ToolBaker dispatch deny-list exists and includes `send_code`, `batch_execute`, `run_baked_tool`, `accept_bake_suggestion`, `dismiss_bake_suggestion`, and `list_baked_tools` (`src/shared/ToolBaker/BakedToolDispatchAuthorizer.cs:30-36`).
- Bake compiler banned API policy exists for file/process/environment/network APIs (`src/shared/ToolBaker/BakeCompilerPolicy.cs:25-38`).

## Verification Output

### Build: server

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/server -c Debug
```

Output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.03
```

### Tests

Command:

```powershell
dotnet test D:/Projects/bimwright/inventor-mcp/tests/Bimwright.Inventor.Tests -c Debug
```

Output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll
  Bimwright.Inventor.Tests -> D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll
Test run for D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   116, Skipped:     0, Total:   116, Duration: 215 ms - Bimwright.Inventor.Tests.dll (net8.0)
```

### Build: plugin-inv25

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/plugin-inv25 -c Debug
```

Output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Plugin.Inv25 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv25\bin\Debug\net8.0-windows7.0\Bimwright.Inventor.Plugin.Inv25.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.04
```

### Build: plugin-inv27

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/plugin-inv27 -c Debug
```

Output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Plugin.Inv27 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv27\bin\Debug\net10.0-windows7.0\Bimwright.Inventor.Plugin.Inv27.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.62
```

### Shape-only full solution build

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/InventorMcp.sln -c Debug /p:SkipInventorReferenceCheck=true
```

Observed result:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
C:\Program Files\dotnet\sdk\10.0.204\Microsoft.Common.CurrentVersion.targets(2451,5): warning MSB3245: Could not resolve this reference. Could not locate the assembly "Autodesk.Inventor.Interop". [...]
  Bimwright.Inventor.Plugin.Inv26 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv26\bin\Debug\net8.0-windows7.0\Bimwright.Inventor.Plugin.Inv26.dll
  Bimwright.Inventor.Plugin.Inv25 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv25\bin\Debug\net8.0-windows7.0\Bimwright.Inventor.Plugin.Inv25.dll
  Bimwright.Inventor.Plugin.Inv27 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv27\bin\Debug\net10.0-windows7.0\Bimwright.Inventor.Plugin.Inv27.dll
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll
  Bimwright.Inventor.Tests -> D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll
D:\Projects\bimwright\inventor-mcp\src\shared\Handlers\Code\SendCodeHandler.cs(12,24): error CS0400: The type or namespace name 'Inventor' could not be found in the global namespace [...]
    3 Warning(s)
    243 Error(s)

Time Elapsed 00:00:02.05
```

Interpretation:

This is expected for this machine because net48 2022-2024 plugin projects have no available Inventor interop DLLs. It is not counted as a defect.

### Tool count and wire/handler mapping

Command summary:

```powershell
# Extract MCP tool attributes from src/server/Tools/*.cs
# Extract Call("<wire>") / SendAsync("<wire>") from server tools
# Extract IInventorCommand Name strings from src/shared/Handlers/**/*.cs
```

Output:

```text
count=46
server_wire_count=39
handler_count=39
server_minus_handlers:
handlers_minus_server:
```

## Fix Order Recommendation

1. Fix read-only registration by splitting `DocumentTools` or adding method-level filtering, then add absent-name tests.
2. Add real add-in read-only mode and real-registry dispatcher coverage.
3. Stop returning `auth_token` from meta tools.
4. Fix `send_code` plugin-disabled error path and STA execution model.
5. Normalize parameter values at the unit boundary.
6. Centralize active-document/type checking to return `NO_DOCUMENT` vs `WRONG_DOCUMENT_TYPE` correctly.
7. Sanitize all handler-returned errors and `send_code` exception payloads.
8. Enforce export path policy inside add-in handlers.
9. Resolve the `dismiss_bake_suggestion` wire contract.
10. Correct or implement `switch_target` year/session matching.
11. Add bounded server response reads and optional descriptor-file cleanup.

## Items Prior Phase Reviews Missed

The earlier phase reviews correctly caught and fixed the missing `inventor_health` wrapper and the `.addin` bracket bug, but they did not catch:

- type-level read-only registration leaking document write tools;
- add-in read-only being hard-coded false;
- auth tokens exposed by meta tools;
- `send_code` worker-thread STA violation;
- plugin-disabled `send_code` returning the wrong error;
- raw parameter internal units;
- handler-returned error leaks;
- export path checks existing only in server wrappers;
- server response read lacking a bound.
