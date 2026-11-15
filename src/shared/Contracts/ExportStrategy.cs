using System.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Host-free export decisions shared by the drawing export handler and its tests.</summary>
public static class ExportStrategy
{
    /// <summary>
    /// True when the selection is every sheet of the drawing in document order and no sheet is
    /// excluded from printing, so translating the source gives the same pages as a sheet copy.
    /// </summary>
    public static bool IsWholeDocument(string[] selectedSheetIds, string[] documentSheetIds, bool anyExcludedFromPrinting) =>
        !anyExcludedFromPrinting && selectedSheetIds.Length > 0 && selectedSheetIds.SequenceEqual(documentSheetIds);
}
