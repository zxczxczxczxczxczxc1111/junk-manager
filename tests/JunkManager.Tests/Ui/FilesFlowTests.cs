using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// The four steps of the cleanup flow on a running window. Nothing is deleted:
/// the flow is walked up to the last button and turned back.
/// </summary>
[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class FilesFlowTests(UiFixture stend)
{
    /// <summary>Приводит поток к первому шагу, где бы он ни остался.</summary>
    private void NaVybor()
    {
        stend.ObespechitProhod();
        stend.Perejti("files");

        if (stend.Est("confirm-back"))
        {
            stend.Nazhat("confirm-back");
            UiFixture.Podozhdat(() => !stend.Est("confirm-back"), TimeSpan.FromSeconds(5));
        }

        if (!stend.Dostupna("files-to-confirm"))
        {
            stend.Nazhat("files-mark-safe");
        }
    }

    [Fact]
    public void Knopka_udaleniya_ne_ozhivaet_poka_spisok_ne_dolistan()
    {
        // Единственный ограничитель перед безвозвратным. Если он не работает,
        // полный предпросмотр превращается обратно в число.
        NaVybor();
        stend.Nazhat("files-to-confirm");

        UiFixture.Podozhdat(() => stend.Est("confirm-delete"), TimeSpan.FromSeconds(10))
            .Should().BeTrue("шаг подтверждения не открылся");

        stend.Dostupna("confirm-delete").Should().BeFalse("список ещё не долистан");

        stend.ProkrutitVKonec("confirm-list");

        UiFixture.Podozhdat(() => stend.Dostupna("confirm-delete"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("после конца списка кнопка обязана ожить");
    }

    [Fact]
    public void Na_podtverzhdenii_vidna_oblast_udaleniya()
    {
        NaVybor();
        stend.Nazhat("files-to-confirm");

        UiFixture.Podozhdat(() => stend.Skolko("confirm-row-scope") > 0, TimeSpan.FromSeconds(10))
            .Should().BeTrue("строки подтверждения не появились");

        stend.Imena("confirm-row-scope").Should().Contain(
            p => p.Contains("папка останется", StringComparison.Ordinal)
              || p.Contains("папка целиком", StringComparison.Ordinal),
            "экран подтверждения обязан говорить, уйдёт каталог или файлы внутри");
    }

    [Fact]
    public void Nazad_k_vyboru_ne_teryaet_otmetki()
    {
        NaVybor();
        var bylo = stend.Imya("files-selected-count");

        stend.Nazhat("files-to-confirm");
        UiFixture.Podozhdat(() => stend.Est("confirm-back"), TimeSpan.FromSeconds(10))
            .Should().BeTrue("шаг подтверждения не открылся");

        stend.Nazhat("confirm-back");
        UiFixture.Podozhdat(() => stend.Est("files-selected-count"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("возврат к выбору не сработал");

        stend.Imya("files-selected-count").Should().Be(bylo);
    }

    [Fact]
    public void Snyat_vsyo_gasit_perehod_k_podtverzhdeniyu()
    {
        // Ноль отмеченных это ноль работы. Кнопка, живая при пустом выборе,
        // ведёт на подтверждение пустого списка.
        NaVybor();

        stend.Nazhat("files-clear");

        UiFixture.Podozhdat(() => !stend.Dostupna("files-to-confirm"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("при нуле отмеченных переход обязан быть недоступен");

        stend.Nazhat("files-mark-safe");

        UiFixture.Podozhdat(() => stend.Dostupna("files-to-confirm"), TimeSpan.FromSeconds(5))
            .Should().BeTrue("«Отметить безопасное» обязано вернуть переход");
    }
}
