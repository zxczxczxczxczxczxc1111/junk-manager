using System.Windows;
using FluentAssertions;
using JunkManager.App.Controls.Behaviors;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Fitting the window into the work area, in numbers.
/// </summary>
/// <remarks>
/// Размеры окна заданы в точках, а экран меряется в пикселях, и на масштабе
/// 125 процентов и выше окно перестаёт помещаться на экран 1920x1080. У гостя
/// один масштаб за прогон и перезагрузка между ними, поэтому арифметика
/// проверяется здесь, а живая проверка на четырёх масштабах остаётся в классе
/// Ui.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class GeometriyaOknaTests
{
    /// <summary>Рабочая область на экране 1920x1080 с панелью задач, в ТОЧКАХ.</summary>
    private static Rect Rabochaya(double masshtab) =>
        new(0, 0, 1920 / masshtab, 1032 / masshtab);

    private static Geometriya Iskhodnoe() => new(1920, 900, 0, 0, 1180, 720);

    [Fact]
    public void Na_full_hd_okno_zanimaet_dostupnuyu_oblast()
    {
        var itog = GeometriyaOkna.PodEkran(Iskhodnoe(), Rabochaya(1.0));

        itog.Shirina.Should().Be(1920);
        itog.Vysota.Should().Be(900);
        itog.MinVysota.Should().Be(720, "минимум помещается, ужимать нечего");
    }

    [Fact]
    public void Na_bolshom_ekrane_sohranyaetsya_uvelichennyy_razmer()
    {
        var result = GeometriyaOkna.PodEkran(Iskhodnoe(), new Rect(0, 0, 2560, 1392));
        result.Shirina.Should().Be(1920);
        result.Vysota.Should().Be(900);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Na_bolshih_masshtabah_okno_umeshchaetsya(double masshtab)
    {
        var rabochaya = Rabochaya(masshtab);

        var itog = GeometriyaOkna.PodEkran(Iskhodnoe(), rabochaya);

        itog.Shirina.Should().BeLessThanOrEqualTo(rabochaya.Width);
        itog.Vysota.Should().BeLessThanOrEqualTo(rabochaya.Height);
    }

    [Fact]
    public void Minimum_bolshe_ekrana_uzhimaetsya_tozhe()
    {
        // На 200 процентах минимальная высота 720 точек это 1440 пикселей, то
        // есть больше всего экрана. Оставленный минимум перебил бы новую
        // высоту, и окно осталось бы за краем: ужимать надо и его.
        var rabochaya = Rabochaya(2.0);

        var itog = GeometriyaOkna.PodEkran(Iskhodnoe(), rabochaya);

        itog.MinVysota.Should().BeLessThanOrEqualTo(rabochaya.Height);
        itog.MinShirina.Should().BeLessThanOrEqualTo(rabochaya.Width);
        itog.Vysota.Should().BeLessThanOrEqualTo(rabochaya.Height);
    }

    [Fact]
    public void Uzhatoe_okno_vozvrashchaetsya_v_rabochuyu_oblast()
    {
        // Окно стояло по прежним размерам, ужатие оставило бы его смещённым
        // вниз и вправо ровно на разницу.
        var rabochaya = Rabochaya(1.5);
        var daleko = Iskhodnoe() with { Sleva = 900, Sverhu = 600 };

        var itog = GeometriyaOkna.PodEkran(daleko, rabochaya);

        (itog.Sleva + itog.Shirina).Should().BeLessThanOrEqualTo(rabochaya.Right);
        (itog.Sverhu + itog.Vysota).Should().BeLessThanOrEqualTo(rabochaya.Bottom);
        itog.Sleva.Should().BeGreaterThanOrEqualTo(rabochaya.Left);
        itog.Sverhu.Should().BeGreaterThanOrEqualTo(rabochaya.Top);
    }

    [Fact]
    public void Ekran_menshe_minimuma_ne_daet_otricatelnyh_razmerov()
    {
        // Вырожденный случай: экран меньше минимального окна целиком.
        // Отрицательная ширина роняет WPF, поэтому проверяется отдельно.
        var itog = GeometriyaOkna.PodEkran(Iskhodnoe(), new Rect(0, 0, 640, 480));

        itog.Shirina.Should().Be(640);
        itog.Vysota.Should().Be(480);
        itog.Sleva.Should().Be(0);
        itog.Sverhu.Should().Be(0);
    }
}
