# Drawing geometry, sketches, edge visibility and design views

Phase 3 source implements four Inventor 2027 tools. Build, automated tests, live
Inventor checks, installation and runtime catalog regeneration have not been run.
Declared source counts are 111 all-enabled / 107 code-off / 21 read-only, including
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
curves with intent_supported=false cannot be used as attached model intents.

Sketch creation requires the target drawing and sheet to be active with no edit
environment open. Sheet coordinates convert through native SheetToSketchSpace.
View sketches follow their parent view; sheet sketches remain in sheet space.
Native readback includes a sheet-space box, sketch-space entities and attributes.
Edit mode exits and the previous selection restores in finally. A repeated managed
name checks the request signature and native content hash; conflicts fail.

Edge hiding uses DrawingCurveSegment.Visible directly and leaves selection untouched.
It reads at most 20,000 segments, skips already-hidden segments and returns candidate/
hidden counts, criteria, stage timings and a fresh revision. No speedup has been measured.

Design-view settings use occurrence_visibility=[{occurrence_path,visible}] and
appearance=[{occurrence_path,asset}]. All targets/assets resolve before the transaction.
Library assets copy into the assembly; the global library is not edited. Native
representation/occurrence readback checks source preservation and requested settings.
Repeated managed names check native representation hashes; unmanaged/conflicting names fail.

All three write tools use a single transaction, read-only gates, timeout forwarding,
failure/readback reporting and no implicit save or retry. Older hosts fail explicitly.
These four commands are excluded from batch_execute. UI/API acceptance, manual-edit
invalidation, save/reopen persistence, performance and release packaging remain unrun.
