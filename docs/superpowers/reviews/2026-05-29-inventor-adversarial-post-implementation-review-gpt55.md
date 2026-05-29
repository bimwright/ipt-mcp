# Inventor MCP - Adversarial Post-Implementation Review

> Reviewer: GPT-5.5, independent adversarial reviewer  
> Date: 2026-05-29  
> Repo: `D:\Projects\bimwright\inventor-mcp`  
> Branch under review: `feat/inventor-mcp`  
> Scope: Phase 1 implementation fidelity, runtime safety, tool surface, STA marshalling, gates, docs, tests  
> Review mode: read-only. No source code was modified by this review.

## Overall Verdict

**DO-NOT-SHIP** for the Phase 1 scope.

The repository has a passing server build, a passing 116-test suite, and successful `plugin-inv25` / `plugin-inv27` builds. The public MCP tool count is also exactly 46. However, those green checks hide safety-critical defects:

- `--read-only` still exposes document write MCP tools.
- The plugin-side read-only defense is hard-coded off.
- `inventor_send_code` can access `Inventor.Application` from a background thread, violating the STA requirement.
- The documented `SEND_CODE_DISABLED` behavior is not what the plugin actually returns when plugin-side opt-in is missing.
- Unit conversion for parameter values is incomplete.
- Several handlers return incorrect error codes for no-active-document cases.
- Multiple returned errors bypass central sanitization and can leak local paths or stack traces.

This is not a rubber-stamp failure: the implementation has real structure and many correct pieces, but the remaining issues are concentrated in the exact areas the spec treats as safety boundaries.

## Verification Commands Run

### Server Build

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/server -c Debug
```

Real output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.07
```

### Test Suite

Command:

```powershell
dotnet test D:/Projects/bimwright/inventor-mcp/tests/Bimwright.Inventor.Tests -c Debug
```

Real output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll
  Bimwright.Inventor.Tests -> D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll
Test run for D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   116, Skipped:     0, Total:   116, Duration: 221 ms - Bimwright.Inventor.Tests.dll (net8.0)
```

### Plugin 2025 Build

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/plugin-inv25 -c Debug
```

Real output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Plugin.Inv25 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv25\bin\Debug\net8.0-windows7.0\Bimwright.Inventor.Plugin.Inv25.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.08
```

### Plugin 2027 Build

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/plugin-inv27 -c Debug
```

Real output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Bimwright.Inventor.Plugin.Inv27 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv27\bin\Debug\net10.0-windows7.0\Bimwright.Inventor.Plugin.Inv27.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.07
```

### Full Solution Shape Build

Command:

```powershell
dotnet build D:/Projects/bimwright/inventor-mcp/src/InventorMcp.sln -c Debug /p:SkipInventorReferenceCheck=true
```

Real output excerpt:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
C:\Program Files\dotnet\sdk\10.0.204\Microsoft.Common.CurrentVersion.targets(2451,5): warning MSB3245: Could not resolve this reference. Could not locate the assembly "Autodesk.Inventor.Interop". Check to make sure the assembly exists on disk. If this reference is required by your code, you may get compilation errors. [D:\Projects\bimwright\inventor-mcp\src\plugin-inv24\Bimwright.Inventor.Plugin.Inv24.csproj]
C:\Program Files\dotnet\sdk\10.0.204\Microsoft.Common.CurrentVersion.targets(2451,5): warning MSB3245: Could not resolve this reference. Could not locate the assembly "Autodesk.Inventor.Interop". Check to make sure the assembly exists on disk. If this reference is required by your code, you may get compilation errors. [D:\Projects\bimwright\inventor-mcp\src\plugin-inv23\Bimwright.Inventor.Plugin.Inv23.csproj]
C:\Program Files\dotnet\sdk\10.0.204\Microsoft.Common.CurrentVersion.targets(2451,5): warning MSB3245: Could not resolve this reference. Could not locate the assembly "Autodesk.Inventor.Interop". Check to make sure the assembly exists on disk. If this reference is required by your code, you may get compilation errors. [D:\Projects\bimwright\inventor-mcp\src\plugin-inv22\Bimwright.Inventor.Plugin.Inv22.csproj]
D:\Projects\bimwright\inventor-mcp\src\shared\Handlers\Code\SendCodeHandler.cs(12,24): error CS0400: The type or namespace name 'Inventor' could not be found in the global namespace (are you missing an assembly reference?) [D:\Projects\bimwright\inventor-mcp\src\plugin-inv24\Bimwright.Inventor.Plugin.Inv24.csproj]
...
  Bimwright.Inventor.Plugin.Inv27 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv27\bin\Debug\net10.0-windows7.0\Bimwright.Inventor.Plugin.Inv27.dll
  Bimwright.Inventor.Plugin.Inv26 -> D:\Projects\bimwright\inventor-mcp\src\plugin-inv26\bin\Debug\net8.0-windows7.0\Bimwright.Inventor.Plugin.Inv26.dll
  Bimwright.Inventor.Server -> D:\Projects\bimwright\inventor-mcp\src\server\bin\Debug\net8.0\Bimwright.Inventor.Server.dll
  Bimwright.Inventor.Tests -> D:\Projects\bimwright\inventor-mcp\tests\Bimwright.Inventor.Tests\bin\Debug\net8.0\Bimwright.Inventor.Tests.dll

Build FAILED.
...
    3 Warning(s)
    243 Error(s)

Time Elapsed 00:00:02.06
```

Assessment: this full-solution failure is the known environment limit for net48 plugins because Inventor 2022-2024 interop assemblies are absent. It is not counted as a defect.

## Tool Surface And Wire Mapping

Manual extraction results:

```text
tool_count=46
wire_count=39
handler_count=39
server_wire_minus_handlers:
handlers_minus_server_wire:
```

Assessment:

- The advertised MCP count is exactly 46.
- Server wire strings and handler names match in both directions for implemented round-trip commands.
- The 3 meta tools are server-side only as expected.
- The 3 read-only ToolBaker DB tools are server-side only as expected.
- One ToolBaker write tool, `inventor_dismiss_bake_suggestion`, is also server-side only in the current implementation; this is a spec/doc mismatch noted below.

## Findings By Severity

### BLOCKER 1: `--read-only` still exposes document write tools

Evidence:

- `ToolsetFilter` removes the `document` toolset in read-only mode: `src/server/ToolsetFilter.cs:25-29`.
- `Program` registers the same `DocumentTools` class through both `query` and `document`: `src/server/Program.cs:38-40`.
- `DocumentTools` contains read-only query tools and write tools in the same class: `src/server/Tools/DocumentTools.cs:25-75`.
- The write tools exposed from that class include `inventor_new_part`, `inventor_new_assembly`, `inventor_open_document`, `inventor_save_document`, `inventor_close_document`, `inventor_set_units`, and `inventor_set_material`: `src/server/Tools/DocumentTools.cs:42-75`.

Impact:

`--read-only` removes the `document` toolset, but because `query` survives and maps to `DocumentTools`, every public method in `DocumentTools` remains registered. This means read-only mode still exposes document-mutating tools.

Recommended fix:

Split `DocumentTools` into a query-only class and a write-only document class. Register query tools under `query` and write tools under `document`. Add an exact read-only tool-name test, not just a type-level test.

### BLOCKER 2: Plugin-side read-only enforcement is hard-coded off

Evidence:

- `CommandDispatcher` enforces read-only only when `ctx.ReadOnly` is true: `src/shared/Infrastructure/CommandDispatcher.cs:38-39`.
- `InventorAddInServerBase` always sets `ReadOnly = false`: `src/shared/Plugin/InventorAddInServerBase.cs:79-86`.
- `InventorCommandEnvelope` has no read-only field that could carry server state to the plugin: `src/shared/Contracts/InventorCommandEnvelope.cs:7-14`.

Impact:

The documented second line of defense does not work in real plugin dispatch. If a write command is sent to the add-in under server read-only mode, the plugin will not know it is supposed to reject it.

Recommended fix:

Carry server read-only state in the authenticated command envelope, or use another trusted per-session policy channel. Set `ctx.ReadOnly` from that value and add a transport-level test that verifies a write command under read-only returns `READ_ONLY` from the plugin dispatcher path.

### MAJOR 1: `inventor_send_code` violates STA marshalling

Evidence:

- `SendCodeHandler` exposes `Inventor.Application` as the script global `app`: `src/shared/Handlers/Code/SendCodeHandler.cs:31-34`.
- It creates `globals = new Globals { app = app }`: `src/shared/Handlers/Code/SendCodeHandler.cs:75`.
- It then runs the script inside a new background `Thread`: `src/shared/Handlers/Code/SendCodeHandler.cs:81-103`.
- The script evaluates on that worker thread: `src/shared/Handlers/Code/SendCodeHandler.cs:85-87`.

Impact:

The spec requires every Inventor API access to happen on the Inventor STA thread. This handler lets arbitrary snippets touch `Inventor.Application` from a background thread. That can produce COM threading bugs, deadlocks, or unpredictable Inventor behavior.

Recommended fix:

Run dynamic code on the STA thread, or remove direct Inventor API access from worker-executed snippets. If timeout/cancellation is required, use a model that does not move COM access off the STA thread.

### MAJOR 2: Plugin-disabled `send_code` returns the wrong error

Evidence:

- `AddPlatform` only registers `SendCodeHandler` when plugin-side opt-in is enabled: `src/shared/Plugin/InventorCommandRegistry.Platform.cs:20-24`.
- `CommandDispatcher` checks unknown commands before checking the `send_code` gate: `src/shared/Infrastructure/CommandDispatcher.cs:35-43`.
- Therefore, if server-side opt-in is on but plugin-side opt-in is off, `send_code` is not registered and the dispatcher returns `INVALID_ARGUMENT`, not `SEND_CODE_DISABLED`.

Impact:

This contradicts the spec and docs. The required behavior is that missing either side of the opt-in returns `SEND_CODE_DISABLED`.

Recommended fix:

Always register `SendCodeHandler`, then let the dispatcher and handler return `SEND_CODE_DISABLED`, or special-case `send_code` before the unknown-command path.

### MAJOR 3: `inventor_dismiss_bake_suggestion` is server-side-only despite the wire model

Evidence:

- The MCP tool exists at `src/server/Tools/ToolBakerWriteTools.cs:81-88`.
- It only updates the server bake DB and does not call `SendAsync`: `src/server/Tools/ToolBakerWriteTools.cs:82-88`.
- No handler named `dismiss_bake_suggestion` exists under `src/shared/Handlers/**`.

Impact:

The expected server-side-only exceptions were the 3 meta tools plus the 3 read-only ToolBaker DB tools. `dismiss_bake_suggestion` is a write ToolBaker tool, but it is server-side-only in code. This may be acceptable as a design choice, but it is currently inconsistent with the stated wire model.

Recommended fix:

Either add an add-in handler and route through it, or explicitly update the spec/tests/docs to classify dismiss as server-side-only.

### MAJOR 4: Parameter handlers return Inventor internal units

Evidence:

- `ListParametersHandler` returns raw `prm.Value`: `src/shared/Handlers/Parameters/ListParametersHandler.cs:36-46`.
- `GetParameterHandler` returns raw `prm.Value`: `src/shared/Handlers/Parameters/GetParameterHandler.cs:39-48`.
- `SetParameterHandler` returns raw `prm.Value`: `src/shared/Handlers/Parameters/SetParameterHandler.cs:52-60`.
- `CreateParameterHandler` returns raw `prm.Value`: `src/shared/Handlers/Parameters/CreateParameterHandler.cs:53-61`.
- None of those paths use `UnitConvert`.

Impact:

Inventor API parameter values are internal values, commonly cm/radians depending on unit. The public boundary promises mm/degrees. Returning raw values violates the unit contract and can cause wrong downstream modeling decisions.

Recommended fix:

Convert parameter values according to unit type before returning them, or expose raw internal values with explicit names such as `value_internal` and add separate normalized fields like `value_mm` / `value_deg`.

### MAJOR 5: Several handlers return wrong error code when no document is active

Evidence:

- `CreateSketchHandler` returns `WRONG_DOCUMENT_TYPE` whenever `app.ActiveDocument is not PartDocument`: `src/shared/Handlers/Sketch/CreateSketchHandler.cs:22-24`.
- `DrawLineHandler` has the same pattern: `src/shared/Handlers/Sketch/DrawLineHandler.cs:21-23`.
- `ExtrudeHandler` has the same pattern: `src/shared/Handlers/Feature/ExtrudeHandler.cs:23-25`.
- `CreateWorkPlaneHandler` has the same pattern: `src/shared/Handlers/Feature/CreateWorkPlaneHandler.cs:26-28`.

Impact:

If Inventor has no active document, the correct code should be `NO_DOCUMENT`, not `WRONG_DOCUMENT_TYPE` or an accidental `API_ERROR`. The wrong code weakens client recovery behavior and contradicts the spec error table.

Recommended fix:

Use a shared helper to safely read `ActiveDocument`, distinguish null/no-document from wrong type, and apply it consistently across sketch and feature handlers.

### MAJOR 6: Handler-returned errors bypass central sanitization

Evidence:

- `CommandDispatcher` sanitizes only thrown exceptions: `src/shared/Infrastructure/CommandDispatcher.cs:54-57`.
- Many handlers catch exceptions and return raw `ex.Message`, for example `OpenDocumentHandler.cs:35-38`, `ExportStepHandler.cs:48-50`, and `SetMaterialHandler.cs:48-63`.
- `SendCodeHandler` returns stack traces directly: `src/shared/Handlers/Code/SendCodeHandler.cs:151-167`.

Impact:

Local paths, sensitive file names, internal assembly paths, or other environment details can be returned to the MCP client. This contradicts the error-sanitization requirement.

Recommended fix:

Sanitize all handler `Fail` messages centrally, either in `HandlerBase.Fail` and direct `InventorCommandResult.Fail` wrappers, or in `CommandDispatcher.Normalize`. Do not return stack traces outside an explicit debug mode.

### MINOR 1: MCP tool attributes do not carry read-only metadata

Evidence:

- Read-only tools are declared as `[McpServerTool(Name = "...")]` without `ReadOnly = true`, for example `src/server/Tools/DocumentTools.cs:25-38`.

Impact:

The server can still filter by toolset, but MCP clients do not receive explicit tool-level read-only metadata. Sibling projects use this metadata where supported.

Recommended fix:

Add `ReadOnly` and `Idempotent` metadata where supported by the MCP attribute API, and test it.

### MINOR 2: Stale/dead descriptor cleanup is not implemented

Evidence:

- `TargetRegistry.List()` ignores invalid descriptors but never deletes them: `src/shared/Contracts/TargetRegistry.cs:21-33`.

Impact:

The spec asks the server to clean stale/dead descriptors opportunistically. Current behavior is safe for liveness filtering but leaves stale files behind.

Recommended fix:

Best-effort delete descriptor files rejected for stale heartbeat or dead PID. Add a test that confirms cleanup.

### MINOR 3: Documentation overclaims safeguards

Evidence:

- README says `CommandDispatcher` is a second line of defense and write commands under read-only return `READ_ONLY`: `README.md:220`.
- README says missing either send-code gate returns `SEND_CODE_DISABLED`: `README.md:221`.
- `docs/toolbaker.md` repeats the same send-code enforcement claim: `docs/toolbaker.md:41-46`.
- `docs/testing/manual-smoke.md` expects forced default `inventor_send_code` to return `SEND_CODE_DISABLED`: `docs/testing/manual-smoke.md:91-95`.

Impact:

The docs describe the intended behavior, not the actual behavior. This makes the implementation look safer than it is.

Recommended fix:

Fix the code first, then update docs only where behavior intentionally differs from the spec.

## What Prior Reviews Missed

The prior phase reviews correctly caught several earlier issues, but they missed or over-passed the following:

- The type-level read-only registration tests did not detect that `DocumentTools` remains registered through `query` and still exposes write methods.
- The claimed plugin-side read-only defense was not actually wired to server read-only state.
- The send-code dual-gate behavior was not tested in the server-enabled/plugin-disabled combination.
- The STA review did not account for `SendCodeHandler` starting its own worker thread after entering the STA path.
- Unit conversion review did not catch raw parameter values.
- Error-sanitization review did not catch handler-returned `ex.Message` and stack traces.

## Non-Issues Confirmed

These were checked and should not be reported as defects:

- Full solution build failure is due to absent 2022-2024 interop assemblies, matching the known environment limit.
- `plugin-inv25` compiles against the real 2025 interop available on this machine.
- `plugin-inv27` compiles under .NET 10, but the available 2027 interop is a 2025-level stub, matching the known caveat.
- Server project compiles with no Inventor or WinForms references.
- `src/shared/Contracts`, `src/shared/Security`, and `src/shared/ToolBaker` are API-agnostic.
- `InventorCommandContext.Application` is typed as `object`.
- TFM matrix and `.addin` version brackets are structurally correct in the code and guarded by tests.

## Recommended Fix Order

1. Split read-only query tools from document write tools and add exact read-only tool-name tests.
2. Propagate server read-only state into the add-in dispatcher and test the plugin-side block.
3. Fix `send_code` registration/gating so missing plugin opt-in returns `SEND_CODE_DISABLED`.
4. Rework `send_code` execution so Inventor API access remains on the STA thread.
5. Normalize parameter units at the boundary.
6. Add a shared active-document helper and fix `NO_DOCUMENT` vs `WRONG_DOCUMENT_TYPE` behavior.
7. Centralize sanitization for all returned failures and remove stack traces from normal responses.
8. Re-run the same five verification commands and update docs only after code behavior matches.

## Final Assessment

The implementation is close in structure but not safe enough to ship. Passing tests should not be interpreted as sufficient because the tests currently miss several policy and runtime-boundary violations. The correct current verdict is **DO-NOT-SHIP** until the blocker and major findings above are fixed and covered by behavioral tests.
