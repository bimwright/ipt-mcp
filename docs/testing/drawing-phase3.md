# Drawing geometry, sketches, edge visibility and design views

Phase 3 implements four Inventor 2027 tools. Release builds, 792 server tests and
165 toast tests passed. Native handlers passed on disposable Inventor 2027 fixtures.
Built server catalogs confirm 111 all-enabled / 107 code-off / 21 read-only, including
22 drawing tools (2 query, 20 write) and the assembly design-view tool.

| Tool | Behavior |
|---|---|
| `inventor_find_view_geometry` | Read one view using a model point, edge, sheet region or occurrence path; filter kind/visibility and page with max_items/offset. Returns sheet-mm geometry and geometry_id/revision. |
| `inventor_sketch_on_view` | Create a named sketch with 1–500 native lines, circles, arcs or literal text. Omit view for a sheet sketch. Supports sheet/view_local coordinates, existing layer, color and line weight. |
| `inventor_hide_view_edges` | Hide currently visible segments on one explicit document/sheet/view. Occurrence selector and max_model_size_mm combine with AND. dry_run previews candidates; threshold measures projected curve length divided by view scale. |
| `inventor_create_design_view` | Copy a named/current assembly representation, apply exact occurrence visibility/appearance settings and keep the original active unless activate=true. Assets resolve from the document or existing libraries. |

Geometry always resolves fresh on the STA; cache_hit is false and refresh is accepted.
Snapshots refuse more than 50,000 curves. Revision fingerprints include document
lifetime/revision, model revision, view state, curve geometry and visibility. Attached
dimension, balloon, leader and symbol intents can use geometry_id plus revision;
stale revisions fail before mutation. Resolvable model references are reported honestly;
curves with intent_supported=false cannot be used as attached model intents. B-Rep
keys include model_reference_context; assembly proxies retain exact occurrence paths.

Sketch creation requires the target drawing and sheet to be active with no edit
environment open. Sheet coordinates convert through native SheetToSketchSpace.
View sketches follow their parent view; sheet sketches remain in sheet space.
Native readback includes a sheet-space box, sketch-space entities and attributes.
Edit mode exits and the previous selection restores in finally and after transaction
completion. Color and line weight apply to native curve entities; text color applies
to text boxes. Arbitrary text angles use native RotateSketchObjects. A repeated managed
name checks the request signature and native content hash; conflicts fail.

Edge hiding uses DrawingCurveSegment.Visible directly and leaves selection untouched.
It reads at most 20,000 segments, skips already-hidden segments and returns candidate/
hidden counts, criteria, stage timings and a fresh revision. No speedup has been measured.

Design-view creation requires the target assembly active with no in-place edit.
Settings use occurrence_visibility=[{occurrence_path,visible}] and
appearance=[{occurrence_path,asset}]. All targets/assets resolve before the transaction.
Library assets copy into the assembly; the global library is not edited. Native
representation/occurrence readback checks source preservation and requested settings.
Repeated managed names check native representation hashes; unmanaged/conflicting names fail.

All three write tools use a single transaction, read-only gates, timeout forwarding,
failure/readback reporting and no implicit save or retry. Older hosts fail explicitly.
These four commands are excluded from batch_execute.

## Verified functional scope

| Tool | Native checks passed |
|---|---|
| Geometry query | Known lines/circle, paging, stable revision, attached diameter dimension, stale-ID rejection after direct native scale edit, reopen lifetime, exact nested occurrence references. |
| Sketch markup | Line/circle/clockwise arc/literal text at 15 degrees, measured coordinates, layer/color/weight/font, selection restoration, matching repeat/conflict, sheet/view-local space, parent move/scale, edit-mode rejection. |
| Hide edges | Dry-run equals hidden count, repeated hiding changes zero segments, second view unchanged, nested occurrence AND size threshold, unselected occurrence preserved. |
| Design view | Explicit and default Primary source, source visibility/appearance preserved, nested path, local appearance override, matching repeat and activation, invalid target before mutation, save/reopen persistence. |

Host-free tests cover strict inputs, geometry intent contracts, hints, read-only and
batch gates, catalog registration, named-pipe arguments and timeout forwarding.
Tests run with BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS=1 in the test process, matching
the default privacy profile; opt-out behavior has its own tests.

The native harness refuses an existing Inventor process, opens its own empty session,
creates temporary fixtures and quits only that session. Run the main fixture with:

```powershell
dotnet run --project tests/DrawingLive -c Release -- --run-phase3-owned-session
```

Additional Primary/nested/scale cases use IPT_PHASE3_FROM=extended for the harness.
Passed groups are retained; focused continuation modes avoid replaying them.

Installed MCP-client/toast UI acceptance, manual UI crop/change-marker coverage,
large-view and batch-command performance comparisons, agent send_code reduction,
installer/package/release gates remain separate. Fresh resolution remains the
supported cache policy; no cache-hit or universal speedup claim is made.
