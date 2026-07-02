#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

public sealed class SetViewOrientationHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_view_orientation";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        var app = (Application)context.Application!;
        var activeView = app.ActiveView;

        if (activeView == null)
        {
            return Fail(context, "NO_DOCUMENT", "No active view to orient");
        }

        string orientation = ((string?)parameters["orientation"] ?? "").Trim().ToLowerInvariant();
        bool fit = (bool?)parameters["fit"] ?? true;

        ViewOrientationTypeEnum orientationType;
        switch (orientation)
        {
            case "front": orientationType = ViewOrientationTypeEnum.kFrontViewOrientation; break;
            case "back": orientationType = ViewOrientationTypeEnum.kBackViewOrientation; break;
            case "top": orientationType = ViewOrientationTypeEnum.kTopViewOrientation; break;
            case "bottom": orientationType = ViewOrientationTypeEnum.kBottomViewOrientation; break;
            case "left": orientationType = ViewOrientationTypeEnum.kLeftViewOrientation; break;
            case "right": orientationType = ViewOrientationTypeEnum.kRightViewOrientation; break;
            case "iso_top_right": orientationType = ViewOrientationTypeEnum.kIsoTopRightViewOrientation; break;
            case "iso_top_left": orientationType = ViewOrientationTypeEnum.kIsoTopLeftViewOrientation; break;
            case "iso_bottom_right": orientationType = ViewOrientationTypeEnum.kIsoBottomRightViewOrientation; break;
            case "iso_bottom_left": orientationType = ViewOrientationTypeEnum.kIsoBottomLeftViewOrientation; break;
            default:
                return Fail(context, "INVALID_ARGUMENT", "Unsupported orientation: " + orientation);
        }

        try
        {
            var camera = activeView.Camera;
            camera.ViewOrientationType = orientationType;
            if (fit)
            {
                camera.Fit();
            }
            camera.Apply();
            activeView.Update();

            return Ok(context, new JObject { ["orientation"] = orientation });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to orient view: " + ex.Message);
        }
    }
}
#endif
