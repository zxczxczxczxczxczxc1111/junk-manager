using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// What a screen reader and a keyboard see. Both are blind to everything the
/// markup tests check, and both catch things the markup tests cannot.
/// </summary>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class AccessibilityTreeTests(UiFixture stend)
{
    [Theory]
    [InlineData("overview")]
    [InlineData("files")]
    [InlineData("registry")]
    [InlineData("history")]
    [InlineData("settings")]
    public void U_kazhdogo_interaktivnogo_elementa_est_imya(string razdel)
    {
        stend.ObespechitProhod();
        stend.Perejti(razdel);

        var bezimeni = stend.BezymyannyeInteraktivnye();

        bezimeni.Should().BeEmpty(
            "на экране {0} есть элементы без имени: {1}", razdel, string.Join(", ", bezimeni));
    }

    [Fact]
    public void Vsya_navigaciya_dostizhima_s_klaviatury()
    {
        stend.ObespechitProhod();
        stend.Perejti("files");

        var dostizhimye = stend.ObhodTab();

        dostizhimye.Should().Contain("files-to-confirm");
        dostizhimye.Should().Contain("files-mark-safe");
        dostizhimye.Should().Contain("rail-overview");
    }

    [Fact]
    public void Fokus_viden_glazami_a_ne_tolko_v_dereve()
    {
        stend.ObespechitProhod();
        stend.Perejti("files");

        stend.Fokus("files-mark-safe");
        Thread.Sleep(300);

        using var snimok = stend.Snimok();
        var okno = stend.RamkaOkna;
        var ramka = stend.Ramka("files-mark-safe");

        // Кольцо фокуса лиловое и идёт по контуру. Смотрим полосу слева от
        // кнопки: на разных масштабах оно отстоит от рамки на разное число
        // пикселей, поэтому радиус, а не одна точка.
        //
        // Радиус МАСШТАБИРУЕТСЯ. Кольцо задано в точках, а ищем мы в пикселях:
        // на 200 процентах отступ вдвое больше, и фиксированные шесть пикселей
        // до кольца не достают. Проверено прогоном 05.09.2026, где проверка
        // покраснела на 200 при совершенно исправном кольце.
        var mnozhitel = Math.Max(1, stend.MasshtabProcentov / 100);

        UiPalette.EstRyadom(
            snimok,
            ramka.Left - okno.Left - (2 * mnozhitel),
            ramka.Top - okno.Top + (ramka.Height / 2),
            UiPalette.Token("AccentColor"),
            radius: 6 * mnozhitel,
            dopusk: 40)
            .Should().BeTrue("кольцо фокуса не видно глазами");
    }

    [Fact]
    public void Okno_nazyvaet_sebya_i_svoi_prava()
    {
        // Заголовок это первое, что читает средство доступности, и
        // единственное место, где видно, под какими правами идёт работа.
        stend.ZagolovokOkna.Should().Be("Junk Manager");
        stend.Imya("rights-badge").Should().NotBeNullOrWhiteSpace();
    }
}
