using Bimwright.Ipt.Shared.Plugin;
using Bimwright.Ipt.Shared.Views.Toast;
using Forms = System.Windows.Forms;

namespace Bimwright.Ipt.Toast.Wpf.Tests;

public sealed class ToastPositionFormTests
{
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
}
