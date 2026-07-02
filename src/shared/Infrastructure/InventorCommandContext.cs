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
}
