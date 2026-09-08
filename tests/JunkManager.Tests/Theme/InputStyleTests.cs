using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class InputStyleTests
{
    [Fact]
    public void TextBox_perekryvaet_vydelenie_karetku_i_menyu()
    {
        // Selection colours on a TextBox are plain properties, not template
        // brushes: a full ControlTemplate does not touch them, and the Windows
        // blue selection survives underneath a themed border.
        var doc = XamlSource.Load("Controls.Inputs.xaml");

        var style = doc.Descendants()
            .Single(e => e.Name.LocalName == "Style" &&
                         e.Attribute("TargetType")!.Value.Contains("TextBox", StringComparison.Ordinal));

        var set = style.Elements()
            .Where(e => e.Name.LocalName == "Setter")
            .Select(e => e.Attribute("Property")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        set.Should().Contain("CaretBrush");
        set.Should().Contain("SelectionBrush");
        set.Should().Contain("SelectionTextBrush");
        set.Should().Contain("ContextMenu");
    }

    [Fact]
    public void Flazhok_ne_pokazyvaet_podcherkivanie_klavishi_dostupa()
    {
        // RecognizesAccessKey=True renders an underscore under a letter, and
        // underscores are banned as an affordance across the whole product.
        var markup = XamlSource.Load("Controls.Inputs.xaml").ToString();

        markup.Should().Contain("RecognizesAccessKey=\"False\"");
        markup.Should().NotContain("RecognizesAccessKey=\"True\"");
    }

    [Fact]
    public void Dlina_shtriha_galochki_zadana_v_tolshchinah_a_ne_v_pikselyah()
    {
        // The SVG in the mockup uses stroke-dasharray 16 with an absolute unit.
        // WPF counts dashes in multiples of StrokeThickness, so 16 there means
        // 35 device units here and the tick never animates.
        var doc = XamlSource.Load("Controls.Inputs.xaml");

        var tick = doc.Descendants()
            .Single(e => e.Attribute(XamlSource.X + "Name")?.Value == "Tick");

        tick.Attribute("StrokeDashArray")!.Value.Should().Be("7 7");
        tick.Attribute("StrokeDashOffset")!.Value.Should().Be("7");
    }

    [Fact]
    public void U_kazhdogo_polya_vvoda_est_neyavnyy_stil()
    {
        // Стиль с ключом надо не забыть применить. Неявный забыть нельзя.
        var doc = XamlSource.Load("Controls.Inputs.xaml");

        var neyavnye = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style"
                        && e.Attribute(XamlSource.X + "Key") is null
                        && e.Attribute("TargetType") is not null)
            .Select(e => e.Attribute("TargetType")!.Value)
            .ToList();

        foreach (var tip in new[] { "CheckBox", "RadioButton", "TextBox" })
        {
            neyavnye.Should().Contain(
                v => v.Contains("{x:Type " + tip + "}", StringComparison.Ordinal),
                "без неявного стиля голый {0} возьмёт системный глиф", tip);
        }
    }

    [Fact]
    public void Vydelenie_v_pole_vvoda_nepolupozrachnoe()
    {
        // SelectionOpacity по умолчанию 0.4. При лиловой кисти и полупрозрачном
        // выделении текст под ним уезжает в мутный сиреневый, и SelectionTextBrush
        // перестаёт работать вовсе: он рисуется под просвечивающей заливкой.
        var doc = XamlSource.Load("Controls.Inputs.xaml");

        var style = doc.Descendants()
            .Single(e => e.Name.LocalName == "Style" &&
                         e.Attribute("TargetType")!.Value.Contains("TextBox", StringComparison.Ordinal));

        style.Elements()
            .Where(e => e.Name.LocalName == "Setter")
            .Should().Contain(s =>
                s.Attribute("Property")!.Value == "SelectionOpacity" &&
                s.Attribute("Value")!.Value == "1");
    }
}
