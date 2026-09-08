using Xunit;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Scanning;

/// <summary>
/// Два правила, одно из которых указывает внутрь другого.
/// </summary>
/// <remarks>
/// Случай не выдуманный. Правило temp-user берёт %TEMP%, правило
/// dotnet-sdk-temp берёт %LOCALAPPDATA%\Temp\MSBuildTemp*, и на обычной машине
/// это один и тот же каталог верхнего уровня. Дедупликация по точному
/// совпадению строки такое пересечение не видит, и байты внутри считаются
/// дважды. Удаление при этом честное: второй проход получит «путь исчез».
/// Врёт предпросмотр, то есть ровно то, по чему человек принимает решение.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class VlozhennyePutiTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static RuleDefinition Pravilo(string id, string path, int olderThanDays = 0) =>
        new(id, "Правило " + id, [path], "Safe", "Ничего важного не пропадёт.", olderThanDays)
        {
            Category = "Тест",
            Tier = RiskTier.Safe,
        };

    private static void Fayl(string dir, string name, int bytes, int ageDays = 0)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);

        var kogda = DateTime.UtcNow.AddDays(-ageDays);
        File.SetLastWriteTimeUtc(path, kogda);
        File.SetLastAccessTimeUtc(path, kogda);
    }

    [Fact]
    public async Task Vlozhennyy_put_ne_daet_vtoruyu_nahodku_i_ne_udvaivaet_bayty()
    {
        var shirokiy = _pesochnica.CreateDirectory("shirokiy");
        var uzkiy = Path.Combine(shirokiy, "vnutri");
        Fayl(shirokiy, "svoy.bin", 1000);
        Fayl(uzkiy, "obshchiy.bin", 4000);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("shirokiy", shirokiy), Pravilo("uzkiy", uzkiy)],
            null,
            TestContext.Current.CancellationToken);

        itog.Findings.Should().ContainSingle("вложенный путь уже посчитан широким правилом");
        itog.TotalBytes.Should().Be(5000, "5000 байт на диске обязаны остаться 5000 в отчёте");
    }

    [Fact]
    public async Task Poryadok_pravil_na_rezultat_ne_vliyaet()
    {
        var shirokiy = _pesochnica.CreateDirectory("poryadok");
        var uzkiy = Path.Combine(shirokiy, "vnutri");
        Fayl(uzkiy, "obshchiy.bin", 3000);

        var pryamoy = await new FileScanner().ScanAsync(
            [Pravilo("shirokiy", shirokiy), Pravilo("uzkiy", uzkiy)],
            null, TestContext.Current.CancellationToken);

        var obratnyy = await new FileScanner().ScanAsync(
            [Pravilo("uzkiy", uzkiy), Pravilo("shirokiy", shirokiy)],
            null, TestContext.Current.CancellationToken);

        // Побеждает ШИРОКОЕ правило в обоих порядках. Иначе одна и та же машина
        // давала бы разные отчёты в зависимости от того, как отсортировались
        // файлы правил в каталоге.
        pryamoy.Findings.Should().ContainSingle();
        obratnyy.Findings.Should().ContainSingle();
        obratnyy.Findings[0].Path.Should().Be(shirokiy);
        obratnyy.TotalBytes.Should().Be(pryamoy.TotalBytes);
    }

    [Fact]
    public async Task Overlapping_rules_keep_stricter_risk_and_both_process_requirements()
    {
        var root = _pesochnica.CreateDirectory("overlap");
        var child = Path.Combine(root, "child");
        Fayl(child, "cache.bin", 3);
        var broad = Pravilo("broad", root) with { ProcessNames = ["one"] };
        var narrow = Pravilo("narrow", child) with { Tier = RiskTier.Risk, ProcessNames = ["two"], Consequence = "Manual review" };
        var scanner = new FileScanner(processRefusal: _ => null);
        var result = await scanner.ScanAsync([narrow, broad], null, TestContext.Current.CancellationToken);
        result.Findings.Should().ContainSingle();
        result.Findings[0].Tier.Should().Be(RiskTier.Risk);
        result.Findings[0].RequiredStoppedProcesses.Should().BeEquivalentTo("one", "two");
        result.Findings[0].Consequence.Should().Contain("Manual review");
        result.TotalBytes.Should().Be(3);
    }

    [Fact]
    public async Task Sosednie_katalogi_ne_schitayutsya_vlozhennymi()
    {
        // Проверка на подмену вложенности префиксом: строка «...\dann» является
        // префиксом строки «...\dannye», но каталогом её не является.
        var pervyy = _pesochnica.CreateDirectory("dann");
        var vtoroy = _pesochnica.CreateDirectory("dannye");
        Fayl(pervyy, "a.bin", 1000);
        Fayl(vtoroy, "b.bin", 2000);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("pervyy", pervyy), Pravilo("vtoroy", vtoroy)],
            null, TestContext.Current.CancellationToken);

        itog.Findings.Should().HaveCount(2, "соседние каталоги это две разные находки");
        itog.TotalBytes.Should().Be(3000);
    }

    [Fact]
    public async Task Otbroshennyy_put_popadaet_v_propushchennye_s_prichinoy()
    {
        // Молча выброшенный путь неотличим от правила, которое ничего не нашло.
        // Разница важна: во втором случае правило надо чинить, в первом нет.
        var shirokiy = _pesochnica.CreateDirectory("obyasnenie");
        var uzkiy = Path.Combine(shirokiy, "vnutri");
        Fayl(uzkiy, "obshchiy.bin", 1000);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("shirokiy", shirokiy), Pravilo("uzkiy", uzkiy)],
            null, TestContext.Current.CancellationToken);

        itog.Skipped.Should().ContainSingle(s => s.Path == uzkiy)
            .Which.Reason.Should().Contain(shirokiy);
    }
}
