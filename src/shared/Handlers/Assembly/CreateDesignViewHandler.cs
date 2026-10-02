using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
#if INVENTOR2027
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Formatting = Newtonsoft.Json.Formatting;
using Inventor;
using Bimwright.Ipt.Shared.Handlers.Drawing;
using Newtonsoft.Json;
#endif

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class CreateDesignViewHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_design_view";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Design view writes are disabled in read-only mode.");
        try
        {
            DrawingPhase3Input.Validate(Name, p);
#if INVENTOR2027
            if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, Name, out var app, out var assembly, out var failure)) return failure!;
            if (app.ActiveDocument == null || !app.ActiveDocument.Equals(assembly) || !assembly.Equals(app.ActiveEditObject)) throw new ArgumentException("Activate the target assembly and exit the current edit environment before creating a design view.");
            return Create(ctx, app, assembly, p);
#else
            return Fail(ctx, InventorErrorCodes.UNSUPPORTED_HOST, "create_design_view requires Inventor 2027.");
#endif
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (NotSupportedException ex) { return Fail(ctx, InventorErrorCodes.UNSUPPORTED_HOST, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

#if INVENTOR2027
    private static Asset ResolveAsset(Application app, AssemblyDocument document, string name)
    {
        bool Match(Asset asset) => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || asset.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase);
        var local = document.AppearanceAssets.Cast<Asset>().Where(Match).ToArray();
        if (local.Length == 1) return local[0];
        if (local.Length > 1) throw new ArgumentException("Appearance asset is ambiguous in the document: " + name);
        var library = app.AssetLibraries.Cast<AssetLibrary>().SelectMany(x => x.AppearanceAssets.Cast<Asset>()).Where(Match).ToArray();
        if (library.Length != 1) throw new ArgumentException("Appearance asset must resolve uniquely in the document or asset libraries: " + name);
        return library[0];
    }

    private static JObject OccurrenceState(ComponentOccurrence occurrence) => new JObject { ["visible"] = occurrence.Visible, ["asset"] = occurrence.Appearance.Name, ["appearance_source"] = occurrence.AppearanceSourceType.ToString(), ["associative_design_view"] = occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject && occurrence.IsAssociativeToDesignViewRepresentation };

    private static string DesignViewXml(DesignViewRepresentation representation)
    {
        // DesignViewInfo can contain several representations. Fingerprint only this named view.
        var document = new XmlDocument { XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(representation.DesignViewInfo), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        document.Load(reader);
        var views = document.SelectNodes("//*[local-name()='DesignView']")!.Cast<XmlElement>().Where(view => view.GetAttribute("name") == representation.Name).ToArray();
        if (views.Length != 1) throw new ArgumentException("Native XML must identify one design view: " + representation.Name);
        return views[0].OuterXml;
    }

    private static InventorCommandResult Create(InventorCommandContext ctx, Application app, AssemblyDocument assembly, JObject p)
    {
        var manager = assembly.ComponentDefinition.RepresentationsManager; var original = manager.ActiveDesignViewRepresentation;
        var representations = manager.DesignViewRepresentations.Cast<DesignViewRepresentation>().ToArray();
        var name = p.Value<string>("name")!;
        var sourceName = p.Value<string>("source");
        var sources = sourceName == null ? new[] { original } : representations.Where(x => x.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sources.Length != 1) throw new ArgumentException("source must resolve to one existing design view representation.");
        var source = sources[0];
        var visibility = new List<(string path, ComponentOccurrence occurrence, bool visible)>();
        var appearances = new List<(string path, ComponentOccurrence occurrence, Asset asset)>();
        foreach (JObject entry in p["occurrence_visibility"] as JArray ?? new JArray())
        {
            var path = entry.Value<string>("occurrence_path")!; var occurrence = DrawingGeometry.Occurrence(assembly, path);
            if (occurrence.Suppressed) throw new ArgumentException("Cannot set visibility on a suppressed occurrence: " + path);
            visibility.Add((path, occurrence, entry.Value<bool>("visible")));
        }
        foreach (JObject entry in p["appearance"] as JArray ?? new JArray())
        {
            var path = entry.Value<string>("occurrence_path")!; var occurrence = DrawingGeometry.Occurrence(assembly, path);
            if (occurrence.Suppressed) throw new ArgumentException("Cannot set appearance on a suppressed occurrence: " + path);
            appearances.Add((path, occurrence, ResolveAsset(app, assembly, entry.Value<string>("asset")!)));
        }
        var signatureInput = (JObject)p.DeepClone(); signatureInput.Remove("activate");
        var existing = representations.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (existing.Length > 1) throw new ArgumentException("Design view name is ambiguous.");
        var reuse = existing.Length == 1;
        if (reuse)
        {
            DrawingSupport.Existing(existing[0].AttributeSets, signatureInput);
            if (DrawingSupport.Read(existing[0].AttributeSets, "native_hash") != DrawingGeometry.Hash(DesignViewXml(existing[0]))) throw new ArgumentException("Managed design view was edited. Choose a new name or edit the representation explicitly.");
            // Reuse without activation is a readback-only path, including when the source was omitted.
            if (p.Value<bool?>("activate") != true) return Ok(ctx, new JObject { ["created"] = false, ["existing"] = true, ["created_count"] = 0, ["changed_count"] = 0, ["name"] = existing[0].Name, ["source"] = DrawingSupport.Read(existing[0].AttributeSets, "source"), ["document"] = assembly.DisplayName, ["dirty"] = assembly.Dirty, ["active_design_view"] = original.Name, ["native_hash"] = DrawingGeometry.Hash(DesignViewXml(existing[0])), ["items"] = JArray.Parse(DrawingSupport.Read(existing[0].AttributeSets, "settings") ?? "[]"), ["readback_mode"] = "unchanged_native_hash", ["document_unchanged"] = true, ["source_unchanged"] = true, ["saved"] = false });
        }
        var affectedSet = new HashSet<ComponentOccurrence>();
        foreach (var target in visibility.Select(x => x.occurrence).Concat(appearances.Select(x => x.occurrence)))
        {
            var occurrence = target;
            while (occurrence != null) { affectedSet.Add(occurrence); occurrence = occurrence.ParentOccurrence; }
        }
        var affected = affectedSet.ToArray();
        var originalState = affected.Select(OccurrenceState).ToArray();
        var originalXml = DesignViewXml(original); var sourceXml = DesignViewXml(source);
        var originalAutoCamera = original.AutoSaveCamera; var sourceAutoCamera = source.AutoSaveCamera;
        Transaction? transaction = null; DesignViewRepresentation? created = null;
        try
        {
            transaction = app.TransactionManager.StartTransaction((_Document)(object)assembly, "MCP: create_design_view");
            // Prevent temporary activation from rewriting the camera of the source/current view.
            original.AutoSaveCamera = false; if (!source.Equals(original)) source.AutoSaveCamera = false;
            created = reuse ? existing[0] : source.Copy(name);
            if (!reuse) { created.Locked = false; created.AutoSaveCamera = false; }
            created.Activate();
            if (!reuse)
            {
                foreach (var group in visibility.GroupBy(x => x.visible))
                {
                    var collection = app.TransientObjects.CreateObjectCollection(); foreach (var item in group) collection.Add(item.occurrence);
                    created.SetVisibilityOfOccurrences(collection, group.Key);
                }
                var copiedAssets = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in appearances)
                {
                    if (!copiedAssets.TryGetValue(item.asset.Name, out var asset))
                    {
                        asset = assembly.AppearanceAssets.Cast<Asset>().SingleOrDefault(x => x.Name == item.asset.Name) ?? item.asset.CopyTo((global::Inventor.Document)(object)assembly, Type.Missing);
                        copiedAssets.Add(item.asset.Name, asset);
                    }
                    item.occurrence.Appearance = asset;
                }
                assembly.Update();
            }
            var rows = new JArray();
            foreach (var item in visibility) { if (item.occurrence.Visible != item.visible) throw new ArgumentException("Visibility readback failed: " + item.path); rows.Add(new JObject { ["occurrence_path"] = item.path, ["visible"] = item.occurrence.Visible }); }
            foreach (var item in appearances) { if (item.occurrence.Appearance.Name != item.asset.Name) throw new ArgumentException("Appearance readback failed: " + item.path); rows.Add(new JObject { ["occurrence_path"] = item.path, ["asset"] = item.occurrence.Appearance.DisplayName, ["asset_id"] = item.occurrence.Appearance.Name, ["appearance_source"] = item.occurrence.AppearanceSourceType.ToString() }); }
            // Native representations and original occurrence overrides must survive restoring the current view.
            if (!created.Equals(original)) original.Activate();
            if (DesignViewXml(original) != originalXml || DesignViewXml(source) != sourceXml || affected.Where((occurrence, index) => !JToken.DeepEquals(OccurrenceState(occurrence), originalState[index])).Any()) throw new ArgumentException("Source/current representation changed during design view creation; transaction will be aborted.");
            created.Activate();
            if (visibility.Any(item => item.occurrence.Visible != item.visible) || appearances.Any(item => item.occurrence.Appearance.Name != item.asset.Name)) throw new ArgumentException("Design view settings did not persist across representation activation.");
            if (p.Value<bool?>("activate") != true && !created.Equals(original)) original.Activate();
            original.AutoSaveCamera = originalAutoCamera; if (!source.Equals(original)) source.AutoSaveCamera = sourceAutoCamera;
            if (DesignViewXml(original) != originalXml || DesignViewXml(source) != sourceXml) throw new ArgumentException("Source/current native representation changed during final activation.");
            var nativeHash = DrawingGeometry.Hash(DesignViewXml(created));
            if (reuse && DrawingSupport.Read(created.AttributeSets, "native_hash") != nativeHash) throw new ArgumentException("Existing design view changed during activation; inspect the representation before retrying.");
            if (!reuse) { DrawingSupport.Mark(created.AttributeSets, name, signatureInput); DrawingSupport.Write(created.AttributeSets, "native_hash", nativeHash); DrawingSupport.Write(created.AttributeSets, "source", source.Name); DrawingSupport.Write(created.AttributeSets, "settings", rows.ToString(Formatting.None)); }
            var data = new JObject { ["created"] = !reuse, ["existing"] = reuse, ["created_count"] = reuse ? 0 : 1, ["changed_count"] = reuse ? 0 : rows.Count, ["name"] = created.Name, ["source"] = source.Name, ["document"] = assembly.DisplayName, ["dirty"] = assembly.Dirty, ["active_design_view"] = manager.ActiveDesignViewRepresentation.Name, ["source_unchanged"] = true, ["active_representation_restored"] = p.Value<bool?>("activate") != true, ["native_hash"] = nativeHash, ["items"] = rows, ["saved"] = false };
            transaction.End(); transaction = null; return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            var aborted = false; string? abortError = null; string? restoreError = null;
            try { transaction?.Abort(); aborted = transaction != null; } catch (Exception rollback) { abortError = rollback.Message; }
            try { original.AutoSaveCamera = originalAutoCamera; if (!source.Equals(original)) source.AutoSaveCamera = sourceAutoCamera; original.Activate(); } catch (Exception restore) { restoreError = restore.Message; }
            var result = Fail(ctx, ex is ArgumentException ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR, ex.Message);
            result.Data = new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = result.Error!.Code, ["message"] = ex.Message }, ["rolled_back"] = aborted, ["mutation_applied"] = aborted ? new JValue(false) : JValue.CreateNull(), ["abort_error"] = abortError, ["restore_error"] = restoreError, ["ui_restored"] = restoreError == null, ["readback_required"] = !aborted || restoreError != null };
            return result;
        }
    }
#endif
}
