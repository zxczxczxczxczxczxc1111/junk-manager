using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// The "Explorer" reading of the overview screen, on live data from the guest.
/// </summary>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class OverviewExplorerTests(UiFixture stend)
{
    [Fact]
    public void Klik_po_kategorii_menyaet_spisok_nahodok()
    {
        stend.ObespechitProhod();
        stend.Perejti("overview");

        stend.Skolko("category-row").Should().BeGreaterThan(
            1, "нужны хотя бы две категории, иначе проверять нечего");

        var bylo = stend.Imya("category-title");
        stend.NazhatVSpiske("category-row", 1);

        UiFixture.Podozhdat(
            () => stend.Imya("category-title") != bylo, TimeSpan.FromSeconds(5))
            .Should().BeTrue("выбор категории не сменил правую половину");
    }

    [Fact]
    public void U_kazhdoy_nahodki_vidny_odnovremenno_imya_posledstvie_i_put()
    {
        // Ровно то, ради чего подача «Проводник» победила карту занятости.
        stend.ObespechitProhod();
        stend.Perejti("overview");

        // Категории обходятся все. Первая по счёту это находки обработчика
        // Windows, у которых адрес вида «volumecache:Thumbnail Cache» и
        // никакого файлового пути нет вовсе: требовать от неё обратный слеш
        // значит проверять не то. Проверено живым прогоном 05.09.2026.
        var kategoriy = stend.Skolko("category-row");
        kategoriy.Should().BeGreaterThan(0, "без категорий проверять нечего");

        var putVstretilsya = false;
        var strokBylo = 0;

        for (var i = 0; i < kategoriy; i++)
        {
            stend.NazhatVSpiske("category-row", i);
            Thread.Sleep(300);

            var teksty = stend.TekstyVnutri("finding-list");
            if (teksty.Count == 0)
            {
                continue;
            }

            strokBylo++;
            teksty.Count.Should().BeGreaterThan(
                2, "имя, последствие и адрес это как минимум три подписи на строку");

            putVstretilsya |= teksty.Any(t => t.Contains('\\', StringComparison.Ordinal));
        }

        strokBylo.Should().BeGreaterThan(0, "ни одна категория не показала находок");
        putVstretilsya.Should().BeTrue(
            "ни в одной категории не видно файлового пути, а он обязан читаться без наведения");
    }

    [Fact]
    public void Polosa_toma_narisovana_i_ne_pustaya()
    {
        stend.ObespechitProhod();
        stend.Perejti("overview");

        var polosa = stend.Ramka("disk-strip");

        polosa.Height.Should().BeGreaterThan(0);
        polosa.Width.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Obshchiy_itog_nazyvaet_obyom_a_ne_chislo_faylov()
    {
        // «Найдено 412» это не ответ на вопрос, ради которого приложение
        // открывают. Ответ это сколько места вернётся.
        stend.ObespechitProhod();
        stend.Perejti("overview");

        var itog = stend.Imya("overview-total");

        itog.Should().MatchRegex(
            @"\d+([,.]\d+)?\s*(Б|КБ|МБ|ГБ|ТБ)", "итог обязан быть объёмом, получено «{0}»", itog);
    }
}
