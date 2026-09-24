#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InvApi = global::Inventor;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>
/// "Bimwright ▸ MCP" ribbon panel in ZeroDoc/Part/Assembly/Drawing (spike 1a verified these four):
/// a <b>Toasts</b> toggle (ButtonDefinition.Pressed, no idle polling), a <b>Status</b> dialog,
/// and a <b>History</b> button showing the session call count (rvt-mcp parity).
/// Inventor STA only. Rebuilt when the user resets the ribbon.
/// </summary>
internal sealed class BimwrightRibbon
{
    private static readonly string[] RibbonNames = { "ZeroDoc", "Part", "Assembly", "Drawing" };
    private const string TabId = "Bimwright_Ipt_Tab";
    private const string PanelId = "Bimwright_Ipt_Panel";
    private const string ToggleId = "Bimwright_Ipt_ToastToggle";
    private const string StatusId = "Bimwright_Ipt_Status";
    private const string HistoryId = "Bimwright_Ipt_History";

    private readonly InvApi.Application _app;
    private readonly string _clientId;
    private readonly Func<bool> _isOn;
    private readonly Action<bool> _setOn;
    private readonly Func<string> _statusText;
    private readonly Action _onHistory;
    private readonly Func<int> _historyCount;
    private InvApi.ButtonDefinition? _toggle;
    private InvApi.ButtonDefinition? _status;
    private InvApi.ButtonDefinition? _history;
    private readonly List<InvApi.CommandControl> _historyControls = new();
    private int _lastHistoryCount = -1;
    private InvApi.UserInterfaceEvents? _uiEvents;

    public BimwrightRibbon(InvApi.Application app, string clientId, Func<bool> isOn, Action<bool> setOn,
        Func<string> statusText, Action onHistory, Func<int> historyCount)
    {
        _app = app;
        _clientId = clientId;
        _isOn = isOn;
        _setOn = setOn;
        _statusText = statusText;
        _onHistory = onHistory;
        _historyCount = historyCount;
    }

    /// <summary>Idempotent: also used after a ribbon reset.</summary>
    public void Build()
    {
        var defs = _app.CommandManager.ControlDefinitions;
        if (_toggle == null)
        {
            _toggle = Existing(defs, ToggleId) ?? defs.AddButtonDefinition(
                "Toasts", ToggleId, InvApi.CommandTypesEnum.kQueryOnlyCmdType, _clientId,
                "Show or hide Bimwright MCP activity toasts",
                "Each MCP command the agent runs shows a short notification over the Inventor window.",
                RibbonIcons.Letter('T', 16, Color.SteelBlue), RibbonIcons.Letter('T', 32, Color.SteelBlue),
                InvApi.ButtonDisplayEnum.kAlwaysDisplayText);
            _toggle.OnExecute += OnToggle;
        }
        if (_status == null)
        {
            _status = Existing(defs, StatusId) ?? defs.AddButtonDefinition(
                "Status", StatusId, InvApi.CommandTypesEnum.kQueryOnlyCmdType, _clientId,
                "Bimwright MCP status",
                "Target, transport, send_code / read-only gates, toast settings and privacy notes.",
                RibbonIcons.Letter('S', 16, Color.SeaGreen), RibbonIcons.Letter('S', 32, Color.SeaGreen),
                InvApi.ButtonDisplayEnum.kAlwaysDisplayText);
            _status.OnExecute += OnStatus;
        }
        // History caption carries the live count ("History (N)"). Inventor DisplayName is
        // creation-time only, so the definition is (re)made with the current count baked in.
        _historyControls.Clear();
        RebuildHistoryDefinition(HistoryCaption(_historyCount()));
        _toggle.Pressed = _isOn();
        _lastHistoryCount = _historyCount();

        var ui = _app.UserInterfaceManager;
        foreach (var name in RibbonNames)
        {
            try
            {
                var tabs = ui.Ribbons[name].RibbonTabs;
                var tab = FindTab(tabs, TabId + "_" + name) ?? tabs.Add("Bimwright", TabId + "_" + name, _clientId, "", false, false);
                var panel = FindPanel(tab.RibbonPanels, PanelId + "_" + name) ?? tab.RibbonPanels.Add("MCP", PanelId + "_" + name, _clientId, "", false);
                if (panel.CommandControls.Count == 0)
                {
                    panel.CommandControls.AddButton(_toggle, true, true, "", false);
                    panel.CommandControls.AddButton(_status, true, true, "", false);
                    _historyControls.Add(panel.CommandControls.AddButton(_history, true, true, "", false));
                }
            }
            catch
            {
                // one environment failing must not block the others
            }
        }

        if (_uiEvents == null)
        {
            _uiEvents = ui.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += OnResetRibbonInterface;
        }
    }

    public void Remove()
    {
        try { if (_uiEvents != null) _uiEvents.OnResetRibbonInterface -= OnResetRibbonInterface; } catch { }
        foreach (var name in RibbonNames)
        {
            try { FindTab(_app.UserInterfaceManager.Ribbons[name].RibbonTabs, TabId + "_" + name)?.Delete(); } catch { }
        }
        try { if (_toggle != null) { _toggle.OnExecute -= OnToggle; _toggle.Delete(); } } catch { }
        try { if (_status != null) { _status.OnExecute -= OnStatus; _status.Delete(); } } catch { }
        try { if (_history != null) { _history.OnExecute -= OnHistory; _history.Delete(); } } catch { }
        _toggle = null;
        _status = null;
        _history = null;
        _historyControls.Clear();
        _lastHistoryCount = -1;
        _uiEvents = null;
    }

    private void OnToggle(InvApi.NameValueMap context)
    {
        try
        {
            var on = !_toggle!.Pressed;
            _toggle.Pressed = on;
            _setOn(on);
        }
        catch { }
    }

    private void OnStatus(InvApi.NameValueMap context)
    {
        try
        {
            MessageBox.Show(new Win32Owner(new IntPtr(_app.MainFrameHWND)), _statusText(),
                "Bimwright Inventor MCP", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch { }
    }

    /// <summary>"History (N)" — the live session call count, like rvt-mcp's ribbon label.
    /// Inventor has no settable caption on a ribbon control, so the definition + its bound
    /// controls are recreated in place on each count change (STA thread, cheap COM calls).</summary>
    public void SetHistoryCount(int count)
    {
        if (count == _lastHistoryCount) return;
        _lastHistoryCount = count;
        try
        {
            var caption = HistoryCaption(count);
            foreach (var c in _historyControls) { try { c.Delete(); } catch { } }
            _historyControls.Clear();
            RebuildHistoryDefinition(caption);
            foreach (var name in RibbonNames)
            {
                try
                {
                    var tab = FindTab(_app.UserInterfaceManager.Ribbons[name].RibbonTabs, TabId + "_" + name);
                    var panel = tab == null ? null : FindPanel(tab.RibbonPanels, PanelId + "_" + name);
                    if (panel != null)
                        _historyControls.Add(panel.CommandControls.AddButton(_history, true, true, "", false));
                }
                catch { }
            }
        }
        catch { }
    }

    private static string HistoryCaption(int count) => count > 0 ? $"History ({count})" : "History";

    private void RebuildHistoryDefinition(string caption)
    {
        var defs = _app.CommandManager.ControlDefinitions;
        var old = _history ?? Existing(defs, HistoryId);
        if (old != null)
        {
            try { old.OnExecute -= OnHistory; old.Delete(); } catch { }
            _history = null;
        }
        _history = defs.AddButtonDefinition(
            caption, HistoryId, InvApi.CommandTypesEnum.kQueryOnlyCmdType, _clientId,
            "MCP command history",
            "Every MCP command run in this Inventor session — tool, parameters, result, status, duration.",
            RibbonIcons.Letter('H', 16, Color.MediumPurple), RibbonIcons.Letter('H', 32, Color.MediumPurple),
            InvApi.ButtonDisplayEnum.kAlwaysDisplayText);
        _history.OnExecute += OnHistory;
    }

    private void OnHistory(InvApi.NameValueMap context)
    {
        try { _onHistory(); } catch { }
    }

    private void OnResetRibbonInterface(InvApi.NameValueMap context)
    {
        try { Build(); } catch { }
    }

    private static InvApi.ButtonDefinition? Existing(InvApi.ControlDefinitions defs, string id)
    {
        try { return (InvApi.ButtonDefinition)defs[id]; } catch { return null; }
    }

    private static InvApi.RibbonTab? FindTab(InvApi.RibbonTabs tabs, string id)
    {
        foreach (InvApi.RibbonTab t in tabs)
            if (t.InternalName == id) return t;
        return null;
    }

    private static InvApi.RibbonPanel? FindPanel(InvApi.RibbonPanels panels, string id)
    {
        foreach (InvApi.RibbonPanel p in panels)
            if (p.InternalName == id) return p;
        return null;
    }

    private sealed class Win32Owner : IWin32Window
    {
        public Win32Owner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}

/// <summary>
/// Ribbon icons as OLE IPictureDisp via OleCreatePictureIndirect (spike 1a: the same code renders on
/// net48 / net8 / net10, with no WinForms helper subclass).
/// </summary>
internal static class RibbonIcons
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PICTDESC
    {
        public int cbSizeOfStruct;
        public int picType;
        public IntPtr hbitmap;
        public IntPtr hpal;
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void OleCreatePictureIndirect(
        ref PICTDESC desc, ref Guid riid, bool fOwn, [MarshalAs(UnmanagedType.IUnknown)] out object pic);

    private static Guid _iidPictureDisp = new("7BF80981-BF32-101A-8BBB-00AA00300CAB");

    public static object Letter(char letter, int size, Color background)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var fill = new SolidBrush(background))
        using (var font = new Font("Segoe UI", size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.FillRectangle(fill, 0, 0, size, size);
            g.DrawString(letter.ToString(), font, Brushes.White, new RectangleF(0, 0, size, size), format);
        }
        var desc = new PICTDESC
        {
            cbSizeOfStruct = Marshal.SizeOf(typeof(PICTDESC)),
            picType = 1,                      // PICTYPE_BITMAP
            hbitmap = bmp.GetHbitmap(),       // a copy: the picture owns it (fOwn = true)
            hpal = IntPtr.Zero,
        };
        OleCreatePictureIndirect(ref desc, ref _iidPictureDisp, true, out var pic);
        return pic;
    }
}
#endif
