using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Code;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Bimwright.Ipt.Tests;

public interface GeneralDimension { string Text { get; } object Style { get; } int Precision { get; } }
// Flat like the real interop: a subtype repeats every member of its base.
public interface LinearGeneralDimension { string Text { get; } object Style { get; } int Precision { get; } object IntentOne { get; } object IntentTwo { get; } }
public interface Sheet { string Name { get; } }
public interface DrawingCurve { object Color { get; } DrawingCurveSegments Segments { get; } }
public interface DrawingCurveSegments { int Count { get; } }
public interface DrawingCurveSegment { DrawingCurve Parent { get; } object Layer { get; } }
public interface IRxSheet { object Gizmo { get; } }

public class ApiMemberHintsTests
{
    private const string InteropPath = @"C:\Program Files\Autodesk\Inventor 2027\Bin\Autodesk.Inventor.Interop.dll";

    private static readonly Type[] Universe =
    {
        typeof(GeneralDimension), typeof(LinearGeneralDimension), typeof(Sheet),
        typeof(DrawingCurve), typeof(DrawingCurveSegments), typeof(DrawingCurveSegment), typeof(IRxSheet),
    };

    [Fact]
    public void Neighbour_reachable_through_a_property_names_the_property_not_a_cast()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061", "'DrawingCurveSegment' does not contain a definition for 'Color'", Universe);
        Assert.Contains("'Color' exists on DrawingCurve, a different object (not a cast): use x.Parent.Color.", hint);
        Assert.DoesNotContain("x is", hint);
    }

    [Fact]
    public void Neighbour_not_reachable_gets_neutral_wording()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061", "'DrawingCurve' does not contain a definition for 'Layer'", Universe);
        Assert.StartsWith("'Layer' exists on DrawingCurveSegment, a different object (not a cast).", hint);
        Assert.DoesNotContain("x is", hint);
        Assert.DoesNotContain("and cast", hint);
    }

    [Fact]
    public void IRx_types_are_not_owners()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061", "'Sheet' does not contain a definition for 'Gizmo'", Universe);
        Assert.Contains("No API type has a member 'Gizmo'", hint);
        Assert.DoesNotContain("IRxSheet", hint);
    }

    [Fact]
    public void Missing_member_names_the_subtype_that_has_it()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061",
            "'GeneralDimension' does not contain a definition for 'IntentOne' and no accessible extension method 'IntentOne' accepting a first argument of type 'GeneralDimension' could be found", Universe);
        Assert.NotNull(hint);
        Assert.Contains("'IntentOne' exists on LinearGeneralDimension: check the object with `x is LinearGeneralDimension` and cast", hint);
    }

    [Fact]
    public void Member_that_exists_nowhere_says_so_and_lists_nearest()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061", "'Sheet' does not contain a definition for 'IsActive'", Universe);
        Assert.Contains("No API type has a member 'IsActive'", hint);
        Assert.Contains("Sheet members closest to 'IsActive': Name", hint);
    }

    [Fact]
    public void Unknown_type_or_other_code_gives_no_hint()
    {
        Assert.Null(ApiMemberHints.ForMissingMember("CS1061", "'List<int>' does not contain a definition for 'Foo'", Universe));
        Assert.Null(ApiMemberHints.ForMissingMember("CS0266", "'Sheet' does not contain a definition for 'Foo'", Universe));
    }

    // ---- signatures for argument errors: resolved with the semantic model ---------------------------------------

    /// <summary>Stand-ins in namespace Inventor: two unrelated Add methods, and a Point passed where a Vector is wanted.</summary>
    private const string StandIns = @"
namespace Inventor
{
    public enum DocumentTypeEnum { kPartDocumentObject = 12290 }
    public interface Document { }
    public interface Face { }
    public interface Point { }
    public interface Vector { }
    public interface NameValueMap { void Add(string Name, object Value); }
    public interface BrowserFolder { void Add(object Node); }
    public interface Documents { Document Add(DocumentTypeEnum DocumentType, string TemplateFileName = """", bool CreateVisible = true); }
    public interface Application { Documents Documents { get; } }
    public interface TransientGeometry { Point CreatePoint(double XCoord = 0, double YCoord = 0, double ZCoord = 0); }
    public interface Matrix { void SetTranslation(Vector Translation, bool ResetRotation = false); }
    public interface SurfaceBody { object get_IsPointInside(ref double[] Point, bool UseBox = false); }
}";

    [Fact]
    public void Signature_names_the_called_member_not_another_with_the_same_name()
    {
        var hint = Hint("Application app = null; var d = app.Documents.Add(\"part\");");
        Assert.Equal("Signature: Documents.Add(DocumentTypeEnum DocumentType, string TemplateFileName = optional, bool CreateVisible = optional) -> Document.", hint);
    }

    [Theory]
    [InlineData("var list = new List<Face>(); list.Add(1, 2);")]
    [InlineData("Matrix m = null; m.SetTranslation(new V());")]          // CS7036 on the constructor, inside an API call
    [InlineData("Matrix m = null; m.SetTranslation(new V(1).Make(2));")] // CS1501 on a script method, inside an API call
    public void Non_api_member_gets_no_signature(string script)
    {
        Assert.Null(Hint("class V : Vector { public V(int x) {} public Vector Make() => this; }\n" + script));
    }

    [Theory]
    [InlineData("m.SetTranslation(tg.CreatePoint(0, 0, 0));")]
    [InlineData("m.SetTranslation(list[0]);")]
    public void Argument_error_names_the_call_that_takes_the_argument(string call)
    {
        var hint = Hint("TransientGeometry tg = null; Matrix m = null; var list = new List<Point>();\n" + call);
        Assert.Equal("Signature: Matrix.SetTranslation(Vector Translation, bool ResetRotation = optional) -> void.", hint);
    }

    [Fact]
    public void Ref_parameter_shows_its_type_and_how_to_pass_it()
    {
        var hint = Hint("SurfaceBody b = null; Point p = null; var r = b.get_IsPointInside(ref p);");
        Assert.NotNull(hint);
        Assert.Contains("SurfaceBody.get_IsPointInside(ref double[] Point, bool UseBox = optional) -> object", hint);
        Assert.Contains("then pass ref p", hint);
    }

    /// <summary>Cases from real sessions against the real Inventor 2027 interop when it is installed on this machine.</summary>
    [Fact]
    public void Real_interop_field_cases()
    {
        if (!File.Exists(InteropPath)) return;
        var universe = Assembly.LoadFrom(InteropPath).GetExportedTypes().Where(t => t.Namespace == "Inventor").ToArray();

        string? Missing(string type, string member) =>
            ApiMemberHints.ForMissingMember("CS1061", $"'{type}' does not contain a definition for '{member}'", universe);

        // Real subtypes keep the cast advice.
        Assert.Contains("'IntentOne' exists on LinearGeneralDimension, AngularGeneralDimension: check the object with `x is LinearGeneralDimension` and cast", Missing("GeneralDimension", "IntentOne"));
        var intentHint = Missing("GeneralDimension", "Intent");
        Assert.Contains("RadiusGeneralDimension", intentHint);
        Assert.Contains("DiameterGeneralDimension", intentHint);
        Assert.Contains("and cast", intentHint);
        foreach (var m in new[] { "HasConnectedTexture", "ConnectedTexture" })
        {
            var asset = Missing("AssetValue", m);
            Assert.Contains("FloatAssetValue", asset);
            Assert.Contains("ColorAssetValue", asset);
            Assert.Contains("and cast", asset);
        }
        Assert.StartsWith("'SketchToSheetSpace' exists on DrawingSketch, not on DrawingView.", Missing("DrawingView", "SketchToSheetSpace"));

        // Neighbours by name are different objects: no cast toward them.
        var color = Missing("DrawingCurveSegment", "Color");
        Assert.Contains("use x.Parent.Color", color);
        foreach (var (type, member, neighbour) in new[]
        {
            ("DrawingCurveSegment", "Color", "DrawingCurve"), ("DrawingCurve", "Layer", "DrawingCurveSegment"),
            ("Cell", "FormattedText", "HoleTableCell"), ("SketchedSymbol", "Visible", "Sketch"),
            ("ComponentOccurrencesEnumerator", "ItemByName", "ComponentOccurrences"), ("Face", "RangeBox", "FaceShell"),
            ("Documents", "Item", "DocumentsEnumerator"),
        })
        {
            var hint = Missing(type, member);
            Assert.NotNull(hint);
            Assert.DoesNotContain($"x is {neighbour}`", hint);
            Assert.DoesNotContain($"(({neighbour})x)", hint);
            Assert.DoesNotContain("IRx", hint);
        }
        Assert.StartsWith("'Item' is the indexer of Documents: use x[index].", Missing("Documents", "Item"));

        var lumps = ApiMemberHints.ForMissingMember("CS1061", "'SurfaceBody' does not contain a definition for 'Lumps'", universe);
        Assert.Contains("No API type has a member 'Lumps'", lumps);

        var interop = MetadataReference.CreateFromFile(InteropPath);
        var add = Hint("Application app = null; var d = app.Documents.Add(\"part\");", interop);
        Assert.StartsWith("Signature: Documents.Add(", add);
        Assert.DoesNotContain("NameValueMap", add);

        Assert.Null(Hint("var list = new List<Face>(); list.Add(1, 2);", interop));

        foreach (var call in new[] { "m.SetTranslation(tg.CreatePoint(0, 0, 0));", "m.SetTranslation(list[0]);" })
        {
            var hint = Hint("TransientGeometry tg = null; Matrix m = null; var list = new List<Point>();\n" + call, interop);
            Assert.StartsWith("Signature: Matrix.SetTranslation(", hint);
            Assert.DoesNotContain("CreatePoint", hint);
        }

        var inside = Hint("SurfaceBody b = null; Point p = null; var r = b.IsPointInside[ref p];", interop);
        Assert.Contains("SurfaceBody.IsPointInside[ref double[] Point", inside);
        Assert.Contains("then pass ref p", inside);
        var count = Hint("SurfaceBody b = null; double[] a = null; var r = b.IsPointInside[ref a, true, 3];", interop);
        Assert.StartsWith("Signature: SurfaceBody.IsPointInside[ref double[] Point", count);

        var view = Hint("Sheet sh = null; Document doc = null; sh.DrawingViews.AddBaseView(doc, 1, 2);", interop);
        Assert.StartsWith("Signature: DrawingViews.AddBaseView(", view);
    }

    /// <summary>Compiles a script against <paramref name="api"/> (default: the stand-ins) and returns the first signature hint.</summary>
    private static string? Hint(string script, MetadataReference? api = null)
    {
        api ??= CSharpCompilation.Create("standins_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(StandIns) },
            PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();
        var tree = CSharpSyntaxTree.ParseText(script, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var compilation = CSharpCompilation.CreateScriptCompilation("script", tree, PlatformReferences().Append(api),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: new[] { "System", "System.Collections.Generic", "System.Linq", "Inventor" }),
            returnType: typeof(object));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        return errors.Select(d => ScriptSignatureHints.ForDiagnostic(d, compilation)).FirstOrDefault(h => h != null);
    }

    public enum CurveKind { kLine, kArc }
    public interface Body { object this[double[] Point, bool UseBox = false] { get; } void Move(ref double[] at, string name); }

    [Fact]
    public void Api_lists_real_members_with_signatures_and_filters_them()
    {
        var universe = Universe.Concat(new[] { typeof(Body), typeof(CurveKind) }).ToArray();

        var all = ApiMemberHints.Describe("drawingcurvesegment", null, universe);
        Assert.Equal(new[] { "DrawingCurveSegment.Layer -> object (read-only)", "DrawingCurveSegment.Parent -> DrawingCurve (read-only)" }, all);

        Assert.Equal(new[] { "DrawingCurveSegment.Parent -> DrawingCurve (read-only)" }, ApiMemberHints.Describe("Inventor.DrawingCurveSegment", "PARENT", universe));

        var body = ApiMemberHints.Describe("ApiMemberHintsTests.Body", null, universe);
        Assert.Contains("ApiMemberHintsTests.Body.Item[double[] Point, bool UseBox = optional] -> object (read-only)", body);
        Assert.Contains("ApiMemberHintsTests.Body.Move(ref double[] at, string name) -> void", body);

        Assert.Equal(new[] { "ApiMemberHintsTests.CurveKind.kLine", "ApiMemberHintsTests.CurveKind.kArc" }, ApiMemberHints.Describe("CurveKind", null, universe));
    }

    [Fact]
    public void Api_answers_an_unknown_type_or_member_with_the_closest_names()
    {
        var type = Assert.Single(ApiMemberHints.Describe("DrawingCurveSegmant", null, Universe));
        Assert.StartsWith("No API type 'DrawingCurveSegmant'. Closest: DrawingCurveSegment", type);

        var member = Assert.Single(ApiMemberHints.Describe("DrawingCurve", "colour", Universe));
        Assert.StartsWith("No member of DrawingCurve contains 'colour'. Closest: Color", member);

        Assert.Contains("Pass a type name", Assert.Single(ApiMemberHints.Describe(" ", null, Universe)));
    }

    [Fact]
    public void Missing_member_hint_points_to_the_api_lookup()
    {
        var hint = ApiMemberHints.ForMissingMember("CS1061", "'Sheet' does not contain a definition for 'IsActive'", Universe);
        Assert.EndsWith("All members: return Ipt.Api(\"Sheet\");", hint);
    }

    [Fact]
    public void Real_interop_api_lookup()
    {
        if (!File.Exists(InteropPath)) return;
        var universe = Assembly.LoadFrom(InteropPath).GetExportedTypes().Where(t => t.Namespace == "Inventor").ToArray();

        Assert.Contains("SurfaceBody.IsPointInside[ref double[] Point, bool UseBox = optional] -> ContainmentEnum (read-only)", ApiMemberHints.Describe("SurfaceBody", "pointinside", universe));
        Assert.Contains(ApiMemberHints.Describe("SurfaceBody", "shell", universe), l => l.StartsWith("SurfaceBody.FaceShells -> FaceShells"));
        Assert.Contains("DrawingCurve.Color -> Color", ApiMemberHints.Describe("DrawingCurve", "color", universe));
        Assert.Contains("CurveTypeEnum.kLineSegmentCurve", ApiMemberHints.Describe("CurveTypeEnum", null, universe));

        var document = ApiMemberHints.Describe("Document", null, universe);
        Assert.Equal(ApiMemberHints.MaxDescribed + 1, document.Length);
        Assert.Contains("pass a filter", document[document.Length - 1]);
    }

    [Fact]
    public void Reflection_in_a_script_gets_the_api_lookup_hint_only_for_a_v2_add_in()
    {
        const string code = "return typeof(DrawingView).GetProperties().Select(p => p.Name).ToArray();";
        Assert.Contains("Ipt.Api(", SendCodeHelperHints.Match(code, v2: true));
        Assert.Null(SendCodeHelperHints.Match(code));
        Assert.Null(SendCodeHelperHints.Match("var x = json.GetProperty(\"a\");", v2: true));
    }

    private static MetadataReference[] PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
}
