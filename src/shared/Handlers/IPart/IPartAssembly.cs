#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.IPart;

internal sealed class IPartAssembly : HandlerBase
{
    // Inventor may dirty/recompute a member when an occurrence is grounded or solved.
    // Trust only memory states verified by this handler, never arbitrary dirty documents.
    private static readonly ConditionalWeakTable<PartDocument, JObject> VerifiedLoaded = new();
    private static JObject Signature(PartDocument part, string fileHash) => new()
    {
        ["file_sha256"] = fileHash, ["geometry_version"] = part.ComponentDefinition.ModelGeometryVersion,
        ["part_number"] = IPartSupport.Property(part, 5).Value?.ToString(),
        ["description"] = IPartSupport.Property(part, 29).Value?.ToString(), ["material"] = part.ActiveMaterial.DisplayName
    };
    internal static InventorCommandResult Execute(InventorCommandContext ctx, string command, JObject p)
    {
        Transaction? tx = null; var committed = false; string? beforeMember = null; ComponentOccurrence? occurrence = null;
        try
        {
            if (!ActiveDocumentSupport.TryGetAssembly(ctx, p, command, out var app, out var assembly, out var failure)) return failure!;
            var memberName = IPartInput.Member(IPartInput.Required(p, "member")); var cd = assembly.ComponentDefinition;
            CheckConstraints(cd);
            PartDocument factory;
            if (command == "place_ipart_member")
            {
                if (!ActiveDocumentSupport.TryGetPart(ctx, new JObject { ["document"] = IPartInput.Required(p, "factory_document") }, command, out _, out factory, out failure)) return failure!;
            }
            else
            {
                var name = IPartInput.Required(p, "occurrence");
                occurrence = cd.Occurrences.Cast<ComponentOccurrence>().SingleOrDefault(o => o.Name == name) ?? throw new ArgumentException("Top-level occurrence not found: " + name);
                if (occurrence.Suppressed || occurrence.Definition is not PartComponentDefinition def || !def.IsiPartMember)
                    throw new ArgumentException("Occurrence must be an unsuppressed Standard iPart member");
                factory = (PartDocument)def.iPartMember.ParentFactory.Parent; beforeMember = def.iPartMember.Row.MemberName;
            }
            using var gate = IPartSupport.Lock(factory.FullFileName);
            var state = IPartSupport.Managed(factory, true); IPartSupport.CheckSource(state);
            if (state["pending"] != null) throw new ArgumentException("Finish the factory's pending operation first");
            var row = IPartSupport.Row(factory.ComponentDefinition.iPartFactory, memberName);
            if (!IPartSupport.Fresh(factory, state, row)) throw new ArgumentException("Generate and verify the selected member before placing/changing it");
            var path = IPartSupport.CachePath(factory, row); var fileHash = IPartInput.FileHash(path);
            var loaded = IPartSupport.OpenPart(app, path);
            if (loaded?.Dirty == true && (!VerifiedLoaded.TryGetValue(loaded, out var known) || !JToken.DeepEquals(known, Signature(loaded, fileHash))))
                throw new ArgumentException("Selected member has unverified unsaved changes");
            var position = new double[3];
            if (p["position_mm"] is JToken token && token.Type != JTokenType.Null)
            {
                if (token is not JArray a || a.Count != 3 || a.Any(v => v.Type != JTokenType.Float && v.Type != JTokenType.Integer)) throw new ArgumentException("position_mm must contain three finite numbers");
                position = a.Select(v => (double)v).ToArray();
                if (position.Any(v => double.IsInfinity(v) || double.IsNaN(v))) throw new ArgumentException("position_mm must be finite");
            }
            loaded ??= (PartDocument)app.Documents.Open(path, false);
            IPartSupport.VerifyMember(factory, loaded, memberName);
            var bodies = loaded.ComponentDefinition.SurfaceBodies.Cast<SurfaceBody>().Select(b => app.TransientBRep.Copy(b)).ToArray();
            var properties = Signature(loaded, fileHash);
            tx = app.TransactionManager.StartTransaction((global::Inventor._Document)(object)assembly, "MCP: " + command);
            StandardIPartHandler.RefuseParent(app);
            if (occurrence == null)
            {
                var matrix = app.TransientGeometry.CreateMatrix(); matrix.SetTranslation(app.TransientGeometry.CreateVector(position[0] / 10, position[1] / 10, position[2] / 10));
                // The member is generated and verified beforehand, so placement need not create files.
                occurrence = cd.Occurrences.Add(path, matrix); occurrence.Grounded = (bool?)p["grounded"] ?? false;
            }
            else if (beforeMember != memberName) occurrence.ChangeRowOfiPartMember(row);
            if (!assembly.Update2(false)) throw new InvalidOperationException("Assembly recompute failed");
            CheckConstraints(cd);
            var actual = (PartDocument)((PartComponentDefinition)occurrence.Definition).Document;
            IPartSupport.VerifyMember(factory, actual, memberName);
            if (!string.Equals(actual.FullFileName, path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Occurrence references an unexpected member file");
            if (!IPartSupport.BodiesMatch(app, bodies, actual)) throw new InvalidOperationException("Assembly recompute changed the verified member geometry; regenerate the member");
            var after = Signature(actual, fileHash);
            foreach (var key in new[] { "part_number", "description", "material" })
                if (!JToken.DeepEquals(properties[key], after[key])) throw new InvalidOperationException("Assembly recompute changed a verified member property: " + key);
            if (IPartInput.FileHash(path) != fileHash) throw new InvalidOperationException("Inventor changed the cached member file during assembly update; regenerate/verify the member before retrying");
            var data = new JObject { ["occurrence"] = occurrence.Name, ["member"] = memberName, ["previous_member"] = beforeMember,
                ["member_path"] = path, ["factory_path"] = factory.FullFileName, ["geometry"] = IPartSupport.Geometry(actual),
                ["matching_occurrences"] = cd.Occurrences.Cast<ComponentOccurrence>().Count(o => !o.Suppressed && string.Equals(o.ReferencedDocumentDescriptor.FullDocumentName, path, StringComparison.OrdinalIgnoreCase)),
                ["assembly_saved"] = false, ["constraints_verified"] = cd.Constraints.Count };
            tx.End(); committed = true; tx = null;
            // Reuse the verified signature; do not read COM again after committing the assembly.
            VerifiedLoaded.Remove(actual); VerifiedLoaded.Add(actual, after);
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            var aborted = false; string? abortError = null;
            if (tx != null) { try { tx.Abort(); aborted = true; } catch (Exception abort) { abortError = abort.Message; } }
            var result = HandlerBase.FailForSupport(ctx, ex is ArgumentException && abortError == null ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR, ex.Message + (abortError == null ? "" : "; abort failed: " + abortError));
            result.Data = new JObject { ["assembly_transaction_aborted"] = aborted, ["previous_member"] = beforeMember,
                ["file_rollback_claimed"] = false, ["mutation_applied"] = committed ? new JValue(true) : tx == null ? new JValue(false) : new JValue("unknown") };
            return result;
        }
    }
    private static void CheckConstraints(AssemblyComponentDefinition definition)
    {
        foreach (AssemblyConstraint constraint in definition.Constraints)
            if (!constraint.Suppressed && constraint.HealthStatus != HealthStatusEnum.kUpToDateHealth) throw new InvalidOperationException("Unhealthy assembly constraint: " + constraint.Name);
    }
}
#endif
