# Drawing notes and custom tables

Two additional drawing writes are available on this branch: `inventor_add_drawing_note`
and `inventor_add_drawing_table`. The current surface is 101 all-enabled / 97 with
code off / 20 read-only. The other six tools planned for the next drawing delivery
are not registered yet. Installed-client acceptance for these additions is pending.

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

Both tools use the existing validation, transport, transaction, output guard,
toast and History paths. They are hidden/rejected in read-only mode and blocked
inside `batch_execute`. Identical managed creates are reused without dirtying the
document. Conflicting names and changed native content fail without overwriting.
`get_drawing_info(include=items)` now includes notes and tables; nested table
columns, rows and heights use the same bounded pagination as other collections.
Cold inactive sheets continue to report annotation data as unavailable.

The owned Inventor 2027 fixture exercises general/title/boxed/attached leader notes,
custom/empty tables, supplied styles/layers, repeats/conflicts, table pagination,
read-only rejection, query dirty-state preservation and save/reopen persistence.
It can run only when no Inventor process is present:

```powershell
dotnet build tests/DrawingLive/DrawingLive.csproj -c Release -p:IptMcpSkipDeploy=true
dotnet tests/DrawingLive/bin/Release/net10.0-windows7.0/DrawingLive.dll --run-phase2-owned-session
```

The fixture creates and closes its own Inventor session and only saves generated
models/drawings under a fresh temporary directory. The existing
[Phase 1 fixture](drawing-phase1.md) remains a separate regression check.
