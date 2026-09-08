using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.Core.Registry;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Служба очистки реестра без реестра: проверяется порядок и учёт точки
/// восстановления, а не удаление. Удаление проверено RegistryCleanupRunnerTests.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class RegistryCleanupServiceTests : IDisposable
{
    private readonly RegistrySandbox _vetka = new();
    private readonly string _katalogBekapov =
        Path.Combine(Path.GetTempPath(), "jm-bekapy-" + Guid.NewGuid().ToString("N"));
    private readonly string _katalogZhurnala =
        Path.Combine(Path.GetTempPath(), "jm-zhurnal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _vetka.Dispose();

        foreach (var katalog in new[] { _katalogBekapov, _katalogZhurnala })
        {
            if (Directory.Exists(katalog))
            {
                Directory.Delete(katalog, recursive: true);
            }
        }
    }

    private sealed class Nastroyki(bool tochka, bool backup = false) : ISettingsService
    {
        public string FilePath => "нет файла";

        public AppSettings Current { get; private set; } =
            new() { RestorePointBeforeRegistry = tochka, BackupRegistryBeforeCleanup = backup };

        public Task<AppSettings> LoadAsync(CancellationToken ct) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken ct)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private RegistryFinding Nahodka(string imya) => new(
        Hive: RegistryHive.CurrentUser,
        SubKey: _vetka.SubKey,
        ValueName: imya,
        View: RegistryView.Default,
        Kind: RegistryEntryKind.Value,
        RawValue: @"C:\net-takogo-fayla\ushlo.exe",
        MissingTarget: @"C:\net-takogo-fayla\ushlo.exe",
        Name: "Автозапуск",
        Consequence: "Windows пытается запустить это при каждом входе",
        RuleId: "test-run");

    private RegistryCleanupService Sobrat(
        bool tochkaVklyuchena, Func<string, RestorePointResult> tochka, bool backup = false) =>
        new(new Nastroyki(tochkaVklyuchena, backup), tochka, _katalogBekapov, _katalogZhurnala);

    [Fact]
    public async Task Backup_off_deletes_without_creating_backup_directory()
    {
        // Optional means optional; the disk is not a shrine for unwanted exports.
        _vetka.SetString("optional", @"C:\net-takogo-fayla\ushlo.exe");
        var service = Sobrat(false, _ => throw new InvalidOperationException("restore point is disabled"));

        var result = await service.RunAsync(
            [Nahodka("optional")], null, TestContext.Current.CancellationToken);

        result.Report.DeletedCount.Should().Be(1);
        Directory.Exists(_katalogBekapov).Should().BeFalse();
    }

    [Fact]
    public async Task Tochka_sozdaetsya_odin_raz_za_seans_a_ne_pered_kazhdym_progonom()
    {
        // Раздел 9.2 спеки: «перед ПЕРВОЙ очисткой реестра в сессии». Точка на
        // каждое нажатие это десяток точек за вечер и вытесненные из списка
        // чужие, то есть страховка, съевшая сама себя.
        _vetka.SetString("pervoe", @"C:\net-takogo-fayla\ushlo.exe");
        _vetka.SetString("vtoroe", @"C:\net-takogo-fayla\tozhe.exe");

        var vyzovov = 0;
        var sluzhba = Sobrat(true, _ =>
        {
            vyzovov++;
            return new RestorePointResult(true, 100 + vyzovov, null);
        });

        await sluzhba.RunAsync([Nahodka("pervoe")], null, TestContext.Current.CancellationToken);
        await sluzhba.RunAsync([Nahodka("vtoroe")], null, TestContext.Current.CancellationToken);

        vyzovov.Should().Be(1);
    }

    [Fact]
    public async Task Neudachnaya_tochka_ne_otmenyaet_ochistku_no_popadaet_v_otchet()
    {
        // Решение 2 в шапке плана. Отменяет удаление только неудачный бэкап:
        // спека называет .reg единственным путём отката, а защита системы на
        // свежей Windows 11 выключена по умолчанию.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var sluzhba = Sobrat(true, _ => new RestorePointResult(
            false, 0,
            "точка восстановления не создана: защита системы выключена на этом томе (код 1058)"));

        var itog = await sluzhba.RunAsync(
            [Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        itog.Report.DeletedCount.Should().Be(1, "бэкап удался, а точка это не бэкап");
        itog.RestorePointNote.Should().Contain("защита системы");
    }

    [Fact]
    public async Task Tochka_bez_nomera_ne_pishetsya_v_otchet_nulem()
    {
        // Windows 11 сборки 26200 номер точки не возвращает вовсе, замер лежит
        // в SystemRestorePointTests. Подпись «номер 0» человек прочитает как
        // сбой, хотя точка есть. Отчёт обязан сказать, где её искать.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var sluzhba = Sobrat(true, _ => new RestorePointResult(true, 0, null));

        var itog = await sluzhba.RunAsync(
            [Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        itog.RestorePointNote.Should().NotContain("номер 0");
        itog.RestorePointNote.Should().Contain("создана");
        itog.Report.DeletedCount.Should().Be(1);
    }

    [Fact]
    public async Task Vyklyuchennaya_nastroyka_ne_zovet_tochku_i_govorit_ob_etom()
    {
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var vyzovov = 0;
        var sluzhba = Sobrat(false, _ =>
        {
            vyzovov++;
            return new RestorePointResult(true, 1, null);
        });

        var itog = await sluzhba.RunAsync(
            [Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        vyzovov.Should().Be(0);
        itog.RestorePointNote.Should().Contain("выключена в настройках");
        itog.Report.DeletedCount.Should().Be(1);
    }

    [Fact]
    public async Task Otchet_nazyvaet_katalog_bekapov()
    {
        // Единственный путь отката обязан быть назван адресом, а не словом
        // «бэкап сделан». Человек, которому нужен откат, ищет файл.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var sluzhba = Sobrat(false, _ => new RestorePointResult(true, 1, null), backup: true);

        var itog = await sluzhba.RunAsync(
            [Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        itog.BackupDirectory.Should().Be(_katalogBekapov);
        itog.BackupEnabled.Should().BeTrue();
        Directory.GetFiles(_katalogBekapov, "*.reg").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pustoy_spisok_ne_delaet_tochku_vovse()
    {
        // Точка восстановления перед ничем это страховка от ничего и
        // вытесненная из списка чужая точка.
        var vyzovov = 0;
        var sluzhba = Sobrat(true, _ =>
        {
            vyzovov++;
            return new RestorePointResult(true, 1, null);
        });

        var itog = await sluzhba.RunAsync([], null, TestContext.Current.CancellationToken);

        vyzovov.Should().Be(0);
        itog.Report.Outcomes.Should().BeEmpty();
    }

    [Fact]
    public async Task Zhurnal_prohoda_zapisan_v_tot_zhe_katalog_chto_i_faylovyy()
    {
        // Экран «Журнал» читает ОДИН каталог. Очистка реестра, пишущая в свой,
        // была бы прогоном, которого в журнале нет вовсе.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var sluzhba = Sobrat(false, _ => new RestorePointResult(true, 1, null));

        await sluzhba.RunAsync([Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        Directory.GetFiles(_katalogZhurnala, "*.jsonl").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Zametka_pro_tochku_odna_i_ta_zhe_do_i_posle_ochistki()
    {
        // Экран зовёт страховку ПЕРЕД подтверждением и показывает её словами, а
        // потом показывает отчёт. Две разные формулировки об одном и том же
        // событии человек читает как два события.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var sluzhba = Sobrat(true, _ => new RestorePointResult(true, 42, null));

        var doOchistki = await sluzhba.ZastrahovatAsync(TestContext.Current.CancellationToken);
        var itog = await sluzhba.RunAsync(
            [Nahodka("mertvoe")], null, TestContext.Current.CancellationToken);

        doOchistki.Should().Contain("42");
        itog.RestorePointNote.Should().Be(doOchistki);
    }
}
