using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

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
     Description("Run Inventor's interference analysis over the active assembly. occurrences=null analyzes ALL top-level occurrences (a subassembly counts as one unit). Returns count (pairs), total_volume_mm3, bodies (raw result count) and pairs[{a,b,volume_mm3}] — volumes of multiple contact bodies of the same pair are summed. Expect count=0 for a sound design outside declared weld zones.")]
    public Task<string> CheckInterference(string[]? occurrences = null, CancellationToken ct = default)
        => Call("check_interference", new JObject
        {
            ["occurrences"] = occurrences is null ? null : new JArray(occurrences),
        }, ct);

    [McpServerTool(Name = "inventor_measure_min_distance"),
     Description("Minimum 3D distance (mm) between two occurrences, or between two named refs (iMate/work/origin names, same resolution as inventor_add_constraint). Refs are optional — omit ref to measure the whole occurrence body. Expect 0 on mated faces.")]
    public Task<string> MeasureMinDistance(string aOccurrence, string bOccurrence,
        string? aRef = null, string? bRef = null, CancellationToken ct = default)
        => Call("measure_min_distance", new JObject
        {
            ["a_occurrence"] = aOccurrence, ["a_ref"] = aRef,
            ["b_occurrence"] = bOccurrence, ["b_ref"] = bRef,
        }, ct);

    [McpServerTool(Name = "inventor_get_assembly_bom"),
     Description("Walk the active assembly: occurrences[{name,path,depth,grounded,dof_translation,dof_rotation,suppressed}] plus a grouped bom[{part_number,path,qty,unit_mass_g}]. DOF counts reveal under-constrained parts (grounded rows are 0/0). max_rows caps the flat list.")]
    public Task<string> GetAssemblyBom(int maxRows = 500, CancellationToken ct = default)
        => Call("get_assembly_bom", new JObject { ["max_rows"] = maxRows }, ct);

    [McpServerTool(Name = "inventor_list_constraints"),
     Description("Read back the assembly's relationship graph: every constraint with name, type (mate|flush|insert|angle|other), health (up_to_date expected), suppressed flag and the two occurrence names. Run after building to audit that no constraint is sick.")]
    public Task<string> ListConstraints(CancellationToken ct = default)
        => Call("list_constraints", new JObject(), ct);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }
}
