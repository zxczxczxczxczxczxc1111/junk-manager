using Xunit;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Scanning;

[Trait("Class", "Sandbox")]
public sealed class FileScannerTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public FileScannerTests(SandboxFixture sandbox) => _sandbox = sandbox;

    private static RuleDefinition Pravilo(
        string id, string path, int olderThanDays = 0, string? fileFilter = null,
        RiskTier tier = RiskTier.Safe) =>
        new(id, "Правило " + id, [path], tier.ToString(), "Ничего важного не пропадёт.",
            olderThanDays, fileFilter)
        {
            Category = "Тест",
            Tier = tier,
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

    [Fact]
    public async Task Vozrast_iz_budushchego_ne_stanovitsya_otricatelnym()
    {
        // Найдено снимком живого окна 05.09.2026: в строке «Временные файлы
        // пользователя» стояло «-1 день назад». Файл с меткой времени чуть
        // впереди системных часов это обычное дело: расхождение часов,
        // распаковка архива с будущей меткой, запись во время самого прохода.
        // Math.Floor от -0.00003 даёт -1, и на экране появляется возраст,
        // которого не бывает.
        var dir = _sandbox.CreateDirectory("budushchee");
        Fayl(dir, "vperedi.bin", 1000, ageDays: -1);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("r", dir)], null, CancellationToken.None);

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].LastUsedDays.Should().Be(0,
            "метка из будущего это «сегодня», а не отрицательное число дней");
    }

    [Fact]
    public async Task ScanAsync_schitaet_razmer_obhodom_a_ne_ocenkoy()
    {
        var dir = _sandbox.CreateDirectory("razmer");
        Fayl(dir, "a.bin", 1000);
        Fayl(dir, "b.bin", 2000);
        Fayl(Path.Combine(dir, "vlozhennyy"), "c.bin", 3000);

        var itog = await new FileScanner().ScanAsync([Pravilo("r", dir)], null, CancellationToken.None);

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].SizeBytes.Should().Be(6000, "обход считает и вложенные каталоги");
        itog.TotalBytes.Should().Be(6000);
    }

    [Fact]
    public async Task ScanAsync_olderThanDays_otsekaet_svezhee()
    {
        var dir = _sandbox.CreateDirectory("vozrast");
        Fayl(dir, "staryy.bin", 5000, ageDays: 40);
        Fayl(dir, "svezhiy.bin", 7000, ageDays: 1);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("r", dir, olderThanDays: 30)], null, CancellationToken.None);

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].SizeBytes.Should().Be(5000, "свежий файл в находку не входит");
    }

    [Fact]
    public async Task ScanAsync_olderThanDays_bez_podhodyashchih_faylov_ne_daet_nahodki()
    {
        var dir = _sandbox.CreateDirectory("vsyo-svezhee");
        Fayl(dir, "svezhiy.bin", 7000, ageDays: 1);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("r", dir, olderThanDays: 30)], null, CancellationToken.None);

        itog.Findings.Should().BeEmpty("находка на ноль байт это шум в списке");
    }

    [Fact]
    public async Task ScanAsync_fileFilter_beret_tolko_svoi_fayly()
    {
        var dir = _sandbox.CreateDirectory("filtr");
        Fayl(dir, "a.log", 1000);
        Fayl(dir, "b.log", 2000);
        Fayl(dir, "c.txt", 9000);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("r", dir, fileFilter: "*.log")], null, CancellationToken.None);

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].SizeBytes.Should().Be(3000);
    }

    [Fact]
    public async Task ScanAsync_put_ne_proshedshiy_guard_ne_popadaet_v_nahodki()
    {
        // System32 существует, раскрывается и имеет размер. В находки он не
        // попадает не потому, что его не нашли, а потому, что guard сказал нет.
        var zapret = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("opasnoe", zapret)], null, CancellationToken.None);

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("запрещённом корне",
                "отказ guard обязан быть виден, а не проглочен молча");
    }

    [Fact]
    public async Task ScanAsync_nedostupnyy_katalog_daet_SkippedItem_a_ne_isklyuchenie()
    {
        // Несуществующая переменная окружения это ровно тот же класс: правило
        // нераскрываемо, и это факт для отчёта, а не повод уронить весь скан.
        var rule = Pravilo("bez-peremennoy", @"%JUNKMANAGER_NO_SUCH_VAR%\cache");

        var act = async () => await new FileScanner().ScanAsync([rule], null, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var itog = await new FileScanner().ScanAsync([rule], null, CancellationToken.None);
        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("JUNKMANAGER_NO_SUCH_VAR");
    }

    [Fact]
    public async Task ScanAsync_odno_slomannoe_pravilo_ne_meshaet_ostalnym()
    {
        var dir = _sandbox.CreateDirectory("zhivoe");
        Fayl(dir, "a.bin", 1234);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("slomannoe", @"%JUNKMANAGER_NO_SUCH_VAR%\cache"), Pravilo("zhivoe", dir)],
            null, CancellationToken.None);

        itog.Findings.Should().ContainSingle().Which.SizeBytes.Should().Be(1234);
        itog.Skipped.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAsync_otmena_ostanavlivaet_i_vozvrashchaet_chastichnyy_rezultat()
    {
        var dir = _sandbox.CreateDirectory("otmena");
        Fayl(dir, "a.bin", 100);

        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var itog = await new FileScanner().ScanAsync([Pravilo("r", dir)], null, otmena.Token);

        itog.Cancelled.Should().BeTrue(
            "частичный результат обязан отличаться от полного, иначе его прочитают как полный");
        itog.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_pustoy_katalog_daet_nol_nahodok_bez_oshibok()
    {
        var dir = _sandbox.CreateDirectory("pustoy");

        var itog = await new FileScanner().ScanAsync([Pravilo("r", dir)], null, CancellationToken.None);

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().BeEmpty();
        itog.Cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_ne_zahodit_v_junction_pri_podschete_razmera()
    {
        // Обход внутрь ссылки посчитал бы чужие байты и предложил их удалить.
        var dir = _sandbox.CreateDirectory("s-ssylkoy");
        Fayl(dir, "svoy.bin", 500);

        var chuzhoy = _sandbox.CreateDirectory("chuzhoy-tolstyy");
        Fayl(chuzhoy, "tolstyy.bin", 100000);
        _sandbox.CreateJunction(Path.Combine("s-ssylkoy", "vnutr"), chuzhoy);

        var itog = await new FileScanner().ScanAsync([Pravilo("r", dir)], null, CancellationToken.None);

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].SizeBytes.Should().Be(500, "байты за ссылкой чужие");
    }

    [Fact]
    public async Task ScanAsync_nahodka_neset_posledstvie_i_stupen_iz_pravila()
    {
        var dir = _sandbox.CreateDirectory("polya");
        Fayl(dir, "a.bin", 10);

        var itog = await new FileScanner().ScanAsync(
            [Pravilo("r", dir, tier: RiskTier.Risk)], null, CancellationToken.None);

        var n = itog.Findings.Should().ContainSingle().Subject;
        n.Tier.Should().Be(RiskTier.Risk);
        n.Consequence.Should().NotBeNullOrWhiteSpace();
        n.Source.Should().Be(FindingSource.Rule);
        n.RuleId.Should().Be("r");
        n.LastUsedDays.Should().NotBeNull();
    }

    [Fact]
    public async Task ScanAsync_nichego_ne_menyaet_na_diske()
    {
        // Сканирование это чтение. Это доказывается снимком, а не заявляется.
        var dir = _sandbox.CreateDirectory("nedvizhimoe");
        Fayl(dir, "a.bin", 100, ageDays: 3);
        Fayl(Path.Combine(dir, "sub"), "b.bin", 200, ageDays: 9);

        var do_ = Snimok(dir);

        await new FileScanner().ScanAsync([Pravilo("r", dir)], null, CancellationToken.None);

        Snimok(dir).Should().BeEquivalentTo(do_);
    }

    private static List<string> Snimok(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p):O}"
                : $"{p}|dir")
            .ToList();
}
