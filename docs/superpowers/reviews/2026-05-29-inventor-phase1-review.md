# Inventor MCP — Phase 1 Review Gate

> **Reviewer:** Opus 4.8 (MAX-effort review gate)
> **Date:** 2026-05-29
> **Branch:** `feat/inventor-mcp`
> **Scope:** Phase 1 — server-only runtime skeleton, no Inventor SDK.
> **Commits reviewed:** 19f0967 (bootstrap), afcab04 (WS1-A), 038b34c (WS1-B), f481ce2 (WS1-C).

## Verdict: **PASS**

`dotnet build src/InventorMcp.sln -c Debug` → succeeds, 0 warnings, 0 errors.
`dotnet test tests/Bimwright.Inventor.Tests -c Debug` → **33 passed, 0 failed, 0 skipped**.

One **major** finding was found and fixed (`server.json` / `.mcp.json.example` location); everything else verified correct against the spec and the nwd-mcp ground truth.

---

## Checklist results

### 1. Contracts vs spec envelope — PASS
- `InventorCommandEnvelope` (`id`, `command`, `params`, `timeout_ms`, `auth_token`) and `InventorCommandResult` (`id`, `ok`, `data`, `error`, `meta`) JSON shapes match the spec's Command Envelope section exactly (`src/shared/Contracts/InventorCommandEnvelope.cs`, `InventorCommandResult.cs`).
- Meta year field is `inventor_year` (`InventorResponseMeta.InventorYear` → `[JsonProperty("inventor_year")]`). `EnvelopeTests.Meta_year_field_is_inventor_year_not_navisworks_year` asserts both presence of `inventor_year` and **absence** of `navisworks_year`. PASS.
- `InventorError` is `{ code, message }`. Error codes in `InventorErrorCodes.cs` are the full frozen set (12 codes), matching the spec Error Handling table and the plan Frozen Contracts list.
- `TargetDescriptor` carries `transport`, `pipe_name`, `host_app` (value `"Inventor"` enforced downstream), `inventor_year`, plus `port`, `process_id`, `auth_token`, `document_title/path`, `last_heartbeat_utc`. PASS.

### 2. Read-only + toolset semantics — PASS
- `ToolsetFilter.DefaultOn` = everything except `code` → `code` OFF by default. Verified by `ToolsetFilterTests.DefaultSurfaceIncludesEverythingExceptCode` and `RegistrationTests.Default_registration_excludes_code_toolset`.
- `ToolsetFilter.WriteCapable` = `document, parameters, properties, sketch, feature, export, code, toolbaker_write` — `export` **is** included (Phase 1 has no output-path policy, so export is hidden in read-only; spec Read-Only Mode allows this). `--read-only` removes all of them.
- Read-only keeps `meta`, `query`, and read-only `toolbaker`; verified by `ToolsetFilterTests.ReadOnlyRemovesWriteCapableToolsetsButKeepsReadOnlyOnes` and `RegistrationTests.ReadOnly_removes_write_toolsets_but_keeps_meta_query_toolbaker`.
- `inventor_switch_target` (plus list/get) stay exposed in read-only — `RegistrationTests.ReadOnly_keeps_switch_target_meta_tool` asserts all 3 meta tool names survive. PASS.

### 3. Transport — PASS
- `PluginClient.SendLineAsync` branches `pipe` → `NamedPipeClientStream(".", PipeName, InOut)`; default (`tcp`) → `TcpClient` to `127.0.0.1:Port`. Connect-timeout → `TIMEOUT`; other connect failure → `TARGET_UNAVAILABLE`; no live target → `NO_TARGET`; closed connection on read → `TARGET_UNAVAILABLE`; read timeout → `TIMEOUT`. (`src/server/PluginClient.cs`)
- Both `TcpTransportServer` and `PipeTransportServer` validate each envelope's `auth_token` against the constructor-supplied descriptor token via `AuthToken.Verify` (constant-time compare), drop the connection on mismatch with `UNAUTHORIZED`. This is the **descriptor-based** auth model (token from the `TargetDescriptor`), not rvt's global-file scheme. PASS.
- NDJSON framing (`ReadLineBounded`, `\n` delimited, `\r` stripped), 1 MiB line bound → `INVALID_ARGUMENT`, 60 s STA-dispatch timeout → `TIMEOUT`, 20 req/10 s rate limit. PASS.

### 4. No Inventor leak — PASS
- Server csproj compiles only `..\shared\Contracts\*.cs` + `..\shared\Security\*.cs` (no Infrastructure/Transport/Plugin). Grep for `Autodesk.Inventor` / `using Inventor` / `Inventor.Application` across `src/` finds **only doc-comment mentions** in `InventorCommandContext.cs`; no code references the SDK.
- `InventorCommandContext.Application` is `object?` (API-agnostic at source). PASS.
- `IsExternalInitPolyfill.cs` is guarded by `#if !NET5_0_OR_GREATER`. The test project globs `shared/Infrastructure/*.cs` (including this file) at net8 where `NET5_0_OR_GREATER` is true, so the `IsExternalInit` body is excluded → **no CS0433 / duplicate definition** (build is clean, 0 warnings). On net48 (Phase 2 add-ins) the guard lets it through, supplying the `init`/`record` polyfill the shared glob needs. PASS.

### 5. Tests are real — PASS
- All 33 tests assert behavior, not tautologies (read-only blocks the handler from running, sanitizer strips a real `C:\secret\path`, stale/dead/non-Inventor/out-of-range descriptors are dropped, exact-limit size boundary is inclusive, etc.).
- `HAS_INVENTOR_DISPATCHER` is defined in the test csproj `Condition="Exists('..\..\src\shared\Infrastructure\CommandDispatcher.cs')"`. That file exists, so the constant is **ACTIVE** and `DispatcherTests` compile + run. Verified via `dotnet test --list-tests`: all 6 dispatcher methods (`UnknownCommandIsInvalidArgument`, `WriteCommandBlockedInReadOnly`, `ReadOnlyCommandAllowedInReadOnly`, `SendCodeBlockedUnlessEnabled`, `HandlerExceptionBecomesSanitizedApiError`, `SuccessfulHandlerResponseKeepsEnvelopeId`) are present in the run, not compiled out. PASS.

### 6. server.json / .mcp.json.example location — **FIXED (major)**
- **Finding:** WS1-A created `server.json` and `.mcp.json.example` under `src/server/`. The spec Repository Layout (lines 118-131) and the plan File Structure (line 108) both place them at **repo root**, and the closest sibling `nwd-mcp` keeps both at repo root. Repo root had neither file.
- **Resolution:** `git mv src/server/server.json server.json` and `git mv src/server/.mcp.json.example .mcp.json.example`. No duplicates left. Verified the root `.mcp.json.example` is **not** caught by the `.gitignore` `.mcp.json` rule (pattern matches the exact name only, not the `.example` suffix) — it is tracked. Content unchanged and correct (`.mcp.json.example` points at `src/server/bin/Debug/net8.0/Bimwright.Inventor.Server.exe`, which is the right path relative to repo root). Build + test re-run green after the move. No code or doc referenced the old path.

### 7. Registration map — PASS
- `Program.ResolveToolTypesForRegistration` maps all 11 toolsets (`meta, query, document, parameters, properties, sketch, feature, export, code, toolbaker, toolbaker_write`). `query` and `document` both → `DocumentTools`, de-duped via `!types.Contains(t)`. `RegistrationTests.DocumentTools_registered_once_despite_query_and_document_toolsets` asserts `Assert.Single`. PASS.

### 8. Spec field/name consistency — PASS
- Grep for `Nwd` / `navisworks` across `src/` and `tests/`: the only hits are (a) doc-comment provenance notes ("Ported from nwd"), and (b) `EnvelopeTests` assertions that intentionally verify `navisworks_year` is **absent**. No leftover functional `Nwd`/`navisworks` identifiers. PASS.

---

## Minor observations (no action required for Phase 1)
- The spec's error-envelope example shows `"meta": {}`. The implementation always emits a populated `InventorResponseMeta` (with null fields when unknown). This matches nwd and is strictly more useful; the spec example is illustrative. Not a defect.
- `.github/` and `scripts/` exist as empty directories. Their content is a Phase 4 deliverable per the plan; acceptable now.
- `IInventorCommand` / `CommandDispatcher` / `PluginClient` doc comments reference nwd provenance — intentional and harmless.

## Files changed by this review
- `server.json` (moved from `src/server/server.json`)
- `.mcp.json.example` (moved from `src/server/.mcp.json.example`)
- `docs/superpowers/reviews/2026-05-29-inventor-phase1-review.md` (this report)
