#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>Fixed-size counter. Updates replace in-flight rolls, never queue or resize the card.</summary>
internal sealed class RollingToastNumber : Grid
{
    internal const double SlotHeight = 23;
    private readonly TextBlock _outgoingText;
    private readonly TextBlock _currentText;
    private readonly Viewbox _outgoing;
    private readonly TranslateTransform _outgoingOffset = new();
    private readonly TranslateTransform _currentOffset = new();
    private bool _initialized;
    internal int Value { get; private set; }

    public RollingToastNumber()
    {
        Width = 25;
        Height = SlotHeight;
        ClipToBounds = true;
        _outgoingText = CreateText();
        _currentText = CreateText();
        _outgoing = Slot(_outgoingText, _outgoingOffset);
        _outgoing.Visibility = Visibility.Hidden;
        Children.Add(_outgoing);
        Children.Add(Slot(_currentText, _currentOffset));
    }

    public void SetValue(int value, Brush foreground, bool animate)
    {
        _outgoingText.Foreground = _currentText.Foreground = foreground;
        if (_initialized && value == Value) return;
        StopAnimation();
        _outgoingText.Text = Value.ToString(CultureInfo.InvariantCulture);
        _currentText.Text = value.ToString(CultureInfo.InvariantCulture);
        ToolTip = _currentText.Text;
        var roll = animate && _initialized;
        _initialized = true;
        Value = value;
        if (!roll) return;
        _outgoing.Visibility = Visibility.Visible;
        var duration = TimeSpan.FromMilliseconds(280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _outgoingOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -SlotHeight, duration) { EasingFunction = ease });
        _currentOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(SlotHeight, 0, duration) { EasingFunction = ease });
    }

    public void StopAnimation()
    {
        _outgoingOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _currentOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _outgoing.Visibility = Visibility.Hidden;
        _currentOffset.Y = 0;
    }

    private static TextBlock CreateText()
    {
        var text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }
    private static Viewbox Slot(TextBlock text, TranslateTransform offset) => new()
    {
        Child = text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        RenderTransform = offset,
    };
}
#endif
