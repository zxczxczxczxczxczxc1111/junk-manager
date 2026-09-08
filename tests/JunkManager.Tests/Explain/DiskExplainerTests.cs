using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Explain;
using JunkManager.Core.Interop;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Explain;

[Trait("Class", "Sandbox")]
public sealed class DiskExplainerTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public DiskExplainerTests(SandboxFixture sandbox) => _sandbox = sandbox;

    private static Finding Nahodka(string path, long bytes, FindingSource source) =>
        new("имя", path, bytes, RiskTier.Safe, "объяснение", source);

    private static void Napolnit(string directory, int files, int bytes)
    {
        Directory.CreateDirectory(directory);

        for (var i = 0; i < files; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, $"f{i}.bin"), new byte[bytes]);
        }
    }

    [Fact]
    public void TryGetVolume_zhivoy_tom_daet_polozhitelnye_chisla()
    {
        DiskSpace.TryGetVolume(@"C:\", out var total, out var free).Should().BeTrue();

        total.Should().BeGreaterThan(0);
        free.Should().BeGreaterThan(0);
        free.Should().BeLessThan(total);
    }

    [Fact]
    public void TryGetVolume_nesushchestvuyushchiy_put_daet_otkaz_a_ne_nuli()
    {
        var net = Path.Combine(@"C:\", $"net-takogo-kataloga-{Guid.NewGuid():N}") + Path.DirectorySeparatorChar;

        DiskSpace.TryGetVolume(net, out var total, out var free).Should().BeFalse(
            "нули, выданные за ответ тома, означают «занято 0 байт» и врут в отчёте");
        total.Should().Be(0);
        free.Should().Be(0);
    }

    [Fact]
    public void Explain_neobyasnennoe_eto_zanyatoe_minus_vse_izvestnoe()
    {
        var scan = new ScanResult(
            [Nahodka(@"C:\Temp\a", 1000, FindingSource.Rule),
             Nahodka("volumecache:Thumbnail Cache", 500, FindingSource.VolumeCache)],
            []);

        var explanation = DiskExplainer.Explain(
            @"C:\", scan, [new ExplainedBucket("профили пользователей", 4000)]);

        explanation.ExplainedBytes.Should().Be(5500);
        explanation.UnexplainedBytes.Should().Be(explanation.UsedBytes - 5500);
    }

    [Fact]
    public void Explain_gruppiruet_nahodki_po_istochniku()
    {
        var scan = new ScanResult(
            [Nahodka(@"C:\Temp\a", 100, FindingSource.Rule),
             Nahodka(@"C:\Temp\b", 200, FindingSource.Rule),
             Nahodka("platformtool:dism/component-store", 700, FindingSource.PlatformTool)],
            []);

        var explanation = DiskExplainer.Explain(@"C:\", scan, []);

        explanation.Buckets.Single(b => b.Name == "Rule").Bytes.Should().Be(300);
        explanation.Buckets.Single(b => b.Name == "PlatformTool").Bytes.Should().Be(700);
    }

    [Fact]
    public void Report_pechataet_neobyasnennoe_otdelnoy_strokoy()
    {
        var explanation = DiskExplainer.Explain(@"C:\", ScanResult.Empty, []);

        explanation.Report().Should().Contain("не объяснено");
        explanation.Report().Should().Contain("занято");
    }

    [Fact]
    public void Explain_neobyasnennoe_nikogda_ne_otricatelno()
    {
        // Double counting is possible: a rule and a detector can both point at
        // the same folder. The remainder clamps at zero instead of printing a
        // negative number that would look like a bug in the disk.
        var huge = new ScanResult(
            [Nahodka(@"C:\Temp\a", long.MaxValue / 4, FindingSource.Rule)], []);

        DiskExplainer.Explain(@"C:\", huge, []).UnexplainedBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Explain_vlozhennaya_nahodka_drugogo_istochnika_ne_schitaetsya_dvazhdy()
    {
        // Дедупликация по вложенности внутри FileScanner работает только между
        // правилами. Здесь находки РАЗНЫХ источников встречаются впервые, а
        // обнаружитель предлагает ровно те подкаталоги (Cache, GPUCache),
        // которые уже забрали правила браузеров. Удвоенные байты в
        // предпросмотре это то, по чему человек принимает решение удалить.
        var scan = new ScanResult(
            [Nahodka(@"C:\Temp\app", 1000, FindingSource.Rule),
             Nahodka(@"C:\Temp\app\Cache", 400, FindingSource.Detector),
             Nahodka(@"C:\Temp\drugoe", 70, FindingSource.Detector)],
            []);

        var explanation = DiskExplainer.Explain(@"C:\", scan, []);

        explanation.ExplainedBytes.Should().Be(1070,
            "вложенный путь уже посчитан внутри более широкой находки");
        explanation.Buckets.Single(b => b.Name == "Detector").Bytes.Should().Be(70);
    }

    [Fact]
    public void Explain_nefaylovye_nahodki_ostayutsya_vse()
    {
        // Обработчик очистки и запись реестра это не место на диске, а личность.
        // Вложенности между ними нет, и любая попытка её увидеть съест находку.
        var scan = new ScanResult(
            [Nahodka("volumecache:Thumbnail Cache", 100, FindingSource.VolumeCache),
             Nahodka("volumecache:Thumbnail Cache Extra", 200, FindingSource.VolumeCache),
             Nahodka("registry:HKCU/Software/X", 0, FindingSource.Registry)],
            []);

        var explanation = DiskExplainer.Explain(@"C:\", scan, []);

        explanation.Buckets.Single(b => b.Name == "VolumeCache").Bytes.Should().Be(300);
    }

    [Fact]
    public void TopLevel_schitaet_kazhdyy_katalog_verhnego_urovnya()
    {
        var root = _sandbox.CreateDirectory("explain-top");
        Napolnit(Path.Combine(root, "pervyy"), 2, 100_000);
        Napolnit(Path.Combine(root, "vtoroy", "vnutri"), 1, 50_000);

        var buckets = DiskExplainer.TopLevel(root);

        buckets.Should().BeEquivalentTo(
        [
            new ExplainedBucket("pervyy", 200_000),
            new ExplainedBucket("vtoroy", 50_000),
        ]);
    }

    [Fact]
    public void TopLevel_ne_schitaet_chuzhoe_za_ssylkoy()
    {
        var root = _sandbox.CreateDirectory("explain-top-junction");
        var chuzhoe = _sandbox.CreateDirectory("explain-top-junction-target");
        Napolnit(chuzhoe, 4, 300_000);
        _sandbox.CreateJunction(Path.Combine("explain-top-junction", "ssylka"), chuzhoe);

        DiskExplainer.TopLevel(root).Should().BeEmpty(
            "за ссылкой лежат чужие байты, и посчитать их значит объяснить место дважды");
    }

    [Fact]
    public void Report_nazyvaet_krupneyshie_katalogi_verhnego_urovnya()
    {
        var explanation = DiskExplainer.Explain(
            @"C:\", ScanResult.Empty, [], [new ExplainedBucket("Windows", 25_000_000_000)]);

        explanation.Report().Should().Contain("Windows");
    }
}
