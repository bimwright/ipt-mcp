namespace Bimwright.Ipt.Server;

/// <summary>
/// The <c>Ipt.*</c> script helpers (<c>script_helpers_v1</c>) as listed on the send_code <c>code</c> parameter.
/// The list is fixed before any add-in connects, so it names only what every v1 add-in has; v2 helpers
/// (<c>Ipt.Api</c> …) reach the agent through <c>helper_hint</c>, the add-in's own compile-error hints and
/// <c>Ipt.Help()</c>.
/// </summary>
internal static class SendCodeHelperGuide
{
    internal const string Text =
        "Ipt.* helpers (imported; mm, deg; lookups return live objects or throw IptException listing what exists; return Ipt.Help() for all signatures): " +
        "docs Ipt.Doc(name|path) Open(path, visible=false) Part/Asm/Dwg(x) DefDoc(occ) FileName(x) Def(x) Docs(type?) Active; " +
        "occs Ipt.Occs(asm, sel, allowEmpty=false) Occ(asm, sel) Leaves(asm) — sel: name|names|new{regex,leaf,max_depth,path_contains,file,include_suppressed}; " +
        "OriginMm TransformMm MoveTo(o,x,y,z) MoveBy SetTransform(o, origin, xAxis, yAxis); " +
        "geom Ipt.BboxMm(x)->MinX..MaxZ, Min/Max/Center[x,y,z], SizeX..Z; XyzMm VolumeMm3 Pt Pt2 Vec Dir Coll(...) Bodies Faces Edges Overlapping(a,b,gapMm); " +
        "transient B-rep Ipt.Box(a,b mm) Cyl Cone Sphere Union Subtract Intersect Moved Rotated AddBody(part,body,name) NewPart(path,bodies); " +
        "refs Ipt.Ref(asm,occ,name) WorkPlane/WorkAxis/WorkPoint(scope,name) Face(part, new{kind,normal,extreme,radius_mm,near_mm}); " +
        "drawings Ipt.Sheet(dwg,name|idx) Views View(x,name) ViewLines(view) SheetPt(x,y) Table(x,title|idx) TableText(t) " +
        "Texts(dwgOrSheet) (sketch texts incl. sketched balloons, general & leader notes with sheet/view/pos mm + Item); " +
        "iProps Ipt.Prop(doc,name) SetProp(doc,name,val,createCustom=false); " +
        "nums Ipt.Mm Cm Eq IsZero Same Parallel Snap Clean, Ipt.TolMm=1e-6. ";
}
