#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>create_bim_connector</c> — author a BIM pipe connector on a part (spec F4-P3). v1 covers
/// <c>kind=pipe</c> only (the WS2 evidence: water-valve pipe connectors). <c>geometry</c> is an
/// edge ref (<c>body:N/edge:M</c>) — a circular port edge supplies origin, direction and diameter.
/// Optional pipe-definition properties map to <see cref="BIMPipeConnectorDefinition"/>.
/// Duct/conduit/cable-tray/electrical kinds and connector links are deferred.
/// </summary>
public sealed class CreateBimConnectorHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_bim_connector";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "create_bim_connector", out var app, out var part, out var failure))
            return failure!;

        var kind = ((string?)p["kind"] ?? "pipe").Trim().ToLowerInvariant();
        if (kind != "pipe")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                $"unknown connector kind '{kind}' (v1 supports: pipe)");
        var geomRef = (string?)p["geometry"];
        if (string.IsNullOrWhiteSpace(geomRef))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "geometry is required — an edge ref (body:N/edge:M), normally a circular port edge");

        try
        {
            var def = part.ComponentDefinition;
            var edge = Bimwright.Ipt.Shared.Handlers.EntityResolver.ResolveEdge(def, geomRef.Trim());

            var geom = app.TransientObjects.CreateObjectCollection();
            geom.Add(edge);

            BIMComponent? bim;
            try { bim = def.BIMComponent; } catch { bim = null; }
            if (bim is null)
                return Fail(ctx, InventorErrorCodes.API_ERROR,
                    "this document has no BIM component — BIM authoring may need a part with solid content first");

            var connDef = (BIMPipeConnectorDefinition)bim.Connectors.CreatePipeConnectorDefinition(
                geom, BIMConnectorShapeEnum.kCircularShapeConnector);

            if (p["nominal_diameter_mm"] is { } nd)
                connDef.NominalDiameter = UnitConvert.MmToCm(nd.Value<double>());
            if (p["system_type"] is { } st)
                connDef.SystemType = PipeSystemType(st.ToString());
            if (p["flow_direction"] is { } fd)
                connDef.FlowDirection = FlowDirection(fd.ToString());
            if (p["connection_type"] is { } ct)
                connDef.ConnectionType = PipeConnectionType(ct.ToString());
            if (p["description"] is { } ds)
                connDef.Description = ds.ToString();

            var name = (string?)p["name"] ?? "";
            // BIMPipeConnectorDefinition and BIMConnectorDefinition are sibling COM interfaces —
            // the explicit cast QIs the same underlying definition object.
            var connector = bim.Connectors.Add((BIMConnectorDefinition)connDef, name);

            var data = new JObject
            {
                ["connector_name"] = connector.Name,
                ["kind"] = "pipe",
            };
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR,
                ex.Message + " — BIM connector creation failed; geometry should be a circular edge on a port face");
        }
    }

    private static BIMPipeSystemTypeEnum PipeSystemType(string s) => s.Trim().ToLowerInvariant() switch
    {
        "domestic_cold" => BIMPipeSystemTypeEnum.kDomesticColdWaterPipeSystemType,
        "domestic_hot" => BIMPipeSystemTypeEnum.kDomesticHotWaterPipeSystemType,
        "sanitary" => BIMPipeSystemTypeEnum.kSanitaryPipeSystemType,
        "hydronic_supply" => BIMPipeSystemTypeEnum.kHydronicSupplyPipeSystemType,
        "hydronic_return" => BIMPipeSystemTypeEnum.kHydronicReturnPipeSystemType,
        "fire_protection" => BIMPipeSystemTypeEnum.kFireProtectionOtherPipeSystemType,
        "other" => BIMPipeSystemTypeEnum.kOtherPipeSystemType,
        _ => throw new ArgumentException($"unknown system_type '{s}' (domestic_cold|domestic_hot|sanitary|hydronic_supply|hydronic_return|fire_protection|other)"),
    };

    private static BIMFlowDirectionEnum FlowDirection(string s) => s.Trim().ToLowerInvariant() switch
    {
        "in" => BIMFlowDirectionEnum.kInFlowDirectionType,
        "out" => BIMFlowDirectionEnum.kOutFlowDirectionType,
        "bidirectional" => BIMFlowDirectionEnum.kBiDirectionalFlowDirectionType,
        _ => throw new ArgumentException($"unknown flow_direction '{s}' (in|out|bidirectional)"),
    };

    private static BIMPipeConnectionTypeEnum PipeConnectionType(string s) => s.Trim().ToLowerInvariant() switch
    {
        "threaded" => BIMPipeConnectionTypeEnum.kThreadedPipeConnectionType,
        "flanged" => BIMPipeConnectionTypeEnum.kFlangePipeConnectionType,
        "welded" => BIMPipeConnectionTypeEnum.kButtWeldedPipeConnectionType,
        "glued" => BIMPipeConnectionTypeEnum.kGluedPipeConnectionType,
        "compression" => BIMPipeConnectionTypeEnum.kCompressionPipeConnectionType,
        "other" => BIMPipeConnectionTypeEnum.kUndefinedPipeConnectionType,
        _ => throw new ArgumentException($"unknown connection_type '{s}' (threaded|flanged|welded|glued|compression|other)"),
    };
}
#endif
