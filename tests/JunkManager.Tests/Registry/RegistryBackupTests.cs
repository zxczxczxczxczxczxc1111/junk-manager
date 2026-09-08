using System.Text;
using FluentAssertions;
using JunkManager.Core.Sources.Platform;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryBackupTests : IDisposable
{
    private readonly RegistrySandbox _vetka = new();
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose()
    {
        _vetka.Dispose();
        _pesochnica.Dispose();
    }

    /// <summary>Бэкап, который всегда отказывает. Ровно для одного теста.</summary>
    private sealed class SlomannyyBekap : IRegistryBackup
    {
        public Task<BackupResult> ExportAsync(
            RegistryHive hive, string subKey, RegistryView view, CancellationToken ct) =>
            Task.FromResult(new BackupResult(false, string.Empty, "reg.exe вернул 1: доступ запрещён"));
    }

    private VerifiedRegistryValue Propusk(string imya)
    {
        RegistryGuard.TryVerifyValue(
            RegistryHive.CurrentUser, _vetka.SubKey, imya, RegistryView.Default,
            out var propusk, out var otkaz)
            .Should().BeTrue(otkaz);

        return propusk;
    }

    private static bool EstZnachenie(string subKey, string imya)
    {
        using var baza = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var klyuch = baza.OpenSubKey(subKey, writable: false);

        return klyuch is not null
            && klyuch.GetValueNames().Contains(imya, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Proverit_otsutstvuyushchiy_fayl_eto_otkaz()
    {
        RegistryBackup.Proverit(Path.Combine(_pesochnica.Root, "net-takogo.reg"), out var prichina)
            .Should().BeFalse();

        prichina.Should().Contain("нет");
    }

    [Fact]
    public void Proverit_pustoy_fayl_eto_otkaz()
    {
        var put = Path.Combine(_pesochnica.Root, "pustoy.reg");
        File.WriteAllBytes(put, []);

        RegistryBackup.Proverit(put, out var prichina).Should().BeFalse();
        prichina.Should().Contain("пустой");
    }

    [Fact]
    public void Proverit_fayl_bez_metki_UTF16_eto_otkaz()
    {
        // Заголовок на месте, но файл в UTF-8. Значит его писали не reg.exe, а
        // импортировать обратно неизвестно что нельзя.
        var put = Path.Combine(_pesochnica.Root, "ne-ta-kodirovka.reg");
        File.WriteAllText(put, "Windows Registry Editor Version 5.00\r\n", new UTF8Encoding(false));

        RegistryBackup.Proverit(put, out var prichina).Should().BeFalse();
        prichina.Should().Contain("FF FE");
    }

    [Fact]
    public void Proverit_fayl_s_chuzhim_zagolovkom_eto_otkaz()
    {
        var put = Path.Combine(_pesochnica.Root, "chuzhoy-zagolovok.reg");
        File.WriteAllText(put, "REGEDIT4\r\n[HKEY_CURRENT_USER\\Software]\r\n", Encoding.Unicode);

        // REGEDIT4 это формат Windows 95. reg.exe его не пишет, и импорт такого
        // файла молча теряет всё, что не влезло в ANSI.
        RegistryBackup.Proverit(put, out var prichina).Should().BeFalse();
        prichina.Should().Contain("Windows Registry Editor Version 5.00");
    }

    [Fact]
    public async Task ExportAsync_nastoyashchaya_vetka_daet_proveryaemyy_fayl()
    {
        _vetka.SetString("chto-to", @"C:\jm\app.exe");

        var itog = await new RegistryBackup(_pesochnica.Root).ExportAsync(
            RegistryHive.CurrentUser, _vetka.SubKey, RegistryView.Default,
            TestContext.Current.CancellationToken);

        itog.Ok.Should().BeTrue(itog.Reason);
        File.Exists(itog.FilePath).Should().BeTrue();
        RegistryBackup.Proverit(itog.FilePath, out _).Should().BeTrue();

        // Проверено на машине DERMO 05.09.2026: reg export пишет UTF-16LE с
        // BOM FF FE, первая строка Windows Registry Editor Version 5.00.
        var nachalo = File.ReadAllBytes(itog.FilePath);
        nachalo[0].Should().Be(0xFF);
        nachalo[1].Should().Be(0xFE);
    }

    [Fact]
    public async Task ExportAsync_nesushchestvuyushchaya_vetka_daet_otkaz_a_ne_isklyuchenie()
    {
        // Проверено там же: код выхода 1 и текст в stderr.
        var itog = await new RegistryBackup(_pesochnica.Root).ExportAsync(
            RegistryHive.CurrentUser,
            RegistrySandbox.Koren + @"\net-takoy-vetki-vovse",
            RegistryView.Default,
            TestContext.Current.CancellationToken);

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Neudachnyy_bekap_otmenyaet_udalenie_celikom()
    {
        // Главный тест всей задачи. Требование раздела 14 спеки дословно: "при
        // неудачном экспорте удаления не происходит".
        _vetka.SetString("dolzhna_ostatsya", @"C:\jm-net-takogo\app.exe");

        var zhurnal = new SpisokZhurnala();
        var ispolnitel = new RegistryExecutor(zhurnal, new SlomannyyBekap());

        var itog = await ispolnitel.DeleteValueAsync(
            Propusk("dolzhna_ostatsya"), TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("бэкап");
        EstZnachenie(_vetka.SubKey, "dolzhna_ostatsya").Should().BeTrue(
            "без проверенного бэкапа удаления не происходит вовсе");
        zhurnal.Zapisi.Should().ContainSingle("отказ пишется в журнал так же, как удаление");
    }

    [Fact]
    public async Task DeleteValueAsync_udalyaet_i_ostavlyaet_bekap()
    {
        _vetka.SetString("na-udalenie", @"C:\jm-net-takogo\app.exe");
        _vetka.SetString("sosednyaya", @"C:\jm-net-takogo\drugoe.exe");

        var zhurnal = new SpisokZhurnala();
        var bekap = new RegistryBackup(_pesochnica.Root);
        var ispolnitel = new RegistryExecutor(zhurnal, bekap);

        var itog = await ispolnitel.DeleteValueAsync(
            Propusk("na-udalenie"), TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted);
        itog.BytesFreed.Should().Be(0, "освобождённые байты для реестра не показываются никогда");
        EstZnachenie(_vetka.SubKey, "na-udalenie").Should().BeFalse();
        EstZnachenie(_vetka.SubKey, "sosednyaya").Should().BeTrue("соседнее значение не трогается");

        Directory.EnumerateFiles(_pesochnica.Root, "*.reg").Should().NotBeEmpty(
            "бэкап обязан остаться на диске, иначе откатывать нечем");
    }

    [Fact]
    public async Task DeleteKeyAsync_udalyaet_podklyuch_i_ne_trogaet_koren()
    {
        _vetka.SetSubKeyDefault("jm-net-takoy.exe", @"C:\jm-net-takogo\jm-net-takoy.exe");
        _vetka.SetSubKeyDefault("jm-sosed.exe", @"C:\jm-net-takogo\jm-sosed.exe");

        RegistryGuard.TryVerifyKey(
            RegistryHive.CurrentUser, _vetka.SubKey + @"\jm-net-takoy.exe", RegistryView.Default,
            out var propusk, out var otkaz).Should().BeTrue(otkaz);

        var ispolnitel = new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root));

        var itog = await ispolnitel.DeleteKeyAsync(propusk, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted);

        using var baza = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var koren = baza.OpenSubKey(_vetka.SubKey, writable: false);

        koren.Should().NotBeNull("корень ветки остаётся на месте");
        koren!.GetSubKeyNames().Should().BeEquivalentTo(["jm-sosed.exe"]);
    }

    [Fact]
    public async Task Pustoy_propusk_nichego_ne_udalyaet()
    {
        // VerifiedRegistryValue это структура, значит default снаружи получить
        // можно всегда. Пустой пропуск не пропуск.
        var itog = await new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root))
            .DeleteValueAsync(default, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("пустой пропуск");
    }

    /// <summary>
    /// Подделка reg.exe: код возврата задаётся, а что окажется на диске решает
    /// сам тест. Настоящий reg.exe так себя не ведёт, и в этом весь смысл: без
    /// подделки утверждение «код 0 сам по себе ничего не доказывает» проверить
    /// нечем, а непроверяемое утверждение в бэкапе однажды окажется ложью.
    /// </summary>
    private static RegExeRunner Podelka(int kod, Action<string>? chtoNaDiske = null) =>
        (argumenty, _, _) =>
        {
            // Третий аргумент reg export это путь к файлу. Подделка обязана
            // писать ровно туда, куда написал бы настоящий reg.exe, иначе она
            // проверяет не тот путь, который потом читает Proverit.
            chtoNaDiske?.Invoke(argumenty[2]);
            return Task.FromResult(new ToolRun(kod, string.Empty, string.Empty));
        };

    [Fact]
    public async Task Kod_vozvrata_0_bez_fayla_eto_ne_bekap()
    {
        _vetka.SetString("chto-to", @"C:\jm\app.exe");

        // Файла нет вовсе, а reg.exe отчитался успехом.
        var itog = await new RegistryBackup(_pesochnica.Root, runner: Podelka(0)).ExportAsync(
            RegistryHive.CurrentUser, _vetka.SubKey, RegistryView.Default,
            TestContext.Current.CancellationToken);

        itog.Ok.Should().BeFalse("код возврата 0 сам по себе не бэкап");
        itog.Reason.Should().NotBeNullOrWhiteSpace();
        itog.FilePath.Should().NotBeEmpty("имя файла нужно, чтобы человек знал, чего искать и не найти");
    }

    [Fact]
    public async Task Kod_vozvrata_0_s_chuzhim_soderzhimym_eto_ne_bekap()
    {
        _vetka.SetString("chto-to", @"C:\jm\app.exe");

        // Файл есть, заголовок на месте, но кодировка чужая: так выглядит
        // подмена, которую код возврата не видит.
        var itog = await new RegistryBackup(
                _pesochnica.Root,
                runner: Podelka(0, put => File.WriteAllText(
                    put, "Windows Registry Editor Version 5.00" + Environment.NewLine, new UTF8Encoding(false))))
            .ExportAsync(
                RegistryHive.CurrentUser, _vetka.SubKey, RegistryView.Default,
                TestContext.Current.CancellationToken);

        itog.Ok.Should().BeFalse("проверяется содержимое файла, а не код возврата");
        itog.Reason.Should().Contain("FF FE");
    }

    [Fact]
    public async Task Neproverennyy_bekap_pri_kode_0_otmenyaet_udalenie()
    {
        // Тот же случай, но целиком: от подделанного reg.exe до значения,
        // которое обязано остаться на месте.
        _vetka.SetString("dolzhna_ostatsya", @"C:\jm-net-takogo\app.exe");

        var zhurnal = new SpisokZhurnala();
        var ispolnitel = new RegistryExecutor(
            zhurnal, new RegistryBackup(_pesochnica.Root, runner: Podelka(0)));

        var itog = await ispolnitel.DeleteValueAsync(
            Propusk("dolzhna_ostatsya"), TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        EstZnachenie(_vetka.SubKey, "dolzhna_ostatsya").Should().BeTrue(
            "нулевой код возврата не оправдывает удаление");
        zhurnal.Zapisi.Should().ContainSingle();
    }

    [Fact]
    public async Task Propusk_klyucha_tozhe_popadaet_v_zhurnal()
    {
        // Журнал пишется на любом исходе, а не только на удачном. Строка про
        // пропуск это единственный след того, что кнопку вообще нажимали.
        var zhurnal = new SpisokZhurnala();

        var itog = await new RegistryExecutor(zhurnal, new RegistryBackup(_pesochnica.Root))
            .DeleteKeyAsync(default, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        zhurnal.Zapisi.Should().ContainSingle("пропуск ключа обязан оставить строку в журнале");
    }

    [Fact]
    public async Task Ischeznuvshee_znachenie_eto_propusk_a_ne_padenie()
    {
        var propusk = Propusk("nikogda-ne-sushchestvovala");

        var itog = await new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root))
            .DeleteValueAsync(propusk, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("исчезло");
    }
}
