#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

/// <summary>
/// Shared target/asset resolution for <c>set_appearance</c> / <c>reset_appearance</c> (E7).
/// Target = <c>occurrences</c> (selector, overrides in the assembly's context) or a part
/// <c>document</c> (optionally narrowed to <c>bodies</c> by name or 'body:N').
/// </summary>
internal static class AppearanceSupport
{
    public sealed class Target
    {
        public global::Inventor.Document Doc { get; init; } = null!;
        public List<OccurrenceSelection.Item> Occurrences { get; init; } = new();
        public List<SurfaceBody> Bodies { get; init; } = new();
        public PartDocument? WholePart { get; init; }
    }

    public static bool TryTarget(InventorCommandContext ctx, JObject p, string command, out Target target, out InventorCommandResult? failure)
    {
        target = null!;
        failure = null;
        var hasOcc = p["occurrences"] is { Type: not JTokenType.Null };
        var hasBodies = p["bodies"] is { Type: not JTokenType.Null };
        if (hasOcc && hasBodies)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.INVALID_ARGUMENT, "use either occurrences (assembly) or document/bodies (part), not both");
            return false;
        }
        if (hasOcc)
        {
            if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, command, out _, out var asm, out failure)) return false;
            if (!OccurrenceSelection.TryResolve(asm.ComponentDefinition, p["occurrences"], "occurrences", out var items, out var error, defaultLimit: 5000))
            {
                failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);
                return false;
            }
            target = new Target { Doc = (global::Inventor.Document)(object)asm, Occurrences = items };
            return true;
        }

        if (!ActiveDocumentSupport.TryGetPart(ctx, p, command, out _, out var part, out failure)) return false;
        if (!hasBodies)
        {
            target = new Target { Doc = (global::Inventor.Document)(object)part, WholePart = part };
            return true;
        }
        var wanted = p["bodies"] is JArray arr ? arr.Select(t => (string?)t ?? "").ToList() : new List<string> { (string?)p["bodies"] ?? "" };
        var bodies = new List<SurfaceBody>();
        var all = part.ComponentDefinition.SurfaceBodies;
        foreach (var w in wanted)
        {
            SurfaceBody? hit = null;
            if (w.StartsWith("body:", StringComparison.OrdinalIgnoreCase) && int.TryParse(w.Substring(5), out var idx) && idx >= 1 && idx <= all.Count)
                hit = all[idx];
            else
                foreach (SurfaceBody b in all) if (string.Equals(b.Name, w, StringComparison.OrdinalIgnoreCase)) { hit = b; break; }
            if (hit is null)
            {
                var names = new List<string>();
                foreach (SurfaceBody b in all) names.Add(b.Name);
                failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    $"body '{w}' not found. Bodies: {string.Join(", ", names)} (or body:1..{all.Count})");
                return false;
            }
            bodies.Add(hit);
        }
        target = new Target { Doc = (global::Inventor.Document)(object)part, Bodies = bodies };
        return true;
    }

    /// <summary>The document-local appearance asset for rgb or a named (document / library) appearance.</summary>
    public static bool TryAsset(Application app, global::Inventor.Document doc, JObject p, out Asset asset, out string? error)
    {
        asset = null!;
        error = null;
        var assets = doc switch
        {
            AssemblyDocument a => a.Assets,
            PartDocument pd => pd.Assets,
            _ => null,
        };
        if (assets is null) { error = "appearances need a part or assembly document"; return false; }

        if (p["rgb"] is { Type: not JTokenType.Null } rgbTok)
        {
            if (p["asset"] is { Type: not JTokenType.Null }) { error = "use either rgb or asset, not both"; return false; }
            if (rgbTok is not JArray rgb || rgb.Count != 3 || rgb.Any(t => t.Type != JTokenType.Integer || (int)t < 0 || (int)t > 255))
            { error = "rgb must be [r,g,b] with 0..255 integers"; return false; }
            double opacity = p["opacity"]?.Type is JTokenType.Float or JTokenType.Integer ? (double)p["opacity"]! : 1.0;
            if (opacity < 0 || opacity > 1) { error = "opacity must be 0..1"; return false; }
            var name = (string?)p["name"];
            if (string.IsNullOrWhiteSpace(name)) name = $"MCP-RGB-{(int)rgb[0]}-{(int)rgb[1]}-{(int)rgb[2]}" + (opacity < 1 ? $"-A{(int)Math.Round(opacity * 100)}" : "");
            Asset? a = null;
            try { a = assets[name]; } catch { }
            if (a is null) a = assets.Add(AssetTypeEnum.kAssetTypeAppearance, "Generic", name, name);
            ((ColorAssetValue)a["generic_diffuse"]).Value = app.TransientObjects.CreateColor((byte)(int)rgb[0], (byte)(int)rgb[1], (byte)(int)rgb[2], opacity);
            asset = a;
            return true;
        }

        var wanted = (string?)p["asset"];
        if (string.IsNullOrWhiteSpace(wanted)) { error = "pass rgb [r,g,b] or asset (appearance name)"; return false; }
        var local = doc switch { AssemblyDocument a => a.AppearanceAssets, PartDocument pd => pd.AppearanceAssets, _ => null };
        foreach (Asset a in local!)
            if (Same(a, wanted!)) { asset = a; return true; }
        var near = new List<string>();
        foreach (AssetLibrary lib in app.AssetLibraries)
        {
            AssetsEnumerator libAssets;
            try { libAssets = lib.AppearanceAssets; } catch { continue; }
            foreach (Asset a in libAssets)
            {
                if (Same(a, wanted!)) { asset = a.CopyTo(doc, Type.Missing); return true; }
                if (near.Count < 8 && !near.Contains(a.DisplayName) && a.DisplayName.IndexOf(wanted!, StringComparison.OrdinalIgnoreCase) >= 0)
                    near.Add(a.DisplayName);
            }
        }
        error = $"appearance '{wanted}' not found in the document or the appearance libraries."
                + (near.Count > 0 ? " Similar: " + string.Join(", ", near) : " Use rgb [r,g,b] for a plain color.");
        return false;
    }

    private static bool Same(Asset a, string name)
    {
        try { return string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

/// <summary><c>set_appearance</c> (E7) — color / appearance overrides on occurrences (selector) or part bodies, one undo step.</summary>
public sealed class SetAppearanceHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_appearance";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (!AppearanceSupport.TryTarget(ctx, p, Name, out var target, out var failure)) return failure!;
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)target.Doc, "MCP: set_appearance");
            if (!AppearanceSupport.TryAsset(app, target.Doc, p, out var asset, out var error))
            {
                tx.Abort(); tx = null;
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, error!);
            }
            var count = 0;
            foreach (var it in target.Occurrences) { it.Occurrence.Appearance = asset; count++; }
            foreach (var b in target.Bodies) { b.Appearance = asset; count++; }
            if (target.WholePart is { } part) { part.ActiveAppearance = asset; count = 1; }
            tx.End(); tx = null;
            try { app.ActiveView?.Update(); } catch { }
            return Ok(ctx, new JObject
            {
                ["appearance"] = asset.DisplayName,
                ["applied_to"] = count,
                ["target"] = target.Occurrences.Count > 0 ? "occurrences" : target.Bodies.Count > 0 ? "bodies" : "part",
            });
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "set_appearance failed (rolled back): " + ex.Message);
        }
    }
}

/// <summary><c>reset_appearance</c> (E7) — drop appearance overrides so occurrences/bodies show their part/material appearance again.</summary>
public sealed class ResetAppearanceHandler : HandlerBase, IInventorCommand
{
    public string Name => "reset_appearance";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (!AppearanceSupport.TryTarget(ctx, p, Name, out var target, out var failure)) return failure!;
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)target.Doc, "MCP: reset_appearance");
            var count = 0;
            foreach (var it in target.Occurrences) { it.Occurrence.AppearanceSourceType = AppearanceSourceTypeEnum.kPartAppearance; count++; }
            foreach (var b in target.Bodies) { b.AppearanceSourceType = AppearanceSourceTypeEnum.kPartAppearance; count++; }
            if (target.WholePart is { } part)
            {
                part.ComponentDefinition.ClearAppearanceOverrides(Type.Missing);
                part.AppearanceSourceType = AppearanceSourceTypeEnum.kMaterialAppearance;
                count = 1;
            }
            tx.End(); tx = null;
            try { app.ActiveView?.Update(); } catch { }
            return Ok(ctx, new JObject { ["reset"] = count });
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "reset_appearance failed (rolled back): " + ex.Message);
        }
    }
}
#endif
