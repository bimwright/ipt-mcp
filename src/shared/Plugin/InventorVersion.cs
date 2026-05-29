namespace Bimwright.Inventor.Shared.Plugin;

/// <summary>
/// The Inventor calendar year of the add-in that compiled this source, resolved from the
/// per-project <c>INVENTORNNNN</c> compile symbol (see each <c>plugin-invNN</c> csproj
/// <c>&lt;DefineConstants&gt;</c>). The server and tests compile this file too (no symbol set),
/// in which case <see cref="Year"/> is <c>0</c>.
/// </summary>
public static class InventorVersion
{
    /// <summary>Inventor calendar year (2022-2027), or <c>0</c> when no version symbol is defined.</summary>
    public static int Year =>
#if INVENTOR2022
        2022
#elif INVENTOR2023
        2023
#elif INVENTOR2024
        2024
#elif INVENTOR2025
        2025
#elif INVENTOR2026
        2026
#elif INVENTOR2027
        2027
#else
        0
#endif
        ;
}
