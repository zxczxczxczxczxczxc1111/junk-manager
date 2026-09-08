using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using JunkManager.App.Controls;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class TrackedTextMetricsTests
{
    [Fact]
    public void Width_pustaya_stroka_daet_nol()
    {
        TrackedTextMetrics.Width([], fontSize: 11, tracking: 0.11).Should().Be(0);
    }

    [Fact]
    public void Width_odna_bukva_bez_hvostovogo_zazora()
    {
        // A trailing gap on the last glyph pushes a centred label off centre by
        // half the tracking. That drift is invisible to the eye and obvious to
        // a measurement, which is why it is pinned here.
        TrackedTextMetrics.Width([8.0], fontSize: 11, tracking: 0.11).Should().Be(8.0);
    }

    [Fact]
    public void Width_tri_bukvy_daet_summu_plyus_dva_zazora()
    {
        // 8 + 9 + 7 = 24, gap = 11 * 0.11 = 1.21, two gaps = 2.42
        TrackedTextMetrics.Width([8.0, 9.0, 7.0], fontSize: 11, tracking: 0.11)
            .Should().BeApproximately(26.42, 0.001);
    }

    [Fact]
    public void Width_nulevaya_razryadka_daet_chistuyu_summu()
    {
        TrackedTextMetrics.Width([8.0, 9.0, 7.0], fontSize: 11, tracking: 0)
            .Should().Be(24.0);
    }

    [Fact]
    public void Width_zazor_rastet_vmeste_s_keglem()
    {
        // Разрядка задана в em, а не в точках: на кегле 22 тот же коэффициент
        // обязан дать вдвое больший зазор. Без этого разрядка перестаёт быть
        // типографской величиной и превращается в подобранное на глаз число,
        // которое разъезжается при смене масштаба экрана.
        var malenkiy = TrackedTextMetrics.Width([8.0, 9.0, 7.0], fontSize: 11, tracking: 0.11);
        var bolshoy = TrackedTextMetrics.Width([8.0, 9.0, 7.0], fontSize: 22, tracking: 0.11);

        (bolshoy - 24.0).Should().BeApproximately((malenkiy - 24.0) * 2, 0.001);
    }

    private const string Nadpis = "СКАНИРОВАТЬ";

    private static double Chernila(FontWeight nachertanie)
    {
        var typeface = new Typeface(
            new FontFamily("Bahnschrift"), FontStyles.Normal, nachertanie, FontStretches.Normal);

        var tekst = new FormattedText(
            Nadpis, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 44.0, Brushes.White, 1.0);

        var vis = new DrawingVisual();
        using (var dc = vis.RenderOpen())
        {
            dc.DrawText(tekst, new Point(0, 0));
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            400, 60, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(vis);

        var pikseli = new byte[400 * 60 * 4];
        bmp.CopyPixels(pikseli, 400 * 4, 0);

        var summa = 0L;
        for (var i = 3; i < pikseli.Length; i += 4)
        {
            summa += pikseli[i];
        }

        return summa / 255.0;
    }

    private static double ShirinaGlifov(FontWeight nachertanie)
    {
        // Ровно тот же путь, которым меряет TrackedText: FormattedText на каждую
        // руну. Ширина берётся суммой, разрядка сюда не входит намеренно, она
        // считается отдельно и уже проверена выше.
        var typeface = new Typeface(
            new FontFamily("Bahnschrift"), FontStyles.Normal, nachertanie, FontStretches.Normal);

        var summa = 0.0;
        foreach (var rune in Nadpis.EnumerateRunes())
        {
            var glif = new FormattedText(
                rune.ToString(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11.0,
                Brushes.White,
                1.0);

            summa += glif.WidthIncludingTrailingWhitespace;
        }

        return summa;
    }

    /// <summary>
    /// Bahnschrift must supply real weights, not a synthesised bold.
    /// </summary>
    /// <remarks>
    /// План требовал доказать это РАЗНОЙ ШИРИНОЙ Bold и Normal и называл
    /// совпадение регрессом. Замерено 05.09.2026: у Bahnschrift ширина всех
    /// начертаний совпадает до последнего знака, 76.60666666666667 точки на
    /// «СКАНИРОВАТЬ» при кегле 11 у Normal, SemiBold и Bold одинаково. Регресса
    /// при этом нет: Bahnschrift это переменный шрифт, его именованные
    /// экземпляры делят одну таблицу метрик, и ширина по построению шрифта не
    /// зависит от веса. Ширина тут просто не отвечает на заданный вопрос.
    ///
    /// Отвечают на него две другие величины, обе замерены здесь:
    /// начертание выбирается настоящее (`IsBoldSimulated` ложно, у выбранного
    /// GlyphTypeface тот вес, который просили), и нарисованных чернил у Bold
    /// объективно больше (2649 у Light, 3508 у Normal, 4480 у Bold по сумме
    /// альфы на кегле 44). Синтетический полужирный провалил бы первую, а
    /// подстановка одного веса под все три вторую.
    /// </remarks>
    [Fact]
    public void U_Bahnschrift_est_nastoyashchie_nachertaniya()
    {
        ShirinaGlifov(FontWeights.Normal)
            .Should().BeGreaterThan(0, "шрифт обязан найтись, иначе меряется подстановка");

        foreach (var ves in new[] { FontWeights.Light, FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
        {
            var typeface = new Typeface(
                new FontFamily("Bahnschrift"), FontStyles.Normal, ves, FontStretches.Normal);

            typeface.IsBoldSimulated.Should().BeFalse(
                "вес {0} обязан быть настоящим начертанием, а не утолщённым программно", ves);

            typeface.TryGetGlyphTypeface(out var glyphs).Should().BeTrue(
                "вес {0} обязан разрешиться в настоящий файл шрифта", ves);
            glyphs.Weight.Should().Be(ves, "выбран не тот вес, который просили");
        }

        // Чернила: единственная величина, которая у переменного шрифта отличает
        // вес от веса. Порядок обязан быть строгим, иначе дисплейный слой макета
        // рисуется одним весом на всё.
        var light = Chernila(FontWeights.Light);
        var normal = Chernila(FontWeights.Normal);
        var semi = Chernila(FontWeights.SemiBold);
        var bold = Chernila(FontWeights.Bold);

        light.Should().BeLessThan(normal, "ожидается около 2649 против 3508");
        normal.Should().BeLessThan(semi);
        semi.Should().BeLessThan(bold, "ожидается около 4480 у Bold");
    }

    /// <summary>
    /// The tracked label must be wider than the plain one by exactly the sum of
    /// the gaps, measured on real glyph widths rather than on invented numbers.
    /// </summary>
    /// <remarks>
    /// Живой замер того же окна через UI Automation дал 76 и 88 точек при 96 dpi,
    /// то есть разницу 12 против расчётных 12.1. Совпадение с точностью до
    /// округления прямоугольника, но именно до округления: BoundingRectangle
    /// целочисленный, и «ровно 12.1» через него не доказывается никак. Точное
    /// число считается здесь.
    /// </remarks>
    [Fact]
    public void Razryadka_dobavlyaet_rovno_summu_zazorov()
    {
        var bukv = Nadpis.EnumerateRunes().Count();
        bukv.Should().Be(11, "надпись из макета, 11 букв и 10 промежутков");

        var glify = ShirinaGlifov(FontWeights.SemiBold);
        var shiriny = new List<double>();
        foreach (var rune in Nadpis.EnumerateRunes())
        {
            var typeface = new Typeface(
                new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var glif = new FormattedText(
                rune.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 11.0, Brushes.White, 1.0);
            shiriny.Add(glif.WidthIncludingTrailingWhitespace);
        }

        var bezRazryadki = TrackedTextMetrics.Width(shiriny, fontSize: 11, tracking: 0);
        var sRazryadkoy = TrackedTextMetrics.Width(shiriny, fontSize: 11, tracking: 0.11);

        bezRazryadki.Should().BeApproximately(glify, 0.001);
        (sRazryadkoy - bezRazryadki).Should().BeApproximately(11 * 0.11 * 10, 0.001);
    }
}
