#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// Shared helpers for the export handlers: resolving Inventor's built-in translator add-ins by their
/// stable ClassId GUIDs and running a <c>SaveCopyAs</c> to a target file. The Inventor API exposes
/// STEP / STL / etc. as <see cref="TranslatorAddIn"/> instances looked up via
/// <c>Application.ApplicationAddIns.ItemById(classId)</c>; the actual write is
/// <see cref="TranslatorAddIn.SaveCopyAs"/> against a <see cref="DataMedium"/> whose
/// <c>FileName</c> is the output path.
/// </summary>
internal static class ExportSupport
{
    // Stable Inventor translator ClassId GUIDs (consistent across 2022-2027).
    public const string StepTranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";
    public const string StlTranslatorId  = "{533E9A98-FC3B-11D4-8E7E-0010B541CD80}";
    public const string SatTranslatorId  = "{89162634-02B6-11D5-8E80-0010B541CD80}";

    /// <summary>Look up a built-in translator add-in by ClassId GUID, or throw a friendly error.</summary>
    public static TranslatorAddIn GetTranslator(Application app, string classId, string label)
    {
        try
        {
            var addin = app.ApplicationAddIns.ItemById[classId] as TranslatorAddIn;
            if (addin == null)
                throw new InvalidOperationException($"{label} translator add-in is not available in this Inventor session.");
            if (!addin.Activated)
                addin.Activate();
            return addin;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"failed to resolve {label} translator: {ex.Message}");
        }
    }

    /// <summary>Run a translator export of <paramref name="source"/> (a document) to <paramref name="outputPath"/>.</summary>
    public static void SaveCopyAs(Application app, TranslatorAddIn translator, object source, string outputPath)
        => SaveCopyAs(app, translator, source, outputPath, null);

    /// <summary>
    /// Run a translator export, first populating the options map via
    /// <see cref="TranslatorAddIn.HasSaveCopyAsOptions"/> so translator-defined keys exist, then
    /// applying <paramref name="configure"/> overrides (e.g. SAT <c>Version</c>).
    /// </summary>
    public static void SaveCopyAs(Application app, TranslatorAddIn translator, object source, string outputPath,
        Action<NameValueMap>? configure)
    {
        var to = app.TransientObjects;
        var context = to.CreateTranslationContext();
        context.Type = IOMechanismEnum.kFileBrowseIOMechanism;
        var options = to.CreateNameValueMap();
        // Indexed property in the interop — populates DefaultOptions with the translator's keys.
        _ = translator.HasSaveCopyAsOptions[source, context, options];
        configure?.Invoke(options);
        var medium = to.CreateDataMedium();
        medium.FileName = outputPath;
        translator.SaveCopyAs(source, context, options, medium);
    }
}
#endif
