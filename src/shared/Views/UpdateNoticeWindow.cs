#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bimwright.Ipt.Shared.Update;

namespace Bimwright.Ipt.Shared.Views
{
    /// <summary>
    /// Small modeless "update available" card in the bottom-right corner of the work area (rvt-mcp
    /// UpdateNoticeWindow parity). Never modal, never takes focus.
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
            Width = 480;
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
            ScrollBarFadeBehavior.SetIsEnabled(this, true);

            var bg = (Brush)FindResource("BimBackground");
            var fg = (Brush)FindResource("BimText");
            var secondary = (Brush)FindResource("BimTextSecondary");
            var surface = (Brush)FindResource("BimSurface");
            var border = (Brush)FindResource("BimBorder");
            var primary = (Brush)FindResource("BimPrimary");
            var pillBg = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            pillBg.Freeze();
            Background = bg;
            Foreground = fg;

            var root = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };

            // Header: version pill on the left, subtle release-page link on the right.
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pillText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pillText.Children.Add(new TextBlock { Text = info.CurrentVersion, Foreground = secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            pillText.Children.Add(new TextBlock { Text = " → ", Foreground = secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            pillText.Children.Add(new TextBlock { Text = info.LatestVersion, Foreground = primary, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var versionPill = new Border
            {
                Background = pillBg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = pillText,
            };
            Grid.SetColumn(versionPill, 0);
            header.Children.Add(versionPill);

            var openLink = CreateLinkButton("Open release page ↗", primary, FontWeights.SemiBold);
            openLink.Click += (_, __) => OpenUrl(_info.ReleaseUrl);
            Grid.SetColumn(openLink, 1);
            header.Children.Add(openLink);
            root.Children.Add(header);

            root.Children.Add(new TextBlock
            {
                Text = "A new version of " + BrandAssets.ProductName + " is available (" + info.LatestVersion + "). Copy the prompt below and paste it to your AI agent to update.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
            });

            if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            {
                root.Children.Add(new TextBlock
                {
                    Text = "What's new",
                    Foreground = secondary,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4),
                });
                root.Children.Add(new Border
                {
                    Background = surface,
                    BorderBrush = border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = new TextBox
                    {
                        Text = info.ReleaseNotes,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        MaxHeight = 160,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = fg,
                        FontSize = 12,
                        Padding = new Thickness(10, 8, 10, 8),
                    },
                });
            }

            var promptExpanded = string.IsNullOrWhiteSpace(info.ReleaseNotes);
            var promptBox = new Border
            {
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 4, 0, 12),
                Visibility = promptExpanded ? Visibility.Visible : Visibility.Collapsed,
                Child = new TextBox
                {
                    Text = _prompt,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 130,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = fg,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Padding = new Thickness(8, 6, 8, 6),
                },
            };
            var promptToggle = CreateLinkButton((promptExpanded ? "▾ " : "▸ ") + "Preview agent update prompt", secondary, FontWeights.Normal);
            promptToggle.HorizontalAlignment = HorizontalAlignment.Left;
            promptToggle.Margin = new Thickness(0, 0, 0, promptExpanded ? 0 : 8);
            promptToggle.Click += (_, __) =>
            {
                promptExpanded = !promptExpanded;
                promptToggle.Content = (promptExpanded ? "▾ " : "▸ ") + "Preview agent update prompt";
                promptToggle.Margin = new Thickness(0, 0, 0, promptExpanded ? 0 : 8);
                promptBox.Visibility = promptExpanded ? Visibility.Visible : Visibility.Collapsed;
            };
            root.Children.Add(promptToggle);
            root.Children.Add(promptBox);

            // Footer: single guaranteed row — Skip link on left, primary Copy + Close on right.
            var footerDivider = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
            };
            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var skip = CreateLinkButton("Skip this version", secondary, FontWeights.Normal);
            skip.HorizontalAlignment = HorizontalAlignment.Left;
            skip.Click += (_, __) => { UpdateChecker.SkipVersion(_info.LatestVersion, _statePath); Close(); };
            Grid.SetColumn(skip, 0);
            footer.Children.Add(skip);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            _copy = new Button
            {
                Content = "Copy prompt",
                Style = (Style)FindResource(BimwrightStyles.PrimaryButtonKey),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 104,
            };
            _copy.Click += (_, __) => CopyPrompt();
            var close = new Button
            {
                Content = "Close",
                Style = (Style)FindResource(BimwrightStyles.ToolbarButtonKey),
                IsCancel = true,
                MinWidth = 72,
            };
            close.Click += (_, __) => Close();
            actions.Children.Add(_copy);
            actions.Children.Add(close);
            Grid.SetColumn(actions, 1);
            footer.Children.Add(actions);

            footerDivider.Child = footer;
            root.Children.Add(footerDivider);
            Content = root;

            Loaded += (_, __) => PlaceBottomRight();
            SizeChanged += (_, __) => ClampInsideWorkArea();
        }

        private static Button CreateLinkButton(string text, Brush foreground, FontWeight weight)
        {
            var template = new ControlTemplate(typeof(Button));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = presenter;

            return new Button
            {
                Content = text,
                Foreground = foreground,
                FontSize = 12,
                FontWeight = weight,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 4, 0, 4),
                MinWidth = 0,
                Height = double.NaN,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Template = template,
            };
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

        private void ClampInsideWorkArea()
        {
            if (!IsLoaded) return;
            try
            {
                var area = SystemParameters.WorkArea;
                if (Top + ActualHeight > area.Bottom - 16)
                    Top = Math.Max(area.Top + 16, area.Bottom - ActualHeight - 16);
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
