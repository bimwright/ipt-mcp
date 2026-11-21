namespace Bimwright.Ipt.Server;

/// <summary>
/// The <c>Ipt.*</c> script helpers (<c>script_helpers_v1</c>) as listed in the send_code description. The description is
/// fixed before any add-in connects, so it names only what every v1 add-in has; v2 helpers (<c>Ipt.Api</c> …) reach the
/// agent through <c>helper_hint</c>, the add-in's own compile-error hints and <c>Ipt.Help()</c>.
/// </summary>
internal static class SendCodeHelperGuide
{
    internal const string Text =
        "Script helpers Ipt.* (imported; lengths mm, angles deg; lookups return live Inventor objects and throw IptException naming what exists; " +
        "return Ipt.Help() lists every signature) — use them instead of search loops, manual *10 and RangeBox math: " +
        "documents Ipt.Doc(name|path) Open(path, visible=false) Part/Asm/Dwg(name|doc|occ) DefDoc(occ) FileName(x) Def(x) Docs(type?) Active; " +
        "occurrences Ipt.Occs(asm, selector, allowEmpty=false) Occ(asm, selector) Leaves(asm) — selector as inventor_list_occurrences: " +
        "name | names | new { regex, leaf, max_depth, path_contains, file, include_suppressed }; OriginMm TransformMm MoveTo(o,x,y,z) MoveBy SetTransform(o, origin, xAxis, yAxis); " +
        "geometry Ipt.BboxMm(occ|body|face|edge|doc) -> MinX..MaxZ, Min/Max/Center [x,y,z], SizeX..Z; XyzMm VolumeMm3 Pt Pt2 Vec Dir Coll(...) Bodies Faces Edges Overlapping(a, b, gapMm); " +
        "transient B-rep Ipt.Box(cornerA, cornerB as [x,y,z] mm) Cyl Cone Sphere Union Subtract Intersect Moved Rotated AddBody(part, body, name) NewPart(path, bodies); " +
        "references Ipt.Ref(asm, occ, name) WorkPlane/WorkAxis/WorkPoint(scope, name) Face(part, new { kind, normal, extreme, radius_mm, near_mm }); " +
        "drawings Ipt.Sheet(dwg, name|index) Views View(dwgOrSheet, name) ViewLines(view) SheetPt(x,y) " +
        "Table(dwgOrSheet, title|index) TableText(table) Texts(dwgOrSheet) — every sketch text (sheet and view sketches, e.g. sketched balloons), general and leader note with sheet, view, position mm and the live Item; iProperties Ipt.Prop(doc, name) SetProp(doc, name, value, createCustom=false); " +
        "numbers Ipt.Mm Cm Eq IsZero Same Parallel Snap Clean, Ipt.TolMm = 1e-6. " +
        "Every for/foreach/while/do body is time-guarded: when the call's time budget runs out the loop stops with its source line and the transaction rolls back. " +
        "Returned doubles are cleaned (12 significant digits, |x| < 1e-9 -> 0; numbers: \"clean_v1\"); raw_numbers=true keeps them raw. ";
}
