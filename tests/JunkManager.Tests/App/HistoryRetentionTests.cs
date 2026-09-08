using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// The retention setting says old journal entries are removed. These are the
/// gates that make the sentence true, and that keep it from removing anything
/// else.
/// </summary>
/// <remarks>
/// Строчка на экране настроек это обещание. Невыполненное, оно ничем не
/// отличается от вранья: человек выставил «30 дней» и считает, что журнал
/// подчищается, а он растёт.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class HistoryRetentionTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    public HistoryRetentionTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private static readonly DateTimeOffset Seychas = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private string Katalog(string imya)
    {
        var katalog = Path.Combine(_pesochnica.Root, "retention-" + imya);
        Directory.CreateDirectory(katalog);
        return katalog;
    }

    /// <summary>Кладёт журнал с одной записью и назначает ему возраст.</summary>
    private static async Task<string> Progon(string katalog, int dneyNazad)
    {
        var kogda = Seychas.AddDays(-dneyNazad);

        string put;
        using (var zhurnal = JsonlOperationLog.CreateForRun(katalog, new FiksirovannoeVremya(kogda)))
        {
            put = zhurnal.FilePath;
            await zhurnal.RecordAsync(
                new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 10), CancellationToken.None);
        }

        // Возраст берётся по последней записи файла: имя можно переименовать,
        // а принесённый с другой машины файл имени вовсе не подчиняется.
        File.SetLastWriteTimeUtc(put, kogda.UtcDateTime);
        return put;
    }

    private static HistoryService Sluzhba(string katalog) =>
        new(katalog, new FiksirovannoeVremya(Seychas));

    [Fact]
    public async Task Zapis_starshe_sroka_udalyaetsya()
    {
        var katalog = Katalog("staroe");
        var staryy = await Progon(katalog, dneyNazad: 100);
        var svezhiy = await Progon(katalog, dneyNazad: 2);

        var ubrano = await Sluzhba(katalog).UbratStaryeAsync(90, TestContext.Current.CancellationToken);

        ubrano.Should().Be(1);
        File.Exists(staryy).Should().BeFalse();
        File.Exists(svezhiy).Should().BeTrue();
    }

    [Fact]
    public async Task Rovno_na_granice_ostaetsya()
    {
        // Отсечка строгая. Иначе запись, сделанную ровно 90 дней назад, уносит
        // при сроке «90 дней», то есть срок на деле оказывается на день короче.
        var katalog = Katalog("granica");
        var granichnyy = await Progon(katalog, dneyNazad: 90);

        var ubrano = await Sluzhba(katalog).UbratStaryeAsync(90, TestContext.Current.CancellationToken);

        ubrano.Should().Be(0);
        File.Exists(granichnyy).Should().BeTrue();
    }

    [Fact]
    public async Task Nol_znachit_hranit_vsyo()
    {
        var katalog = Katalog("bessrochno");
        var drevniy = await Progon(katalog, dneyNazad: 4000);

        var ubrano = await Sluzhba(katalog).UbratStaryeAsync(0, TestContext.Current.CancellationToken);

        ubrano.Should().Be(0);
        File.Exists(drevniy).Should().BeTrue();
    }

    [Fact]
    public async Task Chuzhie_fayly_v_kataloge_ne_trogayutsya()
    {
        // Каталог наш, но лежать в нём может что угодно: человек мог сложить
        // туда свою выписку. Уборка знает ровно про свои .jsonl.
        var katalog = Katalog("chuzhie");
        await Progon(katalog, dneyNazad: 500);

        var chuzhoy = Path.Combine(katalog, "zametka.txt");
        await File.WriteAllTextAsync(chuzhoy, "не трогать", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(chuzhoy, Seychas.AddDays(-500).UtcDateTime);

        await Sluzhba(katalog).UbratStaryeAsync(30, TestContext.Current.CancellationToken);

        File.Exists(chuzhoy).Should().BeTrue();
    }

    [Fact]
    public async Task Otsutstvuyushchiy_katalog_ne_oshibka()
    {
        var net = Path.Combine(_pesochnica.Root, "retention-net-takogo");

        var ubrano = await Sluzhba(net).UbratStaryeAsync(30, TestContext.Current.CancellationToken);

        ubrano.Should().Be(0);
    }

    [Fact]
    public async Task Zanyatyy_fayl_ne_ronyaet_uborku()
    {
        // Журнал идущей очистки открыт на запись. Уборка обязана пройти мимо
        // него и убрать остальное, а не упасть и не убрать ничего.
        var katalog = Katalog("zanyatyy");
        var pervyy = await Progon(katalog, dneyNazad: 300);
        var vtoroy = await Progon(katalog, dneyNazad: 301);

        using (var derzhatel = new FileStream(
            pervyy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ubrano = await Sluzhba(katalog).UbratStaryeAsync(
                30, TestContext.Current.CancellationToken);

            ubrano.Should().Be(1);
            derzhatel.CanRead.Should().BeTrue();
        }

        File.Exists(pervyy).Should().BeTrue("файл был занят, и уборка прошла мимо");
        File.Exists(vtoroy).Should().BeFalse();
    }

    [Fact]
    public async Task Otricatelnyy_srok_eto_oshibka_a_ne_uborka_vsego()
    {
        var katalog = Katalog("otricatelnyy");
        var progon = await Progon(katalog, dneyNazad: 1);

        var deystvie = async () => await Sluzhba(katalog).UbratStaryeAsync(
            -1, TestContext.Current.CancellationToken);

        await deystvie.Should().ThrowAsync<ArgumentOutOfRangeException>();
        File.Exists(progon).Should().BeTrue();
    }
}
