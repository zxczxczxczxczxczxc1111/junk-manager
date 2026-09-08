using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// The rail, on a running window. Markup tests see the six entries; only this
/// sees what happens when a person presses them.
/// </summary>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class ShellNavigationTests(UiFixture stend, ITestOutputHelper vyvod)
{
    /// <summary>
    /// Скруглённый угол окна Windows 11 показывает то, что ЗА окном, а снимок
    /// берётся по прямоугольнику окна. На синих обоях гостя это давало 363
    /// «системно-синих» пикселя при совершенно чистом интерфейсе. Проверено
    /// прогоном 05.09.2026: считать надо тело окна, а углы называть вслух.
    /// </summary>
    private const int Ugol = 28;

    private static bool VUglu(int x, int y, int shirina, int vysota) =>
        (x < Ugol || x >= shirina - Ugol) && (y < Ugol || y >= vysota - Ugol);


    private static readonly string[] Razdely =
        ["overview", "files", "apps", "registry", "history", "settings"];

    /// <param name="marker">
    /// Идентификатор, по которому видно, что раздел ПОКАЗАЛ своё содержимое.
    /// Общий «screen-host» тут не годится: он на месте и тогда, когда экрана
    /// нет вовсе.
    /// </param>
    [Theory]
    [InlineData("overview", "disk-strip")]
    [InlineData("files", "files-list")]
    [InlineData("apps", "apps-list")]
    // Не empty-title: модуль реестра приехал этапом 5, и раздел показывает
    // настоящий экран с заголовком «Найдено записей».
    [InlineData("registry", "registry-heading")]
    [InlineData("history", "history-heading")]
    [InlineData("settings", "settings-heading")]
    public void Kazhdyy_razdel_pokazyvaet_svoy_ekran(string razdel, string marker)
    {
        stend.ObespechitProhod();
        stend.Perejti(razdel);

        UiFixture.Podozhdat(() => stend.Est(marker), TimeSpan.FromSeconds(10))
            .Should().BeTrue("раздел {0} обязан показать «{1}», а не пустое место", razdel, marker);
    }

    [Fact]
    public void Empty_program_search_explains_how_to_continue()
    {
        // The shipped program list is no longer a placeholder's retirement home.
        stend.Perejti("apps");
        UiFixture.Podozhdat(() => stend.Est("apps-search"), TimeSpan.FromMinutes(2)).Should().BeTrue();
        stend.Vvesti("apps-search", "no-program-" + Guid.NewGuid().ToString("N"));
        try
        {
            UiFixture.Podozhdat(() => stend.Est("apps-empty"), TimeSpan.FromSeconds(10)).Should().BeTrue();
            stend.Imya("apps-empty").Should().Contain("Измените поиск");
            stend.Dostupna("apps-refresh").Should().BeTrue();
        }
        finally { stend.Vvesti("apps-search", string.Empty); }
    }

    [Fact]
    public void V_bokovoy_paneli_rovno_shest_razdelov()
    {
        // Седьмой раздел «Подтверждение и очистка» пытались завести дважды.
        // Он делит один поток надвое и оставляет человека не там, где он
        // выбирал файлы.
        foreach (var razdel in Razdely)
        {
            stend.Est($"rail-{razdel}").Should().BeTrue("раздела {0} нет в рельсе", razdel);
        }

        stend.Skolko("rail-confirm").Should().Be(0);
        stend.Skolko("rail-cleanup").Should().Be(0);
    }

    [Fact]
    public void Aktivnyy_razdel_pomechen_akcentom_a_ne_podcherkivaniem()
    {
        stend.Perejti("files");

        using var snimok = stend.Snimok();
        var knopka = stend.Ramka("rail-files");
        var okno = stend.RamkaOkna;

        // Метка это полоса в две точки у левого края кнопки. Ищем рядом, а не
        // в одной точке: на масштабе 150 процентов полоса шириной три пикселя
        // и смещена на полторы.
        UiPalette.EstRyadom(
            snimok,
            knopka.Left - okno.Left + 2,
            knopka.Top - okno.Top + (knopka.Height / 2),
            UiPalette.Token("AccentColor"),
            radius: 5)
            .Should().BeTrue("метка активного раздела обязана быть лиловой");
    }

    [Fact]
    public void Sistemnoe_sinee_vydelenie_nigde_ne_prohodit()
    {
        // Ради этой проверки затевалась перебивка SystemColors. Синий Windows
        // это #0078D4 и родня: если он появился, значит какой-то шаблон
        // достался контролу от системы.
        stend.ObespechitProhod();
        stend.Perejti("files");

        using var snimok = stend.Snimok();

        var vTele = 0;
        var vUglah = 0;
        var pervyy = (x: -1, y: -1);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (var y = 0; y < snimok.Height; y += 3)
        {
            for (var x = 0; x < snimok.Width; x += 3)
            {
                var c = UiPalette.Pixel(snimok, x, y);
                if (!UiPalette.Siniy(c) || !UiPalette.SploshnoSiniy(snimok, x, y))
                {
                    continue;
                }

                if (VUglu(x, y, snimok.Width, snimok.Height))
                {
                    vUglah++;
                    continue;
                }

                vTele++;
                if (pervyy.x < 0)
                {
                    pervyy = (x, y);
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        // Углы считаются отдельно и в счёт не идут: в вырезе скруглённого угла
        // видны обои гостя, а они синие. После перехода на сплошной квадрат
        // там ноль (обои размыты и однотонного квадрата не дают), но исключение
        // оставлено: обои сменятся, а проверка не должна об этом узнавать
        // падением.
        vyvod.WriteLine($"синих в скруглённых углах: {vUglah}, в теле окна: {vTele}");

        var gde = "нигде";
        if (vTele > 0)
        {
            // Снимок кладётся рядом с отчётом, иначе разбирать нечем: число
            // «362 синих пикселя» не говорит, ЧТО синее, а угадывать по
            // координате дороже, чем посмотреть.
            var put = UiPalette.Sohranit(snimok, "sinie");
            var obrazec = UiPalette.Pixel(snimok, pervyy.x, pervyy.y);
            gde = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"первый в точке {pervyy.x};{pervyy.y}, цвет "
                + $"#{obrazec.R:X2}{obrazec.G:X2}{obrazec.B:X2}, рамка "
                + $"{minX};{minY}..{maxX};{maxY}, снимок {put}");
        }

        vTele.Should().Be(
            0, "нашлось {0} системно-синих пикселей в теле окна: {1}", vTele, gde);
    }
}
