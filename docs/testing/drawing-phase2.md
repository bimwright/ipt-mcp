# Drawing annotations, tables and sheets

Eight additional drawing writes are implemented on this branch: add note, symbol
and custom table; edit annotation, table and sheet; delete drawing items; set
drawing styles. The current surface is 107 all-enabled / 103 with code off /
20 read-only. Installed-client acceptance for these additions is pending.

Notes accept literal text, a persistent name and a position in sheet mm.
`kind=general|sheet_title` uses an existing text style; `kind=leader` uses an existing
dimension style and requires a view plus a uniquely resolved model intent.
An optional `box_mm={width,height}` fixes the general/title text box in mm; the
position is its top-left. Layer/style names must exist in the drawing.

Custom tables accept 1–50 columns `{heading,width_mm}`, 0–500 rows of strings,
and a top-left insertion position in mm. Optional row heights match data rows;
data row/column addresses exclude the title/header. The add-in handles Inventor's
zero-row default explicitly and returns the actual empty table. It applies the
selected table style before column widths, row heights and title, then verifies
the resulting content/dimensions. These are custom tables; BOM parts lists are
outside this tool.

These tools use the existing validation, transport, transaction, output guard,
toast and History paths. They are hidden/rejected in read-only mode and blocked
inside `batch_execute`. Identical managed creates are reused without dirtying the
document. Conflicting names and changed native content fail without overwriting.
`get_drawing_info(include=items)` now includes notes and tables; nested table
columns, rows and heights use the same bounded pagination as other collections.
Cold inactive sheets continue to report annotation data as unavailable.

Symbols use an existing definition and exact prompt labels, with an optional
attached leader. Centermarks require circular view geometry. Annotation edits
accept exact names or document/sheet/reference-key locators from the query;
moving an attached symbol requires `rebuild=true` and an unbranched leader.
Dimension edits preserve measured values separately from literal text overrides.
Leader arrowheads use private local styles. Native BOM balloon edits and dimension
arrowhead setters are unavailable.

Table edits delete original rows, insert sequentially before a current 1-based
data-row index (N+1 appends), then edit final cells. Width/height arrays match the
final table. Header-height changes require a verified rebuild, visible headings
and one unrotated section. Linked/BOM tables are unavailable.

Delete previews take kind/names or a sheet-space region and leave the document
unchanged. Region bounds support views, notes and tables; other kinds require
exact selection. Execution takes exact targets including dependencies; unsupported
native annotation/sketch cascades are rejected. Sheet deletion requires the complete
`contents` inventory, including border/title block, and cannot delete the last sheet.
Rename/code, reorder, activation and resize are separate calls. Resize reports
out-of-bounds items and unavailable bounds without rescaling model views.

Style changes address existing document-local dimension/text styles, layers and
object defaults. They do not save to the global style library. Affected counts
cover supported sheet objects and state their limits; defaults affect future
objects. Tool descriptions specify supported nested fields and enum values.

The owned Inventor 2027 fixture passes 57 successful calls and 25 expected
rejections across all eight additions. It exercises notes, prompted/leadered symbols,
centermarks, diameter and two-/three-intent dimension edits, custom/empty tables,
header rebuild/rollback, stale locators, delete previews/dependencies, local styles,
sheet lifecycle, read-only rejection and save/reopen persistence. Global style-library
files remain unchanged. The final PDF was rendered and inspected; generated
annotations and tables are legible and clear of the frame and title block.
Template title-block formatting remains supplied by the existing template.
The [smoke record](../benchmarks/drawing-phase2-smoke.json) contains anonymized
per-tool invocation metrics. Host-free verification passes 759 unit tests, 165
toast-model tests and 16 WPF tests per net48/net8/net10. Earlier Phase 1 regression
passes 49 positive and 28 expected-negative calls; the eight new writes account
for the additional read-only rejections. Installed Codex/toast/History acceptance
remains pending. This fixture can run only when no Inventor process is present:

```powershell
dotnet build tests/DrawingLive/DrawingLive.csproj -c Release -p:IptMcpSkipDeploy=true
dotnet tests/DrawingLive/bin/Release/net10.0-windows7.0/DrawingLive.dll --run-phase2-owned-session
```

The fixture creates and closes its own Inventor session and only saves generated
models/drawings under a fresh temporary directory. The existing
[Phase 1 fixture](drawing-phase1.md) remains a separate regression check.
