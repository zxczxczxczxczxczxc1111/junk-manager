using System.Drawing;
using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// The blue detector itself, on synthetic bitmaps. No window, no guest.
/// </summary>
/// <remarks>
/// Мера, по которой живая проверка судит о системном выделении, обязана
/// проверяться отдельно и быстро. Живой прогон 05.09.2026 сначала считал
/// системным выделением субпиксельное сглаживание текста и падал 362 раза на
/// совершенно чистом окне: сама мера была неверна, а разбираться приходилось
/// через гостя, снимок и обратную доставку картинки на хост.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class SinevaTests
{
    private static readonly Color Podlozhka = Color.FromArgb(0x14, 0x14, 0x18);
    private static readonly Color SistemnyySiniy = Color.FromArgb(0x00, 0x78, 0xD4);

    private static Bitmap Polotno(int shirina = 200, int vysota = 120)
    {
        var holst = new Bitmap(shirina, vysota);
        using var g = Graphics.FromImage(holst);
        g.Clear(Podlozhka);
        return holst;
    }

    [Fact]
    public void Sglazhivanie_teksta_ne_schitaetsya_vydeleniem()
    {
        // Так выглядит ClearType на тёмном: у каждого вертикального штриха
        // синий край слева и оранжевый справа. Штрихи стоят через четыре
        // точки, поэтому «синий и синие соседи в четырёх точках» такую картину
        // НЕ отличает, а сплошной квадрат отличает.
        using var holst = Polotno();
        for (var x = 20; x < 180; x += 4)
        {
            for (var y = 20; y < 100; y++)
            {
                holst.SetPixel(x, y, Color.FromArgb(0x43, 0xA9, 0xDA));
                holst.SetPixel(x + 1, y, Color.FromArgb(0xF2, 0xF2, 0xEA));
                holst.SetPixel(x + 2, y, Color.FromArgb(0xC0, 0x7B, 0x18));
            }
        }

        UiPalette.Siniy(holst.GetPixel(20, 50)).Should().BeTrue(
            "край штриха синий сам по себе, иначе проверка ничего не докажет");

        Sploshnyh(holst).Should().Be(0, "сглаживание текста это не выделение");
    }

    [Fact]
    public void Sistemnaya_polosa_vydeleniya_lovitsya()
    {
        using var holst = Polotno();
        using (var g = Graphics.FromImage(holst))
        using (var kist = new SolidBrush(SistemnyySiniy))
        {
            g.FillRectangle(kist, 40, 40, 120, 20);
        }

        Sploshnyh(holst).Should().BeGreaterThan(
            0, "полоса системного выделения обязана находиться");
    }

    [Fact]
    public void Lilovyy_akcent_sinim_ne_schitaetsya()
    {
        // Ворота против меры, которая ловит собственный акцент продукта: такая
        // проверка краснела бы на здоровом окне, её бы ослабили, и настоящее
        // системное выделение прошло бы следом.
        using var holst = Polotno();
        using (var g = Graphics.FromImage(holst))
        using (var kist = new SolidBrush(Color.FromArgb(0xA8, 0x8B, 0xE0)))
        {
            g.FillRectangle(kist, 40, 40, 120, 20);
        }

        Sploshnyh(holst).Should().Be(0, "лиловый #A88BE0 это акцент продукта, а не система");
    }

    private static int Sploshnyh(Bitmap holst)
    {
        var naydeno = 0;
        for (var y = 0; y < holst.Height; y += 3)
        {
            for (var x = 0; x < holst.Width; x += 3)
            {
                if (UiPalette.Siniy(holst.GetPixel(x, y)) && UiPalette.SploshnoSiniy(holst, x, y))
                {
                    naydeno++;
                }
            }
        }

        return naydeno;
    }
}
