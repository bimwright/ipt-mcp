using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Sent by a gateway that confirms documents (family contract FC-DOC, FC-PAIR).</summary>
public sealed class DocumentTargetEnvelope
{
    [JsonProperty("protocol")] public string Protocol { get; set; } = DocumentTargetPolicy.Protocol;

    /// <summary>The document confirmed with inventor_switch_target(verify=true); null when not confirmed.</summary>
    [JsonProperty("expected_document_id")] public string? ExpectedDocumentId { get; set; }
}

/// <summary>
/// Which commands must run on the confirmed document (family contract 5.2/5.3, owner decisions 2026-11-16):
/// writes need a confirmed target; a write that relies on the active document must find the pinned document
/// active; an explicit <c>document</c> parameter names its own target and skips that check (Q-A); commands
/// that open or create a document are exempt and move the pin when they activate a new one (Q-B).
/// Shared by the gateway (early refusal) and the add-in (authoritative check on Inventor's thread).
/// </summary>
public static class DocumentTargetPolicy
{
    public const string Protocol = "document_target_v1";

    /// <summary>Pin value for "no document was active when the target was confirmed".</summary>
    public const string NoDocument = "no_document";

    private static readonly HashSet<string> OpensDocument = new(StringComparer.Ordinal)
    {
        "open_document", "open_documents", "new_part", "new_assembly", "new_drawing", "new_sheet_fabrication_part", "create_part",
    };

    // Writes that change no Inventor document (they install a baked tool).
    private static readonly HashSet<string> NoDocumentEffect = new(StringComparer.Ordinal) { "apply_bake" };

    public static bool OpensOrCreatesDocument(string command) => OpensDocument.Contains(command);

    public static bool HasExplicitDocument(JObject? p) =>
        p?["document"] is JValue { Type: JTokenType.String } v && !string.IsNullOrWhiteSpace((string?)v);

    private static bool IsDryCompile(string command, JObject? p) =>
        command == "send_code" && p?["compile_only"]?.Type == JTokenType.Boolean && (bool)p["compile_only"]!;

    /// <summary>A write that needs a confirmed target (FC-DOC-03, FC-DOC-06) and a paired add-in (FC-PAIR-01).</summary>
    public static bool NeedsConfirmedTarget(string command, bool isReadOnly, JObject? p) =>
        !isReadOnly && !NoDocumentEffect.Contains(command) && !IsDryCompile(command, p);

    /// <summary>A write that acts on the active document, so the pinned document must still be active (FC-DOC-04).</summary>
    public static bool ChecksActiveDocument(string command, bool isReadOnly, JObject? p) =>
        NeedsConfirmedTarget(command, isReadOnly, p) && !OpensDocument.Contains(command) && !HasExplicitDocument(p);

    /// <summary>The add-in's decision before the handler runs: null to run, else the refusal (the command is not started).</summary>
    public static (string Code, string Message)? Check(string command, bool isReadOnly, JObject? p,
        DocumentTargetEnvelope? target, string activeDocumentId, string? activeDocumentTitle)
    {
        if (!NeedsConfirmedTarget(command, isReadOnly, p)) return null;
        if (target is null || target.Protocol != Protocol)
            return (InventorErrorCodes.PAIRING_REQUIRED,
                $"{command} was not started: the MCP server does not confirm the target document ({Protocol}), which this add-in requires for writes. " +
                "Update the ipt-mcp server and the Inventor add-in together from the same release, then restart the MCP client. Reads still work.");
        if (!ChecksActiveDocument(command, isReadOnly, p)) return null;
        if (string.IsNullOrEmpty(target.ExpectedDocumentId))
            return (InventorErrorCodes.DOCUMENT_UNCONFIRMED,
                $"{command} was not started: no document is confirmed for writes. Confirm the active document with inventor_switch_target(target, verify=true).");
        if (!string.Equals(target.ExpectedDocumentId, activeDocumentId, StringComparison.Ordinal))
            return (InventorErrorCodes.DOCUMENT_CHANGED,
                $"{command} was not started: the active document changed since it was confirmed. Active now: " +
                (activeDocumentId == NoDocument ? "no document" : $"'{activeDocumentTitle}' (document_id {activeDocumentId})") +
                $"; confirmed: document_id {target.ExpectedDocumentId}. Check with the user which document to change, then confirm it with " +
                "inventor_switch_target(target, verify=true), or pass document=<full path or name> to name the target document.");
        return null;
    }

    /// <summary>
    /// Q-B: when an open/create command activated a new document and the pinned document was active before it,
    /// the pin moves to the new document. A document the user activated in between is never adopted.
    /// </summary>
    public static string? Repin(string command, DocumentTargetEnvelope? target, string beforeDocumentId, string afterDocumentId) =>
        OpensDocument.Contains(command) && target?.ExpectedDocumentId is { Length: > 0 } expected
            && string.Equals(expected, beforeDocumentId, StringComparison.Ordinal)
            && !string.Equals(beforeDocumentId, afterDocumentId, StringComparison.Ordinal)
            ? afterDocumentId : null;
}
