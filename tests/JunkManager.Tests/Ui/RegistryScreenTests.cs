using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// Экран реестра на ЖИВОМ окне. Отвечает на единственный вопрос, на который не
/// отвечает ни один песочный тест: доехал ли модуль до окна.
/// </summary>
/// <remarks>
/// Ровно на этом продукт спотыкался 05.09.2026: пять источников были собраны,
/// покрыты зелёными тестами и не вызывались ниоткуда, а приёмка показывала 13
/// приманок из 18. Проверка разметки такого не видит вовсе, потому что разметка
/// была безупречной.
/// </remarks>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class RegistryScreenTests(UiFixture stend)
{
    [Fact]
    public void Ekran_reestra_pokazyvaet_nahodki_a_ne_zaglushku()
    {
        stend.Perejti("registry");
        stend.Nazhat("registry-scan");

        UiFixture.Podozhdat(() => stend.Skolko("registry-row-check") > 0, TimeSpan.FromMinutes(2))
            .Should().BeTrue(
                "посев кладёт запись автозапуска на несуществующий файл, и она обязана дойти до экрана");
    }

    [Fact]
    public void U_kazhdoy_stroki_est_podpis_togo_chto_uydet()
    {
        stend.Perejti("registry");
        stend.Nazhat("registry-scan");

        UiFixture.Podozhdat(() => stend.Est("registry-row-kind"), TimeSpan.FromMinutes(2))
            .Should().BeTrue();

        stend.Imena("registry-row-kind").Should().OnlyContain(
            t => t == "значение" || t == "ключ целиком",
            "третий столбец обязан говорить, что именно исчезнет, а не быть подписью без смысла");
    }

    [Fact]
    public void Short_visible_confirmation_list_enables_delete_and_survives_back_navigation()
    {
        // Ворота прокрутки существуют только на живом ScrollViewer: модель про
        // прокрутку не знает вовсе, ей приходит уже готовый bool.
        stend.Perejti("registry");
        stend.Nazhat("registry-scan");

        UiFixture.Podozhdat(() => stend.Skolko("registry-row-check") > 0, TimeSpan.FromMinutes(2))
            .Should().BeTrue();

        stend.OtmetitPervyy("registry-row-check");
        stend.Nazhat("registry-delete");

        UiFixture.Podozhdat(() => stend.Est("registry-confirm-delete"), TimeSpan.FromSeconds(10))
            .Should().BeTrue();

        UiFixture.Podozhdat(() => stend.Dostupna("registry-confirm-delete"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("одна строка полностью видна; невозможная прокрутка не должна быть условием");
        stend.Nazhat("registry-back");
        stend.Nazhat("registry-delete");
        UiFixture.Podozhdat(() => stend.Dostupna("registry-confirm-delete"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("повторное подтверждение пересчитывает видимость списка");
        stend.Nazhat("registry-back");
    }

    [Fact]
    public void Zametka_pro_tochku_vosstanovleniya_vidna_do_udaleniya()
    {
        stend.Perejti("registry");
        stend.Nazhat("registry-scan");

        UiFixture.Podozhdat(() => stend.Skolko("registry-row-check") > 0, TimeSpan.FromMinutes(2))
            .Should().BeTrue();

        stend.OtmetitPervyy("registry-row-check");
        stend.Nazhat("registry-delete");

        UiFixture.Podozhdat(() => stend.Est("registry-restore-note"), TimeSpan.FromSeconds(10))
            .Should().BeTrue();

        stend.Imya("registry-restore-note").Should().NotBeNullOrWhiteSpace();
        stend.Nazhat("registry-back");
    }
}
