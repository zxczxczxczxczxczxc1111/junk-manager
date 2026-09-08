using System.Threading;
using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Core;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class FilesViewModelTests : IDisposable
{
    /// <summary>
    /// Все созданные проверкой модели. Закрываются разом в конце.
    /// </summary>
    /// <remarks>
    /// Модель держит источник отмены очистки, то есть закрываемая. Писать
    /// <c>using</c> в каждой из двадцати проверок значит двадцать шансов
    /// забыть, и забытая всплывёт не здесь, а разбуханием набора.
    /// </remarks>
    private readonly List<FilesViewModel> _sozdannye = [];
    private readonly SynchronizationContext? _byvshiyKontekst;

    /// <summary>
    /// Ставит контекст, выполняющий отчёты о ходе на месте. В окне эту роль
    /// играет диспетчер; без него Progress кидает отчёты в пул потоков, и
    /// проверка хода очистки сверяет ещё не заполненный список.
    /// </summary>
    public FilesViewModelTests()
    {
        _byvshiyKontekst = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new PryamoyKontekst());
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(_byvshiyKontekst);

        foreach (var model in _sozdannye)
        {
            model.Dispose();
        }
    }

    private static Finding Celikom(string imya, long bayt, RiskTier stupen) =>
        new(imya, $"C:\\{imya}", bayt, stupen, "пересоздастся", FindingSource.Rule, RuleId: "Тест");

    private static Finding Poelementno(string imya, long bayt, int celey) =>
        new(imya, $"C:\\{imya}", bayt, RiskTier.Safe, "мусор программ", FindingSource.Rule,
            RuleId: "Тест", LastUsedDays: null,
            Scope: DeleteScope.SelectedEntries,
            Targets: [.. Enumerable.Range(0, celey).Select(i => $"C:\\{imya}\\f{i}")]);

    private FilesViewModel Model(params Finding[] nahodki) =>
        SoSluzhboy(new OchistkaZaglushka(), nahodki);

    private FilesViewModel SoSluzhboy(OchistkaZaglushka sluzhba, params Finding[] nahodki)
    {
        var proshloe = new PosledniyProhod();
        var model = Zapomnit(new FilesViewModel(proshloe, sluzhba, new ZaglushkaZanyatyh()));
        proshloe.Polozhit(new ScanResult(nahodki, []));
        return model;
    }

    private FilesViewModel Zapomnit(FilesViewModel model)
    {
        _sozdannye.Add(model);
        return model;
    }

    [Fact]
    public void Bezopasnoe_otmecheno_srazu_opasnoe_net()
    {
        var model = Model(
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.Rows.Single(r => r.Name == "кэш").IsSelected.Should().BeTrue();
        model.Rows.Single(r => r.Name == "офлайн").IsSelected.Should().BeFalse();
        model.SelectedBytes.Should().Be(5_000_000_000);
        model.SelectedCount.Should().Be(1);
    }

    [Fact]
    public void Snyat_vsyo_snimaet_vsyo_vklyuchaya_bezopasnoe()
    {
        var model = Model(
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("шейдеры", 7_000_000_000, RiskTier.Safe));

        model.SnyatVsyoCommand.Execute(null);

        model.SelectedCount.Should().Be(0);
        model.SelectedBytes.Should().Be(0);
        model.CanGoToConfirm.Should().BeFalse("нечего подтверждать");
    }

    [Fact]
    public void Otmetit_bezopasnoe_ne_trogaet_opasnoe()
    {
        var model = Model(
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.SnyatVsyoCommand.Execute(null);
        model.OtmetitBezopasnoeCommand.Execute(null);

        model.Rows.Single(r => r.Name == "офлайн").IsSelected.Should().BeFalse(
            "кнопка называется «Отметить безопасное», и она делает ровно это");
        model.SelectedBytes.Should().Be(5_000_000_000);
    }

    [Fact]
    public void Summa_pereschityvaetsya_pri_ruchnoy_otmetke()
    {
        var model = Model(Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.Rows[0].IsSelected = true;

        model.SelectedBytes.Should().Be(20_000_000_000);
        model.SelectedCount.Should().Be(1);
    }

    [Fact]
    public void Snyataya_ruchnoy_otmetkoy_stroka_uhodit_iz_summy()
    {
        // Обратная сторона: пересчёт обязан идти в обе стороны. Подписка,
        // которая только прибавляет, даёт итог больше отмеченного, и человек
        // жмёт «Удалить безвозвратно», прочитав неверное число.
        var model = Model(Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.Rows[0].IsSelected = false;

        model.SelectedBytes.Should().Be(0);
        model.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void Oblast_udaleniya_vidna_v_stroke()
    {
        var model = Model(Poelementno("temp", 4_800_000_000, celey: 312));

        var stroka = model.Rows[0];

        stroka.Scope.Should().Be(DeleteScope.SelectedEntries);
        stroka.TargetCount.Should().Be(312);
        stroka.ScopeLabel.Should().Be("312 файлов внутри, папка останется");
    }

    [Fact]
    public void Sklonenie_v_oblasti_udaleniya_pravilnoe()
    {
        Model(Poelementno("a", 1, 1)).Rows[0].ScopeLabel.Should().StartWith("1 файл ");
        Model(Poelementno("b", 1, 2)).Rows[0].ScopeLabel.Should().StartWith("2 файла ");
        Model(Poelementno("c", 1, 5)).Rows[0].ScopeLabel.Should().StartWith("5 файлов ");
        Model(Poelementno("d", 1, 11)).Rows[0].ScopeLabel.Should().StartWith("11 файлов ");
        Model(Poelementno("e", 1, 21)).Rows[0].ScopeLabel.Should().StartWith("21 файл ");
    }

    [Fact]
    public void Nahodka_celikom_govorit_chto_uydet_papka()
    {
        Model(Celikom("кэш", 1, RiskTier.Safe)).Rows[0].ScopeLabel
            .Should().Be("папка целиком");
    }

    [Fact]
    public void Do_prohoda_ekran_pust_i_zovet_na_obzor()
    {
        // Экран «Файлы» СВОЕГО прохода не делает. Второй проход по тому же
        // диску это вторые двадцать пять секунд и второй набор чисел, который
        // не сойдётся с показанным на обзоре.
        var model = Zapomnit(new FilesViewModel(new PosledniyProhod(), new OchistkaZaglushka(), new ZaglushkaZanyatyh()));

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.Rows.Should().BeEmpty();
        model.State.EmptyBody.Should().Contain("Обзор");
    }

    [Fact]
    public void Pustoy_rezultat_daet_pustoe_sostoyanie()
    {
        var proshloe = new PosledniyProhod();
        var model = Zapomnit(new FilesViewModel(proshloe, new OchistkaZaglushka(), new ZaglushkaZanyatyh()));

        proshloe.Polozhit(ScanResult.Empty);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Shag_potoka_nachinaetsya_s_vybora()
    {
        Model(Celikom("кэш", 1, RiskTier.Safe)).Step.Should().Be(FlowStep.Selection);
    }

    [Fact]
    public void Rezultat_prohoda_odin_na_dva_ekrana()
    {
        // Обзор и Файлы обязаны показывать ОДИН проход. Иначе «найдено 4,7 ГБ»
        // на обзоре и другое число на очистке, и никакого способа понять, какое
        // из них правда.
        var proshloe = new PosledniyProhod();
        var itog = new ScanResult([Celikom("кэш", 5_000_000_000, RiskTier.Safe)], []);

        var fayly = Zapomnit(new FilesViewModel(proshloe, new OchistkaZaglushka(), new ZaglushkaZanyatyh()));
        proshloe.Polozhit(itog);

        proshloe.Itog.Should().BeSameAs(itog);
        fayly.Rows.Should().HaveCount(1, "экран подхватил чужой проход, а не сделал свой");
        fayly.TotalBytes.Should().Be(5_000_000_000);
    }

    [Fact]
    public void Novyy_prohod_zamenyaet_stroki_a_ne_dobavlyaet_ih()
    {
        // «Сканировать заново» на обзоре обязано перерисовать и очистку. Строки,
        // дописанные к прежним, дали бы удвоенный итог и удаление того, чего
        // уже нет.
        var proshloe = new PosledniyProhod();
        var fayly = Zapomnit(new FilesViewModel(proshloe, new OchistkaZaglushka(), new ZaglushkaZanyatyh()));

        proshloe.Polozhit(new ScanResult([Celikom("кэш", 5_000_000_000, RiskTier.Safe)], []));
        proshloe.Polozhit(new ScanResult([Celikom("другое", 1_000_000_000, RiskTier.Safe)], []));

        fayly.Rows.Should().ContainSingle().Which.Name.Should().Be("другое");
        fayly.TotalBytes.Should().Be(1_000_000_000);
    }

    [Fact]
    public void Novyy_prohod_vozvrashchaet_potok_k_shagu_vybora()
    {
        // Человек ушёл на подтверждение, потом нажал «Сканировать заново» на
        // обзоре. Остаться на подтверждении значило бы предлагать удалить
        // список, которого больше нет.
        var proshloe = new PosledniyProhod();
        var fayly = Zapomnit(new FilesViewModel(proshloe, new OchistkaZaglushka(), new ZaglushkaZanyatyh()));

        proshloe.Polozhit(new ScanResult([Celikom("кэш", 5_000_000_000, RiskTier.Safe)], []));
        fayly.KPodtverzhdeniyuCommand.Execute(null);
        fayly.Step.Should().Be(FlowStep.Confirm);

        proshloe.Polozhit(new ScanResult([Celikom("другое", 1_000_000_000, RiskTier.Safe)], []));

        fayly.Step.Should().Be(FlowStep.Selection);
    }

    [Fact]
    public void K_podtverzhdeniyu_ne_puskaet_s_pustym_vyborom()
    {
        var model = Model(Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.CanGoToConfirm.Should().BeFalse("опасное не отмечено заранее");

        model.KPodtverzhdeniyuCommand.Execute(null);

        model.Step.Should().Be(FlowStep.Selection, "подтверждать нечего");
    }

    [Fact]
    public void Nazad_k_vyboru_ne_snimaet_otmetki()
    {
        // Вернуться и потерять полчаса разбора списка это худший способ узнать,
        // что кнопка «Назад» работает.
        var model = Model(
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.Rows.Single(r => r.Name == "офлайн").IsSelected = true;
        model.KPodtverzhdeniyuCommand.Execute(null);
        model.NazadKVyboruCommand.Execute(null);

        model.Step.Should().Be(FlowStep.Selection);
        model.SelectedCount.Should().Be(2);
        model.SelectedBytes.Should().Be(25_000_000_000);
    }

    // ==== Шаги 2, 3 и 4 ====

    [Fact]
    public void Do_dolistyvaniya_spiska_udalenie_ne_zapuskaetsya()
    {
        // Ворота подтверждения. Кнопка, которую можно нажать, не сдвинув
        // список, превращает полный предпросмотр обратно в число.
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(sluzhba, Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);

        model.UdalitCommand.CanExecute(null).Should().BeFalse();
        model.UdalitCommand.Execute(null);

        sluzhba.Poluchennye.Should().BeEmpty("команда не имела права выполниться");
        model.Step.Should().Be(FlowStep.Confirm);
    }

    [Fact]
    public void Vozvrat_k_vyboru_snimaet_soglasie()
    {
        // Иначе человек, вернувшийся и добавивший десять строк, попадает на
        // подтверждение с уже активной кнопкой: ворота открылись бы за
        // прошлое согласие, данное на другой список.
        var model = Model(Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        model.NazadKVyboruCommand.Execute(null);

        model.Confirmed.Should().BeFalse();
        model.UdalitCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Udalyaetsya_tolko_otmechennoe()
    {
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(
            sluzhba,
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);

        sluzhba.Poluchennye.Should().ContainSingle().Which.Name.Should().Be("кэш");
    }

    [Fact]
    public async Task Posle_ochistki_ekran_pokazyvaet_itog()
    {
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(sluzhba, Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);

        model.Step.Should().Be(FlowStep.Report);
        model.Report.Should().NotBeNull();
        model.Report!.BytesFreed.Should().Be(5_000_000_000);
        model.Report.DeletedCount.Should().Be(1);
        model.FreedBytes.Should().Be(5_000_000_000);
        model.DoneCount.Should().Be(1);
    }

    [Fact]
    public async Task Hod_ochistki_kopit_ishody_po_odnomu()
    {
        // Полоса без списка это «что-то происходит». Человеку в этот момент
        // нужно «что именно унесли и что не смогли».
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(
            sluzhba,
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Celikom("шейдеры", 7_000_000_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);

        model.Running.Should().HaveCount(2);
        // Порядок исходов повторяет порядок строк, а строки идут по убыванию
        // размера. Крупное первым это и порядок чтения, и порядок работы.
        model.Running[0].Path.Should().Be("C:\\шейдеры");
        model.Running[1].Path.Should().Be("C:\\кэш");
    }

    [Fact]
    public async Task Ostanovka_preryvaet_ochistku_i_pomechaet_itog()
    {
        var sluzhba = new OchistkaZaglushka();
        FilesViewModel? model = null;

        // Остановка нажимается ИЗ хода очистки, как это и делает человек.
        sluzhba.PeredKazhdoy = nomer =>
        {
            if (nomer == 1)
            {
                model!.OstanovitCommand.Execute(null);
            }
        };

        model = SoSluzhboy(
            sluzhba,
            Celikom("a", 1_000, RiskTier.Safe),
            Celikom("b", 2_000, RiskTier.Safe),
            Celikom("c", 3_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);

        model.Report!.Cancelled.Should().BeTrue("остановленное нельзя выдавать за законченное");
        model.Report.Outcomes.Should().ContainSingle();
        model.Step.Should().Be(FlowStep.Report);
    }

    [Fact]
    public async Task Zavershenie_ne_predlagaet_udalit_udalennoe()
    {
        // Половина строк только что перестала существовать. Показать их снова
        // значит предложить удалить то, чего нет.
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(sluzhba, Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);
        model.ZavershitCommand.Execute(null);

        model.Step.Should().Be(FlowStep.Selection);
        model.Rows.Should().BeEmpty();
        model.Report.Should().BeNull();
        model.SelectedCount.Should().Be(0);
        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyBody.Should().Contain("Обзор");
    }

    [Fact]
    public void Razbivka_po_oblastyam_udaleniya_schitaetsya_po_otmechennomu()
    {
        // Число объектов ничего не говорит о том, останутся ли каталоги.
        var model = Model(
            Celikom("кэш", 5_000_000_000, RiskTier.Safe),
            Poelementno("temp", 4_800_000_000, celey: 312),
            Celikom("офлайн", 20_000_000_000, RiskTier.Risk));

        model.WholeCount.Should().Be(1, "опасное не отмечено");
        model.EntryCount.Should().Be(1);
        model.EntryFileCount.Should().Be(312);

        model.Rows.Single(r => r.Name == "офлайн").IsSelected = true;

        model.WholeCount.Should().Be(2);
        model.EntryFileCount.Should().Be(312);
    }

    [Fact]
    public void Stroka_pro_poelementnye_nahodki_sobrana_bez_lishnih_probelov()
    {
        // Найдено снимком живого окна: «изнутри 3 каталогов , сами каталоги
        // останутся». Пробел ставил WPF между соседними Run, и в разметке его
        // не видно вовсе. Поэтому строка собирается в модели и проверяется
        // здесь целиком, а не по кускам.
        var model = Model(
            Poelementno("temp", 4_800_000_000, celey: 1823),
            Poelementno("crash", 1_000_000, celey: 6));

        // Разряд отбит НЕРАЗРЫВНЫМ пробелом: так делает русский формат N0, и
        // обычный пробел в ожидании давал расхождение, невидимое глазом.
        model.EntrySummary.Should().Be(
            "1\u00A0829 файлов уйдут изнутри 2 каталогов, сами каталоги останутся");
        model.EntrySummary.Should().NotContain(" ,");
    }

    [Fact]
    public void Stroka_pro_poelementnye_nahodki_sklonyaetsya()
    {
        Model(Poelementno("a", 1, celey: 1)).EntrySummary
            .Should().StartWith("1 файл уйдёт изнутри 1 каталога,");
        Model(Poelementno("b", 1, celey: 3)).EntrySummary
            .Should().StartWith("3 файла уйдут изнутри 1 каталога,");
    }

    [Fact]
    public async Task Svodka_ochistki_sobrana_odnoy_strokoy()
    {
        var sluzhba = new OchistkaZaglushka();
        var model = SoSluzhboy(sluzhba, Celikom("кэш", 5_000_000_000, RiskTier.Safe));

        model.ReportSummary.Should().BeEmpty("очистки ещё не было");

        model.KPodtverzhdeniyuCommand.Execute(null);
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);

        model.ReportSummary.Should().Be("1 объект удалён, 0 пропущено, 0 не удалось");
        model.ReportSummary.Should().NotContain(" ,");
    }

    [Fact]
    public void Razbivka_po_oblastyam_soobshchaet_ob_izmenenii()
    {
        // Считать правильно и не сказать об этом разметке это то же самое, что
        // считать неправильно: на экране остаётся прежнее число.
        var model = Model(Celikom("кэш", 5_000_000_000, RiskTier.Safe));
        var uslyshano = new List<string>();

        model.PropertyChanged += (_, e) => uslyshano.Add(e.PropertyName ?? "");

        model.Rows[0].IsSelected = false;

        uslyshano.Should().Contain(nameof(FilesViewModel.WholeCount));
        uslyshano.Should().Contain(nameof(FilesViewModel.EntryCount));
        uslyshano.Should().Contain(nameof(FilesViewModel.EntryFileCount));
        uslyshano.Should().Contain(nameof(FilesViewModel.EntrySummary));
    }

    [Fact]
    public void Malye_i_nulevye_nahodki_vidny_i_uchityvayutsya_v_vybore()
    {
        // A zero-byte file is still a file; the old size bouncer has been fired.
        var model = Model(
            Celikom("большое", 900L * 1024 * 1024, RiskTier.Safe),
            Celikom("мелочь", 1, RiskTier.Safe),
            Celikom("пустой кэш", 0, RiskTier.Safe),
            Celikom("пустой кандидат", 0, RiskTier.Risk));

        model.Rows.Select(row => row.Name).Should().Equal(
            "большое", "мелочь", "пустой кэш", "пустой кандидат");
        model.SelectedCount.Should().Be(3);
        model.SelectedBytes.Should().Be(943718401);
        model.TotalBytes.Should().Be(943718401);
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.State.HasRestriction.Should().BeFalse();
    }

    [Fact]
    public void Tolko_nulevaya_nahodka_ne_vydaetsya_za_chistyy_disk()
    {
        // Zero bytes do not grant the UI permission to erase a result.
        var model = Model(Celikom("пустой кэш", 0, RiskTier.Safe));
        model.Rows.Should().ContainSingle();
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.CanGoToConfirm.Should().BeTrue();
    }

    [Fact]
    public void Preryvanie_sohranyaet_melkie_nahodki_i_preduprezhdenie()
    {
        var model = Model();
        model.Prinyat(new ScanResult(
            [Celikom("мелочь", 1, RiskTier.Safe)], [], Cancelled: true));
        model.Rows.Should().ContainSingle();
        model.State.RestrictionText.Should().Contain("рервано");
    }
}
