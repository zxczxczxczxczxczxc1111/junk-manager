using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Deletion;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class HistoryViewModelTests
{
    /// <summary>Журнал, который ничего не читает с диска.</summary>
    private sealed class Zaglushka(HistoryPage stranica) : IHistoryService
    {
        public string Directory => "C:\\net-takogo";

        public Task<HistoryPage> ReadAsync(CancellationToken ct) => Task.FromResult(stranica);
    }

    private static HistoryRun Progon(int den, params DeleteOutcome[] ishody) =>
        new($"C:\\zhurnal\\{den}.jsonl",
            new DateTimeOffset(2026, 9, den, 12, 0, 0, TimeSpan.Zero),
            ishody);

    private static async Task<HistoryViewModel> Zagruzit(HistoryPage stranica)
    {
        var model = new HistoryViewModel(new Zaglushka(stranica));
        await model.ZagruzitAsync(TestContext.Current.CancellationToken);
        return model;
    }

    /// <summary>Журнал, отдающий на каждое чтение новую страницу.</summary>
    private sealed class Menyayushchiysya(Queue<HistoryPage> stranicy) : IHistoryService
    {
        public string Directory => "C:\\net-takogo";

        public int Chteniy { get; private set; }

        public Task<HistoryPage> ReadAsync(CancellationToken ct)
        {
            Chteniy++;
            return Task.FromResult(stranicy.Dequeue());
        }
    }

    [Fact]
    public async Task Pokaz_razdela_perechityvaet_zhurnal_cherez_interfeys()
    {
        // Зовётся ИМЕННО через IScreenViewModel, а не напрямую. Договор объявлен
        // методом интерфейса со значением по умолчанию: стоит ошибиться в
        // сигнатуре, и вызов уедет в пустую заглушку интерфейса, метод класса
        // никто не позовёт, а сборка при этом останется зелёной.
        var sluzhba = new Menyayushchiysya(new Queue<HistoryPage>(
        [
            new HistoryPage([], []),
            new HistoryPage(
                [Progon(6, new DeleteOutcome("C:\\svezhee", DeleteStatus.Deleted, 42))], []),
        ]));

        IScreenViewModel ekran = new HistoryViewModel(sluzhba);

        await ekran.PriPokazeAsync(TestContext.Current.CancellationToken);
        await ekran.PriPokazeAsync(TestContext.Current.CancellationToken);

        sluzhba.Chteniy.Should().Be(2, "каждый показ раздела обязан читать журнал заново");
        ((HistoryViewModel)ekran).Runs.Should().ContainSingle(
            "второе чтение обязано доехать до списка, а не осесть в службе");
    }

    [Fact]
    public async Task Pustoy_zhurnal_eto_pustoe_sostoyanie_a_ne_oshibka()
    {
        var model = await Zagruzit(new HistoryPage([], []));

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.Runs.Should().BeEmpty();
        model.State.EmptyBody.Should().Contain("не средство отката");
    }

    [Fact]
    public async Task Pervyy_progon_vybran_srazu()
    {
        // The largest receipt opens first; the empty half of a screen has retired.
        var model = await Zagruzit(new HistoryPage(
            [
                Progon(5, new DeleteOutcome("C:\\novoe", DeleteStatus.Deleted, 10)),
                Progon(4, new DeleteOutcome("C:\\staroe", DeleteStatus.Deleted, 20)),
            ],
            []));

        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.Current.Should().NotBeNull();
        model.Current!.Outcomes[0].Path.Should().Be("C:\\staroe");
    }

    [Fact]
    public async Task Bitaya_zapis_daet_plashku_i_ne_pryachet_celye()
    {
        var model = await Zagruzit(new HistoryPage(
            [Progon(5, new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1))],
            [new HistoryReadError("C:\\zhurnal\\bityy.jsonl", "неожиданный символ на позиции 1208")]));

        model.State.Phase.Should().Be(ScreenPhase.Ready, "целая запись открылась");
        model.Runs.Should().ContainSingle();
        model.State.HasRestriction.Should().BeTrue();
        model.State.RestrictionText.Should().Contain("bityy.jsonl");
        model.State.RestrictionText.Should().Contain("1208", "причина обязана называть место");
    }

    [Fact]
    public async Task Entries_inside_a_run_are_sorted_by_freed_size()
    {
        var model = await Zagruzit(new HistoryPage([Progon(5,
            new DeleteOutcome("small", DeleteStatus.Deleted, 5),
            new DeleteOutcome("failed", DeleteStatus.Failed, 0, "fixture"),
            new DeleteOutcome("large", DeleteStatus.Deleted, 50))], []));
        model.Current!.Outcomes.Select(outcome => outcome.Path).Should().Equal("large", "small", "failed");
    }

    [Fact]
    public async Task Povtornaya_zagruzka_ne_udvaivaet_spisok()
    {
        var stranica = new HistoryPage(
            [Progon(5, new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1))], []);

        var model = new HistoryViewModel(new Zaglushka(stranica));
        await model.ZagruzitAsync(TestContext.Current.CancellationToken);
        await model.ZagruzitAsync(TestContext.Current.CancellationToken);

        model.Runs.Should().ContainSingle();
    }

    [Fact]
    public async Task Plashka_snimaetsya_kogda_bityh_zapisey_bolshe_net()
    {
        // Иначе плашка про сломанный файл переживает его удаление и висит до
        // перезапуска, то есть врёт о состоянии каталога.
        var model = new HistoryViewModel(new Zaglushka(new HistoryPage(
            [Progon(5, new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1))],
            [new HistoryReadError("C:\\zhurnal\\bityy.jsonl", "обрыв")])));

        await model.ZagruzitAsync(TestContext.Current.CancellationToken);
        model.State.HasRestriction.Should().BeTrue();

        var chistyy = new HistoryViewModel(new Zaglushka(new HistoryPage(
            [Progon(5, new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1))], [])));

        await chistyy.ZagruzitAsync(TestContext.Current.CancellationToken);
        chistyy.State.HasRestriction.Should().BeFalse();
    }

    [Fact]
    public async Task Progon_govorit_skolko_strok_konchilis_ne_udaleniem()
    {
        // Иначе прогон, где не удалилось ВСЁ, читается как «0 объектов
        // удалено» и неотличим от прогона, в котором нечего было удалять.
        var model = await Zagruzit(new HistoryPage(
            [
                Progon(5,
                    new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 10),
                    new DeleteOutcome("C:\\b", DeleteStatus.Skipped, 0, "занят"),
                    new DeleteOutcome("C:\\c", DeleteStatus.Failed, 0, "нет прав"),
                    new DeleteOutcome("C:\\d", DeleteStatus.Cancelled, 0, "остановлено")),
                Progon(4, new DeleteOutcome("C:\\e", DeleteStatus.Deleted, 1)),
            ],
            []));

        model.Runs[0].UnfinishedCount.Should().Be(3);
        model.Runs[1].UnfinishedCount.Should().Be(0, "чистому прогону строку показывать не о чем");
    }

    [Fact]
    public async Task Schet_progona_sobiraetsya_po_ishodam()
    {
        var model = await Zagruzit(new HistoryPage(
            [
                Progon(5,
                    new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1000),
                    new DeleteOutcome("C:\\b", DeleteStatus.Skipped, 0, "занят"),
                    new DeleteOutcome("C:\\c", DeleteStatus.Failed, 0, "нет прав"),
                    new DeleteOutcome("C:\\d", DeleteStatus.Cancelled, 0, "остановлено")),
            ],
            []));

        var progon = model.Runs[0];

        progon.BytesFreed.Should().Be(1000);
        progon.DeletedCount.Should().Be(1);
        progon.SkippedCount.Should().Be(1);
        progon.FailedCount.Should().Be(1);
        progon.CancelledCount.Should().Be(1);
    }
}
