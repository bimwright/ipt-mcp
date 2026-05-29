namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;

/// <summary>
/// Builds the add-in's wire-command map. Uses the partial-registrar pattern so each Phase-3
/// workstream drops in its own <c>InventorCommandRegistry.&lt;Domain&gt;.cs</c> implementing exactly
/// one <c>AddXxx</c> registrar, never editing a shared list. Unimplemented <c>partial void</c>
/// registrars compile to no-ops, so the add-in builds in Phase 2 with only the Core handlers.
/// </summary>
public static partial class InventorCommandRegistry
{
    public static IReadOnlyDictionary<string, IInventorCommand> Build(PluginOptions o)
    {
        var d = new Dictionary<string, IInventorCommand>(StringComparer.OrdinalIgnoreCase);
        void Add(IInventorCommand c) => d.Add(c.Name, c);
        AddCore(d, Add);          // Phase 2 (health, get_document_info)
        AddDocument(d, Add);      // Phase 3 WS-A
        AddParameters(d, Add);    // Phase 3 WS-A
        AddProperties(d, Add);    // Phase 3 WS-A
        AddSketch(d, Add);        // Phase 3 WS-B
        AddFeature(d, Add);       // Phase 3 WS-B
        AddExport(d, Add);        // Phase 3 WS-C
        AddPlatform(d, o, Add);   // Phase 3 WS-C (send_code, run_baked_tool)
        return d;
    }

    static partial void AddCore(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddDocument(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddParameters(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddProperties(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddSketch(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddFeature(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddExport(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add);
    static partial void AddPlatform(Dictionary<string, IInventorCommand> d, PluginOptions o, Action<IInventorCommand> add);
}
