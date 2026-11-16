namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Frozen integration contract: canonical error codes returned in
/// <see cref="InventorError.Code"/> across the server and add-in.
/// </summary>
public static class InventorErrorCodes
{
    public const string NO_TARGET = "NO_TARGET";
    public const string TARGET_UNAVAILABLE = "TARGET_UNAVAILABLE";
    /// <summary>The add-in endpoint exists but did not accept the connection in time; nothing was sent (family contract error table).</summary>
    public const string TARGET_BUSY = "TARGET_BUSY";
    /// <summary>Several live Inventor sessions match and none is selected; the gateway will not guess.</summary>
    public const string AMBIGUOUS_TARGET = "AMBIGUOUS_TARGET";
    /// <summary>The selected Inventor session exited or its pid was reused; it is never replaced implicitly.</summary>
    public const string TARGET_LOST = "TARGET_LOST";
    /// <summary>The Inventor process exited while a call was in flight; unsaved edits are lost.</summary>
    public const string HOST_CRASHED = "HOST_CRASHED";
    /// <summary>A write was refused by the gateway: the target and its document are not confirmed (switch_target verify=true).</summary>
    public const string TARGET_UNCONFIRMED = "TARGET_UNCONFIRMED";
    /// <summary>The add-in refused a write: no document is confirmed. The command was not started.</summary>
    public const string DOCUMENT_UNCONFIRMED = "DOCUMENT_UNCONFIRMED";
    /// <summary>The add-in refused a write: the active document is not the confirmed one. The command was not started.</summary>
    public const string DOCUMENT_CHANGED = "DOCUMENT_CHANGED";
    /// <summary>A write was refused because the gateway and add-in do not both support the document target protocol.</summary>
    public const string PAIRING_REQUIRED = "PAIRING_REQUIRED";
    public const string NO_DOCUMENT = "NO_DOCUMENT";
    public const string WRONG_DOCUMENT_TYPE = "WRONG_DOCUMENT_TYPE";
    public const string INVALID_ARGUMENT = "INVALID_ARGUMENT";
    public const string UNSUPPORTED_HOST = "UNSUPPORTED_HOST";
    public const string API_ERROR = "API_ERROR";
    /// <summary>A command did not finish in time after it started; its outcome is unknown.</summary>
    public const string TIMEOUT = "TIMEOUT";
    /// <summary>Inventor could not start the command (modal dialog, full queue, or busy past the deadline). The command was not started.</summary>
    public const string HOST_BUSY = "HOST_BUSY";
    public const string RESPONSE_TOO_LARGE = "RESPONSE_TOO_LARGE";
    public const string READ_ONLY = "READ_ONLY";
    public const string SEND_CODE_DISABLED = "SEND_CODE_DISABLED";
    public const string UNAUTHORIZED = "UNAUTHORIZED";
}
