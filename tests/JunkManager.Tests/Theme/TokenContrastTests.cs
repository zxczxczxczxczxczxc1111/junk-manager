using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class TokenContrastTests
{
    private static IReadOnlyDictionary<string, string> Colors() =>
        XamlSource.KeyedValues(XamlSource.Load("Tokens.xaml"), "Color");

    private static string Get(string key)
    {
        var colors = Colors();
        colors.Should().ContainKey(key);
        return colors[key];
    }

    [Theory]
    [InlineData("InkColor", 7.0)]         // основной текст, ожидается 17.9
    [InlineData("Ink2Color", 4.5)]        // вторичный текст, ожидается 7.5
    [InlineData("InkPathColor", 4.5)]     // путь это улика, её обязано быть видно, 4.7
    [InlineData("AccentColor", 7.0)]      // спека называет 7.08
    [InlineData("Accent2Color", 4.5)]     // наведение, 8.5
    [InlineData("RiskColor", 4.5)]        // 9.2
    [InlineData("SafeInkColor", 4.5)]     // 6.4
    [InlineData("DangerColor", 4.5)]      // 5.1
    [InlineData("OkColor", 4.5)]          // 8.0
    public void Chernila_na_podlozhke_derzhat_porog(string key, double floor)
    {
        Colorimetry.Contrast(Get(key), Get("VoidColor")).Should().BeGreaterThanOrEqualTo(floor);
    }

    [Fact]
    public void Ink3_goditsya_tolko_dlya_melkoy_metki()
    {
        // 3.47 on the void. Deliberate floor, not an oversight: Ink3 carries
        // eyebrows and counters, never a sentence the person must read.
        var c = Colorimetry.Contrast(Get("Ink3Color"), Get("VoidColor"));
        c.Should().BeGreaterThanOrEqualTo(3.0);
        c.Should().BeLessThan(4.5, "иначе Ink3 и Ink2 сливаются в один уровень");
    }

    [Fact]
    public void Chernila_na_akcente_chitayutsya()
    {
        // The primary button: lilac fill, near black ink. Expected 6.9.
        Colorimetry.Contrast(Get("AccentInkColor"), Get("AccentColor"))
            .Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void Chernila_na_medi_chitayutsya()
    {
        // Expected 8.9.
        Colorimetry.Contrast(Get("CuInkColor"), Get("RiskColor"))
            .Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void Belyy_na_zalivke_bezvozvratnogo_udaleniya_chitaetsya()
    {
        // This is why DangerFill exists apart from Danger: white on #E5484D is
        // 3.92, and the label on that button is small, uppercase and bold.
        Colorimetry.Contrast("#FFFFFFFF", Get("DangerFillColor"))
            .Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void Shkala_riska_monotonna_po_svetlote()
    {
        // Ordinal scale, not a categorical pair: Safe must be darker than Risk,
        // and the gap must be big enough to read without colour.
        var safe = Colorimetry.Lightness(Get("SafeInkColor"));   // ожидается 60.3
        var risk = Colorimetry.Lightness(Get("RiskColor"));      // ожидается 71.7

        safe.Should().BeLessThan(risk);
        (risk - safe).Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Zalivki_shkaly_riska_monotonny()
    {
        var safe = Colorimetry.Lightness(Get("SafeFillColor"));  // ожидается 34.5
        var risk = Colorimetry.Lightness(Get("RiskColor"));      // ожидается 71.7

        (risk - safe).Should().BeGreaterThanOrEqualTo(25);
    }

    [Fact]
    public void Shkala_riska_odnogo_ottenka()
    {
        // "Одна медная тональность в три ступени по светлоте" из спеки, но
        // ступеней две. Оттенок обязан остаться один, иначе шкала перестаёт
        // быть порядковой и становится набором разных цветов.
        Colorimetry.HueDistance(Get("SafeFillColor"), Get("RiskColor"))
            .Should().BeLessThanOrEqualTo(20, "ожидается около 10 градусов");
        Colorimetry.HueDistance(Get("SafeInkColor"), Get("RiskColor"))
            .Should().BeLessThanOrEqualTo(20, "ожидается около 6 градусов");
    }

    [Fact]
    public void Akcent_i_stupen_riska_ne_odin_cvet()
    {
        // The whole reason the accent moved off copper: next to the icon the old
        // one measured deltaE 11.9 and read as a build mistake, and the primary
        // button was the same colour as the "опасно" badge.
        Colorimetry.HueDistance(Get("AccentColor"), Get("RiskColor"))
            .Should().BeGreaterThanOrEqualTo(60, "ожидается около 126 градусов");
    }

    /// <summary>
    /// Surfaces are separated by a hairline, not a shadow, so they must form an
    /// ordered ladder instead of collapsing into one flat field.
    /// </summary>
    /// <remarks>
    /// План требовал здесь порог 1.05 по контрасту WCAG между соседними
    /// поверхностями. Порог недостижим и был бы красным на любом наборе цветов,
    /// который согласован: измерено на значениях из спеки, Void/Panel даёт
    /// 1.0382, а Panel/Card 1.0490. Контраст WCAG про читаемость чернил на фоне,
    /// у почти чёрных поверхностей его отношение прижато к единице по построению
    /// формулы (обе яркости меньше 0.008 при слагаемом 0.05). Цвета поверхностей
    /// названы спекой поимённо, а спека главнее плана, поэтому подвинут
    /// инструмент, а не значения: порядок и шаг меряются светлотой CIELAB, где
    /// ступени видны (1.39, 2.23, 4.05, 6.46, 9.96). Проверка стала строже
    /// исходной: она ловит и слипание двух поверхностей, и перепутанный порядок.
    /// </remarks>
    [Fact]
    public void Poverhnosti_stroyat_lestnicu_po_svetlote()
    {
        string[] ladder =
        [
            "SunkenColor", "VoidColor", "PanelColor", "CardColor", "RaisedColor",
            "LineColor", "LineMidColor", "LineHiColor",
        ];

        for (var i = 1; i < ladder.Length; i++)
        {
            var nizhe = Colorimetry.Lightness(Get(ladder[i - 1]));
            var vyshe = Colorimetry.Lightness(Get(ladder[i]));

            (vyshe - nizhe).Should().BeGreaterThanOrEqualTo(
                0.8,
                "поверхность {0} обязана быть светлее {1} на различимый шаг, иначе слой пропадает",
                ladder[i], ladder[i - 1]);
        }
    }
}
