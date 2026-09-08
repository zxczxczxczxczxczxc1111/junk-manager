using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class HistoryServiceTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    public HistoryServiceTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private string PustoyKatalog(string imya)
    {
        var katalog = Path.Combine(_pesochnica.Root, "history-" + imya);
        Directory.CreateDirectory(katalog);
        return katalog;
    }

    private static async Task ZapisatProgon(
        string katalog, DateTimeOffset kogda, params DeleteOutcome[] ishody)
    {
        using var zhurnal = JsonlOperationLog.CreateForRun(
            katalog, new FiksirovannoeVremya(kogda));

        foreach (var ishod in ishody)
        {
            await zhurnal.RecordAsync(ishod, CancellationToken.None);
        }
    }

    private static DateTimeOffset Moment(int den, int chas = 12) =>
        new(2026, 9, den, chas, 14, 8, TimeSpan.Zero);

    [Fact]
    public async Task Pustoy_katalog_daet_pustuyu_stranicu_a_ne_oshibku()
    {
        var sluzhba = new HistoryService(PustoyKatalog("pusto"));

        var stranica = await sluzhba.ReadAsync(CancellationToken.None);

        stranica.Runs.Should().BeEmpty();
        stranica.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Otsutstvuyushchiy_katalog_eto_tozhe_pusto()
    {
        // Каталог журналов создаётся первой очисткой. До неё его нет, и это
        // не сбой чтения.
        var sluzhba = new HistoryService(Path.Combine(_pesochnica.Root, "history-net"));

        var stranica = await sluzhba.ReadAsync(CancellationToken.None);

        stranica.Runs.Should().BeEmpty();
        stranica.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Odin_fayl_eto_odna_ochistka()
    {
        var katalog = PustoyKatalog("odna");
        await ZapisatProgon(katalog, Moment(4),
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1000),
            new DeleteOutcome("C:\\b", DeleteStatus.Deleted, 2000),
            new DeleteOutcome("C:\\c", DeleteStatus.Skipped, 0, "файл занят", "chrome.exe"));

        var stranica = await new HistoryService(katalog).ReadAsync(CancellationToken.None);

        stranica.Runs.Should().ContainSingle();
        stranica.Runs[0].BytesFreed.Should().Be(3000);
        stranica.Runs[0].DeletedCount.Should().Be(2);
        stranica.Runs[0].SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task Zagolovok_zapuska_ne_stanovitsya_strokoy_ishoda()
    {
        // Первая строка файла это НЕ исход, а шапка прогона: машина, человек,
        // процесс. Разобранная как исход, она даёт строку без пути и со
        // статусом, которого не бывает, и человек читает её как удаление.
        var katalog = PustoyKatalog("zagolovok");
        await ZapisatProgon(katalog, Moment(4),
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1000));

        var progon = (await new HistoryService(katalog).ReadAsync(CancellationToken.None)).Runs[0];

        progon.Outcomes.Should().ContainSingle();
        progon.Outcomes[0].Path.Should().Be("C:\\a");
    }

    [Fact]
    public async Task Progony_idut_ot_novyh_k_starym()
    {
        var katalog = PustoyKatalog("poryadok");
        await ZapisatProgon(katalog, Moment(3, chas: 17),
            new DeleteOutcome("C:\\staroe", DeleteStatus.Deleted, 1));
        await ZapisatProgon(katalog, Moment(4, chas: 21),
            new DeleteOutcome("C:\\novoe", DeleteStatus.Deleted, 1));

        var stranica = await new HistoryService(katalog).ReadAsync(CancellationToken.None);

        stranica.Runs[0].Outcomes[0].Path.Should().Be("C:\\novoe");
    }

    [Fact]
    public async Task Vremya_progona_beryotsya_iz_zapisi_a_ne_iz_imeni_fayla()
    {
        // Имя файла это удобство, а не источник правды: файл можно
        // переименовать, скопировать из другой машины или положить руками.
        // Время начала прогона лежит в самой записи.
        var katalog = PustoyKatalog("vremya");
        await ZapisatProgon(katalog, Moment(4, chas: 21),
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1));

        var fayl = Directory.GetFiles(katalog, "*.jsonl")[0];
        File.Move(fayl, Path.Combine(katalog, "pereimenovannyy.jsonl"));

        var stranica = await new HistoryService(katalog).ReadAsync(CancellationToken.None);

        stranica.Runs[0].StartedUtc.UtcDateTime
            .Should().Be(new DateTime(2026, 9, 4, 21, 14, 8, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Bitaya_zapis_ne_unosit_s_soboy_ostalnye()
    {
        var katalog = PustoyKatalog("bityy");
        await ZapisatProgon(katalog, Moment(4),
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1));

        await File.WriteAllTextAsync(
            Path.Combine(katalog, "20260905-090000-000.jsonl"),
            "{\"Kind\":\"outcome\",\"Path\":\"C:\\\\b\",\"Status\":",
            TestContext.Current.CancellationToken);

        var stranica = await new HistoryService(katalog).ReadAsync(CancellationToken.None);

        stranica.Runs.Should().ContainSingle("целая запись обязана открыться");
        stranica.Errors.Should().ContainSingle();
        stranica.Errors[0].Reason.Should().NotBeNullOrEmpty(
            "причина обязана называть позицию: «файл не разбирается» не действие");
    }

    [Fact]
    public async Task Zhurnal_idushchey_ochistki_chitaetsya()
    {
        // Журнал открыт на запись прямо сейчас. Обычное чтение просит
        // FileShare.Read и получает «файл занят другим процессом», то есть
        // экран журнала ломается ровно во время очистки, когда в него и
        // смотрят.
        var katalog = PustoyKatalog("zhivoy");

        using var zhurnal = JsonlOperationLog.CreateForRun(katalog);
        await zhurnal.RecordAsync(
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 5),
            TestContext.Current.CancellationToken);

        var stranica = await new HistoryService(katalog).ReadAsync(CancellationToken.None);

        stranica.Errors.Should().BeEmpty();
        stranica.Runs.Should().ContainSingle();
        stranica.Runs[0].Outcomes.Should().ContainSingle();
    }

    [Fact]
    public async Task Chetyre_ishoda_razlichayutsya_i_schitayutsya_otdelno()
    {
        var katalog = PustoyKatalog("chetyre");
        await ZapisatProgon(katalog, Moment(5, chas: 10),
            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 10),
            new DeleteOutcome("C:\\b", DeleteStatus.Skipped, 0, "нет прав"),
            new DeleteOutcome("C:\\c", DeleteStatus.Failed, 0, "отказано в доступе"),
            new DeleteOutcome("C:\\d", DeleteStatus.Cancelled, 0, "остановлено человеком"));

        var progon = (await new HistoryService(katalog).ReadAsync(CancellationToken.None)).Runs[0];

        progon.DeletedCount.Should().Be(1);
        progon.SkippedCount.Should().Be(1);
        progon.FailedCount.Should().Be(1);
        progon.CancelledCount.Should().Be(1);
        Enum.GetValues<DeleteStatus>().Should().HaveCount(4);
    }

    [Fact]
    public async Task Prichina_i_derzhatel_perezhivayut_zapis_i_chtenie()
    {
        // Ради причины журнал и читают. Потерянная при разборе, она
        // превращает «не смогли, файл держит chrome» в «не смогли».
        var katalog = PustoyKatalog("prichina");
        await ZapisatProgon(katalog, Moment(5),
            new DeleteOutcome("C:\\a", DeleteStatus.Failed, 0, "файл занят", "chrome.exe"));

        var ishod = (await new HistoryService(katalog).ReadAsync(CancellationToken.None))
            .Runs[0].Outcomes[0];

        ishod.Reason.Should().Be("файл занят");
        ishod.HoldingProcess.Should().Be("chrome.exe");
    }
}
