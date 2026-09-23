// PROTOTYPE — throwaway toast compatibility spike (roadmap Phase 1a).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Inv = Inventor;

namespace ToastSpike
{
    internal static class Probes
    {
        private static SpikeAddIn S => SpikeAddIn.Instance;
        private static Inv.Application App => SpikeAddIn.App;
        private static IntPtr Main => new IntPtr(App.MainFrameHWND);
        private static Inv.PlanarSketch _sketch;

        public static object Run(string probe, JObject a, string id)
        {
            switch (probe)
            {
                case "env": return Env("runtime");
                case "host": return Host(a);
                case "show": return Show(a, id);
                case "state": return State();
                case "close": return Close(a);
                case "snap": return SnapProbe(a, id);
                case "block": return Block(a, id);
                case "sta_ping": return StaPing(a, id);
                case "fg": return Fg();
                case "try_foreground": return new { set = Native.SetForegroundWindow(Main), fg = Fg() };
                case "part_sketch": return PartSketch();
                case "exit_sketch": _sketch?.ExitEdit(); return Fg();
                case "cmd_start": App.CommandManager.ControlDefinitions[(string)a["name"]].Execute2(false); return new { started = (string)a["name"] };
                case "cmd_stop": App.CommandManager.StopActiveCommand(); return Fg();
                case "close_doc": return CloseDoc();
                case "minimize": return new { r = Native.ShowWindow(Main, Native.SW_MINIMIZE) };
                case "restore": return new { r = Native.ShowWindow(Main, Native.SW_RESTORE) };
                case "sta_windows": return Native.ThreadWindows(S.StaTid, true);
                case "modal_close": return ModalClose();
                case "ribbon_state": return S.Ribbon?.State((bool?)a["exercise"] ?? false);
                case "dpi_toasts": return DpiToasts(a, id);
                case "ribbon_activate": return S.Ribbon?.ActivateTab((string)a["ribbon"]);
                case "theme_info": return ThemeInfo();
                case "theme_set": return ThemeSet((string)a["name"]);
                case "retheme": return RethemeAuto(ActiveThemeName());
                case "quit":
                    // never leave the user's theme changed by the survey
                    object restored = null;
                    if (OriginalTheme != null && ActiveThemeName() != OriginalTheme) restored = ThemeSet("original");
                    // after this probe's result is written: exercise Deactivate via a clean, prompt-free quit
                    S.Marshaller.BeginInvoke(new Action(() => { App.SilentOperation = true; App.Quit(); }));
                    return new { quitting = true, theme_restored = restored };
                default: throw new ArgumentException("unknown probe " + probe);
            }
        }

        // ---------- Q1 / Q4: environment ----------
        public static object Env(string stage)
        {
            var cur = System.Windows.Application.Current;
            object main = null;
            try { main = Native.Win(Main); } catch (Exception ex) { main = ex.Message; }
            string theme = null;
            try { theme = App.ThemeManager.ActiveTheme.Name; } catch (Exception ex) { theme = "ERR " + ex.Message; }
            int pdpi = -1;
            try { Native.GetProcessDpiAwareness(IntPtr.Zero, out pdpi); } catch { }
            return new
            {
                stage,
                app_current = cur == null ? null : cur.GetType().FullName,
                app_current_thread = cur?.Dispatcher.Thread.ManagedThreadId,
                wpf_dispatcher_on_this_thread = Dispatcher.FromThread(Thread.CurrentThread) != null,
                thread = new { managed = Thread.CurrentThread.ManagedThreadId, win32 = Native.GetCurrentThreadId(), apartment = Thread.CurrentThread.GetApartmentState().ToString() },
                framework = RuntimeInformation.FrameworkDescription,
                is64 = Environment.Is64BitProcess,
                load_context = Alc(typeof(Probes)),
                wpf_load_context = Alc(typeof(System.Windows.Window)),
                process_dpi_awareness = pdpi,              // 0 unaware, 1 system, 2 per-monitor
                thread_dpi_awareness = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()),
                wpf_render_tier = System.Windows.Media.RenderCapability.Tier >> 16,
                main, monitors = Native.Monitors(), theme,
                visible = Safe(() => App.Visible),
                version = Safe(() => App.SoftwareVersion.DisplayVersion),
            };
        }

        private static object Safe(Func<object> f) { try { return f(); } catch (Exception ex) { return "ERR " + ex.Message; } }

        private static string Alc(Type t)
        {
#if NET
            var c = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(t.Assembly);
            return c == null ? null : c.Name + " (" + c.GetType().FullName + ")";
#else
            return "n/a (net48)";
#endif
        }

        // ---------- Q1 / Q2: hosts ----------
        private static ToastHost GetHost(string mode, bool create)
        {
            if (mode == "A") { if (S.HostA == null && create) S.HostA = ToastHost.CreateOnCurrentThread(); return S.HostA; }
            if (S.HostB == null && create) S.HostB = ToastHost.CreateDedicated();
            return S.HostB;
        }

        private static object Host(JObject a)
        {
            var mode = (string)a["mode"];
            var h = GetHost(mode, true);
            int? threadAwareness = null;
            h.D.Invoke(() => { threadAwareness = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()); });
            return new
            {
                mode, managed_tid = h.Thread.ManagedThreadId, win32_tid = h.Win32ThreadId, host_thread_dpi_awareness = threadAwareness,
                app_current_after = System.Windows.Application.Current?.GetType().FullName,
            };
        }

        private static ToastWin ShowVia(ToastHost h, string id, string title, string body, bool noActivate, bool animate, IntPtr owner, int? x, int? y, Palette palette = null, bool autoTheme = false)
        {
            if (h.Mode == "A") return h.ShowOnThisThread(id, title, body, noActivate, animate, owner, x, y, palette, autoTheme);
            ToastWin w = null;
            h.D.Invoke(() => { w = h.ShowOnThisThread(id, title, body, noActivate, animate, owner, x, y, palette, autoTheme); }, TimeSpan.FromSeconds(5));
            return w;
        }

        // ---------- theme survey ----------
        internal static string OriginalTheme;
        internal static int OptionChangeEvents;
        internal static readonly List<object> OptionChangeLog = new List<object>();
        private static readonly string[] CompNames =
        {
            "Background", "ApplicationBackground", "Ribbon", "RibbonBackground", "Browser", "BrowserBackground", "Panel",
            "PanelBackground", "Frame", "ApplicationFrame", "Window", "WindowBackground", "Text", "Foreground", "Accent",
            "Highlight", "StatusBar", "Toolbar", "Tab", "Dialog", "DialogBackground", "Canvas", "ViewBackground", "Border",
        };

        internal static string ActiveThemeName() { try { return App.ThemeManager.ActiveTheme.Name; } catch { return null; } }

        private static object ThemeInfo()
        {
            var tm = App.ThemeManager;
            var active = tm.ActiveTheme.Name;
            if (OriginalTheme == null) OriginalTheme = active;
            var names = new List<string>();
            foreach (Inv.Theme t in tm.Themes) names.Add(t.Name);
            string frame;
            try { frame = App.ColorSchemes.ApplicationFrameColor.ToString(); } catch (Exception ex) { frame = "ERR " + ex.Message; }
            var comps = new Dictionary<string, string>();
            foreach (var n in CompNames)
            {
                try { var c = tm.GetComponentThemeColor(n); comps[n] = "#" + c.Red.ToString("X2") + c.Green.ToString("X2") + c.Blue.ToString("X2"); }
                catch (Exception ex) { comps[n] = "ERR " + ex.Message.Split('\n')[0]; }
            }
            lock (OptionChangeLog)
                return new { active, original = OriginalTheme, themes = names, application_frame_color = frame, option_change_events = OptionChangeEvents, option_change_log = OptionChangeLog.ToList(), component_colors = comps };
        }

        internal static object ThemeSet(string want)
        {
            var tm = App.ThemeManager;
            var before = tm.ActiveTheme.Name;
            if (OriginalTheme == null) OriginalTheme = before;
            if (want == "original") want = OriginalTheme;
            else if (want == "other") foreach (Inv.Theme t in tm.Themes) if (t.Name != before) { want = t.Name; break; }
            Inv.Theme target = null;
            foreach (Inv.Theme t in tm.Themes) if (t.Name == want) target = t;
            if (target == null) return new { ok = false, error = "theme not found: " + want };
            var ev0 = OptionChangeEvents;
            var sw = Stopwatch.StartNew();
            target.Activate();
            return new { ok = true, before, requested = want, after = tm.ActiveTheme.Name, activate_ms = sw.ElapsedMilliseconds, option_change_events_during = OptionChangeEvents - ev0 };
        }

        /// Re-apply the inverse palette to every auto-themed toast (non-blocking).
        internal static object RethemeAuto(string theme)
        {
            var p = Palette.Inverse(theme);
            var n = 0;
            foreach (var h in Hosts())
                foreach (var t in h.Toasts.Values.Where(t => t.AutoTheme).ToList())
                {
                    n++;
                    h.D.BeginInvoke(new Action(() => t.ApplyPalette(p)));
                }
            return new { theme, palette = p.Name, toasts = n };
        }

        private static Palette PaletteFor(string name) => name == "auto-inverse" ? Palette.Inverse(ActiveThemeName()) : Palette.Get(name);

        private static object Show(JObject a, string id)
        {
            var mode = (string)a["mode"] ?? "B";
            var h = GetHost(mode, true);
            var owned = (bool?)a["owned"] ?? true;
            var noAct = (bool?)a["noActivate"] ?? true;
            var animate = (bool?)a["animate"] ?? false;
            int? x = (int?)a["x"], y = (int?)a["y"];
            if (x == null && (string)a["anchor"] == "main")
            {
                // stack top-left of the Inventor main frame, like rvt-mcp's placement
                Native.GetWindowRect(Main, out var mr);
                var n = h.Toasts.Count;
                var dpi = Native.GetDpiForWindow(Main);
                x = mr.L + (int)(16 * dpi / 96.0);
                y = mr.T + (int)((150 + n * 90) * dpi / 96.0);
            }
            var fgBefore = Native.GetForegroundWindow();
            var paletteName = (string)a["palette"] ?? "dark";
            var w = ShowVia(h, (string)a["tid"] ?? id, (string)a["title"] ?? ("Toast " + mode), (string)a["body"] ?? id, noAct, animate, owned ? Main : IntPtr.Zero, x, y,
                PaletteFor(paletteName), paletteName == "auto-inverse");
            var fgAfter = Native.GetForegroundWindow();
            return new
            {
                mode, owned, noActivate = noAct, toast = Native.Win(w.Hwnd),
                fg_before = Native.Win(fgBefore), fg_after = Native.Win(fgAfter), toast_is_foreground = fgAfter == w.Hwnd,
                sta_gui = Native.GuiThread(S.StaTid), host_gui = Native.GuiThread(h.Win32ThreadId),
            };
        }

        private static IEnumerable<ToastHost> Hosts() => new[] { S.HostA, S.HostB }.Where(h => h != null);

        private static object State()
        {
            var fg = Native.GetForegroundWindow();
            return new
            {
                hosts = Hosts().Select(h => new
                {
                    h.Mode,
                    toasts = h.Toasts.Values.Select(t => new { t.Id, win = Native.Win(t.Hwnd), is_foreground = fg == t.Hwnd, hung = Native.IsHungAppWindow(t.Hwnd), expected_px_width = ToastWin.CardWidth * Native.GetDpiForWindow(t.Hwnd) / 96.0 }).ToList(),
                    gui = Native.GuiThread(h.Win32ThreadId),
                }).ToList(),
                fg = Fg(),
            };
        }

        private static object Close(JObject a)
        {
            var n = 0;
            foreach (var h in Hosts())
            {
                var list = h.Toasts.Values.ToList();
                n += list.Count;
                if (h.Mode == "A") foreach (var t in list) t.Close();
                else h.D.Invoke(() => { foreach (var t in list) t.Close(); }, TimeSpan.FromSeconds(5));
            }
            return new { closed = n };
        }

        // ---------- screenshots ----------
        private static Native.RECT ToastUnion()
        {
            var rects = Hosts().SelectMany(h => h.Toasts.Values).Select(t => { Native.GetWindowRect(t.Hwnd, out var r); return r; }).Where(r => r.W > 0).ToList();
            if (rects.Count == 0) return default;
            return new Native.RECT { L = rects.Min(r => r.L) - 20, T = rects.Min(r => r.T) - 20, R = rects.Max(r => r.R) + 20, B = rects.Max(r => r.B) + 20 };
        }

        public static string Snap(string name, Native.RECT r)
        {
            if (r.W <= 0 || r.H <= 0) return null;
            using (var bmp = new Bitmap(r.W, r.H))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.L, r.T, 0, 0, new Size(r.W, r.H));
                var p = Path.Combine(Log.Evidence, name + ".png");
                bmp.Save(p, ImageFormat.Png);
                return p;
            }
        }

        private static object SnapProbe(JObject a, string id)
        {
            var what = (string)a["what"] ?? "toasts";
            var name = (string)a["name"] ?? id;
            Native.RECT r;
            if (what == "main_top") { Native.GetWindowRect(Main, out r); r.B = r.T + 260; }
            else if (what == "main") Native.GetWindowRect(Main, out r);
            else if (what == "virtual") { var v = System.Windows.Forms.SystemInformation.VirtualScreen; r = new Native.RECT { L = v.Left, T = v.Top, R = v.Right, B = v.Bottom }; }
            else r = ToastUnion();
            return new { what, file = Snap(name, r), rect = new[] { r.L, r.T, r.W, r.H } };
        }

        // ---------- Q2 / Q3: STA blocked (simulates a long send_code) ----------
        private static object Block(JObject a, string id)
        {
            var ms = (int?)a["ms"] ?? 8000;
            var mode = (string)a["mode"] ?? "B";
            var h = GetHost(mode, false);
            var newAt = (int?)a["newToastAtMs"] ?? -1;
            var newOwned = (bool?)a["newOwned"] ?? true;
            var snapAt = (int?)a["snapAtMs"] ?? ms / 2;
            var main = Main;
            var hwnds = h?.Toasts.Values.Select(t => t.Hwnd).ToList() ?? new List<IntPtr>();
            var t0 = Stopwatch.StartNew();
            long blockEnd = -1;

            var th = new Thread(() =>
            {
                var samples = new JArray();
                var ops = new ConcurrentBag<object>();
                object newToast = null; bool snappedMid = false, snappedAfter = false, newDone = false;
                var files = new List<string>();
                var k = 0;
                while (t0.ElapsedMilliseconds < ms + 2000)
                {
                    var at = t0.ElapsedMilliseconds;
                    if (h != null)
                    {
                        var kk = k++;
                        var q = t0.ElapsedMilliseconds;
                        h.D.BeginInvoke(new Action(() =>
                        {
                            var ex = t0.ElapsedMilliseconds;
                            ops.Add(new { k = kk, queued = q, executed = ex, lag = ex - q });
                            foreach (var t in h.Toasts.Values) t.SetBody("tick " + kk + " @" + ex + " ms");
                        }));
                    }
                    var per = hwnds.Select(w =>
                    {
                        var sw = Stopwatch.StartNew();
                        var r = Native.SendMessageTimeout(w, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 300, out _);
                        return new { hung = Native.IsHungAppWindow(w), wm_null_ok = r != IntPtr.Zero, wm_null_ms = sw.ElapsedMilliseconds };
                    }).ToList();
                    samples.Add(JToken.FromObject(new { at, main_hung = Native.IsHungAppWindow(main), toasts = per }));
                    if (!snappedMid && at >= snapAt) { snappedMid = true; files.Add(Snap(id + "-mid", ToastUnion())); }
                    if (!snappedAfter && blockEnd > 0 && at >= blockEnd + 1200) { snappedAfter = true; files.Add(Snap(id + "-after", ToastUnion())); }
                    if (!newDone && newAt >= 0 && at >= newAt && h != null)
                    {
                        newDone = true;
                        var sw = Stopwatch.StartNew();
                        string err = null; var status = "";
                        try
                        {
                            Native.RECT fr = default;
                            if (hwnds.Count > 0) Native.GetWindowRect(hwnds[0], out fr);
                            var op = h.D.BeginInvoke(new Action(() => h.ShowOnThisThread(id + "-new", "New toast during block", "created while Inventor STA was blocked", true, false, newOwned ? main : IntPtr.Zero, fr.L, fr.B + 10)));
                            status = op.Wait(TimeSpan.FromSeconds(4)).ToString();
                        }
                        catch (Exception ex) { err = ex.Message; }
                        newToast = new { owned = newOwned, status, wait_ms = sw.ElapsedMilliseconds, at_ms = at, err };
                    }
                    Thread.Sleep(250);
                }
                Log.Result(id + "-watch", "block-watch", new
                {
                    mode, ms, block_end_ms = blockEnd, samples,
                    ops = ops.OrderBy(o => (int)o.GetType().GetProperty("k").GetValue(o)).ToList(),
                    new_toast = newToast, files,
                }, t0.ElapsedMilliseconds);
            }) { IsBackground = true, Name = "ToastSpike.Watch" };
            th.Start();
            Thread.Sleep(ms);   // Inventor STA busy — same effect as a long send_code
            blockEnd = t0.ElapsedMilliseconds;
            return new { ok = true, blocked_ms = ms, mode, toasts = hwnds.Count };
        }

        // ---------- STA queue latency (what inventor_health / every command pays) ----------
        private static object StaPing(JObject a, string id)
        {
            var n = (int?)a["n"] ?? 40;
            var every = (int?)a["every"] ?? 100;
            var label = (string)a["label"] ?? "";
            var th = new Thread(() =>
            {
                var res = new List<double>();
                for (var i = 0; i < n; i++)
                {
                    var sw = Stopwatch.StartNew();
                    using (var done = new ManualResetEventSlim(false))
                    {
                        S.Marshaller.BeginInvoke(new Action(() => done.Set()));
                        done.Wait(10000);
                    }
                    res.Add(sw.Elapsed.TotalMilliseconds);
                    Thread.Sleep(every);
                }
                var sorted = res.OrderBy(x => x).ToList();
                Log.Result(id + "-ping", "sta_ping", new
                {
                    label, n,
                    p50 = sorted[sorted.Count / 2], p95 = sorted[(int)(sorted.Count * 0.95)], max = sorted.Last(), mean = res.Average(),
                    samples = res.Select(x => Math.Round(x, 2)).ToList(),
                }, 0);
            }) { IsBackground = true, Name = "ToastSpike.Ping" };
            th.Start();
            return new { started = true, n, every, label };
        }

        // ---------- Q6: focus ----------
        private static object Fg()
        {
            var fg = Native.GetForegroundWindow();
            string edit = null, cmd = null, doc = null;
            try
            {
                var o = App.ActiveEditObject;
                edit = o is Inv.PlanarSketch ? "PlanarSketch" : o is Inv.Document d ? "Document:" + d.DisplayName : o?.GetType().Name;
            }
            catch (Exception ex) { edit = "ERR " + ex.Message; }
            try { cmd = App.CommandManager.ActiveCommand; } catch (Exception ex) { cmd = "ERR " + ex.Message; }
            try { doc = App.ActiveDocument?.DisplayName; } catch { }
            return new
            {
                foreground = Native.Win(fg), fg_is_main = fg == Main, sta_gui = Native.GuiThread(S.StaTid),
                active_edit = edit, active_command = cmd, active_doc = doc,
            };
        }

        private static object PartSketch()
        {
            var tpl = App.FileManager.GetTemplateFile(Inv.DocumentTypeEnum.kPartDocumentObject);
            var doc = (Inv.PartDocument)App.Documents.Add(Inv.DocumentTypeEnum.kPartDocumentObject, tpl, true);
            var cd = doc.ComponentDefinition;
            _sketch = cd.Sketches.Add(cd.WorkPlanes[3]);
            _sketch.Edit();
            return Fg();
        }

        private static object CloseDoc()
        {
            var d = App.ActiveDocument;
            var name = d?.DisplayName;
            d?.Close(true);
            return new { closed = name };
        }

        private static object ModalClose()
        {
            var dlg = Native.FindThreadWindow(S.StaTid, "#32770");
            var info = Native.Win(dlg);
            if (dlg != IntPtr.Zero) Native.PostMessage(dlg, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return new { closed = info };
        }

        // ---------- Q4: DPI per monitor ----------
        private static object DpiToasts(JObject a, string id)
        {
            var mode = (string)a["mode"] ?? "B";
            var owned = (bool?)a["owned"] ?? false;
            var h = GetHost(mode, true);
            var list = new List<object>();
            var i = 0;
            foreach (var m in Native.MonitorRects())
            {
                var w = ShowVia(h, id + "-m" + i, "Monitor " + i, "at " + m.L + "," + m.T + " " + m.W + "x" + m.H, true, false, owned ? Main : IntPtr.Zero, m.L + 60, m.T + 60);
                list.Add(new { monitor = new[] { m.L, m.T, m.W, m.H }, toast = Native.Win(w.Hwnd) });
                i++;
            }
            return list;
        }
    }
}
