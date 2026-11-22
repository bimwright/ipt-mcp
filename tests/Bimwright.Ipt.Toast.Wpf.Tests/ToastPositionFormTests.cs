using System.IO;
using Bimwright.Ipt.Shared.Plugin;
using Bimwright.Ipt.Shared.Views.Toast;
using Forms = System.Windows.Forms;

namespace Bimwright.Ipt.Toast.Wpf.Tests;

public sealed class ToastPositionFormTests
{
    [Fact]
    public void Status_offers_the_family_idle_duration_choices()
        => Sta.Run(() =>
        {
            using var form = new ToastPositionForm("Target: fixture", () => new ToastPositionOptions(), _ => true);
            var panel = (Forms.FlowLayoutPanel)form.Controls[0];
            var idle = panel.Controls.OfType<Forms.ComboBox>()
                .SingleOrDefault(combo => combo.Items.Count == 4 && combo.Items[0] is int);
            Assert.NotNull(idle);
            Assert.Equal(new[] { 10, 20, 30, 60 }, idle!.Items.Cast<int>());
            Assert.Equal(20, idle.SelectedItem);
        });

    [Fact]
    public void Idle_duration_is_staged_retries_a_failed_save_and_survives_reopening()
        => Sta.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "ipt-toast-idle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "settings.json");
                Assert.True(ToastConfigStore.SaveIdleSeconds(path, 30));
                using var notifier = new ToastNotifier(true, "ipt-mcp 2027", () => { }, ToastConfigStore.LoadIdleSeconds(path));
                bool Save(int seconds)
                {
                    if (!ToastConfigStore.SaveIdleSeconds(path, seconds)) return false;
                    notifier.SetIdleSeconds(seconds);
                    return true;
                }
                using (var form = new ToastPositionForm("Target: fixture", () => new ToastPositionOptions(), _ => true,
                    () => notifier.IdleSeconds, Save))
                {
                    form.Show();
                    var panel = (Forms.FlowLayoutPanel)form.Controls[0];
                    var idle = (Forms.ComboBox)panel.Controls["ToastIdleSeconds"]!;
                    var apply = (Forms.Button)panel.Controls["ApplyToastIdle"]!;
                    var feedback = panel.Controls["ToastIdleError"]!;
                    Assert.Equal(30, idle.SelectedItem);
                    idle.SelectedItem = 60;
                    Assert.True(apply.Enabled);
                    Assert.Equal(30, notifier.IdleSeconds);
                    Assert.Equal(30, ToastConfigStore.LoadIdleSeconds(path));

                    File.WriteAllText(path, "{ invalid JSON");
                    apply.PerformClick();
                    Assert.Contains("could not be saved", feedback.Text);
                    Assert.True(apply.Enabled);
                    Assert.Equal(30, notifier.IdleSeconds);
                    Assert.Equal("{ invalid JSON", File.ReadAllText(path));

                    File.WriteAllText(path, "{}");
                    apply.PerformClick();
                    Assert.Equal(60, notifier.IdleSeconds);
                    Assert.Equal(60, ToastConfigStore.LoadIdleSeconds(path));
                    Assert.Empty(feedback.Text);
                    Assert.False(apply.Enabled);
                }
                using var restarted = new ToastNotifier(true, "ipt-mcp 2027", () => { }, ToastConfigStore.LoadIdleSeconds(path));
                using var reopened = new ToastPositionForm("Target: fixture", () => new ToastPositionOptions(), _ => true,
                    () => restarted.IdleSeconds, Save);
                Assert.Equal(60, ((Forms.ComboBox)((Forms.FlowLayoutPanel)reopened.Controls[0]).Controls["ToastIdleSeconds"]!).SelectedItem);
            }
            finally { Directory.Delete(directory, true); }
        });

    [Fact]
    public void Status_controls_apply_corner_drag_reset_and_show_save_failure()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new ToastPositionOptions(false, false, false, 25, 40);
                var saveOk = true;
                using var form = new ToastPositionForm("Target: fixture", () => options, value =>
                {
                    options = value;
                    return saveOk;
                });
                form.Show();
                var panel = (Forms.FlowLayoutPanel)form.Controls[0];
                var horizontal = (Forms.ComboBox)panel.Controls[2];
                var vertical = (Forms.ComboBox)panel.Controls[3];
                var drag = (Forms.CheckBox)panel.Controls[4];
                var reset = (Forms.Button)panel.Controls[5];
                Assert.True(options.HasOffset);
                reset.PerformClick();
                Assert.False(options.HasOffset);
                horizontal.SelectedIndex = 1;
                vertical.SelectedIndex = 1;
                Assert.True(options.Right && options.Bottom);
                Assert.False(options.HasOffset);
                drag.Checked = true;
                Assert.True(options.DragEnabled);
                saveOk = false;
                reset.PerformClick();
                Assert.False(options.HasOffset);
                Assert.Contains("could not be saved", panel.Controls[6].Text);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(10000));
        if (error != null) throw error;
    }

    // FC-TST-04
    [Fact]
    public void Pause_checkbox_persists_live_syncs_and_shows_errors()
        => Sta.Run(() =>
        {
            var savedNow = ToastSnooze.Now;
            var dir = Path.Combine(Path.GetTempPath(), "ipt-form-pause-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "iptmcp.config.json");
            try
            {
                ToastSnooze.Now = () => new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Local);
                ToastConfigStore.SaveEnableToast(path, true);
                var enabled = true;
                using (var form = new ToastPositionForm(
                    "Target: fixture",
                    () => new ToastPositionOptions(),
                    _ => true,
                    readToastEnabled: () => enabled,
                    readPaused: () => ToastSnooze.IsActive(path),
                    setPaused: v => v ? ToastSnooze.PauseForToday(path) : ToastSnooze.Resume(path)))
                {
                    form.Show();
                    var panel = (Forms.FlowLayoutPanel)form.Controls[0];
                    var pause = (Forms.CheckBox)panel.Controls["PauseToastsForToday"]!;
                    var snoozeError = (Forms.Label)panel.Controls["ToastSnoozeError"]!;

                    Assert.True(pause.Enabled);
                    Assert.False(pause.Checked);

                    pause.Checked = true;
                    Assert.True(ToastSnooze.IsActive(path));
                    Assert.Equal("", snoozeError.Text);

                    // Background resume updates open dialog checkbox via ToastSnooze.Changed -> BeginInvoke.
                    var done = new ManualResetEventSlim();
                    new Thread(() => { ToastSnooze.Resume(path); done.Set(); }).Start();
                    done.Wait(2000);
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    while (pause.Checked && DateTime.UtcNow < deadline)
                    {
                        Forms.Application.DoEvents();
                        Thread.Sleep(15);
                    }
                    Assert.False(pause.Checked);
                    Assert.False(ToastSnooze.IsActive(path));

                    pause.Checked = true;
                    Assert.True(ToastSnooze.IsActive(path));
                    pause.Checked = false;
                    Assert.False(ToastSnooze.IsActive(path));
                }

                enabled = false;
                using (var disabledForm = new ToastPositionForm(
                    "Target: fixture",
                    () => new ToastPositionOptions(),
                    _ => true,
                    readToastEnabled: () => enabled,
                    readPaused: () => false,
                    setPaused: _ => true))
                {
                    disabledForm.Show();
                    var panel = (Forms.FlowLayoutPanel)disabledForm.Controls[0];
                    var pause = (Forms.CheckBox)panel.Controls["PauseToastsForToday"]!;
                    Assert.False(pause.Enabled);
                }

                enabled = true;
                using (var errorForm = new ToastPositionForm(
                    "Target: fixture",
                    () => new ToastPositionOptions(),
                    _ => true,
                    readToastEnabled: () => enabled,
                    readPaused: () => false,
                    setPaused: _ => false))
                {
                    errorForm.Show();
                    var panel = (Forms.FlowLayoutPanel)errorForm.Controls[0];
                    var pause = (Forms.CheckBox)panel.Controls["PauseToastsForToday"]!;
                    var snoozeError = (Forms.Label)panel.Controls["ToastSnoozeError"]!;
                    pause.Checked = true;
                    Assert.False(pause.Checked);
                    Assert.NotEmpty(snoozeError.Text);
                }
            }
            finally
            {
                ToastSnooze.Now = savedNow;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        });
}
