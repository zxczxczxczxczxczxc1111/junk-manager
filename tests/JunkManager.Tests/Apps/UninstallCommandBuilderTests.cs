using FluentAssertions;
using JunkManager.Core.Apps;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class UninstallCommandBuilderTests
{
    [Theory]
    [InlineData("\"C:\\Windows\\System32\\cmd.exe\" /c echo unsafe")]
    [InlineData("\"C:\\Prog\\uninst.exe\" \"unfinished")]
    public void Script_hosts_and_unclosed_arguments_are_refused(string command)
    {
        UninstallCommandBuilder.TryRazobrat(command, Est, out _, out _, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Arguments_keep_empty_values_escaped_quotes_and_shell_metacharacters_literal()
    {
        // Quoting is a grammar, not a motivational toggle for a boolean.
        UninstallCommandBuilder.TryRazobrat(
            "\"C:\\Prog\\uninst.exe\" \"\" \"a\\\"b\" \"C:\\tail\\\\\" & calc.exe",
            Est, out _, out var args, out var reason).Should().BeTrue(reason);
        args.Should().Equal("", "a\"b", "C:\\tail\\", "&", "calc.exe");
    }

    private static InstalledProgram Programma(
        InstallerKind vid,
        string? uninstall,
        string? quiet = null,
        string imya = "Некая программа") =>
        new("Machine64:K", imya, "Некто", "1.0.0", null, uninstall, quiet,
            vid, ProgramScope.Machine64, null);

    private static bool Est(string put) =>
        put.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !put.Contains("net-takogo", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TryBuild_MSI_sobiraet_udalenie_a_ne_vosstanovlenie()
    {
        // 102 of 149 msiexec strings on this machine carry /I. Running them as
        // written opens the repair wizard, and the person watches an installer
        // start when they asked for a removal.
        var p = Programma(InstallerKind.Msi,
            @"MsiExec.exe /I{11111111-2222-3333-4444-555555555555}");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        // Про сам путь до msiexec отвечает соседний тест: тут проверяется, что
        // собрано удаление, а не восстановление.
        komanda.Executable.Should().EndWith("msiexec.exe");
        komanda.Arguments.Should().BeEquivalentTo(
            ["/X{11111111-2222-3333-4444-555555555555}", "/qn", "/norestart"],
            options => options.WithStrictOrdering());
        komanda.Quiet.Should().BeTrue();
    }

    [Fact]
    public void TryBuild_MSI_zovet_msiexec_po_polnomu_puti()
    {
        // Голое имя запускается по правилам CreateProcess, а там каталог
        // приложения идёт РАНЬШЕ System32. Файл msiexec.exe, положенный рядом с
        // JunkManager.exe, получил бы права администратора и полную свободу:
        // эту команду продукт запускает именно повышенным.
        var p = Programma(InstallerKind.Msi,
            @"MsiExec.exe /I{11111111-2222-3333-4444-555555555555}");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        Path.IsPathFullyQualified(komanda.Executable).Should().BeTrue();
        komanda.Executable.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"));
    }

    [Theory]
    [InlineData(@"uninst.exe /S")]
    [InlineData(@"""setup.exe"" --uninstall")]
    [InlineData(@"..\Prog\uninst.exe")]
    public void TryBuild_otnositelnyy_put_deinstallyatora_otvergaetsya(string stroka)
    {
        // File.Exists у относительного пути отвечает про текущий каталог, а
        // CreateProcess потом ищет по своему списку. Это два разных файла, и
        // выбирает второй тот, кто положил свой exe в нужное место.
        var p = Programma(InstallerKind.Nsis, stroka);

        UninstallCommandBuilder.TryBuild(p, Est, out _, out var prichina).Should().BeFalse();
        prichina.Should().Contain("полный путь");
    }

    [Fact]
    public void TryBuild_MSI_bez_koda_produkta_ne_zapuskaetsya()
    {
        var p = Programma(InstallerKind.Msi, @"MsiExec.exe /I");

        UninstallCommandBuilder.TryBuild(p, Est, out _, out var prichina).Should().BeFalse();
        prichina.Should().Contain("код продукта");
    }

    [Fact]
    public void TryBuild_NSIS_dobavlyaet_zaglavnyy_S()
    {
        var p = Programma(InstallerKind.Nsis, @"C:\Prog\Uninstall.exe");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        komanda.Executable.Should().Be(@"C:\Prog\Uninstall.exe");
        komanda.Arguments.Should().BeEquivalentTo(["/S"]);
        komanda.Quiet.Should().BeTrue();
    }

    [Fact]
    public void TryBuild_InnoSetup_beret_oba_klyucha()
    {
        var p = Programma(InstallerKind.InnoSetup, @"C:\Prog\unins000.exe");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        komanda.Arguments.Should().BeEquivalentTo(
            ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void TryBuild_Squirrel_beret_svoi_klyuchi()
    {
        var p = Programma(InstallerKind.Squirrel, @"C:\Users\x\AppData\Local\App\Update.exe --uninstall");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        komanda.Arguments.Should().BeEquivalentTo(["--uninstall", "-s"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void TryBuild_QuietUninstallString_pobezhdaet_sobrannuyu_stroku()
    {
        var p = Programma(InstallerKind.Nsis,
            @"C:\Prog\Uninstall.exe",
            quiet: @"C:\Prog\Uninstall.exe /S /allusers");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        komanda.Arguments.Should().BeEquivalentTo(["/S", "/allusers"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void TryBuild_neizvestnyy_vid_otdaet_negromkuyu_komandu()
    {
        // Not a refusal: the string parsed fine. But nothing here is quiet, and
        // the runner is the one that decides whether an unattended window is
        // acceptable.
        var p = Programma(InstallerKind.Unknown, @"C:\Prog\setup.exe /remove");

        UninstallCommandBuilder.TryBuild(p, Est, out var komanda, out var prichina)
            .Should().BeTrue(prichina);

        komanda.Quiet.Should().BeFalse();
        komanda.Arguments.Should().BeEquivalentTo(["/remove"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryBuild_bez_stroki_udaleniya_otkazyvaet(string? stroka)
    {
        var p = Programma(InstallerKind.Nsis, stroka);

        UninstallCommandBuilder.TryBuild(p, Est, out _, out var prichina).Should().BeFalse();
        prichina.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryRazobrat_put_v_kavychkah()
    {
        UninstallCommandBuilder.TryRazobrat(
            "\"C:\\Program Files\\Некто\\unins000.exe\" /VERYSILENT",
            Est, out var exe, out var args, out var prichina)
            .Should().BeTrue(prichina);

        exe.Should().Be(@"C:\Program Files\Некто\unins000.exe");
        args.Should().BeEquivalentTo(["/VERYSILENT"]);
    }

    [Fact]
    public void TryRazobrat_put_s_probelami_bez_kavychek()
    {
        // Six strings on this machine look exactly like this. Splitting on the
        // first space produces "C:\Program" and a removal that never runs.
        UninstallCommandBuilder.TryRazobrat(
            @"C:\Program Files\Некто\uninst.exe /S",
            Est, out var exe, out var args, out var prichina)
            .Should().BeTrue(prichina);

        exe.Should().Be(@"C:\Program Files\Некто\uninst.exe");
        args.Should().BeEquivalentTo(["/S"]);
    }

    [Fact]
    public void TryRazobrat_bez_argumentov()
    {
        UninstallCommandBuilder.TryRazobrat(
            @"C:\Prog\uninst.exe", Est, out var exe, out var args, out var prichina)
            .Should().BeTrue(prichina);

        exe.Should().Be(@"C:\Prog\uninst.exe");
        args.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\net-takogo\uninst.exe /S")]
    [InlineData(@"eto voobshche ne komanda")]
    [InlineData(@"""C:\Prog\unins.exe /S")]
    public void TryRazobrat_nerazobrannaya_stroka_ne_zapuskaetsya(string stroka)
    {
        // The whole point: a string we could not take apart is a string we do
        // not run. Running the raw text through a shell would work often enough
        // to be trusted, and wrong often enough to start the wrong program.
        UninstallCommandBuilder.TryRazobrat(stroka, Est, out var exe, out _, out var prichina)
            .Should().BeFalse();

        exe.Should().BeEmpty();
        prichina.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(@"MsiExec.exe /X{11111111-2222-3333-4444-555555555555}", "{11111111-2222-3333-4444-555555555555}")]
    [InlineData(@"MsiExec.exe /I{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")]
    public void TryKodProdukta_vytaskivaet_guid(string stroka, string ozhidaemyy)
    {
        UninstallCommandBuilder.TryKodProdukta(stroka, out var kod).Should().BeTrue();
        kod.Should().Be(ozhidaemyy);
    }

    [Theory]
    [InlineData(@"MsiExec.exe /X{ne-guid}")]
    [InlineData(@"MsiExec.exe")]
    public void TryKodProdukta_musor_ne_prohodit(string stroka)
    {
        UninstallCommandBuilder.TryKodProdukta(stroka, out var kod).Should().BeFalse();
        kod.Should().BeEmpty();
    }
}
