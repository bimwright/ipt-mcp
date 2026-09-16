#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>set_camera</c> — read-only at handler level (view state, not model state; spec F4-P0-4).
/// Positions the active view's camera: <c>eye</c>/<c>target</c> in mm, <c>up</c> direction,
/// <c>perspective</c>, <c>extents_mm</c> (view volume w×h, mm), optional <c>fit</c>. Applies via
/// <see cref="Camera.ApplyWithoutTransition"/> so callers can chain straight into capture_view.
/// Response echoes the applied spec plus a readback of the resolved camera state.
/// </summary>
public sealed class SetCameraHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_camera";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        var app = (Application)context.Application!;
        var activeView = app.ActiveView;
        if (activeView == null)
        {
            return Fail(context, "NO_DOCUMENT", "No active view for set_camera");
        }
        if (!CameraParams.TryParse(parameters, out var spec, out var error))
        {
            return Fail(context, "INVALID_ARGUMENT", error);
        }

        try
        {
            var tg = app.TransientGeometry;
            var camera = activeView.Camera;

            if (spec.Perspective.HasValue) camera.Perspective = spec.Perspective.Value;
            if (spec.Eye is not null)
                camera.Eye = tg.CreatePoint(
                    UnitConvert.MmToCm(spec.Eye[0]), UnitConvert.MmToCm(spec.Eye[1]), UnitConvert.MmToCm(spec.Eye[2]));
            if (spec.Target is not null)
                camera.Target = tg.CreatePoint(
                    UnitConvert.MmToCm(spec.Target[0]), UnitConvert.MmToCm(spec.Target[1]), UnitConvert.MmToCm(spec.Target[2]));
            if (spec.Up is not null)
                camera.UpVector = tg.CreateUnitVector(spec.Up[0], spec.Up[1], spec.Up[2]);
            if (spec.ExtentsMm is not null)
                camera.SetExtents(UnitConvert.MmToCm(spec.ExtentsMm[0]), UnitConvert.MmToCm(spec.ExtentsMm[1]));
            if (spec.Fit) camera.Fit();
            camera.ApplyWithoutTransition();
            activeView.Update();

            // Read back the resolved state — callers verify framing without a capture round-trip.
            camera.GetExtents(out var ew, out var eh);
            var e = camera.Eye; var t = camera.Target; var u = camera.UpVector;
            return Ok(context, new JObject
            {
                ["camera"] = new JObject
                {
                    ["eye_mm"] = new JArray(UnitConvert.CmToMm(e.X), UnitConvert.CmToMm(e.Y), UnitConvert.CmToMm(e.Z)),
                    ["target_mm"] = new JArray(UnitConvert.CmToMm(t.X), UnitConvert.CmToMm(t.Y), UnitConvert.CmToMm(t.Z)),
                    ["up"] = new JArray(u.X, u.Y, u.Z),
                    ["perspective"] = camera.Perspective,
                    ["extents_mm"] = new JArray(UnitConvert.CmToMm(ew), UnitConvert.CmToMm(eh)),
                },
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to set camera: " + ex.Message);
        }
    }
}
#endif
