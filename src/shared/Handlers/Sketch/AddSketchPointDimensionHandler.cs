#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

public sealed class AddSketchPointDimensionHandler : HandlerBase, IInventorCommand
{
    public string Name => "add_sketch_point_dimension";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out var app, out var part, out var failure)) return failure!;
        SketchPointDimensionInput input;
        try { input = SketchPointDimensionInput.Parse(p); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        return PartMutation.Run(ctx, app, part, Name, () =>
        {
            // Inventor can expose an unidentified idle transaction whose Document throws.
            // The owned transaction tells us whether an actual parent transaction exists.
            if (app.TransactionManager.CurrentTransaction is { HasParentTransaction: true } current
                && !current.ParentTransaction.Document.Equals(part))
                throw new ArgumentException("An active transaction targets another document; add dimensions to that document or finish the transaction first");
            var expected = input.Expression is null ? UnitConvert.MmToCm(input.ValueMm!.Value)
                : PartMutation.Evaluate(part.ComponentDefinition.Parameters, input.Expression, part.UnitsOfMeasure.LengthUnits, "value");
            if (expected <= 0) throw new ArgumentException("value must evaluate to a positive distance; use geometric constraints for zero offsets");
            var sketch = SketchPointReferences.ResolveSketch(part, input.First);
            if (sketch.DeferUpdates) throw new ArgumentException("Sketch solving is deferred; enable sketch updates before adding dimensions");
            var first = SketchPointReferences.ResolvePoint(part, sketch, input.First);
            var second = SketchPointReferences.ResolvePoint(part, sketch, input.Second);
            if (SketchPointReferences.Key(first) == SketchPointReferences.Key(second)) throw new ArgumentException("Select two distinct sketch points");
            var text = input.TextPosition;
            var position = app.TransientGeometry.CreatePoint2d(text is null ? (first.Geometry.X + second.Geometry.X) / 2 : text[0] / 10,
                text is null ? (first.Geometry.Y + second.Geometry.Y) / 2 + 1 : text[1] / 10);
            var orientation = input.Orientation == "horizontal" ? DimensionOrientationEnum.kHorizontalDim
                : input.Orientation == "vertical" ? DimensionOrientationEnum.kVerticalDim : DimensionOrientationEnum.kAlignedDim;
            var dimension = sketch.DimensionConstraints.AddTwoPointDistance(first, second, orientation, position, false);
            var parameter = dimension.Parameter;
            if (input.Expression is null) parameter.Value = expected; else parameter.Expression = input.Expression;
            if (!part.Update2(false) || sketch.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Positional dimension recompute failed");
            var dx = second.Geometry.X - first.Geometry.X; var dy = second.Geometry.Y - first.Geometry.Y;
            var measured = input.Orientation == "horizontal" ? Math.Abs(dx) : input.Orientation == "vertical" ? Math.Abs(dy) : Math.Sqrt(dx * dx + dy * dy);
            var actual = Convert.ToDouble(parameter.ModelValue); var tolerance = Math.Max(1e-7, expected * 1e-8);
            if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > tolerance || Math.Abs(measured - expected) > tolerance)
                throw new InvalidOperationException("Positional dimension geometry/readback differs from the requested distance");
            var result = new JObject { ["sketch_name"] = sketch.Name, ["dimension_name"] = parameter.Name,
                ["orientation"] = input.Orientation, ["value_mm"] = actual * 10, ["measured_distance_mm"] = measured * 10,
                ["point1_mm"] = new JArray(first.Geometry.X * 10, first.Geometry.Y * 10),
                ["point2_mm"] = new JArray(second.Geometry.X * 10, second.Geometry.Y * 10) };
            if (input.Expression is not null) result["expression"] = parameter.Expression;
            return Ok(ctx, result);
        });
    }
}
#endif
