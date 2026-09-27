#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// Runs a document-lifecycle call (save/open/close/export) under <c>Application.SilentOperation</c>
/// so Inventor answers its own prompts with their defaults instead of opening a modal dialog that
/// nobody can see — a dialog blocks the STA thread and the call times out (S1.1 finding). The
/// previous value is restored on dispose. The <c>silent</c> parameter (default true) opts out.
/// </summary>
internal sealed class SilentOperationScope : IDisposable
{
    private readonly Application? _app;
    private readonly bool _previous;

    public bool Applied { get; }

    private SilentOperationScope(Application app, bool on)
    {
        if (!on) return;
        try
        {
            _previous = app.SilentOperation;
            app.SilentOperation = true;
            _app = app;
            Applied = true;
        }
        catch
        {
            // best effort — never fail the command because the flag could not be set
        }
    }

    /// <summary><c>p.silent</c> (bool, default true).</summary>
    public static SilentOperationScope Enter(Application app, JObject? p)
        => new(app, !(p?["silent"]?.Type == JTokenType.Boolean && !(bool)p["silent"]!));

    public static SilentOperationScope Enter(Application app, bool on) => new(app, on);

    public void Dispose()
    {
        if (_app is null) return;
        try { _app.SilentOperation = _previous; } catch { }
    }
}
#endif
