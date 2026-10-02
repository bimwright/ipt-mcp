#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Drawing;
using System.Windows.Forms;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>Status and toast preferences. Position applies immediately; duration needs Apply.</summary>
internal sealed class ToastPositionForm : Form
{
    public ToastPositionForm(string status, Func<ToastPositionOptions>? read,
        Func<ToastPositionOptions, bool>? save, Func<int>? readIdleSeconds = null,
        Func<int, bool>? saveIdleSeconds = null)
    {
        Text = "Bimwright Inventor MCP";
        Size = new Size(540, 500);
        MinimumSize = new Size(420, 340);
        StartPosition = FormStartPosition.CenterParent;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = status, AutoSize = true, MaximumSize = new Size(475, 0) });
        panel.Controls.Add(new Label { Text = "Toast position", AutoSize = true, Margin = new Padding(3, 16, 3, 4) });
        var horizontal = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        horizontal.Items.AddRange(new object[] { "Left", "Right" });
        var vertical = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        vertical.Items.AddRange(new object[] { "Top", "Bottom" });
        var drag = new CheckBox { Text = "Allow dragging by the title row", AutoSize = true };
        var reset = new Button { Text = "Reset position", AutoSize = true };
        var feedback = new Label { AutoSize = true, MaximumSize = new Size(475, 0), ForeColor = Color.Firebrick };
        var options = read?.Invoke() ?? new ToastPositionOptions();
        horizontal.SelectedIndex = options.Right ? 1 : 0;
        vertical.SelectedIndex = options.Bottom ? 1 : 0;
        drag.Checked = options.DragEnabled;
        void Apply(ToastPositionOptions value)
        {
            options = value;
            feedback.Text = save?.Invoke(value) == true ? "" : "Preference could not be saved; this session uses the new position.";
        }
        horizontal.SelectedIndexChanged += (_, _) => Apply(options.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1));
        vertical.SelectedIndexChanged += (_, _) => Apply(options.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1));
        drag.CheckedChanged += (_, _) => Apply(options.WithDrag(drag.Checked));
        reset.Click += (_, _) => Apply(options.WithOffset(null, null));
        panel.Controls.Add(horizontal);
        panel.Controls.Add(vertical);
        panel.Controls.Add(drag);
        panel.Controls.Add(reset);
        panel.Controls.Add(feedback);

        var savedIdle = ToastConfigStore.NormalizeIdleSeconds(readIdleSeconds?.Invoke() ?? ToastConfigStore.DefaultIdleSeconds);
        var idle = new ComboBox { Name = "ToastIdleSeconds", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        foreach (var seconds in ToastConfigStore.IdleChoices) idle.Items.Add(seconds);
        idle.SelectedItem = savedIdle;
        var applyIdle = new Button { Name = "ApplyToastIdle", Text = "Apply", AutoSize = true, Enabled = false };
        var idleError = new Label { Name = "ToastIdleError", AutoSize = true, MaximumSize = new Size(475, 0), ForeColor = Color.Firebrick };
        idle.SelectedIndexChanged += (_, _) => applyIdle.Enabled = (int)idle.SelectedItem != savedIdle;
        applyIdle.Click += (_, _) =>
        {
            var seconds = (int)idle.SelectedItem;
            if (saveIdleSeconds?.Invoke(seconds) != true)
            {
                idleError.Text = "Toast duration could not be saved. The previous duration is still active. Try Apply again.";
                return;
            }
            savedIdle = seconds;
            idleError.Text = "";
            applyIdle.Enabled = false;
        };
        panel.Controls.Add(new Label { Text = "Toast duration (seconds)", AutoSize = true, Margin = new Padding(3, 16, 3, 4) });
        panel.Controls.Add(idle);
        panel.Controls.Add(new Label { Text = "After Apply, the next result or pointer leave uses this duration.", AutoSize = true, MaximumSize = new Size(475, 0) });
        panel.Controls.Add(applyIdle);
        panel.Controls.Add(idleError);
        FormClosing += (_, e) =>
        {
            if ((int)idle.SelectedItem != savedIdle)
                e.Cancel = MessageBox.Show(this, "Discard the unapplied toast duration change?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes;
        };
    }
}
#endif
