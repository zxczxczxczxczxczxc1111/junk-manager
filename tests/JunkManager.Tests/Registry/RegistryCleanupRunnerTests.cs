using FluentAssertions;
using JunkManager.Core.Registry;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryCleanupRunnerTests : IDisposable
{
    private readonly RegistrySandbox _vetka = new();
    private readonly string _katalogBekapov =
        Path.Combine(Path.GetTempPath(), "jm-bekapy-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _vetka.Dispose();

        if (Directory.Exists(_katalogBekapov))
        {
            Directory.Delete(_katalogBekapov, recursive: true);
        }
    }

    private RegistryFinding Nahodka(string imya, RegistryEntryKind vid, string? podvetka = null) =>
        new(
            Hive: RegistryHive.CurrentUser,
            SubKey: podvetka is null ? _vetka.SubKey : _vetka.SubKey + "\\" + podvetka,
            ValueName: vid == RegistryEntryKind.Value ? imya : string.Empty,
            View: RegistryView.Default,
            Kind: vid,
            RawValue: @"C:\net-takogo-fayla\ushlo.exe",
            MissingTarget: @"C:\net-takogo-fayla\ushlo.exe",
            Name: "Автозапуск",
            Consequence: "Windows пытается запустить это при каждом входе",
            RuleId: "test-run");

    private RegistryCleanupRunner Sobrat(SpisokZhurnala zhurnal) =>
        new(new RegistryExecutor(zhurnal, new RegistryBackup(_katalogBekapov)));

    [Fact]
    public async Task Changed_value_is_preserved_even_if_both_targets_are_missing()
    {
        // Missing files do not make two different registry values interchangeable.
        _vetka.SetString("changed", @"C:\net-takogo-fayla\ushlo.exe");
        var finding = Nahodka("changed", RegistryEntryKind.Value) with
        {
            Snapshot = RegistryEntrySnapshot.Capture(RegistryHive.CurrentUser, _vetka.SubKey,
                "changed", RegistryView.Default, false),
        };
        _vetka.SetString("changed", @"C:\net-takogo-fayla\new.exe");
        var result = await Sobrat(new SpisokZhurnala()).RunAsync([finding], null, TestContext.Current.CancellationToken);
        result.DeletedCount.Should().Be(0);
        result.Outcomes.Single().Reason.Should().Contain("изменилась");
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_vetka.SubKey);
        key!.GetValue("changed").Should().Be(@"C:\net-takogo-fayla\new.exe");
    }

    [Fact]
    public async Task Reappeared_target_preserves_unchanged_registry_value()
    {
        // Reinstallation can finish while the user reads the confirmation screen.
        using var files = new SandboxFixture();
        var target = Path.Combine(files.Root, "returned.exe");
        _vetka.SetString("returned", target);
        var finding = Nahodka("returned", RegistryEntryKind.Value) with
        {
            RawValue = target, MissingTarget = target,
            Snapshot = RegistryEntrySnapshot.Capture(RegistryHive.CurrentUser, _vetka.SubKey,
                "returned", RegistryView.Default, false),
        };
        await File.WriteAllTextAsync(target, "fixture", TestContext.Current.CancellationToken);
        var result = await Sobrat(new SpisokZhurnala()).RunAsync([finding], null, TestContext.Current.CancellationToken);
        result.DeletedCount.Should().Be(0);
        result.Outcomes.Single().Reason.Should().Contain("целевой файл");
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_vetka.SubKey);
        key!.GetValue("returned").Should().Be(target);
    }

    [Fact]
    public async Task Whole_key_with_new_neighbour_is_preserved()
    {
        // A new neighbour is not covered by yesterday's preview.
        _vetka.SetSubKeyDefault("entry.exe", @"C:\net-takogo-fayla\ushlo.exe");
        var finding = Nahodka(string.Empty, RegistryEntryKind.Key, "entry.exe");
        finding = finding with { Snapshot = RegistryEntrySnapshot.Capture(finding.Hive, finding.SubKey,
            finding.ValueName, finding.View, true) };
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(finding.SubKey, true))
            key!.SetValue("NewOwner", "keep");
        var result = await Sobrat(new SpisokZhurnala()).RunAsync([finding], null, TestContext.Current.CancellationToken);
        result.DeletedCount.Should().Be(0);
        result.Outcomes.Single().Reason.Should().Contain("изменилась");
    }

    [Fact]
    public async Task Znachenie_udalyaetsya_a_klyuch_ostaetsya_na_meste()
    {
        // Маршрут по Kind это одно ветвление, и ошибка в нём означает снесённый
        // подключ там, где обещали убрать одну строку.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var zhurnal = new SpisokZhurnala();
        var itog = await Sobrat(zhurnal).RunAsync(
            [Nahodka("mertvoe", RegistryEntryKind.Value)],
            progress: null,
            TestContext.Current.CancellationToken);

        itog.DeletedCount.Should().Be(1);
        itog.Cancelled.Should().BeFalse();

        using var baza = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var klyuch = baza.OpenSubKey(_vetka.SubKey, writable: false);

        klyuch.Should().NotBeNull("удаляли значение, а не ветку");
        klyuch!.GetValueNames().Should().NotContain("mertvoe");
    }

    [Fact]
    public async Task Podklyuch_udalyaetsya_celikom()
    {
        _vetka.SetSubKeyDefault("ushedshaya.exe", @"C:\net-takogo-fayla\ushlo.exe");

        var zhurnal = new SpisokZhurnala();
        var itog = await Sobrat(zhurnal).RunAsync(
            [Nahodka(string.Empty, RegistryEntryKind.Key, "ushedshaya.exe")],
            progress: null,
            TestContext.Current.CancellationToken);

        itog.DeletedCount.Should().Be(1);

        using var baza = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var klyuch = baza.OpenSubKey(_vetka.SubKey, writable: false);

        klyuch!.GetSubKeyNames().Should().NotContain("ushedshaya.exe");
    }

    [Fact]
    public async Task Nahodka_vne_razreshennoy_vetki_eto_propusk_a_ne_padenie()
    {
        // Находку сюда может передать кто угодно, включая будущий экран. Guard
        // спрашивается ЗДЕСЬ, последним рубежом, а не только при сканировании.
        var chuzhaya = Nahodka("chto-to", RegistryEntryKind.Value) with
        {
            Hive = RegistryHive.LocalMachine,
            SubKey = @"SYSTEM\CurrentControlSet\Services",
        };

        var zhurnal = new SpisokZhurnala();
        var itog = await Sobrat(zhurnal).RunAsync(
            [chuzhaya], progress: null, TestContext.Current.CancellationToken);

        itog.DeletedCount.Should().Be(0);
        itog.SkippedCount.Should().Be(1);
        itog.Outcomes[0].Reason.Should().Contain("навсегда");
    }

    [Fact]
    public async Task Odna_neudacha_ne_ostanavlivaet_ostalnye()
    {
        // Иначе первая же запись без прав уносит с собой всю очистку, и человек
        // видит «удалено 0» там, где девять из десяти уходили нормально.
        _vetka.SetString("pervoe", @"C:\net-takogo-fayla\ushlo.exe");
        _vetka.SetString("vtoroe", @"C:\net-takogo-fayla\tozhe.exe");

        var chuzhaya = Nahodka("chto-to", RegistryEntryKind.Value) with
        {
            Hive = RegistryHive.LocalMachine,
            SubKey = "SAM",
        };

        var zhurnal = new SpisokZhurnala();
        var itog = await Sobrat(zhurnal).RunAsync(
            [chuzhaya, Nahodka("pervoe", RegistryEntryKind.Value), Nahodka("vtoroe", RegistryEntryKind.Value) with
                { RawValue = @"C:\net-takogo-fayla\tozhe.exe", MissingTarget = @"C:\net-takogo-fayla\tozhe.exe" }],
            progress: null,
            TestContext.Current.CancellationToken);

        itog.DeletedCount.Should().Be(2);
        itog.SkippedCount.Should().Be(1);
        itog.Outcomes.Should().HaveCount(3);
    }

    [Fact]
    public async Task Otmena_vidna_v_otchete_i_ostavshiesya_ne_poluchayut_ishoda()
    {
        // Строка «отменено» на каждую недошедшую находку это тысячи записей в
        // журнале про то, чего не было. Тот же довод, что у CleanupRunner.
        _vetka.SetString("pervoe", @"C:\net-takogo-fayla\ushlo.exe");
        _vetka.SetString("vtoroe", @"C:\net-takogo-fayla\tozhe.exe");

        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var zhurnal = new SpisokZhurnala();
        var itog = await Sobrat(zhurnal).RunAsync(
            [Nahodka("pervoe", RegistryEntryKind.Value), Nahodka("vtoroe", RegistryEntryKind.Value)],
            progress: null,
            otmena.Token);

        itog.Cancelled.Should().BeTrue();
        itog.Outcomes.Should().BeEmpty("до первой находки дело не дошло");
    }

    [Fact]
    public async Task Hod_soobshchaet_adres_i_ishod_kazhdoy_zapisi()
    {
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var shagi = new List<RegistryCleanupProgress>();
        var zhurnal = new SpisokZhurnala();

        await Sobrat(zhurnal).RunAsync(
            [Nahodka("mertvoe", RegistryEntryKind.Value)],
            new SinhronnyyHod<RegistryCleanupProgress>(shagi.Add),
            TestContext.Current.CancellationToken);

        shagi.Should().NotBeEmpty();
        shagi[^1].Done.Should().Be(1);
        shagi[^1].Total.Should().Be(1);
        shagi[^1].Share.Should().Be(1);
        shagi[^1].Address.Should().Contain("mertvoe");
        shagi[^1].Last!.Status.Should().Be(DeleteStatus.Deleted);
    }

    [Fact]
    public async Task Adres_zvuchit_do_udaleniya_a_ne_tolko_posle()
    {
        // Дыра, найденная мутацией 06.09.2026. Проверка выше смотрит только на
        // ПОСЛЕДНИЙ отчёт, а он приходит одинаковый и когда отчёт до находки
        // отправляется, и когда его убрали совсем. Между тем именно из него
        // экран берёт адрес, пока запись ещё удаляется: без первого отчёта
        // строка хода пустая всё время работы и заполняется задним числом.
        _vetka.SetString("mertvoe", @"C:\net-takogo-fayla\ushlo.exe");

        var shagi = new List<RegistryCleanupProgress>();
        var zhurnal = new SpisokZhurnala();

        await Sobrat(zhurnal).RunAsync(
            [Nahodka("mertvoe", RegistryEntryKind.Value)],
            new SinhronnyyHod<RegistryCleanupProgress>(shagi.Add),
            TestContext.Current.CancellationToken);

        var doUdaleniya = shagi[0];

        doUdaleniya.Last.Should().BeNull("запись ещё не удалялась, исходу взяться неоткуда");
        doUdaleniya.Done.Should().Be(0);
        doUdaleniya.Address.Should().Contain("mertvoe");
    }
}
