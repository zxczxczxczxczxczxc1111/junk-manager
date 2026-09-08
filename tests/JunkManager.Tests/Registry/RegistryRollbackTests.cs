using FluentAssertions;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryRollbackTests : IDisposable
{
    private static readonly string[] MultiSample = ["one", "два"];
    private readonly RegistrySandbox _vetka = new();
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose()
    {
        _vetka.Dispose();
        _pesochnica.Dispose();
    }

    [Fact]
    public async Task Restore_one_value_does_not_overwrite_a_changed_neighbour()
    {
        // A branch export is evidence, not permission to resurrect every neighbour.
        _vetka.SetString("selected", @"C:\jm-missing\selected.exe");
        _vetka.SetString("neighbour", "before");
        RegistryGuard.TryVerifyValue(RegistryHive.CurrentUser, _vetka.SubKey, "selected",
            RegistryView.Default, out var pass, out _).Should().BeTrue();
        await new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root))
            .DeleteValueAsync(pass, TestContext.Current.CancellationToken);
        _vetka.SetString("neighbour", "after");

        var file = Directory.GetFiles(_pesochnica.Root, "*.reg").Single();
        var result = await RegistryRollback.ImportAsync(file, RegistryView.Default, TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue(result.Reason);
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_vetka.SubKey);
        key!.GetValue("selected").Should().Be(@"C:\jm-missing\selected.exe");
        key.GetValue("neighbour").Should().Be("after");
    }

    [Fact]
    public async Task Restore_is_idempotent_and_refuses_changed_data()
    {
        // A second click is not a time machine with permission to overwrite edits.
        _vetka.SetString("selected", "original");
        RegistryGuard.TryVerifyValue(RegistryHive.CurrentUser, _vetka.SubKey, "selected",
            RegistryView.Default, out var pass, out _).Should().BeTrue();
        var deleted = await new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root))
            .DeleteValueAsync(pass, TestContext.Current.CancellationToken);
        deleted.Status.Should().Be(DeleteStatus.Deleted);
        var file = Directory.GetFiles(_pesochnica.Root, "*.reg").Single();
        (await RegistryRollback.ImportAsync(file, RegistryView.Default, TestContext.Current.CancellationToken))
            .Status.Should().Be(RegistryRestoreStatus.Restored);
        (await RegistryRollback.ImportAsync(file, RegistryView.Default, TestContext.Current.CancellationToken))
            .Status.Should().Be(RegistryRestoreStatus.AlreadyPresent);
        _vetka.SetString("selected", "changed");
        (await RegistryRollback.ImportAsync(file, RegistryView.Default, TestContext.Current.CancellationToken))
            .Status.Should().Be(RegistryRestoreStatus.Conflict);
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_vetka.SubKey);
        key!.GetValue("selected").Should().Be("changed");
    }

    [Fact]
    public async Task Restore_subtree_preserves_typed_values_and_empty_keys()
    {
        // Empty keys and expandable strings are data, however unimpressive they look.
        var path = _vetka.SubKey + @"\typed";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path))
        {
            key.SetValue("binary", new byte[] { 0, 255, 7 }, RegistryValueKind.Binary);
            key.SetValue("dword", -17, RegistryValueKind.DWord);
            key.SetValue("qword", long.MaxValue, RegistryValueKind.QWord);
            key.SetValue("multi", MultiSample, RegistryValueKind.MultiString);
            key.SetValue("expand", @"%TEMP%\literal", RegistryValueKind.ExpandString);
            using var empty = key.CreateSubKey("empty");
        }
        var before = RegistryEntrySnapshot.Capture(RegistryHive.CurrentUser, path, string.Empty, RegistryView.Default, true)!;
        RegistryGuard.TryVerifyKey(RegistryHive.CurrentUser, path, RegistryView.Default, out var pass, out _).Should().BeTrue();
        var deleted = await new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(_pesochnica.Root))
            .DeleteKeyAsync(pass, TestContext.Current.CancellationToken);
        deleted.Status.Should().Be(DeleteStatus.Deleted);
        var file = Directory.GetFiles(_pesochnica.Root, "*.reg").Single();
        var restored = await RegistryRollback.ImportAsync(file, RegistryView.Default, TestContext.Current.CancellationToken);
        restored.Ok.Should().BeTrue(restored.Reason);
        before.Matches(before.ReadCurrent()).Should().BeTrue();
    }

    [Fact]
    public async Task Polnyy_krug_eksport_udalenie_import_znachenie_vernulos()
    {
        const string Imya = "vernetsya";
        const string Znachenie = @"C:\jm-net-takogo\zapusk.exe --tiho";

        _vetka.SetString(Imya, Znachenie);

        RegistryGuard.TryVerifyValue(
            RegistryHive.CurrentUser, _vetka.SubKey, Imya, RegistryView.Default,
            out var propusk, out var otkaz).Should().BeTrue(otkaz);

        var zhurnal = new SpisokZhurnala();
        var bekap = new RegistryBackup(_pesochnica.Root);

        var udalenie = await new RegistryExecutor(zhurnal, bekap)
            .DeleteValueAsync(propusk, TestContext.Current.CancellationToken);

        udalenie.Status.Should().Be(DeleteStatus.Deleted);
        RegistryRollback.ValueRestored(
            RegistryHive.CurrentUser, _vetka.SubKey, Imya, RegistryView.Default, Znachenie)
            .Should().BeFalse("сначала значение обязано исчезнуть, иначе откат нечего доказывать");

        var fayl = Directory.EnumerateFiles(_pesochnica.Root, "*.reg").Single();

        var otkat = await RegistryRollback.ImportAsync(
            fayl, RegistryView.Default, TestContext.Current.CancellationToken);

        otkat.Ok.Should().BeTrue(otkat.Reason);

        // Проверяется ЧТЕНИЕМ, а не кодом возврата reg.exe. Импорт, который
        // никто не прочитал обратно, это надежда, а не откат.
        RegistryRollback.ValueRestored(
            RegistryHive.CurrentUser, _vetka.SubKey, Imya, RegistryView.Default, Znachenie)
            .Should().BeTrue("значение обязано вернуться ровно таким, каким было");
    }

    [Fact]
    public async Task Import_ne_nachinaetsya_esli_fayl_ne_proshel_proverku()
    {
        // Тот же порядок, что и у удаления: сначала проверка файла, потом
        // действие. Импорт мусора в реестр это хуже, чем не откатить вовсе.
        var put = Path.Combine(_pesochnica.Root, "musor.reg");
        await File.WriteAllTextAsync(put, "не реестровый файл вовсе", TestContext.Current.CancellationToken);

        var otkat = await RegistryRollback.ImportAsync(
            put, RegistryView.Default, TestContext.Current.CancellationToken);

        otkat.Ok.Should().BeFalse();
        otkat.Reason.Should().Contain("импорт не начинался");
    }

    [Fact]
    public async Task Import_otsutstvuyushchego_fayla_eto_otkaz_a_ne_isklyuchenie()
    {
        var otkat = await RegistryRollback.ImportAsync(
            Path.Combine(_pesochnica.Root, "net-takogo.reg"),
            RegistryView.Default,
            TestContext.Current.CancellationToken);

        otkat.Ok.Should().BeFalse();
        otkat.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ValueRestored_vidit_raznicu_v_soderzhimom_a_ne_tolko_v_nalichii()
    {
        // Ловушка на ленивую проверку: значение на месте, но не то. Откат,
        // который считает это успехом, вернул человеку чужую строку.
        const string Imya = "podmenennaya";

        _vetka.SetString(Imya, @"C:\jm\bylo.exe");
        await Task.CompletedTask;

        RegistryRollback.ValueRestored(
            RegistryHive.CurrentUser, _vetka.SubKey, Imya, RegistryView.Default, @"C:\jm\stalo.exe")
            .Should().BeFalse();

        RegistryRollback.ValueRestored(
            RegistryHive.CurrentUser, _vetka.SubKey, Imya, RegistryView.Default, @"C:\jm\bylo.exe")
            .Should().BeTrue();
    }
}
