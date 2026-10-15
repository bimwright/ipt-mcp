#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Bimwright.Ipt.Shared.Handlers.IPart;

public sealed class StandardIPartHandler : HandlerBase, IInventorCommand
{
    public string Name { get; }
    public bool IsReadOnly => Name == "get_ipart_info";
    public StandardIPartHandler(string name) { Name = name; }
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly && !IsReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "iPart writes are disabled in read-only mode");
        var effects = new JArray();
        try
        {
            if (Name is "place_ipart_member" or "change_ipart_member") return IPartAssembly.Execute(ctx, Name, p);
            if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out var app, out var part, out var failure)) return failure!;
            if (IsReadOnly) return Ok(ctx, Info(part, p));
            var lockPath = Name == "create_ipart_factory" ? IPartInput.LocalPath(IPartInput.Required(p, "output_path"), ".ipt") : part.FullFileName;
            if (lockPath.Length == 0) throw new ArgumentException("Save the source/factory before using iPart tools");
            using var gate = IPartSupport.Lock(lockPath);
            // Reject genuine nesting without assuming Inventor's idle CurrentTransaction is null.
            var probe = app.TransactionManager.StartTransaction((global::Inventor._Document)(object)part, "MCP: iPart preflight");
            try { RefuseParent(app); } finally { probe.Abort(); }
            if (Name == "create_ipart_factory") return Ok(ctx, Create(app, part, p, effects));
            var state = IPartSupport.Managed(part, true);
            var dryRun = (bool?)p["dry_run"] ?? true;
            return Name switch
            {
                "update_ipart_table" => Ok(ctx, Update(ctx, app, part, state, p, dryRun, effects)),
                "sync_ipart_excel" => Ok(ctx, Sync(ctx, app, part, state, p, dryRun, effects)),
                "set_ipart_row" => Ok(ctx, Select(ctx, app, part, state, IPartInput.Member(IPartInput.Required(p, "member")), effects)),
                "generate_ipart_members" => Ok(ctx, Generate(app, part, state, IPartInput.Members(p["members"]), dryRun, effects)),
                _ => throw new ArgumentException("Unknown iPart command")
            };
        }
        catch (Exception ex)
        {
            var result = Fail(ctx, ex is ArgumentException ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR, ex.Message);
            result.Data = new JObject { ["file_effects"] = effects, ["mutation_applied"] = effects.Count == 0 ? new JValue(false) : new JValue("unknown"),
                ["rolled_back"] = false, ["recovery_required"] = effects.Count == 0 ? null : "Inspect the factory and reported files. Resume the same pending operation; do not assume multi-file writes were rolled back." };
            return result;
        }
    }
    private static JObject Info(PartDocument part, JObject p)
    {
        var limit = (int?)p["max_rows"] ?? 100; var offset = (int?)p["row_offset"] ?? 0;
        if (limit < 1 || limit > 1000 || offset < 0) throw new ArgumentException("max_rows must be 1..1000; row_offset must be nonnegative");
        var cd = part.ComponentDefinition;
        var result = new JObject { ["document"] = part.FullFileName, ["dirty"] = part.Dirty,
            ["kind"] = cd.IsiPartFactory ? (cd.iPartFactory.CustomFactory ? "custom_factory" : "standard_factory") : cd.IsiPartMember ? "member" : "ordinary_part" };
        if (cd.IsiPartMember)
        {
            result["factory_path"] = ((PartDocument)cd.iPartMember.ParentFactory.Parent).FullFileName;
            try { result["member"] = cd.iPartMember.Row.MemberName; }
            catch (COMException) { result["member"] = null; result["row_unavailable"] = true; result["requires_regeneration"] = true; }
            return result;
        }
        if (!cd.IsiPartFactory) return result;
        var f = cd.iPartFactory; var state = IPartSupport.ReadState(part);
        result["managed"] = state != null; result["default_member"] = f.DefaultRow.MemberName; result["member_cache_dir"] = f.MemberCacheDir;
        result["columns"] = new JArray(f.TableColumns.Cast<iPartTableColumn>().Select(c => new JObject { ["heading"] = c.Heading, ["display_heading"] = c.DisplayHeading, ["kind"] = c.ReferencedDataType.ToString() }));
        result["configured_columns"] = state?["columns"]?.DeepClone(); result["excel"] = state?["excel"]?.DeepClone(); result["pending"] = state?["pending"]?.DeepClone();
        result["external_factory_change"] = state != null && (string?)state["factory_sha256"] != IPartInput.FileHash(part.FullFileName);
        if (state?["excel"] is JObject binding)
        { try { result["excel_changed"] = IPartInput.FileHash((string)binding["path"]!) != (string?)binding["source_sha256"]; } catch (IOException) { result["excel_changed"] = true; result["excel_unavailable"] = true; } }
        var rows = new JArray(); var size = 0;
        foreach (var row in f.TableRows.Cast<iPartTableRow>().Skip(offset).Take(limit))
        {
            var data = state is null ? new JObject { ["member"] = row.MemberName, ["values"] = new JObject(f.TableColumns.Cast<iPartTableColumn>().Select(c => new JProperty(c.Heading, row[c.Index].Value))) }
                : IPartSupport.RowData(f, row, (JArray)state["columns"]!);
            if (state != null) { data["generated_current"] = !part.Dirty && state["pending"] == null && IPartSupport.Fresh(part, state, row); data["member_path"] = IPartSupport.CachePath(part, row); }
            var length = data.ToString(Formatting.None).Length;
            if (rows.Count > 0 && size + length > 32000) break;
            rows.Add(data); size += length;
        }
        result["rows"] = rows; result["total_rows"] = f.TableRows.Count;
        result["next_row_offset"] = offset + rows.Count < f.TableRows.Count ? new JValue(offset + rows.Count) : JValue.CreateNull();
        return result;
    }
    private static JObject Create(Application app, PartDocument source, JObject p, JArray effects)
    {
        var output = IPartInput.LocalPath(IPartInput.Required(p, "output_path"), ".ipt");
        var member = IPartInput.Member(IPartInput.Required(p, "base_member")); var columns = IPartInput.Columns(p["columns"]);
        if (source.FullFileName.Length == 0 || source.Dirty) throw new ArgumentException("Source must be a clean saved part; save it explicitly before making a factory copy");
        var cd = source.ComponentDefinition;
        if (cd.IsiPartFactory || cd.IsiPartMember || cd is SheetMetalComponentDefinition || cd.Parameters.ParameterTables.Count > 0)
            throw new ArgumentException("Source must be an ordinary solid part with independent parameters, without native Excel links, iPart or sheet-metal state");
        if (File.Exists(output) || File.Exists(output + IPartSupport.StateSuffix) || Directory.Exists(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output))))
            throw new ArgumentException("Output factory/state/cache already exists; choose a new copy path");
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new ArgumentException("Output directory must already exist");
        var initial = new JObject();
        foreach (JObject c in columns)
        {
            var target = (string)c["target"]!; var kind = (string)c["kind"]!; var name = (string)c["name"]!;
            switch (kind)
            {
                case "parameter":
                    var parameter = cd.Parameters[target]; Convert.ToDouble(parameter.Value);
                    c["units"] = parameter.get_Units(); c["heading"] = parameter.Name; initial[name] = parameter.Expression; break;
                case "suppression":
                    var feature = cd.Features.Cast<PartFeature>().SingleOrDefault(f => f.Name == target) ?? throw new ArgumentException("Feature not found: " + target);
                    c["heading"] = feature.Name; initial[name] = feature.Suppressed ? "Suppress" : "Compute"; break;
                case "material": c["heading"] = "Material"; initial[name] = source.ActiveMaterial.DisplayName; break;
                case "description": c["heading"] = "Description [Project]"; initial[name] = IPartSupport.Property(source, 29).Value?.ToString() ?? ""; break;
            }
        }
        if (columns.Select(c => (string)c["heading"]!).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count) throw new ArgumentException("Configured columns resolve to duplicate native headings");
        effects.Add(new JObject { ["path"] = output, ["action"] = "create_factory_copy_started" }); File.Copy(source.FullFileName, output);
        var part = (PartDocument)app.Documents.Open(output, false);
        var factory = part.ComponentDefinition.CreateFactory();
        object? sheetObject = null, bookObject = null;
        try
        {
            dynamic sheet = sheetObject = factory.ExcelWorkSheet; dynamic book = bookObject = sheet.Parent;
            // Preserve Inventor's reserved Member header, including its filename/default-row tags.
            sheet.Cells[2, 1].Value2 = member; sheet.Cells[2, 2].Value2 = member;
            for (var i = 0; i < columns.Count; i++)
            { sheet.Cells[1, i + 3].Value2 = (string)columns[i]["heading"]!; sheet.Cells[2, i + 3].Value2 = (string)initial[(string)columns[i]["name"]!]!; }
            book.Save(); book.Close(true); Marshal.FinalReleaseComObject(bookObject); bookObject = null;
        }
        finally
        {
            if (bookObject != null) { try { ((dynamic)bookObject).Close(false); } catch { } Marshal.FinalReleaseComObject(bookObject); }
            if (sheetObject != null) Marshal.FinalReleaseComObject(sheetObject);
        }
        foreach (var c in columns.Where(c => (string?)c["kind"] == "material")) factory.MaterialColumn = IPartSupport.Column(factory, (string)c["heading"]!);
        var state = new JObject { ["version"] = 1, ["factory_path"] = output, ["factory_id"] = part.InternalName,
            ["source_path"] = source.FullFileName, ["columns"] = columns, ["generated"] = new JObject() };
        factory.DefaultRow = IPartSupport.Row(factory, member); IPartSupport.Healthy(part); IPartSupport.VerifyActive(part, state, factory.DefaultRow);
        part.Save(); IPartSupport.Finish(part, state); IPartSupport.Managed(part, false);
        effects.Add(new JObject { ["path"] = output, ["action"] = "factory_saved" });
        return new JObject { ["factory_path"] = output, ["state_path"] = output + IPartSupport.StateSuffix, ["member"] = member, ["columns"] = columns.DeepClone(), ["file_effects"] = effects, ["geometry"] = IPartSupport.Geometry(part) };
    }
    private static JArray Plan(PartDocument part, JObject state, JArray rows)
    {
        var factory = part.ComponentDefinition.iPartFactory; var columns = (JArray)state["columns"]!; var changes = new JArray();
        foreach (var input in rows)
        {
            var name = (string)input["member"]!; var native = factory.TableRows.Cast<iPartTableRow>().SingleOrDefault(r => r.MemberName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (native != null && native.MemberName != name) throw new ArgumentException("Member case differs from existing row: " + name);
            var before = native is null ? null : IPartSupport.RowData(factory, native, columns);
            var after = (JObject)input.DeepClone(); var values = (JObject)after["values"]!;
            foreach (var c in columns)
            {
                var key = (string)c["name"]!; var value = (string)values[key]!;
                if ((string?)c["kind"] == "parameter")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) value += " " + (string)c["units"]!;
                    var evaluated = Convert.ToDouble(part.UnitsOfMeasure.GetValueFromExpression(value, (string)c["units"]!));
                    if (double.IsNaN(evaluated) || double.IsInfinity(evaluated)) throw new ArgumentException("Non-finite parameter expression: " + key);
                }
                else if ((string?)c["kind"] == "suppression") value = value == "suppress" ? "Suppress" : "Compute";
                else if ((string?)c["kind"] == "material" && !part.MaterialAssets.Cast<Asset>().Any(a => a.DisplayName == value))
                    throw new ArgumentException("Material must already exist in the factory document: " + value);
                values[key] = value;
            }
            if (before is null || !JToken.DeepEquals(before, after)) changes.Add(new JObject { ["member"] = name, ["action"] = before is null ? "add" : "update", ["before"] = before, ["after"] = after });
        }
        return changes;
    }
    private JObject Update(InventorCommandContext ctx, Application app, PartDocument part, JObject state, JObject p, bool dryRun, JArray effects)
    {
        if (state["excel"] != null) throw new ArgumentException("Excel owns this table; edit the workbook and use inventor_sync_ipart_excel");
        var rows = IPartInput.Rows(p["rows"], (JArray)state["columns"]!);
        return ApplyTable(ctx, app, part, state, rows, dryRun, effects, null);
    }
    private JObject Sync(InventorCommandContext ctx, Application app, PartDocument part, JObject state, JObject p, bool dryRun, JArray effects)
    {
        var binding = state["excel"] as JObject;
        string Source(string key, string? fallback = null) => (string?)p[key] ?? (string?)binding?[key] ?? fallback ?? throw new ArgumentException(key + " is required on the first sync");
        var source = IPartExcelCatalog.Read(Source("path"), Source("sheet"), Source("header_cell", "A1"), p["mapping"] as JObject ?? binding?["mapping"] as JObject, (JArray)state["columns"]!);
        var names = new HashSet<string>(source.Rows.Select(r => (string)r["member"]!), StringComparer.Ordinal);
        var missing = part.ComponentDefinition.iPartFactory.TableRows.Cast<iPartTableRow>().Select(r => r.MemberName).Where(n => !names.Contains(n)).ToArray();
        if (missing.Length > 0) throw new ArgumentException("Catalog omits existing members; row deletion is not automatic: " + string.Join(", ", missing));
        return ApplyTable(ctx, app, part, state, source.Rows, dryRun, effects, source);
    }
    private JObject ApplyTable(InventorCommandContext ctx, Application app, PartDocument part, JObject state, JArray rows, bool dryRun, JArray effects, IPartExcelCatalog? source)
    {
        var changes = Plan(part, state, rows); var receipt = new JObject { ["dry_run"] = dryRun, ["changes"] = changes, ["changed_rows"] = changes.Count, ["members_generated"] = false };
        if (source != null) receipt["excel"] = source.Binding.DeepClone();
        if (dryRun) return receipt;
        IPartSupport.InvalidateExternalChanges(part, state);
        var signature = source is null ? IPartInput.Hash(rows.ToString(Formatting.None)) : IPartInput.Hash(source.Path + "|" + source.Binding["sheet"] + "|" + source.Binding["mapping"]);
        IPartSupport.Begin(part, state, Name, signature);
        effects.Add(new JObject { ["path"] = part.FullFileName + IPartSupport.StateSuffix, ["action"] = "operation_checkpoint" });
        if (changes.Count == 0)
        {
            source?.VerifyUnchanged(); if (source != null) state["excel"] = source.Binding.DeepClone();
            IPartSupport.Finish(part, state); receipt["file_effects"] = effects; return receipt;
        }
        var factory = part.ComponentDefinition.iPartFactory; var selected = factory.DefaultRow.MemberName; var columns = (JArray)state["columns"]!;
        var update = PartMutation.Run(ctx, app, part, Name, () =>
        {
            RefuseParent(app);
            foreach (var change in changes)
            {
                var row = factory.TableRows.Cast<iPartTableRow>().SingleOrDefault(r => r.MemberName == (string)change["member"]!) ?? factory.TableRows[1].Copy((string)change["member"]!);
                var after = change["after"]!; row[IPartSupport.PartNumberColumn(factory).Index].Value = (string)after["part_number"]!;
                foreach (var c in columns) row[IPartSupport.Column(factory, (string)c["heading"]!).Index].Value = (string)after["values"]![(string)c["name"]!]!;
                factory.DefaultRow = row; IPartSupport.Healthy(part); IPartSupport.VerifyActive(part, state, row);
                ((JObject)state["generated"]!).Remove(row.MemberName);
            }
            factory.DefaultRow = IPartSupport.Row(factory, selected); IPartSupport.Healthy(part); IPartSupport.VerifyActive(part, state, factory.DefaultRow);
            source?.VerifyUnchanged(); return Ok(ctx, new JObject());
        });
        if (!update.Ok)
        {
            effects.Add(new JObject { ["action"] = "table_transaction_failed", ["transaction"] = update.Data }); IPartSupport.WriteState(part, state);
            throw new InvalidOperationException(update.Error!.Message + "; factory state remains pending; resume the same table operation after inspecting/restoring a clean factory");
        }
        if (changes.Count > 0) { part.Save(); effects.Add(new JObject { ["path"] = part.FullFileName, ["action"] = "factory_table_saved" }); }
        source?.VerifyUnchanged(); if (source != null) state["excel"] = source.Binding.DeepClone();
        IPartSupport.Finish(part, state); receipt["file_effects"] = effects; return receipt;
    }
    private JObject Select(InventorCommandContext ctx, Application app, PartDocument part, JObject state, string name, JArray effects)
    {
        IPartSupport.CheckSource(state); var factory = part.ComponentDefinition.iPartFactory; var row = IPartSupport.Row(factory, name);
        IPartSupport.InvalidateExternalChanges(part, state); IPartSupport.Begin(part, state, Name, name);
        effects.Add(new JObject { ["path"] = part.FullFileName, ["action"] = "select_row_started" });
        var result = PartMutation.Run(ctx, app, part, Name, () => { RefuseParent(app); factory.DefaultRow = row; IPartSupport.Healthy(part); IPartSupport.VerifyActive(part, state, row); return Ok(ctx, IPartSupport.Geometry(part)); });
        if (!result.Ok) throw new InvalidOperationException(result.Error!.Message);
        part.Save(); IPartSupport.Finish(part, state);
        return new JObject { ["member"] = name, ["geometry"] = result.Data, ["file_effects"] = effects };
    }
    internal static void RefuseParent(Application app)
    { if (app.TransactionManager.CurrentTransaction is { HasParentTransaction: true }) throw new ArgumentException("iPart operations cannot run inside another transaction or batch"); }
    private static JObject Generate(Application app, PartDocument part, JObject state, string[] names, bool dryRun, JArray effects)
    {
        IPartSupport.CheckSource(state); var factory = part.ComponentDefinition.iPartFactory; var selected = factory.DefaultRow.MemberName;
        var rows = names.Select(n => IPartSupport.Row(factory, n)).ToArray();
        var plan = new JArray(rows.Select(r => new JObject { ["member"] = r.MemberName, ["path"] = IPartSupport.CachePath(part, r), ["action"] = IPartSupport.Fresh(part, state, r) ? "reuse" : "generate" }));
        if (dryRun) return new JObject { ["dry_run"] = true, ["members"] = plan };
        if (state["pending"] == null && plan.All(item => (string?)item["action"] == "reuse"))
            return new JObject { ["dry_run"] = false, ["members"] = plan, ["generated_count"] = 0, ["file_effects"] = effects };
        // Preflight every existing target before creating or replacing any file.
        foreach (var item in plan.Where(p => (string?)p["action"] == "generate" && File.Exists((string)p["path"]!)))
        {
            var existing = IPartSupport.OpenPart(app, (string)item["path"]!); var opened = existing == null;
            var member = existing ?? (PartDocument)app.Documents.Open((string)item["path"]!, false);
            // A stale member's Row getter can fail after its factory table changes.
            // Check factory ownership here; verify the exact row after CreateMember.
            try { if (member.Dirty) throw new ArgumentException("An existing member is dirty: " + member.FullFileName); IPartSupport.VerifyMemberFactory(part, member); }
            finally { if (opened) member.Close(true); }
        }
        IPartSupport.InvalidateExternalChanges(part, state); IPartSupport.Begin(part, state, "generate_ipart_members", string.Join("|", names.OrderBy(n => n, StringComparer.Ordinal)));
        effects.Add(new JObject { ["path"] = part.FullFileName + IPartSupport.StateSuffix, ["action"] = "operation_checkpoint" });
        var completed = new JArray(); Exception? error = null;
        try
        {
            foreach (var item in plan)
            {
                var name = (string)item["member"]!; var row = IPartSupport.Row(factory, name); var path = (string)item["path"]!;
                if ((string?)item["action"] == "reuse") { completed.Add(item.DeepClone()); continue; }
                factory.DefaultRow = row; IPartSupport.Healthy(part); IPartSupport.VerifyActive(part, state, row); part.Save();
                state["factory_sha256"] = IPartInput.FileHash(part.FullFileName); IPartSupport.WriteState(part, state);
                effects.Add(new JObject { ["path"] = path, ["member"] = name, ["action"] = "member_generation_started" });
                var alreadyOpen = IPartSupport.OpenPart(app, path) != null;
                var generated = factory.CreateMember(row); var member = (PartDocument)generated.Parent.Document;
                try
                {
                    IPartSupport.VerifyMember(part, member, name); IPartSupport.Healthy(member, false);
                    if (!string.Equals(member.FullFileName, path, StringComparison.OrdinalIgnoreCase) || !IPartSupport.BodiesMatch(app, part, member)) throw new InvalidOperationException("Generated member geometry/path differs from its factory row");
                    if (IPartSupport.Property(member, 5).Value?.ToString() != row[IPartSupport.PartNumberColumn(factory).Index].Value || member.ActiveMaterial.DisplayName != part.ActiveMaterial.DisplayName)
                        throw new InvalidOperationException("Member property/material readback mismatch");
                    member.Save(); var geometry = IPartSupport.Geometry(member);
                    ((JObject)state["generated"]!)[name] = new JObject { ["row_sha256"] = IPartSupport.RowFingerprint(factory, row, (JArray)state["columns"]!), ["file_sha256"] = IPartInput.FileHash(path), ["path"] = path };
                    IPartSupport.WriteState(part, state); completed.Add(new JObject { ["member"] = name, ["path"] = path, ["action"] = "generated", ["geometry"] = geometry });
                }
                finally { if (!alreadyOpen) member.Close(true); }
            }
        }
        catch (Exception ex) { error = ex; }
        finally
        {
            try { factory.DefaultRow = IPartSupport.Row(factory, selected); IPartSupport.Healthy(part); part.Save(); state["factory_sha256"] = IPartInput.FileHash(part.FullFileName); IPartSupport.WriteState(part, state); }
            catch (Exception restore) { error = new InvalidOperationException((error?.Message ?? "") + "; factory row restore failed: " + restore.Message); }
        }
        if (error != null) throw new InvalidOperationException(error.Message + "; completed member count=" + completed.Count + "; resume generate_ipart_members after recovery");
        IPartSupport.CheckSource(state); IPartSupport.Finish(part, state);
        return new JObject { ["dry_run"] = false, ["members"] = completed, ["generated_count"] = completed.Count(x => (string?)x["action"] == "generated"), ["file_effects"] = effects };
    }
}
#endif
