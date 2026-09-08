using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// Runs at whatever scale the guest was set to by scripts\vm-dpi.ps1. The test
/// reads the scale rather than setting it: changing DPI needs a sign-out, and a
/// test that signs the session out kills its own run.
/// </summary>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class DpiTests(UiFixture stend)
{
    [Fact]
    public void Okno_pomeshchaetsya_v_rabochuyu_oblast()
    {
        // На 200 процентах окно 1280x880 в логических точках это 2560x1760 в
        // физических, и на экране 1920x1080 оно не помещается. Окно обязано
        // ужаться, а не уехать за край.
        var okno = stend.RamkaOkna;
        var rabochaya = System.Windows.SystemParameters.WorkArea;

        // WorkArea в логических точках процесса тестов, окно в физических:
        // приводим к одному через масштаб самого окна, иначе на 150 процентах
        // проверка врёт в полтора раза.
        var mnozhitel = stend.MasshtabProcentov / 100.0;

        okno.Width.Should().BeLessThanOrEqualTo(
            (int)Math.Ceiling(rabochaya.Width * mnozhitel) + 2,
            "на масштабе {0}% окно шире рабочей области", stend.MasshtabProcentov);
        okno.Height.Should().BeLessThanOrEqualTo(
            (int)Math.Ceiling(rabochaya.Height * mnozhitel) + 2,
            "на масштабе {0}% окно выше рабочей области", stend.MasshtabProcentov);
    }

    [Fact]
    public void Ni_odin_element_ne_shlopnut_v_nol()
    {
        stend.ObespechitProhod();
        stend.Perejti("overview");

        var shlopnutye = stend.Shlopnutye();

        shlopnutye.Should().BeEmpty(
            "на масштабе {0}% схлопнулись: {1}",
            stend.MasshtabProcentov, string.Join(", ", shlopnutye));
    }

    [Fact]
    public void Podpisi_ne_vylezayut_za_svoi_kontenery()
    {
        stend.Perejti("settings");

        var vylezshie = stend.Vylezshie();

        vylezshie.Should().BeEmpty(
            "на масштабе {0}% за окно вылезли: {1}",
            stend.MasshtabProcentov, string.Join(", ", vylezshie));
    }

    [Fact]
    public void Masshtab_prochitan_i_pravdopodoben()
    {
        // Ворота против пустой проверки: набор, который померял 100 процентов
        // на госте, поставленном в 200, проверяет не то, что думает.
        stend.MasshtabProcentov.Should().BeOneOf(100, 125, 150, 175, 200);
    }
}
