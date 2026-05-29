namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// Immutable add-in startup options captured at <c>Activate</c>.
/// Ported from nwd's <c>PluginOptions</c>; the third positional field is the
/// response-size cap (bytes) instead of nwd's TCP port, since Inventor add-ins
/// pick TCP vs Named Pipe from the year and the port/pipe-name lives on the descriptor.
/// A value of <c>0</c> means "use the dispatcher default".
/// </summary>
/// <param name="Year">Inventor calendar year (2022-2027), resolved from the compile symbol.</param>
/// <param name="EnableSendCode">Add-in-side <c>send_code</c> opt-in (env <c>BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE</c>).</param>
/// <param name="ReadOnly">Add-in-side write lock (env <c>BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY</c> or <c>BIMWRIGHT_INVENTOR_READ_ONLY</c>).</param>
/// <param name="MaxResponseBytes">Response-size guard cap in bytes; <c>0</c> = dispatcher default.</param>
public sealed record PluginOptions(int Year, bool EnableSendCode, bool ReadOnly, int MaxResponseBytes);
