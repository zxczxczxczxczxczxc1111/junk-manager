using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Apps;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class LeftoverFinderTests
{
    private static InstalledProgram Programma(string imya, string izdatel) =>
        new("Machine64:K", imya, izdatel, "1.0.0", null,
            @"C:\Prog\unins000.exe", null, InstallerKind.InnoSetup,
            ProgramScope.Machine64, null);

    private static UninstallResult Itog(UninstallOutcome ishod) =>
        new("Machine64:K", ishod, ExitCode: 0, BytesFreed: 0, Reason: null);

    [Theory]
    [InlineData(UninstallOutcome.Refused)]
    [InlineData(UninstallOutcome.Failed)]
    [InlineData(UninstallOutcome.TimedOut)]
    [InlineData(UninstallOutcome.Cancelled)]
    public void FindAfterRemoval_bez_uspeshnogo_udaleniya_nichego_ne_ishchet(UninstallOutcome ishod)
    {
        // While the program is still installed, its directories are its working
        // data, not traces. Offering them is how a working program gets broken.
        var poisk = LeftoverFinder.FindAfterRemoval(
            Programma("Некая программа", "Некто"), Itog(ishod));

        poisk.Found.Should().BeEmpty();
        poisk.Skipped.Should().ContainSingle(
            s => s.Reason.Contains("удаление не завершилось", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Desktop")]
    [InlineData("App")]
    [InlineData("Data")]
    [InlineData("Update")]
    [InlineData("Cache")]
    [InlineData("Launcher")]
    [InlineData("Inc")]
    [InlineData("Ltd")]
    public void Tokeny_stop_slova_ne_uchastvuyut(string stopSlovo)
    {
        // Telegram Desktop matched %LOCALAPPDATA%\com.vault.desktop and the
        // system %ProgramData%\Desktop, both on the word Desktop. Two false
        // positives from one token, and one of them was somebody's password
        // vault.
        LeftoverFinder.Tokeny("Некто", $"Программа {stopSlovo}")
            .Should().NotContain(t => t.Equals(stopSlovo, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void Tokeny_korotkie_tokeny_ne_uchastvuyut(string korotkiy)
    {
        // Издатель здесь тоже короткий, и это не косметика: «Некто» ровно пять
        // символов, то есть сам проходит порог, и проверка с ним говорила бы
        // про издателя, а не про короткий токен.
        LeftoverFinder.Tokeny("Кто", korotkiy).Should().BeEmpty();
    }

    [Fact]
    public void Tokeny_rovno_pyat_simvolov_ostayutsya()
    {
        // Граница правила спеки: короче пяти отбрасывается, ровно пять остаётся.
        // Без этой проверки сдвиг порога на единицу молча выкосит половину имён.
        LeftoverFinder.Tokeny("Некто", "abcde").Should().BeEquivalentTo(["Некто", "abcde"]);
    }

    [Fact]
    public void Tokeny_beret_i_izdatelya_i_produkt()
    {
        var tokeny = LeftoverFinder.Tokeny("Telegram FZ-LLC", "Telegram Desktop");

        tokeny.Should().Contain("Telegram");
        tokeny.Should().NotContain("LLC");
        tokeny.Should().NotContain("Desktop");
    }

    [Fact]
    public void SovpadaetImya_sravnivaet_celymi_tokenami_a_ne_podstrokoy()
    {
        var tokeny = LeftoverFinder.Tokeny("Некто", "Telegram");

        LeftoverFinder.SovpadaetImya("Telegram", tokeny).Should().BeTrue();
        LeftoverFinder.SovpadaetImya("Telegram Desktop", tokeny).Should().BeTrue();

        // Substring matching would take this one too, and it belongs to a
        // different program entirely.
        LeftoverFinder.SovpadaetImya("TelegramBotFarm", tokeny).Should().BeFalse();
    }

    [Theory]
    [InlineData("Cache", true)]
    [InlineData("cache", true)]
    [InlineData("Code Cache", true)]
    [InlineData("GPUCache", true)]
    [InlineData("ShaderCache", true)]
    [InlineData("logs", true)]
    [InlineData("Crashpad", true)]
    [InlineData("Crash Reports", false)]
    [InlineData("User Data", false)]
    [InlineData("Local Storage", false)]
    [InlineData("Chrome", false)]
    public void YavlyaetsyaMetkoy_uznaet_musornyy_podkatalog(string imya, bool ozhidaemo)
    {
        // "Crash Reports" is deliberately not a marker: that name belongs to the
        // pattern scan, and claiming it here would report the right path under
        // the wrong source and fail the seeded acceptance run.
        LeftoverFinder.YavlyaetsyaMetkoy(imya).Should().Be(ozhidaemo);
    }

    [Fact]
    public void FindOrphans_nahodit_katalog_bez_metki_esli_on_dostatochno_star_i_krupen()
    {
        // Щель между двумя механизмами, найденная приёмкой 05.09.2026.
        // Точечные каталоги профиля берёт AbandonedFolderDetector, бесточечные
        // с меткой внутри берёт эта же функция первым плечом, а бесточечный
        // каталог БЕЗ метки не брал никто. На живой машине таких семь штук на
        // 62 МБ, и среди них Opera Software, не менявшаяся 403 дня.
        var lokalnyy = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sled = Path.Combine(lokalnyy, "jm-test-bez-metki-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(sled);

            var fayl = Path.Combine(sled, "telo.bin");
            File.WriteAllBytes(fayl, new byte[4 * 1024 * 1024]);
            File.SetLastWriteTimeUtc(fayl, DateTime.UtcNow.AddDays(-500));

            var poisk = LeftoverFinder.FindOrphans([], [], idleDays: 90);

            poisk.Found.Select(s => s.Path).Should().NotContain(sled);
            poisk.Skipped.Single(s => s.Path == sled).Reason.Should().Contain("не доказывают");
        }
        finally
        {
            if (Directory.Exists(sled))
            {
                Directory.Delete(sled, recursive: true);
            }
        }
    }

    [Fact]
    public void FindOrphans_svezhiy_katalog_bez_metki_ne_predlagaetsya()
    {
        var lokalnyy = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var svezhiy = Path.Combine(lokalnyy, "jm-test-svezhiy-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(svezhiy);
            File.WriteAllBytes(Path.Combine(svezhiy, "telo.bin"), new byte[4 * 1024 * 1024]);

            LeftoverFinder.FindOrphans([], [], idleDays: 90)
                .Found.Select(s => s.Path).Should().NotContain(svezhiy,
                    "каталог, в который писали вчера, это рабочий каталог, а не след");
        }
        finally
        {
            if (Directory.Exists(svezhiy))
            {
                Directory.Delete(svezhiy, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("VirtualStore")]
    [InlineData("Package Cache")]
    [InlineData("Packages")]
    [InlineData("Microsoft")]
    public void FindOrphans_ne_predlagaet_katalogi_samoy_Windows(string imya)
    {
        // Замер на живой машине 05.09.2026 выдал в кандидаты VirtualStore и
        // Package Cache. Первый хранит то, что программа записала в Program
        // Files и что Windows перенаправила в профиль: это данные человека.
        // Второй хранит установочные пакеты, которыми Windows чинит и удаляет
        // уже установленные программы. Ни один не след.
        //
        // Проверяется, что поиск СМОТРИТ в список, а не что список непуст.
        // Первая версия этого теста звала DetectorExclusions напрямую, и
        // мутация, снявшая проверку с поиска, выжила: список был на месте, а
        // спрашивать его перестали.
        using var pesochnica = new SandboxFixture();
        var chuzhoy = Path.Combine(pesochnica.Root, imya);
        Directory.CreateDirectory(chuzhoy);

        var fayl = Path.Combine(chuzhoy, "telo.bin");
        File.WriteAllBytes(fayl, new byte[8 * 1024 * 1024]);
        File.SetLastWriteTimeUtc(fayl, DateTime.UtcNow.AddDays(-500));

        LeftoverFinder.FindOrphans([], [], idleDays: 90, koren: pesochnica.Root)
            .Found.Should().BeEmpty();
    }

    [Fact]
    public void FindOrphans_v_pesochnice_beret_katalog_bez_metki()
    {
        // Тот же случай, но имя не из списка исключений: ворота против
        // обратного дефекта, когда исключается вообще всё и тест выше проходит
        // по причине «поиск не работает».
        using var pesochnica = new SandboxFixture();
        var sled = Path.Combine(pesochnica.Root, "brosennaya-programma");
        Directory.CreateDirectory(sled);

        var fayl = Path.Combine(sled, "telo.bin");
        File.WriteAllBytes(fayl, new byte[8 * 1024 * 1024]);
        File.SetLastWriteTimeUtc(fayl, DateTime.UtcNow.AddDays(-500));

        LeftoverFinder.FindOrphans([], [], idleDays: 90, koren: pesochnica.Root)
            .Skipped.Should().Contain(s => s.Path == sled && s.Reason.Contains("не доказывают", StringComparison.Ordinal));
    }

    [Fact]
    public void FindOrphans_nahodit_metku_bez_zapisi_deinstallyacii_i_ne_beret_sam_katalog()
    {
        // Приманка program-leftover в polygon/posev.json переведена в
        // required: true, а флаг ставится только там, где источник построен и
        // ПРОВЕРЕН. Проверка здесь, а не только в госте: каталог кладётся в
        // настоящий %LOCALAPPDATA% ровно той же формы, что и посев, и убирается
        // в finally при любом исходе.
        var lokalnyy = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var postavshchik = Path.Combine(lokalnyy, "jm-test-sled-" + Guid.NewGuid().ToString("N"));
        var metka = Path.Combine(postavshchik, "cache");

        try
        {
            Directory.CreateDirectory(metka);

            var fayl = Path.Combine(metka, "kusok.bin");
            File.WriteAllBytes(fayl, new byte[1_048_576]);
            File.SetLastWriteTimeUtc(fayl, DateTime.UtcNow.AddDays(-200));

            // Рядом с меткой лежат данные человека: они не предлагаются никогда,
            // и ровно на этом стоят две ловушки описи посева.
            var dannye = Path.Combine(postavshchik, "User Data");
            Directory.CreateDirectory(dannye);
            File.WriteAllText(Path.Combine(dannye, "Login Data"), "чужое");

            var poisk = LeftoverFinder.FindOrphans([], [], idleDays: 90);

            poisk.Found.Select(s => s.Path).Should().Contain(metka);
            poisk.Found.Select(s => s.Path).Should().NotContain(postavshchik,
                "предлагается подкаталог-метка, а не каталог поставщика целиком");
            poisk.Found.Select(s => s.Path).Should().NotContain(dannye);

            var sled = poisk.Found.Single(s => s.Path == metka);
            sled.SizeBytes.Should().BeGreaterThanOrEqualTo(1_048_576);
            sled.Basis.Should().Contain("нет записи деинсталляции");

            // А теперь та же метка, но запись деинсталляции с таким именем есть.
            // Это уже не след, это рабочий каталог установленной программы.
            var imya = Path.GetFileName(postavshchik);
            var ustanovlennaya = new InstalledProgram(
                "Machine64:K", imya, "Некто", "1.0.0", null, null, null,
                InstallerKind.Unknown, ProgramScope.Machine64, null);

            LeftoverFinder.FindOrphans([ustanovlennaya], [], idleDays: 90)
                .Found.Select(s => s.Path).Should().NotContain(metka);
        }
        finally
        {
            if (Directory.Exists(postavshchik))
            {
                Directory.Delete(postavshchik, recursive: true);
            }
        }
    }

    [Fact]
    public void ToFinding_sled_eto_vsegda_Risk_i_vsegda_s_osnovaniem()
    {
        var sled = new Leftover(
            LeftoverKind.Directory,
            @"C:\Users\x\AppData\Local\jm-seed-udalyonnaya-programma\cache",
            1_048_576,
            "нет записи деинсталляции с таким именем");

        var nahodka = LeftoverFinder.ToFinding(sled);

        nahodka.Source.Should().Be(FindingSource.Program);
        nahodka.Tier.Should().Be(RiskTier.Risk, "след это догадка, а не факт");
        nahodka.Consequence.Should().Contain("нет записи деинсталляции");
        nahodka.SizeBytes.Should().Be(1_048_576);
    }
}
