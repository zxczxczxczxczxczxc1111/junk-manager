using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Scanning;
using Xunit;

namespace JunkManager.Tests.Scanning;

/// <summary>
/// Дедупликация по вложенности МЕЖДУ источниками. Внутри правил она уже есть и
/// живёт в FileScanner, но между источниками не работала вовсе: тот проход
/// видит только правила. Ближайший случай прямо из общих ограничений плана:
/// LeftoverFinder предлагает подкаталог Cache, а ровно такой же путь уже берут
/// правила браузеров, и 5000 байт на диске показывались бы как 10000.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class SliyanieIstochnikovTests
{
    private static Finding Nahodka(string put, long bayt, FindingSource istochnik) =>
        new("имя", put, bayt, RiskTier.Safe, "последствие", istochnik);

    private static ScanResult Osnova(params Finding[] nahodki) => new(nahodki, []);

    /// <summary>
    /// Пути строятся от настоящего %LOCALAPPDATA%, а не выдумываются. Вложенность
    /// сверяется через SafetyGuard, и путь в чужом профиле guard отказывает
    /// целиком: тест на выдуманном C:\Users\x проверял бы не слияние, а отказ
    /// guard, и был бы зелёным при сломанном слиянии.
    /// </summary>
    private static string Put(params string[] chasti)
    {
        var koren = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine([koren, .. chasti]);
    }

    [Fact]
    public void Slit_odin_i_tot_zhe_put_iz_dvuh_istochnikov_schitaetsya_odin_raz()
    {
        var osnova = Osnova(Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Rule));
        var sled = Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], []);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(5000,
            "5000 байт на диске не превращаются в 10000 от того, что их нашли дважды");
        itog.Skipped.Should().ContainSingle(s => s.Reason.Contains("уже посчитан", StringComparison.Ordinal));
    }

    [Fact]
    public void Slit_vlozhennyy_put_ne_dobavlyaetsya()
    {
        var osnova = Osnova(Nahodka(Put("Vendor"), 9000, FindingSource.Rule));
        var sled = Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], []);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(9000);
    }

    [Fact]
    public void Slit_ohvatyvayushchiy_put_tozhe_ne_dobavlyaetsya()
    {
        // Обратная сторона той же монеты: добавляемый путь ШИРЕ уже посчитанного.
        // Взять его значит посчитать внутренние байты второй раз, а уступает
        // именно догадка: правило знает, что берёт, след только предполагает.
        var osnova = Osnova(Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Rule));
        var sled = Nahodka(Put("Vendor"), 9000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], []);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(5000);
    }

    [Fact]
    public void Slit_nepersekayushchiysya_put_dobavlyaetsya()
    {
        var osnova = Osnova(Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Rule));
        var sled = Nahodka(Put("Drugoy", "Cache"), 3000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], []);

        itog.Findings.Should().HaveCount(2);
        itog.TotalBytes.Should().Be(8000);
    }

    [Fact]
    public void Slit_dva_dobavlyaemyh_ne_perekryvayut_drug_druga()
    {
        var pervyy = Nahodka(Put("Vendor"), 9000, FindingSource.Program);
        var vtoroy = Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(Osnova(), [pervyy, vtoroy], []);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(9000);
    }

    [Fact]
    public void Slit_propuski_edut_vmeste_s_nahodkami()
    {
        var itog = SliyanieIstochnikov.Slit(
            Osnova(), [], [new SkippedItem(@"C:\gde-to", "каталог не читается")]);

        itog.Skipped.Should().ContainSingle(s => s.Path == @"C:\gde-to");
    }

    [Fact]
    public void Slit_otmenennyy_prohod_ostayotsya_otmenennym()
    {
        // Иначе неполный результат читается как полный, а это ровно тот
        // неверный ответ, который никто не идёт проверять.
        var osnova = new ScanResult([], [], Cancelled: true);

        SliyanieIstochnikov.Slit(osnova, [], []).Cancelled.Should().BeTrue();
    }
}
