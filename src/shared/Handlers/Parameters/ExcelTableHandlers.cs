#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

public sealed class ListExcelTablesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_excel_tables";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out _, out var part, out var failure)) return failure!;
        var tables = new JArray();
        foreach (ParameterTable table in part.ComponentDefinition.Parameters.ParameterTables) tables.Add(ExcelTables.Describe(table));
        return Ok(ctx, new JObject { ["tables"] = tables, ["count"] = tables.Count });
    }
}

public sealed class LinkExcelTableHandler : HandlerBase, IInventorCommand
{
    public string Name => "link_excel_table";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out var app, out var part, out var failure)) return failure!;
        ExcelParameterSource source;
        try { source = ExcelParameterSource.Read((string?)p["path"] ?? "", (string?)p["start_cell"] ?? "A1"); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        try
        {
            var existing = ExcelTables.Find(part, source.TableId);
            if (existing is not null)
            {
                var checkpoint = ExcelTables.ReadCheckpoint(part, source.TableId);
                if (checkpoint is not null && !JToken.DeepEquals(checkpoint, ExcelTables.Values(existing)))
                    throw new ArgumentException("linked values changed since the last verified update; use reload_excel_table");
                ExcelTables.Verify(part, existing, source);
                source.VerifyUnchanged();
                return Ok(ctx, ExcelTables.Receipt(existing, source, "existing", ExcelTables.Values(existing), 0));
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Parameter parameter in part.ComponentDefinition.Parameters) names.Add(parameter.Name);
            var conflicts = source.Rows.Where(r => names.Contains(r.Name)).Select(r => r.Name).ToArray();
            if (conflicts.Length != 0) throw new ArgumentException("parameter names already exist: " + string.Join(", ", conflicts));
            ExcelTables.Expected(part, source); // Reject invalid units before mutation.
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        return PartMutation.Run(ctx, app, part, Name, () =>
        {
            var table = part.ComponentDefinition.Parameters.ParameterTables.AddExcelTable(source.Path, source.StartCell, true);
            ExcelTables.Recompute(part);
            ExcelTables.Verify(part, table, source);
            source.VerifyUnchanged();
            ExcelTables.SaveCheckpoint(part, source.TableId, ExcelTables.Values(table));
            return Ok(ctx, ExcelTables.Receipt(table, source, "linked", new JObject(), 1));
        });
    }
}

public sealed class ReloadExcelTableHandler : HandlerBase, IInventorCommand
{
    public string Name => "reload_excel_table";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out var app, out var part, out var failure)) return failure!;
        ParameterTable table; ExcelParameterSource source; JObject? before = null; bool hasCheckpoint = false;
        string? id = null;
        try
        {
            id = (string?)p["table_id"];
            if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, @"\Aexcel:[a-f0-9]{64}\z"))
                throw new ArgumentException("valid table_id is required; use list_excel_tables");
            // Read the persisted baseline before touching the native linked table: even a query can refresh its cache.
            before = ExcelTables.ReadCheckpoint(part, id);
            hasCheckpoint = before is not null;
            table = ExcelTables.Find(part, id!) ?? throw new ArgumentException("linked table_id was not found; refresh list_excel_tables");
            source = ExcelParameterSource.Read(table.FileName, table.StartCell);
            before ??= ExcelTables.Values(table);
            var expectedNames = new HashSet<string>(source.Rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            if (!expectedNames.SetEquals(before.Properties().Select(v => v.Name)))
                throw new ArgumentException("reload requires unchanged parameter names; adding/removing rows is not supported");
            ExcelTables.Expected(part, source);
        }
        catch (Exception ex)
        {
            var failureResult = Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message);
            return before is null ? failureResult : ExcelTables.VerifyFailure(part, id!, before, hasCheckpoint, failureResult);
        }
        var result = PartMutation.Run(ctx, app, part, Name, () =>
        {
            var attempts = 0;
            for (; attempts < 2; attempts++)
            {
                table.FileName = source.Path;
                ExcelTables.Recompute(part);
                // Reassignment can be ignored once by Inventor; retry only the unchanged saved source.
                source.VerifyUnchanged();
                if (ExcelTables.Matches(part, table, source)) break;
            }
            ExcelTables.Verify(part, table, source);
            source.VerifyUnchanged();
            var receipt = ExcelTables.Receipt(table, source, "reloaded", before, Math.Min(attempts + 1, 2));
            receipt["change_basis"] = hasCheckpoint ? "last_verified_link_or_reload" : "unavailable_until_first_verified_reload";
            if (!hasCheckpoint) receipt["changed_parameters"] = null;
            ExcelTables.SaveCheckpoint(part, source.TableId, ExcelTables.Values(table));
            return Ok(ctx, receipt);
        });
        return result.Ok ? result : ExcelTables.VerifyFailure(part, source.TableId, before, hasCheckpoint, result);
    }
}

internal static class ExcelTables
{
    internal static InventorCommandResult VerifyFailure(PartDocument part, string id, JObject before, bool hasCheckpoint, InventorCommandResult result)
    {
        // Linked spreadsheet caches can refresh before a transaction, or survive its abort.
        var receipt = result.Data as JObject ?? new JObject { ["rolled_back"] = false, ["mutation_applied"] = false, ["rollback_failed"] = false };
        result.Data = receipt;
        receipt["transaction_aborted"] = receipt["rolled_back"]?.DeepClone();
        receipt["parameters_before"] = before;
        try
        {
            var restored = Find(part, id);
            var after = restored is null ? new JObject() : Values(restored);
            receipt["parameters_after_failure"] = after;
            if (!hasCheckpoint || !JToken.DeepEquals(before, after))
                throw new InvalidOperationException("linked values are not verified as restored to the last successful checkpoint");
            foreach (PartFeature feature in part.ComponentDefinition.Features)
                if (!feature.Suppressed && feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("unhealthy feature remains after failure: " + feature.Name);
        }
        catch (Exception ex)
        {
            receipt["rolled_back"] = false; receipt["rollback_failed"] = true; receipt["mutation_applied"] = "unknown";
            receipt["recovery_required"] = hasCheckpoint
                ? "Restore the last verified Excel values shown in parameters_before, then reload and verify the part before saving"
                : "Restore a known-good Excel workbook, then reload and verify the part before saving; no previous verified checkpoint is available";
            result.Error!.Message += "; " + ex.Message;
        }
        return result;
    }
    private const string CheckpointSet = "BimwrightExcelParameters";
    private static string CheckpointKey(string id) => "T" + id.Substring("excel:".Length);
    internal static JObject? ReadCheckpoint(PartDocument part, string id)
    {
        var sets = part.AttributeSets;
        if (!sets.NameIsUsed[CheckpointSet]) return null;
        var set = sets[CheckpointSet]; var key = CheckpointKey(id);
        return set.NameIsUsed[key] ? JObject.Parse((string)set[key].Value) : null;
    }
    internal static void SaveCheckpoint(PartDocument part, string id, JObject values)
    {
        var sets = part.AttributeSets;
        var set = sets.NameIsUsed[CheckpointSet] ? sets[CheckpointSet] : sets.Add(CheckpointSet);
        var key = CheckpointKey(id); var json = values.ToString(Newtonsoft.Json.Formatting.None);
        if (set.NameIsUsed[key]) set[key].Value = json;
        else set.Add(key, ValueTypeEnum.kStringType, json);
    }
    internal static ParameterTable? Find(PartDocument part, string id)
    {
        ParameterTable? found = null;
        foreach (ParameterTable table in part.ComponentDefinition.Parameters.ParameterTables)
        {
            if (!table.Linked || ExcelParameterSource.Identity(table.FileName, table.StartCell) != id) continue;
            if (found is not null) throw new ArgumentException("ambiguous duplicate linked tables for this path and start cell");
            found = table;
        }
        return found;
    }
    internal static JObject Describe(ParameterTable table) => new JObject
    {
        ["table_id"] = table.Linked ? ExcelParameterSource.Identity(table.FileName, table.StartCell) : null,
        ["linked"] = table.Linked, ["path"] = table.Linked ? table.FileName : null,
        ["start_cell"] = table.StartCell, ["parameters"] = Values(table),
    };
    internal static JObject Values(ParameterTable table)
    {
        var values = new JObject();
        foreach (TableParameter p in table.TableParameters)
            values[p.Name] = new JObject { ["value_database"] = JToken.FromObject(p.Value), ["units"] = p.get_Units(),
                ["expression"] = p.Expression, ["health"] = p.HealthStatus.ToString() };
        return values;
    }
    internal static Dictionary<string, double> Expected(PartDocument part, ExcelParameterSource source)
        => source.Rows.ToDictionary(r => r.Name,
            r => Convert.ToDouble(part.UnitsOfMeasure.GetValueFromExpression(r.Value.ToString("R", CultureInfo.InvariantCulture), r.Units)), StringComparer.OrdinalIgnoreCase);
    internal static bool Matches(PartDocument part, ParameterTable table, ExcelParameterSource source)
    {
        var expected = Expected(part, source);
        var units = source.Rows.ToDictionary(r => r.Name, r => r.Units, StringComparer.OrdinalIgnoreCase);
        if (!table.Linked || table.TableParameters.Count != expected.Count) return false;
        foreach (TableParameter p in table.TableParameters)
        {
            if (!expected.TryGetValue(p.Name, out var value) || p.HealthStatus != HealthStatusEnum.kUpToDateHealth) return false;
            try { part.UnitsOfMeasure.ConvertUnits(1.0, units[p.Name], p.get_Units()); }
            catch { return false; }
            var actual = Convert.ToDouble(p.Value);
            if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - value) > Math.Max(1e-8, Math.Abs(value) * 1e-9)) return false;
        }
        return true;
    }
    internal static void Verify(PartDocument part, ParameterTable table, ExcelParameterSource source)
    {
        if (!Matches(part, table, source)) throw new InvalidOperationException("linked parameter readback does not match saved Excel values; no successful reload is claimed");
    }
    internal static void Recompute(PartDocument part)
    {
        if (!part.Update2(false)) throw new InvalidOperationException("Excel parameter recompute failed");
        foreach (PartFeature feature in part.ComponentDefinition.Features)
            if (!feature.Suppressed && feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Excel parameter recompute left an unhealthy feature: " + feature.Name);
    }
    internal static JObject Receipt(ParameterTable table, ExcelParameterSource source, string action, JObject before, int attempts)
    {
        var data = Describe(table); var after = (JObject)data["parameters"]!; var changed = new JArray();
        foreach (var row in after.Properties())
            if (!JToken.DeepEquals(before[row.Name], row.Value))
                changed.Add(new JObject { ["name"] = row.Name, ["before"] = before[row.Name]?.DeepClone(), ["after"] = row.Value.DeepClone() });
        data["action"] = action; data["sheet"] = source.Sheet; data["source_sha256"] = source.Sha256;
        data["verified_saved_values"] = true; data["attempts"] = attempts; data["changed_parameters"] = changed;
        data["value_basis"] = "saved numeric cells/formula caches; Excel formulas are not recalculated by this tool";
        return data;
    }
}
#endif
