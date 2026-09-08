using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace JunkManager.App.Controls;

/// <summary>
/// Letter spacing, which TextBlock does not have at all. The whole display layer
/// of this product is uppercase and tracked, so without this element the
/// approved mockup is simply not reproducible.
/// </summary>
/// <remarks>
/// Glyphs are laid out one at a time, so kerning between letters is lost. That
/// is acceptable here and only here: tracking already breaks kerning by
/// definition, and this element carries short uppercase labels, never prose.
/// The automation peer reports the plain string, so a screen reader hears the
/// word and not a spaced-out spelling.
/// </remarks>
internal sealed class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Spacing in em, matching CSS letter-spacing in the mockup.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(TrackedText),
            new FrameworkPropertyMetadata(
                Brushes.White,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(
            typeof(TrackedText),
            new FrameworkPropertyMetadata(
                new FontFamily("Segoe UI"),
                FrameworkPropertyMetadataOptions.Inherits
                | FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(
            typeof(TrackedText),
            new FrameworkPropertyMetadata(
                12.0,
                FrameworkPropertyMetadataOptions.Inherits
                | FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(
            typeof(TrackedText),
            new FrameworkPropertyMetadata(
                FontWeights.Normal,
                FrameworkPropertyMetadataOptions.Inherits
                | FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner(
            typeof(TrackedText),
            new FrameworkPropertyMetadata(
                FontStretches.Normal,
                FrameworkPropertyMetadataOptions.Inherits
                | FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => (FontStretch)GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (width, height) = Layout(null);
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Layout(drawingContext);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TrackedTextPeer(this);

    private (double Width, double Height) Layout(DrawingContext? dc)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text) || FontSize <= 0)
        {
            return (0, 0);
        }

        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widths = new List<double>();
        var height = 0.0;
        var x = 0.0;
        var gap = FontSize * Tracking;

        // Runes, not chars: a surrogate pair must not be split into two boxes.
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = new FormattedText(
                rune.ToString(),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                Foreground,
                dpi);

            dc?.DrawText(glyph, new Point(x, 0));

            widths.Add(glyph.WidthIncludingTrailingWhitespace);
            x += glyph.WidthIncludingTrailingWhitespace + gap;
            height = Math.Max(height, glyph.Height);
        }

        return (TrackedTextMetrics.Width(widths, FontSize, Tracking), height);
    }

    private sealed class TrackedTextPeer(TrackedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(TrackedText);

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Text;

        protected override string GetNameCore() => ((TrackedText)Owner).Text;

        protected override bool IsControlElementCore() => true;
    }
}
