#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Bimwright.Ipt.Shared.Handlers.IPart;

internal static class IPartSupport
{
    internal const string StateSuffix = ".ipart-mcp.json";
    internal const string PropertySetId = "{32853F0F-3444-11D1-9E93-0060B03C1CA6}";
    internal static Inventor.Property Property(PartDocument part, int id)
        => part.PropertySets[PropertySetId].Cast<Inventor.Property>().Single(p => p.PropId == id);
    internal static IDisposable Lock(string path)
    {
        var mutex = new Mutex(false, "Local\\BimwrightStandardIPart_" + IPartInput.Hash(Path.GetFullPath(path).ToUpperInvariant()));
        bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new ArgumentException("Another iPart operation is active on this factory"); }
        return new Gate(mutex);
    }
    private sealed class Gate : IDisposable
    { private readonly Mutex _mutex; internal Gate(Mutex mutex) { _mutex = mutex; } public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); } }
    internal static JObject? ReadState(PartDocument part)
    {
        var path = part.FullFileName + StateSuffix;
        if (part.FullFileName.Length == 0 || !File.Exists(path)) return null;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new ArgumentException("iPart state exceeds 8 MiB");
        var state = JObject.Parse(File.ReadAllText(path));
        if ((int?)state["version"] != 1 || !string.Equals((string?)state["factory_path"], part.FullFileName, StringComparison.OrdinalIgnoreCase)
            || (string?)state["factory_id"] != part.InternalName) throw new ArgumentException("iPart state belongs to another factory or is unsupported");
        return state;
    }
    internal static void WriteState(PartDocument part, JObject state)
    {
        var path = part.FullFileName + StateSuffix; var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, state.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static JObject Managed(PartDocument part, bool write)
    {
        var cd = part.ComponentDefinition;
        if (!cd.IsiPartFactory || cd.iPartFactory.CustomFactory || cd is SheetMetalComponentDefinition)
            throw new ArgumentException("A managed Standard iPart factory is required; custom/sheet-metal factories are not supported for writes");
        var state = ReadState(part) ?? throw new ArgumentException("Factory has no managed state; create a factory copy with inventor_create_ipart_factory");
        if (write && (part.Dirty || !part.IsModifiable)) throw new ArgumentException("Factory must be clean, saved and writable before this operation");
        if (state["columns"] is not JArray columns) throw new ArgumentException("Factory schema is missing");
        var f = cd.iPartFactory;
        var expectedHeadings = new HashSet<string>(columns.Select(c => (string)c["heading"]!), StringComparer.Ordinal)
        { MemberColumn(f).Heading, PartNumberColumn(f).Heading };
        if (f.TableColumns.Count != expectedHeadings.Count || !expectedHeadings.SetEquals(f.TableColumns.Cast<iPartTableColumn>().Select(c => c.Heading)))
            throw new ArgumentException("Native iPart schema changed; restore the configured columns before writing");
        if (f.FileNameColumn.Heading != MemberColumn(f).Heading) throw new ArgumentException("Managed factories require member-based file names");
        foreach (var c in columns)
        {
            var native = Column(f, (string)c["heading"]!); var target = (string)c["target"]!; var kind = (string)c["kind"]!;
            if (native.CustomColumn || (kind == "parameter" && (native.ReferencedObject is not Inventor.Parameter p || p.Name != target))
                || (kind == "suppression" && (native.ReferencedObject is not PartFeature feature || feature.Name != target))
                || (kind == "description" && (native.ReferencedObject is not Inventor.Property prop || prop.PropId != 29))
                || (kind == "material" && f.MaterialColumn.Heading != native.Heading)) throw new ArgumentException("Column no longer targets its configured object: " + c["name"]);
        }
        foreach (iPartTableRow row in f.TableRows)
            foreach (iPartTableColumn column in f.TableColumns)
                if (row[column.Index].CustomCell || row[column.Index].HasFormula) throw new ArgumentException("Custom or Excel-formula cells inside the native iPart table are not supported; use the external catalog");
        return state;
    }
    internal static iPartTableColumn MemberColumn(iPartFactory f)
        => f.TableColumns.Cast<iPartTableColumn>().Single(c => c.ReferencedDataType.ToString() == "kMemberNameColumn");
    internal static iPartTableColumn PartNumberColumn(iPartFactory f)
        => f.TableColumns.Cast<iPartTableColumn>().Single(c => c.ReferencedDataType.ToString() == "kFilePropertyColumn" && c.ReferencedObject is Inventor.Property p && p.PropId == 5);
    internal static iPartTableColumn Column(iPartFactory f, string heading)
        => f.TableColumns.Cast<iPartTableColumn>().Single(c => c.Heading == heading);
    internal static iPartTableRow Row(iPartFactory f, string member)
        => f.TableRows.Cast<iPartTableRow>().SingleOrDefault(r => r.MemberName == member) ?? throw new ArgumentException("Member not found: " + member);
    internal static JObject RowData(iPartFactory f, iPartTableRow row, JArray columns)
        => new() { ["member"] = row.MemberName, ["part_number"] = row[PartNumberColumn(f).Index].Value,
            ["values"] = new JObject(columns.Select(c => new JProperty((string)c["name"]!, row[Column(f, (string)c["heading"]!).Index].Value))) };
    internal static string RowFingerprint(iPartFactory f, iPartTableRow row, JArray columns)
        => IPartInput.Hash(RowData(f, row, columns).ToString(Formatting.None));
    internal static string CachePath(PartDocument part, iPartTableRow row)
    {
        var factory = part.ComponentDefinition.iPartFactory; IPartInput.Member(row.MemberName);
        var cache = Path.GetFullPath(factory.MemberCacheDir);
        var expected = Path.Combine(Path.GetDirectoryName(part.FullFileName)!, Path.GetFileNameWithoutExtension(part.FullFileName));
        if (!string.Equals(cache.TrimEnd('\\', '/'), expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Shared/proxy member caches are not supported; use a factory copy in its own folder");
        var file = row.PartName;
        if (file.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 4);
        IPartInput.Member(file);
        if (!string.Equals(file, row.MemberName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Member/file naming drift: " + row.MemberName);
        return Path.Combine(cache, file + ".ipt");
    }
    internal static void CheckSource(JObject state)
    {
        if (state["excel"] is JObject binding && IPartInput.FileHash((string)binding["path"]!) != (string?)binding["source_sha256"])
            throw new ArgumentException("Excel source changed; run inventor_sync_ipart_excel before selecting or generating members");
    }
    internal static bool Fresh(PartDocument part, JObject state, iPartTableRow row)
    {
        var path = CachePath(part, row);
        var record = state["generated"]?[row.MemberName];
        return record != null && (string?)state["factory_sha256"] == IPartInput.FileHash(part.FullFileName)
            && (string?)record["row_sha256"] == RowFingerprint(part.ComponentDefinition.iPartFactory, row, (JArray)state["columns"]!)
            && File.Exists(path) && (string?)record["file_sha256"] == IPartInput.FileHash(path);
    }
    internal static void InvalidateExternalChanges(PartDocument part, JObject state)
    {
        if ((string?)state["factory_sha256"] != IPartInput.FileHash(part.FullFileName)) state["generated"] = new JObject();
    }
    internal static void Begin(PartDocument part, JObject state, string operation, string signature)
    {
        if (state["pending"] is JObject pending && ((string?)pending["operation"] != operation || (string?)pending["signature"] != signature))
            throw new ArgumentException("An interrupted operation must be resumed first: " + pending["operation"]);
        state["pending"] = new JObject { ["operation"] = operation, ["signature"] = signature };
        WriteState(part, state);
    }
    internal static void Finish(PartDocument part, JObject state)
    { state.Remove("pending"); state["factory_sha256"] = IPartInput.FileHash(part.FullFileName); WriteState(part, state); }
    internal static JObject Geometry(PartDocument part)
    {
        var cd = part.ComponentDefinition; var box = cd.RangeBox;
        // Computing a missing mass cache otherwise dirties even an untouched member.
        var mass = cd.MassProperties; var cache = mass.CacheResultsOnCompute; double volume;
        try { mass.CacheResultsOnCompute = false; volume = mass.Volume * 1000; }
        finally { mass.CacheResultsOnCompute = cache; }
        return new JObject { ["solid_count"] = cd.SurfaceBodies.Cast<SurfaceBody>().Count(b => b.IsSolid), ["volume_mm3"] = volume,
            ["bbox_min_mm"] = new JArray(box.MinPoint.X * 10, box.MinPoint.Y * 10, box.MinPoint.Z * 10),
            ["bbox_max_mm"] = new JArray(box.MaxPoint.X * 10, box.MaxPoint.Y * 10, box.MaxPoint.Z * 10),
            ["material"] = part.ActiveMaterial.DisplayName, ["part_number"] = Property(part, 5).Value?.ToString(), ["description"] = Property(part, 29).Value?.ToString() };
    }
    internal static void Healthy(PartDocument part, bool rebuild = true)
    {
        if (!(rebuild ? part.Rebuild2(false) : part.Update2(false))) throw new InvalidOperationException("iPart recompute failed");
        foreach (PartFeature feature in part.ComponentDefinition.Features)
            if (!feature.Suppressed && feature.HealthStatus != HealthStatusEnum.kUpToDateHealth) throw new InvalidOperationException("Unhealthy feature: " + feature.Name);
        foreach (PlanarSketch sketch in part.ComponentDefinition.Sketches)
            if (sketch.HealthStatus != HealthStatusEnum.kUpToDateHealth && sketch.HealthStatus != HealthStatusEnum.kSuppressedHealth) throw new InvalidOperationException("Unhealthy sketch: " + sketch.Name);
        if (!part.ComponentDefinition.SurfaceBodies.Cast<SurfaceBody>().Any(b => b.IsSolid)) throw new InvalidOperationException("A generated Standard iPart must contain a solid body");
    }
    internal static void VerifyActive(PartDocument part, JObject state, iPartTableRow row)
    {
        var f = part.ComponentDefinition.iPartFactory;
        if (f.DefaultRow.MemberName != row.MemberName) throw new InvalidOperationException("Wrong active iPart row");
        foreach (var c in (JArray)state["columns"]!)
        {
            var cell = row[Column(f, (string)c["heading"]!).Index];
            if (!cell.IsValid) throw new ArgumentException("Invalid native table cell: " + row.MemberName + "/" + c["name"]);
            var value = cell.Value;
            switch ((string)c["kind"]!)
            {
                case "parameter":
                    var parameter = part.ComponentDefinition.Parameters[(string)c["target"]!];
                    var expected = Convert.ToDouble(part.UnitsOfMeasure.GetValueFromExpression(value, (string)c["units"]!));
                    var actual = Convert.ToDouble(parameter.Value);
                    if (double.IsNaN(expected) || double.IsInfinity(expected) || Math.Abs(actual - expected) > Math.Max(1e-8, Math.Abs(expected) * 1e-8)) throw new InvalidOperationException("Parameter readback mismatch: " + c["name"]);
                    break;
                case "suppression":
                    var feature = part.ComponentDefinition.Features.Cast<PartFeature>().Single(x => x.Name == (string)c["target"]!);
                    if (feature.Suppressed != value.Equals("Suppress", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Feature suppression readback mismatch: " + feature.Name);
                    break;
                case "material": if (part.ActiveMaterial.DisplayName != value) throw new InvalidOperationException("Material readback mismatch"); break;
                case "description": if (Property(part, 29).Value?.ToString() != value) throw new InvalidOperationException("Description readback mismatch"); break;
            }
        }
        if (Property(part, 5).Value?.ToString() != row[PartNumberColumn(f).Index].Value) throw new InvalidOperationException("Part Number readback mismatch");
    }
    internal static bool BodiesMatch(Application app, PartDocument left, PartDocument right)
    {
        if (!BodiesMatch(app, left.ComponentDefinition.SurfaceBodies.Cast<SurfaceBody>().ToArray(), right)) return false;
        var l = Geometry(left); var r = Geometry(right);
        foreach (var key in new[] { "bbox_min_mm", "bbox_max_mm" })
            for (var i = 0; i < 3; i++) if (Math.Abs((double)l[key]![i]! - (double)r[key]![i]!) > 1e-5) return false;
        return Math.Abs((double)l["volume_mm3"]! - (double)r["volume_mm3"]!) < Math.Max(1e-4, (double)l["volume_mm3"]! * 1e-8);
    }
    internal static bool BodiesMatch(Application app, SurfaceBody[] a, PartDocument right)
    {
        var remaining = right.ComponentDefinition.SurfaceBodies.Cast<SurfaceBody>().ToList();
        if (a.Length != remaining.Count) return false;
        foreach (var body in a)
        {
            var index = remaining.FindIndex(other =>
            {
                var input = app.TransientObjects.CreateObjectCollection(); input.Add(body); input.Add(other);
                var options = app.TransientObjects.CreateNameValueMap(); options.Add("MatchTopology", true); options.Add("MatchReflection", false); options.Add("Tolerance", 1e-8);
                var groups = app.TransientBRep.GetIdenticalBodies(input, options);
                return groups.Count == 1 && ((ObjectCollection)groups[1]).Count == 2;
            });
            if (index < 0) return false; remaining.RemoveAt(index);
        }
        return true;
    }
    internal static void VerifyMember(PartDocument factory, PartDocument member, string name)
    {
        VerifyMemberFactory(factory, member);
        if (member.ComponentDefinition.iPartMember.Row.MemberName != name)
            throw new ArgumentException("Existing file is not this factory's member: " + name);
    }
    internal static void VerifyMemberFactory(PartDocument factory, PartDocument member)
    {
        if (!member.ComponentDefinition.IsiPartMember || member.ComponentDefinition.iPartMember.CustomMember
            || !string.Equals(((PartDocument)member.ComponentDefinition.iPartMember.ParentFactory.Parent).FullFileName, factory.FullFileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Existing file is not a Standard member of this factory: " + member.FullFileName);
    }
    internal static PartDocument? OpenPart(Application app, string path)
        => app.Documents.Cast<global::Inventor.Document>().OfType<PartDocument>().SingleOrDefault(p => string.Equals(p.FullFileName, path, StringComparison.OrdinalIgnoreCase));
}
#endif
