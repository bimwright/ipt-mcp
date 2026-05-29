#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Inventor.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Inventor.Shared.Infrastructure;
using Bimwright.Inventor.Shared.Handlers.Document;

/// <summary>
/// Phase-3 WS3-A registrar: document/core write + query commands. <c>get_document_info</c> is owned by
/// the Phase-2 Core registrar and is intentionally NOT re-added here.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddDocument(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new ListOpenDocumentsHandler());
        add(new NewPartHandler());
        add(new NewAssemblyHandler());
        add(new OpenDocumentHandler());
        add(new SaveDocumentHandler());
        add(new CloseDocumentHandler());
        add(new SetUnitsHandler());
        add(new SetMaterialHandler());
    }
}
#endif
