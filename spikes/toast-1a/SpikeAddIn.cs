// PROTOTYPE — throwaway toast compatibility spike (roadmap Phase 1a).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Inv = Inventor;

namespace ToastSpike
{
    [ComVisible(true)]
    [Guid("5B1D7A0E-3C2F-4E8A-9D61-7A2C0F1E5B11")]
    public class SpikeAddIn : Inv.ApplicationAddInServer
    {
        public const string ClientId = "{5B1D7A0E-3C2F-4E8A-9D61-7A2C0F1E5B11}";
        internal static SpikeAddIn Instance;
        internal static Inv.Application App;
        internal System.Windows.Forms.Control Marshaller;
        internal uint StaTid;
        internal ToastHost HostA, HostB;
        internal SpikeRibbon Ribbon;
        private System.Windows.Forms.Timer _timer;
        private bool _busy;

        public void Activate(Inv.ApplicationAddInSite site, bool firstTime)
        {
            Instance = this;
            App = site.Application;
            Log.Init();
            StaTid = Native.GetCurrentThreadId();
            // Environment BEFORE this add-in creates any WPF object (Application.Current question).
            Log.Event("activate-env", Probes.Env("activate"));

            Marshaller = new System.Windows.Forms.Control();
            _ = Marshaller.Handle;

            try { Ribbon = new SpikeRibbon(); Log.Event("ribbon-build", Ribbon.Build(firstTime)); }
            catch (Exception ex) { Log.Event("ribbon-build", new { ok = false, error = ex.ToString() }); }

            try
            {
                _appEvents = App.ApplicationEvents;
                _appEvents.OnApplicationOptionChange += OnOptionChange;
                Log.Event("option-change-subscribe", new { ok = true, theme = Probes.ActiveThemeName() });
            }
            catch (Exception ex) { Log.Event("option-change-subscribe", new { ok = false, error = ex.ToString() }); }

            _timer = new System.Windows.Forms.Timer { Interval = 250 };
            _timer.Tick += Poll;
            _timer.Start();
            File.WriteAllText(Path.Combine(Log.Root, "ready.json"),
                JsonConvert.SerializeObject(new { pid = Process.GetCurrentProcess().Id, at = DateTime.UtcNow, firstTime }));
        }

        public void Deactivate()
        {
            try { _timer?.Stop(); } catch { }
            HostA?.Shutdown();
            HostB?.Shutdown();
            try { Ribbon?.Remove(); } catch { }
            try { Marshaller?.Dispose(); } catch { }
            try { File.Delete(Path.Combine(Log.Root, "ready.json")); } catch { }
            App = null;
        }

        private Inv.ApplicationEvents _appEvents;

        private void OnOptionChange(Inv.EventTimingEnum timing, Inv.NameValueMap context, out Inv.HandlingCodeEnum handling)
        {
            handling = Inv.HandlingCodeEnum.kEventNotHandled;
            Probes.OptionChangeEvents++;
            var theme = Probes.ActiveThemeName();
            var keys = new List<string>();
            try { for (var i = 1; i <= context.Count; i++) keys.Add(context.Name[i]); } catch (Exception ex) { keys.Add("ERR " + ex.Message); }
            object rethemed = null;
            if (timing == Inv.EventTimingEnum.kAfter) rethemed = Probes.RethemeAuto(theme);
            lock (Probes.OptionChangeLog)
                Probes.OptionChangeLog.Add(new { at = DateTime.UtcNow.ToString("o"), timing = timing.ToString(), theme, keys, rethemed });
        }

        public void ExecuteCommand(int commandID) { }
        public object Automation => null;

        private void Poll(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                foreach (var f in Directory.GetFiles(Log.Inbox, "*.json").OrderBy(x => x))
                {
                    string text;
                    try { text = File.ReadAllText(f); File.Delete(f); } catch { continue; }
                    var cmd = JObject.Parse(text);
                    var id = (string)cmd["id"];
                    var probe = (string)cmd["probe"];
                    var args = cmd["args"] as JObject ?? new JObject();
                    var sw = Stopwatch.StartNew();
                    object res;
                    try { res = Probes.Run(probe, args, id); }
                    catch (Exception ex) { res = new { ok = false, error = ex.ToString() }; }
                    Log.Result(id, probe, res, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) { Log.Event("poll-error", new { error = ex.ToString() }); }
            finally { _busy = false; }
        }
    }

    internal static class Log
    {
        public static string Root, Inbox, Outbox, Evidence;
        private static readonly object Gate = new object();

        public static void Init()
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "ipt-mcp", "toast-spike");
            Inbox = Path.Combine(Root, "inbox"); Outbox = Path.Combine(Root, "outbox"); Evidence = Path.Combine(Root, "evidence");
            foreach (var d in new[] { Root, Inbox, Outbox, Evidence }) Directory.CreateDirectory(d);
        }

        public static void Result(string id, string probe, object res, long ms)
        {
            var jo = new JObject
            {
                ["id"] = id, ["probe"] = probe, ["ms"] = ms, ["at"] = DateTime.UtcNow.ToString("o"),
                ["result"] = res == null ? JValue.CreateNull() : JToken.FromObject(res),
            };
            lock (Gate)
            {
                var tmp = Path.Combine(Outbox, id + ".tmp");
                File.WriteAllText(tmp, jo.ToString());
                var dst = Path.Combine(Outbox, id + ".json");
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
                File.AppendAllText(Path.Combine(Root, "spike-log.jsonl"), jo.ToString(Formatting.None) + "\n");
            }
        }

        public static void Event(string name, object o) => Result(name, name, o, 0);
    }

    internal sealed class SpikeRibbon
    {
        private Inv.ButtonDefinition _toggle, _status;
        private Inv.UserInterfaceEvents _uiEvents;
        public int ToggleExecutes, StatusExecutes, ResetEvents;
        public static readonly string[] RibbonNames = { "ZeroDoc", "Part", "Assembly", "Drawing" };

        private static object Step(string name, Func<object> f)
        {
            try { return new { step = name, ok = true, data = f() }; }
            catch (Exception ex) { return new { step = name, ok = false, data = (object)ex.GetType().FullName + ": " + ex.Message }; }
        }

        private static string TypeInfo(object o) => o == null ? "null" : o.GetType().FullName + (Marshal.IsComObject(o) ? " (COM)" : " (managed)");

        public object Build(bool firstTime)
        {
            var app = SpikeAddIn.App;
            var cds = app.CommandManager.ControlDefinitions;
            var steps = new List<object>();
            object sA = null, lA = null, sO = null, lO = null;
            steps.Add(Step("icon_axhost", () =>
            {
                sA = Icons.ViaAxHost(Icons.Make(16, System.Drawing.Color.SteelBlue, "T"));
                lA = Icons.ViaAxHost(Icons.Make(32, System.Drawing.Color.SteelBlue, "T"));
                return TypeInfo(sA);
            }));
            steps.Add(Step("icon_ole", () =>
            {
                sO = Icons.ViaOle(Icons.Make(16, System.Drawing.Color.SeaGreen, "S"));
                lO = Icons.ViaOle(Icons.Make(32, System.Drawing.Color.SeaGreen, "S"));
                return TypeInfo(sO);
            }));
            steps.Add(Step("def_toggle_axhost_icons", () =>
            {
                _toggle = Existing(cds, "BwSpike_Toast") ?? cds.AddButtonDefinition("Toast (spike)", "BwSpike_Toast",
                    Inv.CommandTypesEnum.kQueryOnlyCmdType, SpikeAddIn.ClientId, "Toggle spike toasts", "Toast toggle — AxHost icons",
                    sA ?? sO, lA ?? lO, Inv.ButtonDisplayEnum.kAlwaysDisplayText);
                _toggle.OnExecute += ctx => { ToggleExecutes++; _toggle.Pressed = !_toggle.Pressed; };
                return new { icons = sA != null ? "axhost" : "ole-fallback", pressed = _toggle.Pressed };
            }));
            steps.Add(Step("def_status_ole_icons", () =>
            {
                _status = Existing(cds, "BwSpike_Status") ?? cds.AddButtonDefinition("Status (spike)", "BwSpike_Status",
                    Inv.CommandTypesEnum.kQueryOnlyCmdType, SpikeAddIn.ClientId, "Spike status", "Status — OleCreatePictureIndirect icons",
                    sO ?? sA, lO ?? lA, Inv.ButtonDisplayEnum.kAlwaysDisplayText);
                _status.OnExecute += ctx => { StatusExecutes++; };
                return new { icons = sO != null ? "ole" : "axhost-fallback" };
            }));
            var ui = app.UserInterfaceManager;
            steps.Add(Step("interface_style", () => ui.InterfaceStyle.ToString()));
            foreach (var name in RibbonNames)
            {
                steps.Add(Step("ribbon_" + name, () =>
                {
                    var r = ui.Ribbons[name];
                    Inv.RibbonTab tab = null;
                    foreach (Inv.RibbonTab t in r.RibbonTabs) if (t.InternalName == "BwSpike_Tab_" + name) tab = t;
                    if (tab == null) tab = r.RibbonTabs.Add("Bimwright Spike", "BwSpike_Tab_" + name, SpikeAddIn.ClientId, "", false, false);
                    Inv.RibbonPanel p = null;
                    foreach (Inv.RibbonPanel x in tab.RibbonPanels) if (x.InternalName == "BwSpike_Panel_" + name) p = x;
                    if (p == null) p = tab.RibbonPanels.Add("Toast spike", "BwSpike_Panel_" + name, SpikeAddIn.ClientId, "", false);
                    if (p.CommandControls.Count == 0)
                    {
                        p.CommandControls.AddButton(_toggle, true, true, "", false);
                        p.CommandControls.AddButton(_status, true, true, "", false);
                    }
                    return new { tab = tab.InternalName, controls = p.CommandControls.Count };
                }));
            }
            steps.Add(Step("reset_event_subscribe", () =>
            {
                _uiEvents = ui.UserInterfaceEvents;
                _uiEvents.OnResetRibbonInterface += ctx => { ResetEvents++; Log.Event("ribbon-reset-event", new { count = ResetEvents }); };
                return true;
            }));
            return new { firstTime, steps };
        }

        private static Inv.ButtonDefinition Existing(Inv.ControlDefinitions cds, string internalName)
        {
            try { return (Inv.ButtonDefinition)cds[internalName]; } catch { return null; }
        }

        public object State(bool exercise)
        {
            var ui = SpikeAddIn.App.UserInterfaceManager;
            var ribbons = new List<object>();
            foreach (var name in RibbonNames)
            {
                try
                {
                    var r = ui.Ribbons[name];
                    int controls = -1;
                    foreach (Inv.RibbonTab t in r.RibbonTabs)
                        if (t.InternalName == "BwSpike_Tab_" + name)
                            foreach (Inv.RibbonPanel p in t.RibbonPanels) controls = p.CommandControls.Count;
                    ribbons.Add(new { name, controls });
                }
                catch (Exception ex) { ribbons.Add(new { name, error = ex.Message }); }
            }
            object exercised = null;
            if (exercise && _toggle != null)
            {
                var before = new { pressed = _toggle.Pressed, executes = ToggleExecutes };
                _toggle.Pressed = true;
                var afterSet = _toggle.Pressed;
                _toggle.Execute();
                var afterExecute = new { pressed = _toggle.Pressed, executes = ToggleExecutes };
                exercised = new { before, pressed_after_set_true = afterSet, after_execute = afterExecute };
            }
            return new
            {
                ribbons, toggle_pressed = _toggle?.Pressed, toggle_executes = ToggleExecutes, status_executes = StatusExecutes,
                reset_events = ResetEvents, exercised,
            };
        }

        public object ActivateTab(string ribbon)
        {
            foreach (Inv.RibbonTab t in SpikeAddIn.App.UserInterfaceManager.Ribbons[ribbon].RibbonTabs)
                if (t.InternalName == "BwSpike_Tab_" + ribbon) { t.Active = true; return new { ribbon, active = t.Active }; }
            return new { ribbon, active = false, error = "tab not found" };
        }

        public void Remove()
        {
            var ui = SpikeAddIn.App.UserInterfaceManager;
            foreach (var name in RibbonNames)
            {
                try
                {
                    foreach (Inv.RibbonTab t in ui.Ribbons[name].RibbonTabs)
                        if (t.InternalName == "BwSpike_Tab_" + name) { t.Delete(); break; }
                }
                catch { }
            }
            try { _toggle?.Delete(); } catch { }
            try { _status?.Delete(); } catch { }
        }
    }
}
