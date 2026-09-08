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
    [Theory]
    [InlineData(2048)]
    [InlineData(32768)]
    public void Large_disjoint_caches_do_not_rescan_every_previously_found_file(int count)
    {
        // Two caches should not hold a billion pairwise introductions before dinner.
        var first = new BoundedTargets(Enumerable.Range(0, count).Select(i => Put("FirstCache", $"{i}.bin")).ToArray(), count * 4);
        var second = Enumerable.Range(0, count).Select(i => Put("SecondCache", $"{i}.bin")).ToArray();
        var left = Nahodka(Put("FirstCache"), count, FindingSource.Rule) with { Scope = DeleteScope.SelectedEntries, Targets = first };
        var right = Nahodka(Put("SecondCache"), count, FindingSource.Rule) with { Scope = DeleteScope.SelectedEntries, Targets = second };
        var merge = () => SliyanieIstochnikov.Slit(ScanResult.Empty, [left, right], [], TestContext.Current.CancellationToken);
        var result = merge.Should().NotThrow("работа должна расти с числом файлов, а не числом всех их пар").Subject;
        result.TotalBytes.Should().Be(count * 2);
        result.Findings.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancellation_during_target_indexing_keeps_only_complete_findings(bool existing)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var targets = new BoundedTargets(Enumerable.Range(0, 2048).Select(i => Put("CancelCache", $"{i}.bin")).ToArray(), 32,
            reads => { if (reads == 16) stop.Cancel(); });
        var cache = Nahodka(Put("CancelCache"), 2048, FindingSource.Rule) with { Scope = DeleteScope.SelectedEntries, Targets = targets };
        var next = Nahodka(Put("LaterCache"), 10, FindingSource.Program);
        var result = existing
            ? SliyanieIstochnikov.Slit(Osnova(cache), [next], [], stop.Token)
            : SliyanieIstochnikov.Slit(ScanResult.Empty, [cache, next], [], stop.Token);
        result.Cancelled.Should().BeTrue();
        result.Findings.Should().NotContain(next);
        result.TotalBytes.Should().Be(existing ? 2048 : 0);
    }

    [Fact]
    public void Selected_snapshot_does_not_cover_unseen_siblings_or_similar_prefixes()
    {
        var first = Nahodka(Put("Cache"), 5, FindingSource.Rule) with
        { Scope = DeleteScope.SelectedEntries, Targets = [Put("Cache", "old.bin")] };
        var sibling = Nahodka(Put("Cache", "new.bin"), 3, FindingSource.Program);
        var prefix = Nahodka(Put("CacheBackup"), 7, FindingSource.Program);
        var parent = Nahodka(Put("Cache"), 8, FindingSource.Program);
        var alias = Nahodka(Put("CACHE", "nested", "..", "old.bin"), 5, FindingSource.Program);
        var result = SliyanieIstochnikov.Slit(Osnova(first), [sibling, prefix, parent, alias], [], TestContext.Current.CancellationToken);
        result.Findings.Should().Equal(first, sibling, prefix);
        result.TotalBytes.Should().Be(15);
    }

    [Fact]
    public void Vacuum_is_not_confused_with_deleting_the_database_file()
    {
        var deletion = Nahodka(Put("Cache", "data.db"), 100, FindingSource.Rule);
        var vacuum = deletion with { Source = FindingSource.Vacuum, SizeBytes = 10 };
        var result = SliyanieIstochnikov.Slit(Osnova(vacuum), [deletion, vacuum], [], TestContext.Current.CancellationToken);
        result.Findings.Should().Equal(vacuum, deletion);
    }

    private sealed class BoundedTargets(string[] paths, int budget, Action<int>? onRead = null) : IReadOnlyList<string>
    {
        private int _reads;
        public int Count => paths.Length;
        public string this[int index] => Read(index);
        private string Read(int index)
        {
            if (++_reads > budget) throw new InvalidOperationException("Repeated target traversal exceeded the linear read budget");
            onRead?.Invoke(_reads);
            return paths[index];
        }
        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < paths.Length; i++) yield return Read(i);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

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

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], [], TestContext.Current.CancellationToken);

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

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], [], TestContext.Current.CancellationToken);

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

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], [], TestContext.Current.CancellationToken);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(5000);
    }

    [Fact]
    public void Slit_nepersekayushchiysya_put_dobavlyaetsya()
    {
        var osnova = Osnova(Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Rule));
        var sled = Nahodka(Put("Drugoy", "Cache"), 3000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(osnova, [sled], [], TestContext.Current.CancellationToken);

        itog.Findings.Should().HaveCount(2);
        itog.TotalBytes.Should().Be(8000);
    }

    [Fact]
    public void Slit_dva_dobavlyaemyh_ne_perekryvayut_drug_druga()
    {
        var pervyy = Nahodka(Put("Vendor"), 9000, FindingSource.Program);
        var vtoroy = Nahodka(Put("Vendor", "Cache"), 5000, FindingSource.Program);

        var itog = SliyanieIstochnikov.Slit(Osnova(), [pervyy, vtoroy], [], TestContext.Current.CancellationToken);

        itog.Findings.Should().ContainSingle();
        itog.TotalBytes.Should().Be(9000);
    }

    [Fact]
    public void Slit_propuski_edut_vmeste_s_nahodkami()
    {
        var itog = SliyanieIstochnikov.Slit(
            Osnova(), [], [new SkippedItem(@"C:\gde-to", "каталог не читается")], TestContext.Current.CancellationToken);

        itog.Skipped.Should().ContainSingle(s => s.Path == @"C:\gde-to");
    }

    [Fact]
    public void Slit_otmenennyy_prohod_ostayotsya_otmenennym()
    {
        // Иначе неполный результат читается как полный, а это ровно тот
        // неверный ответ, который никто не идёт проверять.
        var osnova = new ScanResult([], [], Cancelled: true);

        SliyanieIstochnikov.Slit(osnova, [], [], TestContext.Current.CancellationToken).Cancelled.Should().BeTrue();
    }
}
