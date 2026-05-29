# Inventor MCP — Phase 2 Review Gate

> **Reviewer:** Opus 4.8 (MAX-rigor review gate)
> **Date:** 2026-05-29
> **Branch:** `feat/inventor-mcp`
> **Scope:** Phase 2 — add-in shells + transport listener + STA marshalling.
> **Commits reviewed:** 47f4add (bootstrap), 71cfcea (WS2-A net48 inv22/23/24), 8ee93c3 (WS2-B net8/net10 inv25/26/27), 0c734bd (WS2-C shared core).

## Verdict: **PASS** (one BLOCKER found and fixed)

- `dotnet build src/server` → **Build succeeded, 0 errors**.
- `dotnet build src/plugin-inv25` (real interop) → **Build succeeded, 0 errors**.
- `dotnet build src/plugin-inv26` (stub interop) → **Build succeeded, 0 errors**.
- `dotnet build src/plugin-inv27` (.NET 10, stub interop) → **Build succeeded, 0 errors**.
- `dotnet test tests/Bimwright.Inventor.Tests` → **46 passed, 0 failed, 0 skipped**.
- `dotnet build src/plugin-inv24 /p:SkipInventorReferenceCheck=true` → fails with 3× CS0246/CS0400
  ("type or namespace 'Inventor' could not be found"). **Expected** — the net48 2022-2024 interop is
  not installed on this machine; the TFM/shape is covered by `TfmSplitTests`. NOT a Phase-2 blocker.

One **blocker** (B1: 2025/26/27 version-filter brackets would prevent the add-ins loading) was found
and fixed. Everything else verified correct against the spec intent and the WS2-A / rvt-mcp / nwd-mcp
ground truth.

---

## Findings

### B1 — BLOCKER (FIXED): WS2-B `.addin` version brackets exclude their own target year
**Evidence.** The Autodesk `.addin` filter loads an add-in iff
`GreaterThanValue < Inventor.SoftwareVersion.Major < LessThanValue`. The WS2-A net48 manifests use the
correct single-version idiom `GreaterThan (M-1).. / LessThan (M+1)..`:

| year | major M | WS2-A bracket | result |
|------|---------|---------------|--------|
| 2022 | 26 | GT 25.. / LT 27.. | 25 < 26 < 27 → loads ✓ |
| 2023 | 27 | GT 26.. / LT 28.. | loads ✓ |
| 2024 | 28 | GT 27.. / LT 29.. | loads ✓ |

WS2-B used `GreaterThan M.. / LessThan (M+1)..` instead, which strictly excludes M itself:

| year | major M | WS2-B (before) | result |
|------|---------|----------------|--------|
| 2025 | 29 | GT 29.. / LT 30.. | 29 < 29 is FALSE → **does NOT load** ✗ |
| 2026 | 30 | GT 30.. / LT 31.. | **does NOT load** ✗ |
| 2027 | 31 | GT 31.. / LT 32.. | **does NOT load** ✗ |

So the 2025/26/27 add-ins would silently fail to load in their own Inventor year — a shipping blocker
that no test covered (no test asserts the bracket values).

**Resolution.** Re-bracketed the three WS2-B manifests to the same strict `(M-1).. / (M+1)..` idiom as
WS2-A, and added a documenting header comment to each:
- `src/plugin-inv25/...addin`: `GT 28.. / LT 30..` (isolates 29)
- `src/plugin-inv26/...addin`: `GT 29.. / LT 31..` (isolates 30)
- `src/plugin-inv27/...addin`: `GT 30.. / LT 32..` (isolates 31)

Re-ran builds + tests after the edit: server + inv25/26/27 build 0 errors; 46/46 tests green. The
`.addin` files are not referenced by any csproj (deployed in Phase-4 packaging), so the edit has no
build impact — but it is the difference between the add-in loading and not.

### F1 — FLAG (environment-limited, comment recorded): 2026/2027 internal versions are derived, not observed
**Evidence.** On this build machine the only installed interop assemblies are stubs, all reporting the
same version:
```
Extensions 2025\...\Autodesk.Inventor.Interop.dll => 29.0.0.0   (matches 2025-1996=29; VERIFIED)
Extensions 2026\...\Autodesk.Inventor.Interop.dll => 29.0.0.0   (STUB — should be 30)
Extensions 2027\...\Autodesk.Inventor.Interop.dll => 29.0.0.0   (STUB — should be 31)
Extensions 2022/2023/2024 => MISSING
```
The mapping is the documented Autodesk rule **internal major = calendar year − 1996** (2022=26 … 2027=31).
2025=29 is directly confirmed by the real interop. 2026=30 and 2027=31 are **derived** from the rule
because the 2026/2027 interops are v29 stubs (the WS2-B flag). The 2022/2024 = 28 claim that the net48
comments call "Confirmed" can no longer be re-verified here (those interops are gone), but the arithmetic
rule holds.
**Resolution.** Kept the FLAG visible: each fixed WS2-B manifest header now states the derived-vs-observed
status and instructs Phase-4 to confirm against a real Inventor 2026/2027 About box / SDK. No code change.

### F2 — FLAG (caveat recorded): inv26/inv27 "real compile" used v29 stub interops
The inv26/inv27 builds succeed, but they compiled against the v29 **stub** interop surface. This proves
TFM / SDK / code-shape / `EnableDynamicLoading` / .NET-10 toolchain, but **not** the genuine 2026/2027
Inventor API surface. Recorded as a Phase-4-on-real-Inventor item (also already noted in CLAUDE.md:
"treat all Inventor-API handler bodies as compile-only until the Phase-4 smoke run").

### O1 — Observation (no action): bracket bug was untestable by design
`TfmSplitTests` parse the csproj TFMs and `TransportSelectionTests` cover transport selection, but no
test inspects `.addin` `SupportedSoftwareVersion*` values (the manifests aren't compiled). That is why
B1 slipped through. A small XML-parsing test asserting `GT (year-1996-1)../LT (year-1996+1)..` for all
six manifests would close the gap; deferred (Phase-4 packaging), not required for the Phase-2 gate.

---

## Checklist results

1. **TFM matrix exact — PASS.** inv22/23/24 = `net48`, inv25/26 = `net8.0-windows7.0`, inv27 =
   `net10.0-windows7.0` (read all 6 csproj). `TfmSplitTests` genuinely load each `plugin-invNN` csproj as
   XML and assert these exact values (2022/23/24→net48, 2025/26→net8.0-windows7.0, 2027→net10.0-windows7.0).

2. **STA dispatcher — PASS.** `InventorStaDispatcher` (`shared/Plugin/`) uses a hidden message-only
   WinForms `Control`, forces `_marshal.Handle` in the ctor, marshals via `BeginInvoke`, and disposes on
   the STA thread (`_marshal.Invoke(() => _marshal.Dispose())`). It is the ONLY file under `src/shared`
   that references `System.Windows.Forms`, and `shared/Plugin` is NOT globbed by the net8 test project.
   `InventorAddInServerBase.Activate` creates the dispatcher on the STA thread (`_sta = new
   InventorStaDispatcher()`); the listener-thread `HandleLine` only stores `_app` into the context and
   runs all `dispatcher.Dispatch` inside `_sta.InvokeAsync` — it never calls the Inventor API directly.
   `ReadActiveDocument` touches `_app.ActiveDocument` only during `Activate` (already on the STA thread).

3. **Entrypoint — PASS.** `InventorAddInServerBase` implements all of `ApplicationAddInServer`
   (`Activate`, `Deactivate`, `ExecuteCommand`, `Automation`). It captures `site.Application`, verifies
   each envelope `auth_token` against the descriptor token via `AuthToken.Verify` (→ `UNAUTHORIZED`),
   writes the descriptor via `TargetDescriptorWriter`, and STARTS the transport via
   `TransportFactory.CreateStarted` — which for TCP reads the OS-assigned `tcp.Port` back into the
   descriptor (`descriptor.Port = tcp.Port`). `TransportSelectionTests.CreateStarted_tcp_populates_port_on_descriptor`
   asserts `descriptor.Port > 0`, so the "port 0" bug is actively guarded. `Deactivate` disposes server +
   writer + sta, nulls `_app`, and `GC.Collect()`s.

4. **GUID uniqueness — PASS.** All six entrypoint `[Guid]` values are distinct; each `.addin`
   `ClassId == ClientId == its entrypoint Guid` (case-insensitive); assembly names match
   `Bimwright.Inventor.Plugin.InvNN.dll` in both the `.addin` `<Assembly>` and the csproj `<AssemblyName>`.

5. **Version filter — FIXED (B1) + FLAGGED (F1).** Mapping `year − 1996` confirmed (2025=29 from the real
   interop). Brackets corrected to isolate exactly one major version per year, matching the WS2-A idiom.
   2026/2027 internal versions remain DERIVED (stub interops report v29); FLAG kept in each manifest
   comment and in F1.

6. **UseInventorAssemblyContext — PASS.** Present as `0` (isolated) in the inv27 `.addin` (where it is
   honoured) and harmlessly `0` in all the others.

7. **No leak — PASS.** Server csproj compiles only `shared/Contracts/*` + `shared/Security/*` (no
   Inventor, no WinForms, no `UseWindowsForms`). The net8 test csproj globs only `shared/Infrastructure/*`
   + `shared/Transport/*` (NOT `shared/Plugin/*`). `shared/Transport` (TransportFactory + descriptor
   writer + servers) is API-free and WinForms-free (only doc-comment mentions of `Inventor.Application`).
   `InventorCommandRegistry.Build` + the `partial void` declarations live in an unguarded file so they
   compile without interop, while the `AddCore` implementation and both Core handlers are `#if INVENTOR…`
   guarded. Server + tests build clean.

8. **No leftovers — PASS.** No `__WS2B_TEMP_StubBase.cs` anywhere in the working tree or git history; a
   single `InventorAddInServerBase` definition (in `shared/Plugin/`); each `plugin-invNN/InventorAddInServer.cs`
   is a thin sealed subclass with no duplicate base.

9. **Caveat recorded — PASS.** See F2: inv26/inv27 compiled against v29 stub interops; recorded as a
   Phase-4-on-real-Inventor item.

---

## Files changed by this review
- `src/plugin-inv25/Bimwright.Inventor.Inv25.addin` (B1 fix + documenting comment)
- `src/plugin-inv26/Bimwright.Inventor.Inv26.addin` (B1 fix + FLAG comment)
- `src/plugin-inv27/Bimwright.Inventor.Inv27.addin` (B1 fix + FLAG comment)
- `docs/superpowers/reviews/2026-05-29-inventor-phase2-review.md` (this report)
