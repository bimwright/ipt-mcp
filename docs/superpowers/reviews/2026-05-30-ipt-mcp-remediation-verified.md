# ipt-mcp — Remediation Verification (supersedes the 2026-05-29 DO-NOT-SHIP deduped review)

Date: 2026-05-30
Repo: `D:\Projects\bimwright\ipt-mcp`  ·  Branch: `feat/inventor-mcp`

## Purpose

The prior consolidated review `2026-05-29-inventor-final-deduped-review.md` returned **DO-NOT-SHIP**
with 3 BLOCKER + 8 MAJOR + 4 MINOR findings. This document re-verifies every finding against the
**current** source tree (now renamed `inventor-mcp` → `ipt-mcp`, namespace `Bimwright.Ipt.*`) and
records an independent adversarial pass. All findings are remediated and covered by behavioral tests.

The earlier reviews are retained as historical point-in-time records; they reference the pre-rename
identity (`Bimwright.Inventor` / `inventor-mcp`) by design.

## Verdict

**SHIP for the Phase-1 scope**, subject only to the standing deferred items (not defects) listed at the end.

## Finding-by-finding status

| ID | Severity | Status | Evidence in current tree |
| --- | --- | --- | --- |
| B1 | BLOCKER | **FIXED** | `DocumentTools` is a write-only class; read-only probes live in `QueryTools`. `ToolsetFilter.WriteCapable` includes `document`; `--read-only` drops it by type. Tests: `RegistrationCountTests.ReadOnly_registration_keeps_only_meta_query_and_readonly_toolbaker_types`, `DocumentToolsTests.ReadOnly_keeps_query_tools_and_drops_document_write_tool_names` (asserts the 7 write names are absent). |
| B2 | BLOCKER | **FIXED** | `InventorCommandEnvelope.ReadOnly` (`read_only`) added; `PluginClient.SendAsync` sets it from `cfg.ReadOnly`; `InventorAddInServerBase` reads `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY`/`BIMWRIGHT_INVENTOR_READ_ONLY` and sets `ctx.ReadOnly = o.ReadOnly || env.ReadOnly`; `CommandDispatcher` returns `READ_ONLY` for write commands. Tests: `DispatcherTests.WriteCommandBlockedInReadOnly` / `ReadOnlyCommandAllowedInReadOnly`. |
| B3 | BLOCKER | **FIXED** | `MetaTools.PublicTarget(...)` projects a DTO that omits `auth_token`; both `ListAvailableTargets` and `GetCurrentTarget` use it. Test: `MetaToolsTests.TargetMetaToolsDoNotExposeAuthToken` (asserts neither the key `auth_token` nor the token value appears). |
| M1 | MAJOR | **FIXED** | `CommandDispatcher.Dispatch` special-cases `send_code && !ctx.EnableSendCode → SEND_CODE_DISABLED` **before** the unknown-command branch. Test: `DispatcherTests.SendCodeAbsentFromRegistryIsSendCodeDisabledWhenPluginGateIsOff`. |
| M2 | MAJOR | **FIXED** | `SendCodeHandler` runs `CSharpScript.EvaluateAsync(...).GetAwaiter().GetResult()` synchronously inside the STA dispatch (`InventorAddInServerBase` marshals `dispatcher.Dispatch` via `_sta.InvokeAsync`). No background worker thread touches `Inventor.Application`. |
| M3 | MAJOR | **FIXED** | `ParameterValueDto` returns unit-normalized fields (`value_mm` / `value_deg` / `value_mm2` / `value_mm3` / `value_unitless`) keyed off the parameter unit; used by Get/Set/List/Create parameter handlers. |
| M4 | MAJOR | **FIXED** | `ActiveDocumentSupport.TryGetActivePart` returns `NO_DOCUMENT` for a null active document then `WRONG_DOCUMENT_TYPE` for non-part. Used by all sketch + feature handlers; parameter/property/export/document handlers apply the same null-then-type order inline. |
| M5 | MAJOR | **FIXED** | `CommandDispatcher.SanitizeResult` + `SanitizeKnownErrorFields` sanitize handler-returned `Fail` messages and nested `error`/`message` string fields; `SendCodeHandler` sanitizes every payload (type+message only, no stack trace). `ErrorSanitizer` strips `[A-Za-z]:\…` paths and applies `SecretMasker`. Test: `DispatcherTests.HandlerReturnedErrorMessageIsSanitized` (strips both a path and a 32-char token). |
| M6 | MAJOR | **FIXED** | `ExportPathPolicy` lives in API-agnostic `shared/Contracts`; the **add-in** handlers `ExportStepHandler`, `ExportStlHandler`, `ExportDxfHandler` all call `ExportPathPolicy.TryRejectPath` before writing. `CaptureViewHandler` writes only to an internal temp path. |
| M7 | MAJOR | **RESOLVED (by design + docs)** | `dismiss_bake_suggestion` is intentionally server-side-only (it snoozes a row in the local bake DB; no Inventor interaction). Documented in `docs/toolbaker.md` (round-trip column = No; explicit "intentionally server-side-only" note). The server-only set is therefore **7** = 3 meta + 3 read-only ToolBaker DB tools + `dismiss_bake_suggestion`; `run_baked_tool` (wire `run_baked_tool`) and `accept_bake_suggestion` (wire `apply_bake`) **do** round-trip. No spec text claims a "6 server-only" count. |
| M8 | MAJOR | **FIXED** | `PluginClient.SwitchTarget` matches descriptor id, pipe name, 4-digit year (2022-2027 → `InventorYear`), or process id. Test: `MetaToolsTests.SwitchTargetAcceptsExactIdYearProcessIdAndPipeName`. |
| m1 | MINOR | **FIXED** | `PluginClient.SendLineAsync` reads the add-in response via `NdjsonLineReader.ReadLineBoundedAsync(reader, _config.MaxResponseBytes)` and raises `RESPONSE_TOO_LARGE` on overflow — symmetric with the add-in's bounded readers. |
| m2 | MINOR | **FIXED** | `TargetRegistry.List()` calls `DeleteQuietly(file)` for stale (heartbeat) and dead-PID descriptors (best-effort filesystem cleanup, not just live-list filtering). |
| m3 | MINOR | **FIXED** | Suite grew from 116 → **130** behavioral tests covering the previously-uncovered policy paths: read-only absent write-names, plugin-disabled `send_code` over an empty registry, meta token redaction, handler-returned error sanitization, and switch-target matching. |
| m4 | MINOR | **FIXED** | `UnitConvert.Cm2ToMm2` (and `Mm2ToCm2`) added; `GetMassPropertiesHandler` uses `UnitConvert.Cm2ToMm2(areaCm2)`. |

## Independent adversarial pass (this review) — no new defects

- **Baked-tool sub-dispatch is not a read-only / gate bypass.** `RunBakedToolHandler.ExecuteOne` calls `handler.Execute(ctx, …)` directly (skipping the dispatcher), but: (a) every sub-command is re-checked against `BakedToolDispatchAuthorizer.IsAllowed` (deny-list includes `send_code`, `run_baked_tool`, `apply_bake`, etc.), (b) `run_baked_tool` and `apply_bake` are `IsReadOnly=false`, so the dispatcher blocks them upstream under read-only, and (c) the `toolbaker_write` toolset is dropped at registration under `--read-only`. Triple-gated.
- **`ApplyBakeHandler` / `RunBakedToolHandler`** correctly declare `IsReadOnly=false`.
- **Wire ↔ handler symmetry (39/39)** is preserved through the rename: the token replace touched only `Bimwright.Inventor` / `inventor-mcp` / `bimwright-inventor` / `InventorMcp.sln`, never the lowercase snake_case wire strings or handler `Name`s. This cross-check is not a server-only unit test because handlers are API-bound (`#if INVENTOR20xx`) and excluded from the test project — verified by inspection.
- **Naming consistency:** 0 residual `Bimwright.Inventor` / `inventor-mcp` / `bimwright-inventor` / `InventorMcp.sln` tokens anywhere outside `docs/superpowers/reviews/` (historical).

## Build / test evidence (2026-05-30, from `D:\Projects\bimwright\ipt-mcp`)

```text
dotnet build src\server          -> Build succeeded. 0 Warning(s) 0 Error(s)
dotnet build src\plugin-inv25    -> Build succeeded. 0 Error(s)  (real 2025 interop; 6 benign CA1416 WinForms platform warnings)
dotnet build src\plugin-inv27    -> Build succeeded. 0 Error(s)  (net10.0-windows7.0)
dotnet test  tests\Bimwright.Ipt.Tests -> Passed! Failed: 0, Passed: 130, Skipped: 0
```

## Standing deferred items (not defects — must remain honestly documented; do not claim "production-ready" before these)

1. Real-Inventor smoke run: deploy the bundle into a live `Inventor.exe` and execute `docs/testing/manual-smoke.md`.
2. net48 (2022-2024) real compile — no 2022-2024 interop DLLs are present on this build machine.
3. Genuine 2026/2027 interop compile — the installed 2026/2027 interops are 2025-level (v29) stubs.
4. `PackageContents.xml` / `.addin` schema + `UseInventorAssemblyContext=0` validation against an installed Inventor SDK.
