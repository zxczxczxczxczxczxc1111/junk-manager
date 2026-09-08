using System.Windows;
using System.Windows.Media;

namespace JunkManager.App.Controls;

/// <summary>
/// Hover and pressed colours as attached properties, so one button template
/// serves the plain, the primary and the irreversible variants. A Style setter
/// cannot carry a Binding, but a ControlTemplate trigger can, and that is the
/// whole trick: the trigger reads these off the templated parent.
/// </summary>
internal static class Chrome
{
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackground", typeof(Brush), typeof(Chrome),
            new FrameworkPropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty HoverBorderBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBorderBrush", typeof(Brush), typeof(Chrome),
            new FrameworkPropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackground", typeof(Brush), typeof(Chrome),
            new FrameworkPropertyMetadata(Brushes.Transparent));

    public static Brush GetHoverBackground(DependencyObject d) =>
        (Brush)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(HoverBackgroundProperty);

    public static void SetHoverBackground(DependencyObject d, Brush value) =>
        (d ?? throw new ArgumentNullException(nameof(d))).SetValue(HoverBackgroundProperty, value);

    public static Brush GetHoverBorderBrush(DependencyObject d) =>
        (Brush)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(HoverBorderBrushProperty);

    public static void SetHoverBorderBrush(DependencyObject d, Brush value) =>
        (d ?? throw new ArgumentNullException(nameof(d))).SetValue(HoverBorderBrushProperty, value);

    public static Brush GetPressedBackground(DependencyObject d) =>
        (Brush)(d ?? throw new ArgumentNullException(nameof(d))).GetValue(PressedBackgroundProperty);

    public static void SetPressedBackground(DependencyObject d, Brush value) =>
        (d ?? throw new ArgumentNullException(nameof(d))).SetValue(PressedBackgroundProperty, value);
}
