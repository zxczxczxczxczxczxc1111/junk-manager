using Xunit;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Scanning;

/// <summary>
/// Правило с отсечкой считает байты по фильтру, а каталог удаляется целиком.
/// Пока эти две вещи расходятся, продукт показывает одно число и уносит другое,
/// причём разница это ровно те файлы, которые человек хотел оставить. Здесь
/// проверяется, что находка знает свои настоящие цели.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class OblastUdaleniyaTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static RuleDefinition Pravilo(
        string id, string path, int olderThanDays = 0, string? fileFilter = null) =>
        new(id, "Правило " + id, [path], "Safe", "Ничего важного не пропадёт.",
            olderThanDays, fileFilter)
        {
            Category = "Тест",
            Tier = RiskTier.Safe,
        };

    private static string Fayl(string dir, string name, int bytes, int ageDays = 0)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);

        var kogda = DateTime.UtcNow.AddDays(-ageDays);
        File.SetLastWriteTimeUtc(path, kogda);
        File.SetLastAccessTimeUtc(path, kogda);
        return path;
    }

    private static async Task<Finding> OdnaNahodka(RuleDefinition pravilo)
    {
        var itog = await new FileScanner().ScanAsync([pravilo], null, TestContext.Current.CancellationToken);
        itog.Findings.Should().ContainSingle("правило обязано дать ровно одну находку");
        return itog.Findings[0];
    }

    [Fact]
    public async Task Pravilo_bez_filtra_zapominaet_fayly_a_ne_budushchee_soderzhimoe()
    {
        var dir = _pesochnica.CreateDirectory("celikom");
        var first = Fayl(dir, "a.bin", 1000);
        var second = Fayl(dir, "b.bin", 2000);

        var nahodka = await OdnaNahodka(Pravilo("celikom", dir));

        var later = Fayl(dir, "later.bin", 500);
        nahodka.Scope.Should().Be(DeleteScope.SelectedEntries);
        nahodka.DeletionTargets.Should().BeEquivalentTo(first, second);
        nahodka.DeletionTargets.Should().NotContain(later);
    }

    [Fact]
    public async Task Otsechka_po_vozrastu_perevodit_nahodku_v_poelementnyy_rezhim()
    {
        var dir = _pesochnica.CreateDirectory("vozrast-celi");
        var staryy = Fayl(dir, "staryy.bin", 5000, ageDays: 40);
        var svezhiy = Fayl(dir, "svezhiy.bin", 7000, ageDays: 1);

        var nahodka = await OdnaNahodka(Pravilo("vozrast", dir, olderThanDays: 30));

        nahodka.Scope.Should().Be(DeleteScope.SelectedEntries);
        nahodka.DeletionTargets.Should().Equal(staryy);
        nahodka.DeletionTargets.Should().NotContain(svezhiy,
            "свежий файл не входил в подсчёт байтов, значит не имеет права уйти");
        nahodka.Path.Should().Be(dir, "человек в списке видит каталог, а не пять тысяч строк");
    }

    [Fact]
    public async Task Bayty_nahodki_ravny_summe_ee_celey()
    {
        var dir = _pesochnica.CreateDirectory("chestnyy-schet");
        Fayl(dir, "staryy-1.bin", 4000, ageDays: 40);
        Fayl(dir, "staryy-2.bin", 6000, ageDays: 50);
        Fayl(dir, "svezhiy.bin", 100000, ageDays: 0);

        var nahodka = await OdnaNahodka(Pravilo("schet", dir, olderThanDays: 30));

        var poCelyam = nahodka.DeletionTargets.Sum(c => new FileInfo(c).Length);

        nahodka.SizeBytes.Should().Be(poCelyam,
            "показанное число и унесённое число обязаны совпадать, иначе отчёт врёт");
        nahodka.SizeBytes.Should().Be(10000);
    }

    [Fact]
    public async Task Svezhiy_fayl_v_glubine_ne_popadaet_v_celi()
    {
        // Ровно случай пользовательского %TEMP%: старый мусор и живой лог лежат
        // в одном подкаталоге. Каталог целиком брать нельзя.
        var dir = _pesochnica.CreateDirectory("smeshannyy");
        var vlozhennyy = Path.Combine(dir, "vlozhennyy");
        var staryy = Fayl(vlozhennyy, "staryy.tmp", 3000, ageDays: 40);
        var zhivoy = Fayl(vlozhennyy, "zhivoy.log", 500, ageDays: 0);

        var nahodka = await OdnaNahodka(Pravilo("smesh", dir, olderThanDays: 7));

        nahodka.DeletionTargets.Should().Equal(staryy);
        nahodka.DeletionTargets.Should().NotContain(vlozhennyy,
            "подкаталог со свежим файлом внутри не является целью");
        nahodka.DeletionTargets.Should().NotContain(zhivoy);
    }

    [Fact]
    public async Task Maska_imeni_tozhe_perevodit_v_poelementnyy_rezhim()
    {
        var dir = _pesochnica.CreateDirectory("maska");
        var kesh = Fayl(dir, "dannye.cache", 2000);
        var nastroyki = Fayl(dir, "nastroyki.json", 300);

        var nahodka = await OdnaNahodka(Pravilo("maska", dir, fileFilter: "*.cache"));

        nahodka.Scope.Should().Be(DeleteScope.SelectedEntries);
        nahodka.DeletionTargets.Should().Equal(kesh);
        nahodka.DeletionTargets.Should().NotContain(nastroyki,
            "маска отбирала файлы для подсчёта, значит она же отбирает их для удаления");
    }

    [Fact]
    public async Task Fayl_kak_put_pravila_ostaetsya_celym_dazhe_pri_filtre()
    {
        // Путь правила указывает прямо в файл. Поэлементный режим тут ничего не
        // значит: цель и есть сам файл.
        var dir = _pesochnica.CreateDirectory("odin-fayl");
        var fayl = Fayl(dir, "staryy.bin", 1000, ageDays: 40);

        var nahodka = await OdnaNahodka(Pravilo("odin", fayl, olderThanDays: 30));

        nahodka.Scope.Should().Be(DeleteScope.Whole);
        nahodka.DeletionTargets.Should().Equal(fayl);
    }

    [Fact]
    public void Poelementnaya_nahodka_bez_spiska_celey_ne_molchit()
    {
        // Собрать такую находку значит попросить удалить «всё или ничего» без
        // указания чего именно. Тихий пустой список тут опаснее исключения.
        var kriv = new Finding(
            "Кривая", @"C:\Users\kto-to\AppData\Local\chto-to", 100,
            RiskTier.Safe, "Последствие.", FindingSource.Rule,
            Scope: DeleteScope.SelectedEntries);

        var act = () => kriv.DeletionTargets;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*без списка целей*");
    }
}
