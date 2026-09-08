using FluentAssertions;
using JunkManager.App.ViewModels;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// Навигация это поведение, а не разметка, поэтому она проверяется здесь, а не
/// глазами на запущенном окне. Плана этого файла не было; он заведён потому,
/// что иначе единственной проверкой бокового меню оставался запуск.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class ShellViewModelTests
{
    private static ShellViewModel Obolochka() => new(isElevated: true);

    [Fact]
    public void Razdelov_rovno_shest_i_v_poryadke_iz_speki()
    {
        // Спека раздел 15: шесть, а не семь. Отдельного «Подтверждение и
        // очистка» нет, это шаги внутри «Файлов».
        Obolochka().Sections.Select(s => s.Id).Should().Equal(
            "overview", "files", "apps", "registry", "history", "settings");
    }

    [Fact]
    public void U_kazhdogo_razdela_svoy_identifikator_avtomatizacii()
    {
        Obolochka().Sections.Select(s => s.AutomationId).Should().Equal(
            "rail-overview", "rail-files", "rail-apps", "rail-registry",
            "rail-history", "rail-settings");
    }

    [Fact]
    public void Podpis_v_relse_propisnymi_a_imya_dlya_dostupnosti_net()
    {
        // Макет ставит рельс прописными с разрядкой. Заглавные считаются в
        // модели, а не преобразователем: средство чтения с экрана обязано
        // услышать «Обзор», а не «О Б З О Р».
        var razdel = new SectionViewModel("overview", "Обзор");

        razdel.RailLabel.Should().Be("ОБЗОР");
        razdel.Title.Should().Be("Обзор");
    }

    [Fact]
    public void Pri_zapuske_otkryt_pervyy_razdel_i_pomechen_tolko_on()
    {
        var obolochka = Obolochka();

        obolochka.Current.Id.Should().Be("overview");
        obolochka.Sections.Count(s => s.IsCurrent).Should().Be(1);
        obolochka.Sections[0].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Vybor_razdela_snimaet_metku_s_prezhnego()
    {
        // Две помеченные строки сразу это две лиловые полосы слева, и человек
        // не понимает, что открыто. Ловится только здесь: разметка снимет
        // метку сама лишь у ListBox, а рельс собран из ToggleButton.
        var obolochka = Obolochka();
        var byl = obolochka.Current;

        obolochka.VybratCommand.Execute(obolochka.Sections[3]);

        obolochka.Current.Id.Should().Be("registry");
        byl.IsCurrent.Should().BeFalse();
        obolochka.Sections.Count(s => s.IsCurrent).Should().Be(1);
    }

    [Fact]
    public void Povtornyy_vybor_otkrytogo_razdela_nichego_ne_lomaet()
    {
        // ToggleButton сообщает о нажатии и на уже открытом разделе. Наивный
        // обработчик снимет метку с текущего и поставит её ему же обратно, но
        // между этими двумя строками список секунду стоит вообще без метки.
        var obolochka = Obolochka();

        obolochka.VybratCommand.Execute(obolochka.Sections[0]);

        obolochka.Sections[0].IsCurrent.Should().BeTrue();
        obolochka.Sections.Count(s => s.IsCurrent).Should().Be(1);
    }

    [Fact]
    public void Pustoy_razdel_v_komande_ne_ronyaet_okno()
    {
        var obolochka = Obolochka();

        var act = () => obolochka.VybratCommand.Execute(null);

        act.Should().NotThrow();
        obolochka.Current.Id.Should().Be("overview");
    }

    [Fact]
    public void Nol_i_otsutstvie_schetchika_eto_raznye_veshchi()
    {
        // «0» это посчитанный ноль, null это «ещё не считали». Нарисовать их
        // одинаково значит соврать про второе.
        var razdel = new SectionViewModel("files", "Файлы");

        razdel.HasBadge.Should().BeFalse("до подсчёта счётчика нет вовсе");

        razdel.Badge = "0";
        razdel.HasBadge.Should().BeTrue("посчитанный ноль показывается");
    }

    [Fact]
    public void Poyavlenie_schetchika_soobshchaet_ob_izmenenii()
    {
        var razdel = new SectionViewModel("files", "Файлы");
        var izmenennye = new List<string>();
        razdel.PropertyChanged += (_, e) => izmenennye.Add(e.PropertyName ?? string.Empty);

        razdel.Badge = "12";

        izmenennye.Should().Contain(nameof(SectionViewModel.HasBadge));
    }

    [Theory]
    [InlineData(true, "администратор")]
    [InlineData(false, "обычные права")]
    public void Prava_nazvany_v_stroke_zagolovka_vsegda(bool podnyaty, string ozhidaemo)
    {
        // Спека раздел 11: чем продукту разрешено пользоваться, меняет смысл
        // всех чисел ниже, поэтому это не прячется за меню.
        new ShellViewModel(podnyaty).RightsLabel.Should().Be(ozhidaemo);
    }

    [Fact]
    public void Razdel_bez_ekrana_pokazyvaet_zaglushku_a_ne_pustotu()
    {
        // Пять разделов из шести собираются задачами 9-12. До них раздел обязан
        // объяснять словами, что экрана ещё нет: пустая правая половина окна
        // читается как «приложение сломалось», а не как «сюда мы ещё не дошли».
        var razdel = new SectionViewModel("registry", "Реестр");

        razdel.Screen.Should().BeNull();
        razdel.State.Phase.Should().Be(ScreenPhase.Empty);
        razdel.State.EmptyTitle.Should().Be("Реестр");
    }

    [Fact]
    public void Sostoyanie_razdela_s_ekranom_eto_sostoyanie_ekrana()
    {
        // Два состояния на один раздел означали бы, что рельс показывает одно,
        // а экран другое: раздел «Обзор» с полосой хода и «Обзор» с пустым
        // экраном одновременно.
        var ekran = new PoddelnyyEkran();
        var razdel = new SectionViewModel("overview", "Обзор", ekran);

        razdel.Screen.Should().BeSameAs(ekran);
        razdel.State.Should().BeSameAs(ekran.State);
    }

    [Fact]
    public void Obzor_v_obolochke_eto_tot_ekran_kotoryy_peredali()
    {
        var ekran = new PoddelnyyEkran();

        var obolochka = new ShellViewModel(isElevated: true, obzor: ekran);

        obolochka.Sections[0].Screen.Should().BeSameAs(ekran);
        obolochka.Sections.Skip(1).Should().OnlyContain(
            s => s.Screen == null, "остальные экраны собираются задачами 9-12");
    }

    [Fact]
    public void Perehod_po_identifikatoru_otkryvaet_razdel()
    {
        // Кнопка «Перейти к очистке» на обзоре знает только слово «files»:
        // ссылки на чужую модель раздела у неё нет и быть не должно.
        var obolochka = Obolochka();

        obolochka.PereytiCommand.Execute("files");

        obolochka.Current.Id.Should().Be("files");
        obolochka.Sections.Count(s => s.IsCurrent).Should().Be(1);
    }

    [Fact]
    public void Perehod_po_neizvestnomu_identifikatoru_ne_ronyaet_okno()
    {
        // Опечатка в разметке не ломает сборку: параметр команды это строка.
        // Упасть на ней значит убить окно нажатием на кнопку.
        var obolochka = Obolochka();

        var act = () => obolochka.PereytiCommand.Execute("takogo-razdela-net");

        act.Should().NotThrow();
        obolochka.Current.Id.Should().Be("overview");
    }

    [Fact]
    public async Task Perehod_na_razdel_prosit_ekran_perechitat_sebya()
    {
        // Журнал читался ровно один раз, при сборке окна. Значит очистка,
        // сделанная после запуска, появлялась в нём только после перезапуска
        // программы: человек шёл смотреть, что у него удалили, и видел вчерашнее.
        var zhurnal = new SchitayushchiyEkran();
        var obolochka = new ShellViewModel(isElevated: true, zhurnal: zhurnal);

        obolochka.VybratCommand.Execute(obolochka.Sections.Single(s => s.Id == "history"));
        await obolochka.PoslednyyPokaz;

        zhurnal.Pokazov.Should().Be(1, "переход обязан просить экран перечитать себя");
    }

    [Fact]
    public async Task Vozvrat_na_razdel_prosit_perechitat_snova()
    {
        // Ушёл, почистил, вернулся. Второй показ обязан читать заново, иначе
        // обновление работало бы ровно один раз за запуск.
        var zhurnal = new SchitayushchiyEkran();
        var obolochka = new ShellViewModel(isElevated: true, zhurnal: zhurnal);

        var istoriya = obolochka.Sections.Single(s => s.Id == "history");
        var fayly = obolochka.Sections.Single(s => s.Id == "files");

        obolochka.VybratCommand.Execute(istoriya);
        await obolochka.PoslednyyPokaz;
        obolochka.VybratCommand.Execute(fayly);
        await obolochka.PoslednyyPokaz;
        obolochka.VybratCommand.Execute(istoriya);
        await obolochka.PoslednyyPokaz;

        zhurnal.Pokazov.Should().Be(2);
    }

    [Fact]
    public async Task Razdel_bez_ekrana_ne_ronyaet_perehod()
    {
        // У раздела «Программы» экрана ещё нет вовсе. Переход на него обязан
        // остаться переходом, а не падением на пустой ссылке.
        var obolochka = Obolochka();

        var act = async () =>
        {
            obolochka.VybratCommand.Execute(obolochka.Sections.Single(s => s.Id == "apps"));
            await obolochka.PoslednyyPokaz;
        };

        await act.Should().NotThrowAsync();
    }

    private sealed class PoddelnyyEkran : IScreenViewModel
    {
        public ScreenStateViewModel State { get; } = new();
    }

    /// <summary>Экран, считающий, сколько раз его просили перечитать себя.</summary>
    private sealed class SchitayushchiyEkran : IScreenViewModel
    {
        public ScreenStateViewModel State { get; } = new();

        public int Pokazov { get; private set; }

        public Task PriPokazeAsync(CancellationToken ct)
        {
            Pokazov++;
            return Task.CompletedTask;
        }
    }
}
