# Drawing Phase 1 checks

The branch adds 11 tools: one `drawing_query` tool and ten `drawing` write tools.
The built surface is 99 with all toolsets/send-code enabled, 95 with code off,
and 20 in the current read-only registration. Drawing handlers are implemented
for Inventor 2027; older add-ins return `UNSUPPORTED_HOST`.

| Tool | Live generic fixture coverage |
|---|---|
| `inventor_get_drawing_info` | Sheets/views/definitions/items, pagination, dirty state preserved; cold inactive sheets report unavailable annotations |
| `inventor_new_drawing` | Unsaved template drawing, first/third-angle projection, visible/hidden load, existing dimension style defaults, repeated create |
| `inventor_add_sheet` | Second A3 sheet, persistent code, existing border/title definition, prompts, repeated create |
| `inventor_set_title_block` | Prompt replacement and Title in Summary Information |
| `inventor_add_drawing_view` | Base/projected/arbitrary/circular detail, assembly design view, reference display/margin/hidden-line options, repeated/conflicting names |
| `inventor_add_section_view` | Cut line, positive/negative direction, full/finite depth, inherited view, rotation; explicit position readback |
| `inventor_edit_drawing_view` | Scale/move and attached dimensions preserved; alignment, suppression, rotation and hidden-line style; shaded base rebuilt without dependencies and PDF render inspected |
| `inventor_add_drawing_dimension` | Horizontal 100 mm, vertical/aligned 60 mm, diameter 20 mm, radius 10 mm, angle 90°, chain, model-point intents, supplied style/precision/text override, repeated create, invalid batch |
| `inventor_add_balloon` | Supplied prompted symbol with text readback, nested occurrence, target region, column/angle layout, repeated create; native leader attachment after assembly scale/move and reopen |
| `inventor_capture_sheet` | Sheet PNG, region/inline, UI/dirty state restoration, existing-path rejection |
| `inventor_export_drawing` | All/subset/ordered PDF, per-sheet AutoCAD DWG, native IDW copy, overwrite rejection/opt-in, fresh nonempty artifacts |

The [smoke record](../benchmarks/drawing-phase1-smoke.json) summarizes invocations of
the actual registry/dispatcher/handlers against generated geometry, with send-code disabled.
Host-free tests invoke every MCP wrapper over a named pipe and check wire DTOs/timeouts,
annotations, read-only registration, input failures and response compaction.
Installed Codex/Inventor 2027 checks at `ccb2681` passed all eleven tool paths, save/reopen,
exports and capture, native leader/dimension attachment and exception-after-create rollback.
The user observed drawing toasts; the add-in journal recorded successful and failed outcomes.
The [read-only allowlist](readonly-tools.json) is generated from the built server's
`tools/list`; it describes registration, not a claim of legacy annotation/add-in parity.

## Remaining acceptance

This is **partial implementation acceptance**, not a release verdict or production approval.

- Four variants are deferred from Phase 1 under the revised scope; their explicit rejection stays
  part of the supported contract. They are not counted as implemented variants.
- Drawing crop is deferred: the 2027 interop has no drawing crop API. The tool rejects crop explicitly.
- Native BOM balloons are deferred until rollback of model BOM changes across documents is verified.
  `mode=bom` never silently changes a model; symbol balloons are the supported path.
- Shaded moves rebuild only managed base/arbitrary views without dependent views or annotations.
  Dependency-preserving rebuilds remain pending; a dependency-free generic base view passed PDF visual inspection.
- Hidden-loaded drawing capture is rejected to preserve window visibility. Visible sheet capture is supported.
- A native label failure after view creation rolled back and left the view count unchanged.
  Queued timeout containment, further template variants and deployed add-in toast/History behavior
  still need acceptance evidence.
- Legacy annotation/read-only/catalog parity, global configurable response guarding,
  whole-product release artifacts/MCPB/client lifecycle and the release gate remain separate dependencies.

## Run the disposable fixture

After opening a drawing, Inventor can report unknown/no-data status on an inactive sheet
and expose empty annotation collections despite saved dimensions being present.
The query returns `annotation_data_available=false`, `sheet_status_bits`, null annotation
counts and null requested annotation collections in that state. Activate the sheet in
Inventor and query again before deciding whether annotations are missing. The query itself
does not activate or update sheets, and unavailable data must never trigger a create replay.
Annotation creation and shaded rebuild preflight also reject that state with `API_ERROR`
before mutation, since name conflicts and dependencies cannot yet be checked reliably.

Close existing Inventor sessions yourself. The runner refuses to attach to any existing process,
creates its own 2027 session and generic part/nested assembly/template, writes artifacts under a
fresh temp directory and closes only that session. It never installs an add-in or edits MCP clients.

```powershell
dotnet run --project tests/DrawingLive/DrawingLive.csproj -- --run-owned-session
```

The final `ROOT` identifies retained fixture files and `results.jsonl` for local inspection.
No local fixture logs, drawings, screenshots or personal paths belong in the public benchmark.

Coordinates are mm from sheet lower-left; model points use the referenced model's root coordinates.
Dimension item values use explicit `unit=mm|deg` in queries and create readbacks.
Scale is a ratio and angles are degrees. `annotation_defaults` currently accepts
`{dimension_style: "existing template style"}` only. Names/signatures/codes use native
AttributeSets and survive save/reopen; queries never create attributes. Repeated creates reuse
matching managed objects; mismatches require explicit edits. Dimension/balloon batches preflight
all items and abort on failure. New drawing tools are excluded from `batch_execute`.

File writes require explicit, fully qualified allowed paths. Existing exports require
`overwrite_existing=true`; captures never overwrite. Ordered PDF subsets use a hidden disposable
sheet copy, leaving source state intact. Large completed write results return compact effect
summaries; do not replay the write for missing detail. A timeout is an unknown outcome:
check `inventor_health`, then query the named document/items before any further mutation.

On Inventor 2027, drawing `save_document` uses SaveAs2 with SaveDependents=false or
Save2(false); native IDW exports also disable saving dependents. Live checks of capture,
exports, Save-As and in-place save preserved a dirty referenced part and its disk bytes.
Part/assembly save behavior and older-host drawing saves keep the existing contract.

One server-wide output guard measures the serialized final MCP result in UTF-8, including
content, structured content and metadata. Defaults are warning 64 KiB, strong warning 256 KiB
and budget 1 MiB. `--disable-output-guard` preserves the independent transport fence;
`--enable-output-guard` enables it. Configure thresholds with `--output-warning-bytes`,
`--output-strong-warning-bytes` and `--output-budget-bytes` or JSON `enableOutputGuard`,
`outputWarningBytes`, `outputStrongWarningBytes`, `outputBudgetBytes`. Environment equivalents
are `BIMWRIGHT_INVENTOR_OUTPUT_GUARD`, `BIMWRIGHT_INVENTOR_OUTPUT_WARNING_BYTES`,
`BIMWRIGHT_INVENTOR_OUTPUT_STRONG_WARNING_BYTES`, `BIMWRIGHT_INVENTOR_OUTPUT_BUDGET_BYTES`.
Thresholds must be ordered; budgets and the transport cap must be at least 4096 bytes.
CLI overrides environment, which overrides JSON. This replaces the drawing-only size policy.

Spill retention defaults to 36 hours and is configurable through `spillRetentionHours`,
`BIMWRIGHT_INVENTOR_SPILL_RETENTION_HOURS` or `--spill-retention-hours`. The server carries
the setting to the add-in; cleanup never removes younger files to satisfy a count cap.
