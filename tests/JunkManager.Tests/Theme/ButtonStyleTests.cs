using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class ButtonStyleTests
{
    private static string Markup() => XamlSource.Load("Controls.Buttons.xaml").ToString();

    [Fact]
    public void Med_ne_rabotaet_knopkoy()
    {
        // Spec section 15: copper is a status and only a status. While the accent
        // was copper, the "Удалить" button and the "Опасно" badge were the same
        // colour, and that is the defect the whole palette change fixed.
        var markup = Markup();

        markup.Should().NotContain("RiskBrush");
        markup.Should().NotContain("RiskColor");
        markup.Should().NotContain("SafeFillBrush");
        markup.Should().NotContain("SafeInkBrush");
    }

    [Fact]
    public void Glavnaya_knopka_lilovaya()
    {
        var doc = XamlSource.Load("Controls.Buttons.xaml");

        var primary = doc.Descendants()
            .Single(e => e.Attribute(XamlSource.X + "Key")?.Value == "PrimaryButton");

        primary.Descendants()
            .Where(e => e.Name.LocalName == "Setter")
            .Should().Contain(s =>
                s.Attribute("Property")!.Value == "Background" &&
                s.Attribute("Value")!.Value.Contains("AccentBrush", StringComparison.Ordinal));
    }

    [Fact]
    public void Vse_stili_knopok_zadayut_kolco_fokusa()
    {
        // A style that forgets FocusVisualStyle falls back to the dotted system
        // rectangle, and keyboard navigation stops looking like this product.
        var doc = XamlSource.Load("Controls.Buttons.xaml");

        var styles = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute("TargetType") is not null)
            .ToList();

        styles.Should().NotBeEmpty();

        foreach (var style in styles)
        {
            var hasOwn = style.Elements()
                .Any(s => s.Name.LocalName == "Setter" &&
                          s.Attribute("Property")?.Value == "FocusVisualStyle");
            var inherits = style.Attribute("BasedOn") is not null;

            (hasOwn || inherits).Should().BeTrue(
                "стиль {0} не задаёт FocusVisualStyle и не наследует его",
                style.Attribute(XamlSource.X + "Key")?.Value ?? style.Attribute("TargetType")!.Value);
        }
    }

    [Fact]
    public void U_kazhdoy_knopki_est_neyavnyy_stil()
    {
        // Стиль с x:Key надо не забыть применить, а забыть можно. Неявный стиль
        // забыть нельзя физически, и ровно поэтому спека требует именно их.
        var doc = XamlSource.Load("Controls.Buttons.xaml");

        var neyavnye = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style"
                        && e.Attribute(XamlSource.X + "Key") is null
                        && e.Attribute("TargetType") is not null)
            .Select(e => e.Attribute("TargetType")!.Value)
            .ToList();

        foreach (var tip in new[] { "Button", "ToggleButton", "RepeatButton" })
        {
            neyavnye.Should().Contain(
                v => v.Contains("{x:Type " + tip + "}", StringComparison.Ordinal),
                "без неявного стиля голый {0} возьмёт вид Aero", tip);
        }
    }

    [Fact]
    public void Podpis_knopki_svyazana_cherez_Binding_a_ne_TemplateBinding()
    {
        // TemplateBinding не гоняет конвертер типов, а Content это object против
        // string у Text. Подмена на TemplateBinding не ломает сборку и убивает
        // подпись на всех кнопках сразу.
        var doc = XamlSource.Load("Controls.Buttons.xaml");

        var label = doc.Descendants()
            .Single(e => e.Attribute(XamlSource.X + "Name")?.Value == "Label");

        label.Attribute("Text")!.Value.Should().StartWith("{Binding Content");
    }
}
