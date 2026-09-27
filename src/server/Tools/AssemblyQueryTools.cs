using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

public class MeasureSideDto
{
    [JsonPropertyName("occurrence")]
    [Description("Name of the component occurrence.")]
    public string Occurrence { get; set; } = "";

    [JsonPropertyName("ref")]
    [Description("Optional named reference: iMate name, work feature name, or origin plane/axis name.")]
    public string? Ref { get; set; }
}

/// <summary>
/// Assembly verification tools (toolset <c>assembly_query</c>, read-only — survives --read-only).
/// The fully NUMERIC self-check battery: interference, min distance, constraint health, BOM+DOF.
/// A text-only agent can verify an assembly end-to-end with these; captures are for humans.
/// </summary>
[McpServerToolType]
public sealed class AssemblyQueryTools
{
    private readonly PluginClient _client;
    public AssemblyQueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_list_interfaces"),
     Description("List named interfaces of the active document or of one occurrence: iMates (name/type/entity kind), user work features, origin geometry names. Use these EXACT names as refs in inventor_add_constraint. Note: an assembly document scope usually has an empty imates array — iMates live on parts; query an occurrence to see its part's iMates.")]
    public Task<string> ListInterfaces(string? occurrence = null, CancellationToken ct = default)
        => Call("list_interfaces", new JObject { ["occurrence"] = occurrence }, ct);

    [McpServerTool(Name = "inventor_check_interference"),
     Description("Run Inventor's interference analysis over an assembly. Legacy: occurrences=[names] (null = ALL top-level occurrences; a subassembly counts as one unit) checked against each other. Set mode: set_a (+ optional set_b) occurrence selectors — e.g. set_a={file:'BEAM*', leaf:true}, set_b={names:['COL*']} — analyses A against B only (or A within itself); nested/leaf matches are analysed in the assembly's context and reported by path. bbox_prefilter (default true) skips members whose range box touches nothing on the other side. Returns count (pairs), total_volume_mm3, bodies, pairs[{a,b,volume_mm3}] sorted by volume and capped at max_pairs (default 200; pairs_total/truncated report the rest), and analysed counts. Expect count=0 for a sound design outside declared weld zones. " + JsonArg.SelectorDoc + " " + JsonArg.DocumentDoc)]
    public Task<string> CheckInterference(string[]? occurrences = null, [Description("Occurrence selector for side A (JSON object, name or array of names).")] System.Text.Json.JsonElement? set_a = null,
        [Description("Occurrence selector for side B (JSON object, name or array of names).")] System.Text.Json.JsonElement? set_b = null, bool bbox_prefilter = true, int max_pairs = 200, string? document = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["occurrences"] = occurrences is null ? null : new JArray(occurrences),
            ["bbox_prefilter"] = bbox_prefilter,
            ["max_pairs"] = max_pairs,
            ["document"] = document,
        };
        if (JsonArg.From(set_a) is { } a) p["set_a"] = a;
        if (JsonArg.From(set_b) is { } b) p["set_b"] = b;
        return Call("check_interference", p, ct);
    }

    [McpServerTool(Name = "inventor_measure_min_distance"),
     Description("Minimum 3D distance (mm). Single: a + b sides {occurrence, ref?} (ref = iMate/work/origin name, same resolution as inventor_add_constraint; omit ref for the whole occurrence body; expect 0 on mated faces). Batch: pairs=[{a, b, a_ref?, b_ref?}] (occurrence names), or set_a × set_b occurrence selectors (every pair once). threshold_mm reports only pairs at or below it (clearance checks) and lets set mode skip pairs whose range boxes are already farther apart. Batch returns results[{a,b,distance_mm}] sorted ascending (capped at max_results, default 200), min_distance_mm, measured, skipped_by_bbox. " + JsonArg.SelectorDoc + " " + JsonArg.DocumentDoc)]
    public Task<string> MeasureMinDistance(
        MeasureSideDto? a = null,
        MeasureSideDto? b = null,
        [Description("JSON array of {a, b, a_ref?, b_ref?} occurrence-name pairs.")] System.Text.Json.JsonElement? pairs = null,
        [Description("Occurrence selector for side A (JSON object, name or array of names).")] System.Text.Json.JsonElement? set_a = null,
        [Description("Occurrence selector for side B (JSON object, name or array of names).")] System.Text.Json.JsonElement? set_b = null,
        double? threshold_mm = null,
        int max_results = 200,
        string? document = null,
        CancellationToken ct = default)
    {
        var pairsTok = JsonArg.From(pairs);
        var setA = JsonArg.From(set_a);
        if (pairsTok is null && setA is null)
        {
            if (a is null) return Task.FromResult(Error("INVALID_ARGUMENT", "side a is required (or pass pairs / set_a+set_b for a batch)"));
            if (b is null) return Task.FromResult(Error("INVALID_ARGUMENT", "side b is required (or pass pairs / set_a+set_b for a batch)"));
            return Call("measure_min_distance", new JObject
            {
                ["a_occurrence"] = a.Occurrence, ["a_ref"] = a.Ref,
                ["b_occurrence"] = b.Occurrence, ["b_ref"] = b.Ref,
                ["document"] = document,
            }, ct);
        }
        if (a is not null || b is not null)
            return Task.FromResult(Error("INVALID_ARGUMENT", "use either a/b (single) or pairs / set_a+set_b (batch)"));
        var p = new JObject { ["max_results"] = max_results, ["document"] = document };
        if (pairsTok is not null) p["pairs"] = pairsTok;
        if (setA is not null)
        {
            p["set_a"] = setA;
            p["set_b"] = JsonArg.From(set_b) ?? setA.DeepClone();
        }
        if (threshold_mm is { } t) p["threshold_mm"] = t;
        return Call("measure_min_distance", p, ct);
    }

    [McpServerTool(Name = "inventor_get_assembly_bom"),
     Description("Walk the active assembly: occurrences[{name,path,depth,grounded,dof_translation,dof_rotation,suppressed}] plus a grouped bom[{part_number,path,qty,unit_mass_g}]. DOF counts reveal under-constrained parts (grounded rows are 0/0). max_rows caps the flat list.")]
    public Task<string> GetAssemblyBom(int max_rows = 500, CancellationToken ct = default)
        => Call("get_assembly_bom", new JObject { ["max_rows"] = max_rows }, ct);

    [McpServerTool(Name = "inventor_list_constraints"),
     Description("Read back the assembly's relationship graph: every constraint with name, type (mate|flush|insert|angle|other), health (up_to_date expected), suppressed flag and the two occurrence names. Run after building to audit that no constraint is sick.")]
    public Task<string> ListConstraints(CancellationToken ct = default)
        => Call("list_constraints", new JObject(), ct);

    [McpServerTool(Name = "inventor_list_occurrences"),
     Description("List an assembly's occurrences filtered by a selector — the typed replacement for scripts that walk occurrences, match names and read range boxes/transforms. fields picks columns (default name,path,depth,file,suppressed,visible,grounded,bbox_mm; also type, leaf, transform{origin_mm,x_axis,y_axis,z_axis}, material, appearance{name,source}, mass_g, volume_mm3; or [\"all\"]). output=inline (auto-spills above 64 KiB) | file (rows written to a JSON file, path + 10-row preview returned). selector omitted = every occurrence (limit 1000). Read-only. " + JsonArg.SelectorDoc + " " + JsonArg.DocumentDoc)]
    public Task<string> ListOccurrences([Description("Occurrence selector: JSON object {names?, regex?, file?, path_contains?, leaf?, max_depth?, include_suppressed?, limit?}, a name string, or an array of names.")] System.Text.Json.JsonElement? selector = null, string[]? fields = null, string output = "inline",
        string? document = null, CancellationToken ct = default)
    {
        var p = new JObject { ["output"] = output, ["document"] = document };
        if (JsonArg.From(selector) is { } s) p["selector"] = s;
        if (fields is { Length: > 0 }) p["fields"] = new JArray(fields);
        return Call("list_occurrences", p, ct);
    }

    private static string Error(string code, string message)
        => ToolResponse.Error(code, message);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
