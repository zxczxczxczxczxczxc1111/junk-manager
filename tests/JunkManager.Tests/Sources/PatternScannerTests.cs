using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Sources.Pattern;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class PatternScannerTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public PatternScannerTests(SandboxFixture sandbox) => _sandbox = sandbox;

    private static PatternScanOptions Options(string root, int depth = 4, int minAge = 0) =>
        new([root], ["*.tmp", "*.bak", "*.old", "*.dmp", "Thumbs.db"], depth, minAge);

    private static void Napolnit(string katalog, int faylov, int bayt, int vozrastDney)
    {
        Directory.CreateDirectory(katalog);

        for (var i = 0; i < faylov; i++)
        {
            var fayl = Path.Combine(katalog, $"kusok-{i}.bin");
            File.WriteAllBytes(fayl, new byte[bayt]);
            using (var stream = File.OpenWrite(fayl)) stream.Write("MDMP"u8);
            File.SetLastWriteTimeUtc(fayl, DateTime.UtcNow.AddDays(-vozrastDney));
        }

        Directory.SetLastWriteTimeUtc(katalog, DateTime.UtcNow.AddDays(-vozrastDney));
    }

    [Fact]
    public async Task ScanAsync_nahodit_tolko_diagnostiku_v_kataloge_avariy()
    {
        // Отчёты об авариях лежат в каталоге с известным именем, а внутри у них
        // .bin, .dmp и что угодно ещё: имена файлов задаёт разработчик
        // приложения, имя каталога задаёт фреймворк. До 05.09.2026 поиск по
        // образцу умел только маски ФАЙЛОВ, поэтому приманка crash-dumps-pattern
        // не находилась ничем.
        var root = _sandbox.CreateDirectory("pattern-crash");
        Napolnit(Path.Combine(root, "prilozhenie", "Crash Reports"), 3, 400_000, 120);
        var keep = Path.Combine(root, "prilozhenie", "Crash Reports", "important.txt");
        File.WriteAllText(keep, "User notes, not a crash dump with a marketing department.");

        var result = await new PatternScanner()
            .ScanAsync(Options(root, minAge: 30), null, CancellationToken.None);

        var nahodka = result.Findings.Should().ContainSingle(
            f => Path.GetFileName(f.Path) == "Crash Reports").Subject;

        nahodka.Tier.Should().Be(RiskTier.Risk, "каталог по имени это догадка");
        nahodka.SizeBytes.Should().BeGreaterThanOrEqualTo(1_200_000);
        nahodka.Scope.Should().Be(DeleteScope.SelectedEntries);
        nahodka.DeletionTargets.Should().HaveCount(3).And.NotContain(keep);
    }

    [Theory]
    [InlineData("crash reports")]
    [InlineData("CRASHPAD")]
    [InlineData("Crashes")]
    public async Task ScanAsync_imya_kataloga_sravnivaetsya_bez_uchyota_registra(string imya)
    {
        // Регистр в именах каталогов на Windows не значит ничего: файловая
        // система его хранит, но не различает, и один и тот же сборщик отчётов
        // пишет то "Crashpad", то "crashpad" в зависимости от версии. Сравнение
        // с учётом регистра делает список масок списком опечаток.
        ArgumentNullException.ThrowIfNull(imya);

        // Свой корень на каждый случай: песочница одна на класс, и три случая
        // в одном корне видели бы находки друг друга.
        var root = _sandbox.CreateDirectory(
            "pattern-crash-registr-" + imya.Replace(' ', '-'));
        Napolnit(Path.Combine(root, "prilozhenie", imya), 3, 400_000, 120);

        var result = await new PatternScanner()
            .ScanAsync(Options(root, minAge: 30), null, CancellationToken.None);

        result.Findings.Should().ContainSingle(f => Path.GetFileName(f.Path) == imya);
    }

    [Fact]
    public async Task ScanAsync_svezhiy_katalog_avariy_ne_beretsya()
    {
        var root = _sandbox.CreateDirectory("pattern-crash-svezhiy");
        Napolnit(Path.Combine(root, "prilozhenie", "Crashpad"), 3, 400_000, 0);

        var result = await new PatternScanner()
            .ScanAsync(Options(root, minAge: 30), null, CancellationToken.None);

        result.Findings.Should().NotContain(f => Path.GetFileName(f.Path) == "Crashpad",
            "каталог, в который писали сегодня, это работающий сборщик отчётов");
    }

    [Fact]
    public async Task ScanAsync_ne_schitaet_fayly_vnutry_nazvannogo_kataloga_vtoroy_raz()
    {
        // Иначе каталог и лежащий в нём .dmp попадают в список оба, и человек
        // видит вдвое больше освобождаемого места, чем есть.
        var root = _sandbox.CreateDirectory("pattern-crash-dvoynoy");
        var avarii = Path.Combine(root, "prilozhenie", "CrashDumps");
        Napolnit(avarii, 1, 400_000, 120);

        var damp = Path.Combine(avarii, "upal.dmp");
        File.WriteAllBytes(damp, new byte[400_000]);
        File.SetLastWriteTimeUtc(damp, DateTime.UtcNow.AddDays(-120));

        // Возраст каталогу ставится ПОСЛЕ всех файлов: запись файла обновляет
        // отметку каталога, и состаренный до этого каталог снова становится
        // свежим. Первый заход теста как раз на это и наступил.
        Directory.SetLastWriteTimeUtc(avarii, DateTime.UtcNow.AddDays(-120));

        var result = await new PatternScanner()
            .ScanAsync(Options(root, minAge: 30), null, CancellationToken.None);

        result.Findings.Select(f => f.Path).Should().NotContain(damp);
        result.Findings.Should().ContainSingle(f => Path.GetFileName(f.Path) == "CrashDumps");
    }

    [Fact]
    public async Task ScanAsync_nahodit_po_maske_i_ne_beret_lishnego()
    {
        var root = _sandbox.CreateDirectory("pattern-basic");
        File.WriteAllText(Path.Combine(root, "a.tmp"), new string('x', 100));
        File.WriteAllText(Path.Combine(root, "b.txt"), "не мусор");
        File.WriteAllText(Path.Combine(root, "Thumbs.db"), "мусор");

        var result = await new PatternScanner().ScanAsync(Options(root), null, CancellationToken.None);

        result.Findings.Select(f => Path.GetFileName(f.Path))
            .Should().BeEquivalentTo(["a.tmp", "Thumbs.db"]);
    }

    [Fact]
    public async Task ScanAsync_ne_vyhodit_za_zadannyy_koren()
    {
        var inside = _sandbox.CreateDirectory("pattern-inside");
        var outside = _sandbox.CreateDirectory("pattern-outside");
        File.WriteAllText(Path.Combine(outside, "chuzhoy.tmp"), "не наш");
        File.WriteAllText(Path.Combine(inside, "svoy.tmp"), "наш");

        var result = await new PatternScanner().ScanAsync(Options(inside), null, CancellationToken.None);

        result.Findings.Should().ContainSingle();
        result.Findings[0].Path.Should().Contain("svoy.tmp");
    }

    [Fact]
    public async Task ScanAsync_soblyudaet_ogranichenie_glubiny()
    {
        var root = _sandbox.CreateDirectory("pattern-depth");
        var deep = Path.Combine(root, "a", "b", "c", "d");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(root, "a", "blizko.tmp"), "виден");
        File.WriteAllText(Path.Combine(deep, "daleko.tmp"), "не виден");

        var result = await new PatternScanner().ScanAsync(Options(root, depth: 2), null, CancellationToken.None);

        result.Findings.Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(["blizko.tmp"]);
    }

    [Fact]
    public async Task ScanAsync_ne_zahodit_vnutr_reparse_tochki()
    {
        // The classic way a pattern scan quietly leaves the root it was given.
        var root = _sandbox.CreateDirectory("pattern-junction");
        var outside = _sandbox.CreateDirectory("pattern-junction-target");
        File.WriteAllText(Path.Combine(outside, "za-ssylkoy.tmp"), "не наш");
        _sandbox.CreateJunction(Path.Combine("pattern-junction", "link"), outside);

        var result = await new PatternScanner().ScanAsync(Options(root), null, CancellationToken.None);

        result.Findings.Should().BeEmpty("обход внутрь reparse-точки запрещён");
    }

    [Fact]
    public async Task ScanAsync_koren_ne_proshedshiy_guard_daet_Skipped()
    {
        var options = new PatternScanOptions([@"C:\Windows\System32"], ["*.tmp"], 2, 0);

        var result = await new PatternScanner().ScanAsync(options, null, CancellationToken.None);

        result.Findings.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ScanAsync_fayl_v_zapreshchyonnom_poddereve_ne_stanovitsya_nahodkoy()
    {
        // Каталог обходится СВЕРХУ ВНИЗ и guard про каталоги не спрашивают: его
        // спрашивают про каждый файл. Разница видна ровно в одном месте на
        // настоящей машине: «Пуск» лежит внутри разрешённого %APPDATA%, но сам
        // запрещён навсегда. Песочница такую вложенность воспроизвести не может,
        // потому что списки guard строятся из окружения, а не из параметров.
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var parent = Path.GetDirectoryName(startMenu)!;

        SafetyGuard.TryVerify(parent, out _, out _)
            .Should().BeTrue("корень обхода обязан проходить guard, иначе тест проверяет не то");
        SafetyGuard.TryVerify(startMenu, out _, out var otkaz)
            .Should().BeFalse("«Пуск» запрещён guard, и это предпосылка теста");
        otkaz.Should().NotBeNullOrWhiteSpace();
        Directory.EnumerateFiles(startMenu, "*.lnk", SearchOption.AllDirectories)
            .Should().NotBeEmpty("без ярлыков внутри «Пуска» проверять нечего");

        var options = new PatternScanOptions([parent], ["*.lnk"], 5, 0);

        var result = await new PatternScanner().ScanAsync(options, null, CancellationToken.None);

        result.Findings.Should().NotContain(
            f => f.Path != null && f.Path.StartsWith(startMenu, StringComparison.OrdinalIgnoreCase),
            "guard отклонил эти файлы, значит предлагать их нельзя");
        result.Findings.Should().NotContain(
            f => string.IsNullOrWhiteSpace(f.Path), "находка без пути это находка без цели");
        result.Skipped.Should().Contain(
            s => s.Path.StartsWith(startMenu, StringComparison.OrdinalIgnoreCase),
            "отказ обязан быть назван, а не проглочен");
    }

    [Fact]
    public async Task ScanAsync_svezhee_otsekaetsya_po_MinAgeDays()
    {
        var root = _sandbox.CreateDirectory("pattern-age");
        var fresh = Path.Combine(root, "svezhiy.tmp");
        var old = Path.Combine(root, "staryy.tmp");
        File.WriteAllText(fresh, "сегодня");
        File.WriteAllText(old, "давно");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

        var result = await new PatternScanner()
            .ScanAsync(Options(root, minAge: 30), null, CancellationToken.None);

        result.Findings.Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(["staryy.tmp"]);
    }

    [Fact]
    public async Task ScanAsync_istochnik_vsegda_PatternScan_i_consequence_ne_pustoy()
    {
        var root = _sandbox.CreateDirectory("pattern-source");
        File.WriteAllText(Path.Combine(root, "a.tmp"), "x");

        var result = await new PatternScanner().ScanAsync(Options(root), null, CancellationToken.None);

        result.Findings.Should().OnlyContain(f => f.Source == FindingSource.PatternScan);
        result.Findings.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Consequence));
        result.Findings.Should().OnlyContain(f => f.Tier == RiskTier.Risk,
            "поиск по образцу менее точен, чем правило, и это видно человеку по ступени");
    }
}
