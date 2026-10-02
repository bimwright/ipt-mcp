#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Drawing;
using System.Windows.Forms;
using Bimwright.Ipt.Shared.Views.Toast;

namespace Bimwright.Ipt.Shared.Plugin;

/// <summary>Status and immediately applied owner-relative toast preferences.</summary>
internal sealed class ToastPositionForm : Form
{
    public ToastPositionForm(string status, Func<ToastPositionOptions>? read,
        Func<ToastPositionOptions, bool>? save)
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
    }
}
#endif
