// Polyfill so C# `init`-only properties and `record` types compile on .NET Framework 4.8
// (the Inventor 2022-2024 add-ins target net48 and compile the shared glob, which includes
// InventorCommandContext's `init` accessors). The type already exists on .NET 5+, so this is
// compiled only on older targets to avoid a duplicate-definition error.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Reserved compiler infrastructure type that enables init-only setters on net48.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
