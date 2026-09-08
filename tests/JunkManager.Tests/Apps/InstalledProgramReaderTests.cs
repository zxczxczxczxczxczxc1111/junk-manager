using FluentAssertions;
using JunkManager.Core.Apps;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class InstalledProgramReaderTests
{
    [Fact]
    public void Same_labels_do_not_merge_different_installations_and_icon_metadata_is_preserved()
    {
        var first = Zapis("A", ProgramScope.Machine32, uninstall: @"C:\Apps\Foo\uninst.exe", location: @"C:\Apps\Foo")
            with { DisplayIcon = "\"C:\\Apps\\Foo\\foo.exe\",0", EstimatedSizeBytes = 2048 };
        var second = Zapis("B", ProgramScope.Machine64, uninstall: @"C:\Apps\Foo64\uninst.exe", location: @"C:\Apps\Foo64");
        var programs = InstalledProgramReader.Svesti([first, second]);
        programs.Should().HaveCount(2);
        programs.Single(p => p.Id == "Machine32:A").ExecutablePath.Should().Be(@"C:\Apps\Foo\foo.exe");
        programs.Single(p => p.Id == "Machine32:A").EstimatedSizeBytes.Should().Be(2048);
    }

    private static RawUninstallEntry Zapis(
        string keyName,
        ProgramScope scope,
        string? displayName = "Некая программа",
        string? version = "1.0.0",
        string? publisher = "Некто",
        string? uninstall = @"C:\Program Files\Nekto\unins000.exe",
        string? quiet = null,
        int systemComponent = 0,
        string? parent = null,
        string? releaseType = null,
        string? windowsInstaller = null,
        string? innoAppPath = null,
        string? installDate = null,
        string? location = null) =>
        new(keyName, scope, displayName, version, publisher, location, uninstall, quiet,
            installDate, systemComponent, parent, releaseType, windowsInstaller, innoAppPath);

    [Fact]
    public void Svesti_shlopyvaet_dvoynika_iz_WOW6432Node()
    {
        // The same install is registered twice by a 32-bit installer on a 64-bit
        // machine. Two rows for one program make the list lie about how much is
        // installed, and the second row's uninstall string is usually the empty one.
        var raw = new[]
        {
            Zapis("{11111111-1111-1111-1111-111111111111}", ProgramScope.Machine64, uninstall: null),
            Zapis("{11111111-1111-1111-1111-111111111111}", ProgramScope.Machine32),
        };

        var svedeno = InstalledProgramReader.Svesti(raw);

        svedeno.Should().ContainSingle();
        svedeno[0].UninstallString.Should().NotBeNullOrWhiteSpace(
            "выживает запись, которой есть чем удалять");
    }

    [Fact]
    public void Svesti_shlopyvaet_po_troyke_imya_versiya_izdatel()
    {
        var raw = new[]
        {
            Zapis("Nekto_is1", ProgramScope.Machine64),
            Zapis("Nekto_is1_x86", ProgramScope.Machine32),
        };

        InstalledProgramReader.Svesti(raw).Should().ContainSingle();
    }

    [Fact]
    public void Svesti_ne_shlopyvaet_raznye_versii()
    {
        var raw = new[]
        {
            Zapis("Nekto_is1", ProgramScope.Machine64, version: "1.0.0"),
            Zapis("Nekto_is2", ProgramScope.Machine64, version: "2.0.0"),
        };

        InstalledProgramReader.Svesti(raw).Should().HaveCount(2,
            "две версии рядом это две установки, а не дубль");
    }

    [Fact]
    public void Svesti_nikogda_ne_shlopyvaet_HKCU_s_HKLM()
    {
        // Same name, same version, different scope. These are two installs
        // belonging to two different people, with two different uninstall
        // strings, and collapsing them hides one of them from its owner.
        var raw = new[]
        {
            Zapis("Nekto_is1", ProgramScope.Machine64),
            Zapis("Nekto_is1", ProgramScope.User),
        };

        var svedeno = InstalledProgramReader.Svesti(raw);

        svedeno.Should().HaveCount(2);
        svedeno.Select(p => p.Scope).Should().BeEquivalentTo(
            [ProgramScope.Machine64, ProgramScope.User]);
    }

    [Theory]
    [InlineData(null, 0, null, null)]
    [InlineData("   ", 0, null, null)]
    [InlineData("Некая программа", 1, null, null)]
    [InlineData("Некая программа", 0, "RoditelskiyKlyuch", null)]
    [InlineData("Некая программа", 0, null, "Security Update")]
    [InlineData("Некая программа", 0, null, "Update")]
    public void Skryta_povtoryaet_to_chto_pryachet_sama_Windows(
        string? displayName, int systemComponent, string? parent, string? releaseType)
    {
        var zapis = Zapis("K", ProgramScope.Machine64,
            displayName: displayName, systemComponent: systemComponent,
            parent: parent, releaseType: releaseType);

        InstalledProgramReader.Skryta(zapis).Should().BeTrue();
    }

    [Fact]
    public void Skryta_obychnuyu_programmu_ne_pryachet()
    {
        InstalledProgramReader.Skryta(Zapis("K", ProgramScope.Machine64)).Should().BeFalse();
    }

    [Fact]
    public void Svesti_skrytye_v_spisok_ne_popadayut()
    {
        var raw = new[]
        {
            Zapis("Vidimaya", ProgramScope.Machine64),
            Zapis("Skrytaya", ProgramScope.Machine64, displayName: "Другая", systemComponent: 1),
        };

        InstalledProgramReader.Svesti(raw).Select(p => p.DisplayName)
            .Should().BeEquivalentTo(["Некая программа"]);
    }

    [Theory]
    [InlineData(@"MsiExec.exe /I{11111111-1111-1111-1111-111111111111}", null, null, InstallerKind.Msi)]
    [InlineData(@"C:\Prog\unins000.exe", null, @"C:\Prog", InstallerKind.InnoSetup)]
    [InlineData(@"C:\Prog\Uninstall.exe", null, null, InstallerKind.Nsis)]
    [InlineData(@"C:\Users\x\AppData\Local\Discord\Update.exe --uninstall", null, null, InstallerKind.Squirrel)]
    [InlineData(@"C:\Prog\setup.exe /remove", null, null, InstallerKind.Unknown)]
    public void Opredelit_uznaet_vid_ustanovshchika(
        string uninstall, string? windowsInstaller, string? innoAppPath, InstallerKind ozhidaemyy)
    {
        var zapis = Zapis("K", ProgramScope.Machine64,
            uninstall: uninstall, windowsInstaller: windowsInstaller, innoAppPath: innoAppPath);

        InstalledProgramReader.Opredelit(zapis).Should().Be(ozhidaemyy);
    }

    [Fact]
    public void Opredelit_znachenie_WindowsInstaller_pereveshivaet_imya_fayla()
    {
        var zapis = Zapis("K", ProgramScope.Machine64,
            uninstall: @"C:\Prog\Uninstall.exe", windowsInstaller: "1");

        InstalledProgramReader.Opredelit(zapis).Should().Be(InstallerKind.Msi);
    }

    [Theory]
    [InlineData("20240115", 2024, 1, 15)]
    [InlineData("2024-01-15", 2024, 1, 15)]
    public void RazobratDatu_beret_oba_zhivyh_formata(string raw, int god, int mesyats, int den)
    {
        InstalledProgramReader.RazobratDatu(raw, new DateOnly(2026, 9, 5))
            .Should().Be(new DateOnly(god, mesyats, den));
    }

    [Theory]
    [InlineData("20253330")]   // тридцать третий месяц, живая запись Discord
    [InlineData("20241332")]
    [InlineData("19700101")]   // раньше, чем существовали эти программы
    [InlineData("20991231")]   // в будущем
    [InlineData("2024")]
    [InlineData("")]
    [InlineData(null)]
    public void RazobratDatu_musor_daet_null_a_ne_vydumannuyu_datu(string? raw)
    {
        InstalledProgramReader.RazobratDatu(raw, new DateOnly(2026, 9, 5)).Should().BeNull();
    }
}

[Trait("Class", "LiveRead")]
public sealed class InstalledProgramReaderZhivyeTests
{
    [Fact]
    public void ReadRaw_chitaet_vse_tri_vetki()
    {
        var raw = InstalledProgramReader.ReadRaw();

        var hklm = raw.Where(z => z.Scope == ProgramScope.Machine64).Select(z => z.KeyName).ToArray();
        var wow = raw.Where(z => z.Scope == ProgramScope.Machine32).Select(z => z.KeyName).ToArray();
        var hkcu = raw.Count(z => z.Scope == ProgramScope.User);

        // Печатается ДО проверок: упавшая проверка обязана оставить числа, иначе
        // разбор начинается с повторного запуска руками.
        Console.WriteLine($"сырых записей HKLM: {hklm.Length}, WOW6432Node: {wow.Length}, HKCU: {hkcu}");

        raw.Should().NotBeEmpty("хотя бы одна программа на машине стоит всегда");
        hklm.Should().NotBeEmpty("ветка удаления HKLM не бывает пустой ни на одной Windows");
        wow.Should().NotBeEmpty("ветку WOW6432Node заводит сама система");

        // Ворота против «обе проекции читаются из одного места». Раньше здесь
        // стоял порог «больше двадцати», снятый с рабочего ПК 05.09.2026, и он
        // кодировал населённость ТОЙ машины: чистый гость честно отдаёт 14 и 16,
        // и проверка падала на правде. Населённость машине не предписывают,
        // проверяют устройство.
        hklm.Should().NotBeEquivalentTo(
            wow, "иначе 64-битная и 32-битная ветки удаления читаются из одного места");
    }

    [Fact]
    public void Read_otdaet_spisok_bez_dubley_i_bez_bezymyannyh()
    {
        var programmy = InstalledProgramReader.Read();

        programmy.Should().NotBeEmpty();
        programmy.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.DisplayName));
        programmy.Select(p => p.Id).Should().OnlyHaveUniqueItems();

        // Схлопывание, съевшее больше четверти видимых строк, это не дедупликация,
        // а потеря программ. Порог из шага 10 плана, проверяется на живой машине,
        // потому что искусственный набор такого перекоса не покажет.
        var vidimyh = InstalledProgramReader.ReadRaw().Count(z => !InstalledProgramReader.Skryta(z));

        Console.WriteLine($"строк после отсева скрытых: {vidimyh}, после сведения: {programmy.Count}");

        programmy.Count.Should().BeGreaterThan(vidimyh * 3 / 4,
            "схлопывание, съевшее больше четверти списка, слишком широкое");
    }

    [Fact]
    public void MsixPackageReader_chitaet_reestr_AppModel_bez_WinRT()
    {
        var pakety = MsixPackageReader.Read();

        pakety.Should().NotBeEmpty("на живой Windows 11 пакетов MSIX больше сотни");
        pakety.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.FullName));

        Console.WriteLine($"пакетов MSIX: {pakety.Count}");
    }
}
