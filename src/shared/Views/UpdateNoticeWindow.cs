#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bimwright.Ipt.Shared.Update;

namespace Bimwright.Ipt.Shared.Views
{
    /// <summary>
    /// Small modeless "update available" card in the bottom-right corner of the work area (rvt-mcp
    /// UpdateNoticeWindow parity). Never modal, never takes focus; any button closes it.
    /// </summary>
    internal sealed class UpdateNoticeWindow : Window
    {
        private readonly UpdateInfo _info;
        private readonly string _statePath;
        private readonly string _prompt;
        private readonly Button _copy;

        public UpdateNoticeWindow(UpdateInfo info, string statePath)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _statePath = statePath;
            _prompt = UpdateNoticeText.BuildAgentPrompt(info);

            Title = BrandAssets.ProductName + " update available";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
            ShowActivated = false;
            // Unowned like the toasts and History: an owner on Inventor's STA would share its input queue, and that
            // thread can be busy for minutes. Topmost keeps the small card visible above a maximized Inventor.
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Resources.MergedDictionaries.Add(BimwrightStyles.Dictionary);
            Background = (Brush)FindResource("BimBackground");
            Foreground = (Brush)FindResource("BimText");

            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
            body.Children.Add(new TextBlock
            {
                Text = "A new version of " + BrandAssets.ProductName + " is available (" + info.LatestVersion + "). Copy the prompt below and paste it to your AI agent to update.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
            });
            body.Children.Add(new TextBlock
            {
                Text = "Installed: " + info.CurrentVersion,
                Foreground = (Brush)FindResource("BimTextSecondary"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 10),
            });
            body.Children.Add(new Border
            {
                Background = (Brush)FindResource("BimSurface"),
                BorderBrush = (Brush)FindResource("BimBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new TextBox
                {
                    Text = _prompt,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 150,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Padding = new Thickness(6),
                },
            });

            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            _copy = new Button { Content = "Copy prompt", Style = (Style)FindResource(BimwrightStyles.PrimaryButtonKey), Margin = new Thickness(0, 0, 8, 0), MinWidth = 96 };
            _copy.Click += (_, __) => CopyPrompt();
            var open = new Button { Content = "Open release page", Style = (Style)FindResource(BimwrightStyles.ToolbarButtonKey), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (_, __) => OpenUrl(_info.ReleaseUrl);
            var skip = new Button { Content = "Skip this version", Style = (Style)FindResource(BimwrightStyles.ToolbarButtonKey), Margin = new Thickness(0, 0, 8, 0) };
            skip.Click += (_, __) => { UpdateChecker.SkipVersion(_info.LatestVersion, _statePath); Close(); };
            var close = new Button { Content = "Close", Style = (Style)FindResource(BimwrightStyles.ToolbarButtonKey), IsCancel = true };
            close.Click += (_, __) => Close();
            buttons.Children.Add(_copy);
            buttons.Children.Add(open);
            buttons.Children.Add(skip);
            buttons.Children.Add(close);
            body.Children.Add(buttons);
            Content = body;

            Loaded += (_, __) => PlaceBottomRight();
        }

        private void CopyPrompt()
        {
            try
            {
                Clipboard.SetText(_prompt);
                _copy.Content = "Copied ✓";
            }
            catch { }
        }

        private void PlaceBottomRight()
        {
            try
            {
                var area = SystemParameters.WorkArea;
                Left = area.Right - ActualWidth - 16;
                Top = area.Bottom - ActualHeight - 16;
            }
            catch { }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
#endif
