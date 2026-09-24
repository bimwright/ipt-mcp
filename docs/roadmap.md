# Bimwright Inventor MCP Roadmap

Status as of **2026-09-24** · current release **v0.2.0** (73 tools: 72 default + `inventor_send_code`). Unreleased working tree: **73 default / 74** with `inventor_report_task_result`.
The Phase 1 roadmap (46 tools) is archived at [`archive/roadmap-phase1.md`](archive/roadmap-phase1.md).

Phases run **in order**. A phase starts only when the previous phase's exit criteria are met, unless the
phase says it can run in parallel. Each phase lists a goal, its scope, the evidence behind it, and exit
criteria. Before implementation, each phase gets its own spec/plan (`docs/superpowers/`); this file only
fixes order and scope.

Evidence sources:
- [send_code usage analysis (2026-09-23)](../analysis/sendcode-2026-09/ANALYSIS.md): 1655 send_code calls,
  56% of all traffic. Its tool proposals are numbered **A#1…A#13** below.
- [Improvement spec F1–F5 (2026-09-15)](improvement-spec-2026-09-15-ws2-bim-run.md) and the
  [F1–F5 handoff](handoff-2026-09-16-f1-f5-typed-tools.md) (shipped as v0.2.0; §6 lists deliberate deferrals).
- [Gap analysis — WS2 BIM-authoring run](gap-analysis-2026-09-15-ws2-bim-run.md).

## Where we are (shipped)

| Release | Surface |
|---|---|
| v0.1.0 (2026-08-28) | Two-process architecture, Inventor 2022–2027 add-ins (TCP ≤2024 / Named Pipe ≥2025), document / sketch / feature / parameter / iProperty / export / ToolBaker tools, target discovery |
| v0.2.0 (2026-09-16) | Assembly (place, constraints, iMates, BOM, interference, min distance), hole/fillet/chamfer/patterns, `combine`, `loft`, `sweep`, `derive_envelope`, `export_sat`, `set_camera`, `batch_execute`, `list_bodies`, `list_features`, `probe_brep`, `create_work_point`, `draw_text`, `create_bim_connector`, `list_iproperty_sets`; call journal v2, response-size guard, stdout spill, send_code `result` + `timeout_ms` |

Most of the Phase 1 roadmap's "deferred" items are now shipped. What is still open from it is in Phase 6.

---

## Phase 1 — Toast notifications (Inventor-native, independent of rvt-mcp)

**Goal:** an Inventor user sees what the agent is doing. Each MCP command that completes shows a small,
non-blocking notification over the Inventor window (read / write / error, summary, duration, thumbnail for
captures). The user can switch it off from the ribbon, and the choice persists.

**Why first:** ipt-mcp has no user-facing surface inside Inventor today. The plug-in has no ribbon, no status
UI and no config file (options are env-only, `PluginOptions`). Agent activity, send_code in particular, is
invisible to the person at the keyboard.

### Principles

- **Independent implementation.** The toast lives entirely in `ipt-mcp` and has no project, source-link,
  shared-package or runtime dependency on `rvt-mcp`. It uses its own namespace
  (`Bimwright.Ipt.Plugin.Views.Toast`), config file, env names and tests. A change in rvt-mcp must never
  change Inventor behavior, and the reverse holds too.
- **rvt-mcp is a behavioral reference only.** It is the UX to match: what a toast shows, read/write/error
  styling, max 4 stacked top-left of the host window, ribbon on/off toggle, privacy block in a status
  dialog. Its code is *not* copied wholesale. A piece of it may be reused only after Phase 1a shows the
  assumption behind it also holds in Inventor.
- **Inventor platform first.** Revit and Inventor differ in threading, UI framework, ribbon API, add-in
  lifecycle and load context (table below). Each difference is decided from evidence gathered on a live
  Inventor, not inferred from Revit.

### Revit vs Inventor — what rvt-mcp's toast assumes, and what must be checked

| Area | rvt-mcp (Revit) assumption | Inventor reality / open question | Consequence |
|---|---|---|---|
| WPF host | Revit owns a WPF `Application.Current`; toasts run on **Revit's UI dispatcher**. rvt-mcp's host notes a second STA thread next to an existing `Application` is unstable | Unknown whether `System.Windows.Application.Current` exists inside `Inventor.exe`, per tier (net48 2022–24, .NET 8 2025–26, .NET 10 2027) | **Spike 1a-1.** Decides the threading model |
| Command thread | Commands run in a Revit `ExternalEvent` on the UI thread; the toast hook is called there | Commands run on Inventor's STA via our hidden WinForms control (`InventorStaDispatcher`). The result reaches `HandleLine` on the **listener (background) thread** | The hook must marshal to the toast dispatcher and never block the listener or the STA |
| Rendering thread | Revit UI thread | Option A: Inventor STA (same thread as commands; toasts can't animate or age out during a long send_code). Option B: dedicated STA thread with its own `Dispatcher` (independent, but see the owner row) | **Spike 1a-2** picks A or B from measurements |
| Owner window | `UIControlledApplication.MainWindowHandle`, same thread | `Application.MainFrameHWND` (read on the STA in `Activate`). With option B the owner is on another thread: Win32 owner/owned windows across threads share input processing, so a busy Inventor STA may freeze the toast thread | **Spike 1a-3:** owner vs unowned + manual z-order/position tracking. Also check undocked document windows on a second monitor |
| Focus | Toast windows are `Topmost`; no explicit no-activate handling | Inventor is sensitive to focus during in-canvas commands and sketch edits. A toast must never take activation | Require `ShowActivated=false` + `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW`; verify during an active sketch and an in-canvas command |
| DPI | `GetDpiForWindow(owner)` scaling | Inventor process DPI-awareness mode per year is unknown; WPF in-proc inherits it | **Spike 1a-4:** mixed-DPI monitors, 100%/150% |
| Theme | rvt-mcp always draws a light card (`#F5F5F5`), whatever Revit's theme | Inventor has `ThemeManager` (DarkTheme/LightTheme). **The UI theme does not change the canvas**: that comes from the colour scheme, often an image. A dark toast on Inventor Dark measured 1.06–1.37:1 (blends in) | **Palette = inverse of the backdrop actually behind the toast** (screen sample, light- or dark-elevated with outline + shadow). Re-evaluate on `OnApplicationOptionChange`. User override `toastTheme` (spike pass4/pass5) |
| Ribbon | Revit UI API: `RibbonPanel` + `PushButtonData` + `IExternalCommand`; button text/icon updated from the `Idling` event (`IdlingUpdater`) | `UserInterfaceManager` → `ControlDefinitions.AddButtonDefinition` + `OnExecute`; ribbons per environment (ZeroDoc, Part, Assembly, Drawing, Presentation); `ButtonDefinition.Pressed` shows the toggle state, so no idle polling; rebuild on `UserInterfaceEvents.OnResetRibbonInterface`; remove in `Deactivate` | New code. No Revit concept carries over |
| Icons | WPF `ImageSource` (`IconGenerator`) | Ribbon icons are `stdole.IPictureDisp`; the conversion differs between net48 and .NET 8/10 (no `AxHost` helper in the same form) | **Spike 1a-5:** icon pipeline for both tiers |
| Load context | Normal AppDomain / Revit ALC | 2027 can load the add-in in an Inventor `AssemblyLoadContext` (`UseInventorAssemblyContext`); XAML `pack://` resources may not resolve there | Build UI in code only: no XAML/BAML, no pack URIs |
| Lifecycle | `OnStartup` / `OnShutdown` | `Activate(firstTime)` / `Deactivate`. Topmost windows must be closed and the dispatcher stopped before unload | Explicit shutdown order: stop hook → dismiss → stop dispatcher → remove ribbon |
| UI-less hosts | Revit always has UI | Inventor can run invisible (`Application.Visible=false`, automation) or as Inventor Server | Toast auto-off when there is no visible main frame |
| Config / env | `%LOCALAPPDATA%\RvtMcp\rvtmcp.config.json`, env `BIMWRIGHT_ENABLE_TOAST` (no app prefix) | Own file `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` key `enableToast`, env **`BIMWRIGHT_INVENTOR_ENABLE_TOAST`** | Setting rvt-mcp's env must not toggle Inventor toasts |
| Content | `ToastContentBuilder` special-cases Revit commands and payloads (`capture_view_image`, views, element counts) | Inventor wire commands and DTOs (`capture_view`, `send_code` result/stdout/spill, `batch_execute`, `check_interference`, `get_mass_properties`, mm units) | Content and classifier rules are written for Inventor |

### Phase 1a — Compatibility spike (no product code merged)

> **Status 2026-09-23: done on Inventor 2027.** Decisions: dedicated toast STA thread, **unowned**
> no-activate windows with our own minimize/foreground tracking, `OleCreatePictureIndirect` icons, and
> toast inside the existing add-in. See `docs/superpowers/specs/2026-09-23-ipt-toast-design.md` (local)
> and branch `spike/ipt-toast-1a`. Theme follow-up (same day): the palette follows the **backdrop**
> behind the toast (screen sample), not Inventor's UI theme, and toasts anchor over the graphics view.
> Open items that move into 1b: runtime on 2022–2026, mixed DPI, `UseInventorAssemblyContext=1`,
> ribbon reset, toasts covering dialogs, and screen-sample cost.

A throwaway add-in branch on **Inventor 2027** plus **one net48 year** (2024), answering with evidence
(logs + screenshots in `docs/superpowers/reviews/`):

1. Is `Application.Current` null or non-null at `Activate`? Can a WPF `Window` be shown from the Inventor
   STA and from a dedicated STA thread?
2. Option A vs B under load: keep a 30 s `send_code` running and fire toasts before and during it; measure
   toast responsiveness and `inventor_health` latency.
3. Owned vs unowned toast windows with option B: any input freeze, z-order or minimize/restore problems, and
   behavior with an undocked document window on a second monitor.
4. DPI scaling on 100% / 150% monitors and when moving Inventor between them.
5. Ribbon: a toggle `ButtonDefinition` in ZeroDoc/Part/Assembly/Drawing with icons on both tiers; state
   survives `OnResetRibbonInterface`.
6. Focus: a toast appearing during an active sketch, an in-canvas command and a modal dialog does not take
   activation or cancel the command.

**Output:** a short design spec (`docs/superpowers/specs/…-ipt-toast-design.md`) that records the chosen
threading model, owner strategy, icon pipeline and anything that differs per year.

### Phase 1b — Implementation (per the spike's spec)

> **Status 2026-09-24: implemented + live-verified on Inventor 2027** (branch `feat/ipt-toast-1b`,
> net10/Named-Pipe tier). Live checks passed: read/write/error + soft-fail toasts, 4-card stack,
> thumbnail, no focus steal, topmost over other apps (D2), modal/minimize suppression, ribbon toggle
> persist + env override + ribbon reset, clean close, status dialog. See
> `docs/superpowers/reviews/ipt-toast-1b-live/RESULTS.md` (local). Still open: live run on 2024
> (net48/TCP) + one net8 year (D8), mixed-DPI / second monitor, undocked document window,
> invisible/automation Inventor, `UseInventorAssemblyContext=1`, hi-DPI icon check (D9).
>
> **Follow-up 2026-09-24 (working tree):** the per-tool queue is replaced by a feed that retains at most three cards and lets the UI pull a snapshot at most twice a second. `inventor_report_task_result` is the explicit job summary. Unit tests passed the same day (toast 146, server 436). Inventor 2027 was left running, so the add-in was not replaced and the burst check — 100 tool calls must not keep creating toasts for tens of seconds after the calls return — is still open. Record: [`testing/smart-toasts.md`](testing/smart-toasts.md).

1. `src/shared/Views/Toast/` (plug-in only, code-built WPF): window, manager (stacking, max 4, ageing),
   host (threading per spec), notifier, view model, content builder, activity classifier, tool-name
   formatter, thumbnail loader, and theme:
   - light-/dark-elevated palettes chosen by backdrop contrast
   - one decision per stack
   - live re-theme on option change
   - anchor over the active view; below the ribbon on Home
2. Hook after every command in the listener result path (`InventorAddInServerBase.HandleLine`):
   success, handler error, validation error, TIMEOUT, unknown command. Fire-and-forget, exception-safe,
   off by default when there is no visible main frame.
3. Config: `iptmcp.config.json` + `BIMWRIGHT_INVENTOR_ENABLE_TOAST` (JSON < env), `SaveEnableToast()`
   preserving other keys, folded into `PluginOptions`. Also `toastTheme` = `auto` (default, inverse of
   backdrop) | `light` | `dark`, env `BIMWRIGHT_INVENTOR_TOAST_THEME`.
4. Ribbon "Bimwright MCP" panel: **Toast** toggle (`Pressed` state, persists, dismisses open toasts when
   turned off) and **Status** (target id, transport, send_code/read-only gates, privacy block).
5. Build: `<UseWPF>true</UseWPF>` on inv25/26/27; `PresentationFramework` / `PresentationCore` /
   `WindowsBase` / `System.Xaml` references on inv22/23/24. The server stays WPF-free.
6. Tests: new `tests/Bimwright.Ipt.Toast.Tests` (net8.0-windows): content builder, classifier, config
   precedence/persistence, thumbnail loader. The existing server test project stays host-free.
7. Docs: README (en/vi/zh-CN/ja) "Toast notifications", CHANGELOG.

### Exit criteria

- The spike spec exists, and each row of the Revit-vs-Inventor table has a recorded decision.
- All six plug-in shells (2022–2027) build; existing tests are green; toast tests pass.
- Live on Inventor 2027 and 2024:
  - read, write and error toasts appear with correct content
  - `capture_view` shows a thumbnail
  - a burst of more than 4 calls stacks and ages out
  - no focus steal during sketch or in-canvas commands
  - correct placement on mixed-DPI monitors
- `inventor_health` latency unchanged while toasts are visible and during a long send_code.
- The ribbon toggle survives an Inventor restart and a ribbon reset; env overrides JSON; with toasts off,
  or with Inventor invisible, no window is created.
- `grep -r "RvtMcp" ipt-mcp/src` finds nothing: no dependency on rvt-mcp.

---

## Phase 2 — Deployment hygiene & output contract (send_code analysis P0)

**Goal:** remove the non-tool causes of send_code use. These are the cheapest fixes with the largest effect.
**Can run in parallel with Phase 1** (server-side, different files).

**Evidence:** native Codex/Claude Code sessions (34% of send_code) ran a server build from 09-16 00:15 that
**lacks 14 v0.2.0 tools** (`batch_execute`, `list_bodies`, `set_camera`, `combine`, …), because every client
config points at `src/server/bin/Debug/net8.0/`.

### Scope

1. **One install path for the server.** Publish the server to a fixed location (e.g.
   `%LOCALAPPDATA%\Bimwright\ipt-mcp\server\`) from the release/dev scripts. Update the documented client
   config (`.mcp.json` example, README) to point there instead of a `bin/` folder.
2. **Build identity:** `inventor_health` and `initialize.serverInfo.version` report version + build hash +
   registered tool count. The add-in reports its own build, and a server/add-in mismatch is flagged.
3. **Spill send_code `result`**, not only stdout. Two calls hit RESPONSE_TOO_LARGE (846 KB, 1.1 MB). Port
   rvt-mcp's `ResponseSpillPolicy` behavior for `send_code` / `run_baked_tool` `result`.
4. **Output-root guidance:** `BIMWRIGHT_INVENTOR_EXPORT_ROOT` is already in the export docs (v0.2.0).
   Also surface it in the `capture_view` / export tool descriptions and `ServerInstructions`, so agents stop
   routing project-folder images through `SaveAsBitmap` in send_code (108 scripts).
5. **Compile-error notes** in `ServerInstructions` + the send_code description:
   - indexed COM props (`get_Units()`)
   - `_Document` cast for `StartTransaction`
   - `AssemblyDocument` vs `PartDocument` casts
   - `MaterialAsset → Asset`
   - `Asset.CopyTo(doc)` (there is no `CopyToDocument`)
6. **Multi-version rebuild debt** (handoff §7.6): rebuild and smoke inv22/23/25/26 against the v0.2.0
   handlers.
7. **Read-only UX (decide in spec):** `get_parameter`, `list_parameters`, `get_iproperty`,
   `list_iproperty_sets`, `get_mass_properties` sit in write-capable toolsets, so `--read-only` hides these
   reads. Move them to a read toolset, or document it.

### Exit criteria

- A fresh client session lists all 74 tools with `--toolsets all --enable-send-code` (73 by default). `inventor_health` shows a build hash that matches the published binary.
- A send_code call returning >700 KiB of `result` spills instead of failing.
- All six shells built and smoke-tested on the Inventor years available on the dev machine.

---

## Phase 3 — Reuse: ToolBaker gets real content

**Goal:** stop re-sending the same C# prelude. 107 scripts carry byte-identical `USection` / `LineLoop` /
`Holes` helpers (a home-made structural-steel kit), and ToolBaker has 0 rows.

### Scope

1. Port rvt-mcp's `BIMWRIGHT_CACHE_SEND_CODE_BODIES` / `BIMWRIGHT_PERSIST_SEND_CODE_BODIES` so send_code
   bodies feed adaptive-bake suggestions (usage_events is 0 today).
2. Seed baked tools from the helper kit (`USection`, `LineLoop`, `Holes`, `Bounds`, `RoundRect`, `Drill`,
   `Bind`) with typed parameters, and document how a project promotes its own snippets.
3. Script-runner guidance (docs, not ipt-mcp code): a step file can be a typed call
   (`{"tool": …, "args": …}`) or a `batch_execute`, not only a `.cs` body. 60% of send_code came from a
   one-server-per-call runner that only issued `.cs` steps.

### Exit criteria

- Re-running `analysis/sendcode-2026-09/reanalyze.py` on a new campaign shows baked-tool calls replacing the kit prelude.
- `bake.db` has suggestions from real usage.

---

## Phase 4 — High-share typed tools

**Goal:** cover the biggest send_code intents with typed tools. Ordered by send_code share (analysis
estimates, assuming Phase 2 is done).

| Order | Tool(s) | Absorbs | Est. share |
|---|---|---|---|
| 4.1 | `inventor_activate_design_view` (open-with / activate design view rep) + `inventor_set_object_visibility` (work features / sketches / bodies) — A#8 | view_capture 181, state_edit 46; DVR in 153 scripts, `AllWorkFeatures` in 218 | 9–12% |
| 4.2 | `inventor_save_all` (Update2 + save dirty docs under a root) + `inventor_close_all`, `inventor_open_documents` (batch), `inventor_save_copy_as`, `inventor_set_design_project` — A#10 | doc_manage 154 | 7–9% |
| 4.3 | `inventor_list_occurrences` (recursive leaf walk, `name_filter`, bbox/transform/material/iProps, `output=inline\|file`) — A#4 | inspect_assembly 138 | 6–8% |
| 4.4 | `inventor_place_occurrences` (pose array, optional bind-to-work-planes) + `inventor_delete_occurrences` (by name pattern) + `inventor_set_occurrence_transform` / `_state` — A#2, A#3 | assembly_edit 90 + generator placement | 6–9% |

**Exit criteria:** each tool is live-verified on Inventor 2027, added to the four READMEs, the `mcps/` schemas
and the tool count in the root `CLAUDE.md` + `.github/profile`. A re-run of the analysis on the next campaign
shows those intents shrinking.

---

## Phase 5 — Appearance, clash, generators, geometry selection

| Order | Tool(s) | Absorbs | Est. share |
|---|---|---|---|
| 5.1 | `inventor_set_appearance` (occurrence / body / face; RGB or library asset via `Asset.CopyTo`) + `inventor_create_appearance` + `inventor_reset_appearance` (via `AppearanceSourceType`) — A#7 | appearance_material 122 | 5–7% |
| 5.2 | Extend `inventor_check_interference`: occurrence-name sets A×B, per-pair overlap volume, bbox prefilter — A#11 | clash_check 108 | 5–6% |
| 5.3 | `inventor_create_section_member` (U / SHS / RHS / plate / tube section + length + hole pattern + iProps + save path) — A#1. Builds on the Phase 3 kit | build_geometry kit/multi-doc | 10–15% |
| 5.4 | `inventor_query_edges` / `inventor_query_faces` (predicate → stable refs) + `edge_filter` on `chamfer` (fillet already has a circular-edge selector) — A#5 | single-part modeling, inspect_part | 6–9% |
| 5.5 | `inventor_measure` (batch pairs → distances / angles) + `inventor_update_document` (Update2 + health report) — A#12, A#9 | measure 46, health checks | 3–4% |

**Target after Phases 2–5:** the analysis puts the typed-tool ceiling at **≈57–81%** of today's send_code
volume. ≥90% is not expected: generator-style scripts and one-off API work stay in send_code or ToolBaker.

---

## Phase 6 — Backlog (unscheduled until evidence arrives)

From the archived Phase 1 roadmap, not yet shipped:
- **Drawing tools:** base / projected / section / detail views, PDF export, balloons, parts lists, annotations.
- **Advanced features:** shell, thread, mirror feature, sketch modify, body split / mirror.
- **Diagnostics:** command search, API documentation lookup, undo.
- **Escape hatches:** iLogic rule execution + log readback. Public Python execution stays a non-goal.
- **Runtime / sessions:** start / adopt / list / terminate managed Inventor sessions.
- **Out-of-process / cloud:** Apprentice Server read workflows, APS Design Automation.

From the v0.2.0 handoff §6 (deliberate deferrals):
- loft guide rails / section conditions / area-graph
- sweep guide rail / surface / twist / `affected_bodies`
- `derive_envelope` per-occurrence pick for .iam
- `create_bim_connector` kinds `duct|conduit|cable_tray|electrical`
- reference-driven work points
- boxed / justified `draw_text`

Low-evidence analysis items: `inventor_set_feature_state` + `inventor_rename_bodies` (A#6, ≤1%),
`inventor_highlight` / `inventor_select` (A#13, 11 scripts).

---

## Measuring progress

After each phase, re-run `analysis/sendcode-2026-09/reanalyze.py` against the live call journal and compare:
- send_code share of all calls, split by channel (driver / native)
- intent mix
- envelope failures

Phase 4/5 success is judged on the **native** channel. Driver-channel share depends on how the campaign's
runner is written (Phase 3).

## Explicit non-goals

- Fusion, Vault, CAM, or Product Design Extension workflows.
- Public Python execution.
- A single add-in binary shared across all Inventor versions.
- Redistribution of Autodesk Inventor binaries or SDK DLLs.

## .NET / framework support

| Inventor years | Target framework | Transport |
|---|---|---|
| 2022 / 2023 / 2024 | `net48` | TCP |
| 2025 / 2026 | `net8.0-windows7.0` | Named Pipe |
| 2027 | `net10.0-windows7.0` | Named Pipe |

> **TFM Upgrade Directive:** when Autodesk ships a new Inventor major version, add a new `plugin-invNN`
> shell targeting the Autodesk-supported .NET runtime for that version. Do **not** copy the `nwd-mcp`
> "all plug-ins target net48" model into Inventor.
