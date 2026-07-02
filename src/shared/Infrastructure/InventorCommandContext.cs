namespace Bimwright.Ipt.Shared.Infrastructure;

using System.Collections.Generic;

/// <summary>
/// Per-request execution context handed to every <see cref="IInventorCommand"/>.
/// Created on (and only touched from) Inventor's STA thread.
/// </summary>
public sealed class InventorCommandContext
{
    /// <summary>The add-in's view of read-only mode (the server also enforces this by tool filtering).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Whether the <c>send_code</c> command is enabled on this add-in.</summary>
    public bool EnableSendCode { get; init; }

    /// <summary>Inventor calendar year (2022-2027), captured from the compile symbol.</summary>
    public int InventorYear { get; init; }

    /// <summary>Descriptor target id for this add-in instance.</summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Inventor.Application captured at Activate. Typed as <see cref="object"/> so this file stays
    /// API-agnostic at the source level (the server and tests compile it without Inventor installed);
    /// handlers cast it to <c>Inventor.Application</c>.
    /// </summary>
    public object? Application { get; init; }

    /// <summary>The full command map, so commands like <c>run_baked_tool</c> can dispatch sub-commands.</summary>
    public IReadOnlyDictionary<string, IInventorCommand>? Commands { get; init; }

#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
    public bool TryGetActiveAssembly(out global::Inventor.AssemblyDocument? assembly, out string errorCode, out string errorMessage)
    {
        assembly = null;
        errorCode = "";
        errorMessage = "";
        if (Application is null)
        {
            errorCode = "API_ERROR";
            errorMessage = "Inventor application not ready";
            return false;
        }

        var app = (global::Inventor.Application)Application;
        if (app.ActiveDocument is not global::Inventor.AssemblyDocument doc)
        {
            errorCode = "WRONG_DOCUMENT_TYPE";
            errorMessage = "Active document is not an assembly";
            return false;
        }
        assembly = doc;
        return true;
    }

    public bool TryGetActivePart(out global::Inventor.PartDocument? part, out string errorCode, out string errorMessage)
    {
        part = null;
        errorCode = "";
        errorMessage = "";
        if (Application is null)
        {
            errorCode = "API_ERROR";
            errorMessage = "Inventor application not ready";
            return false;
        }

        var app = (global::Inventor.Application)Application;
        if (app.ActiveDocument is not global::Inventor.PartDocument doc)
        {
            errorCode = "WRONG_DOCUMENT_TYPE";
            errorMessage = "Active document is not a part";
            return false;
        }
        part = doc;
        return true;
    }
#endif
}
