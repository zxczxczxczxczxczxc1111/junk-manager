using System.Security.Principal;
using FluentAssertions;
using JunkManager.Deletion;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.Safety;

[Trait("Class", "Sandbox")]
public sealed class ElevationTests
{
    // Sandbox holds everything that decides without touching the system: the
    // outcome table, the argument parsing, the SID comparison and the capability
    // list. These are the parts that can be wrong in a way nobody notices, and
    // they must be reachable on a developer machine without a UAC prompt.

    [Theory]
    [InlineData(false, false, ElevationOutcome.NotRequested)]
    [InlineData(false, true, ElevationOutcome.AlreadyElevated)]
    [InlineData(true, true, ElevationOutcome.AlreadyElevated)]
    public void Reshenie_bez_perezapuska_otvechaet_srazu(
        bool nuzhno, bool uzhe, ElevationOutcome ozhidaemyy)
    {
        Elevation.Reshenie(nuzhno, uzhe).Should().Be(ozhidaemyy);
    }

    [Fact]
    public void Reshenie_nuzhno_i_ne_povyshen_trebuet_perezapuska()
    {
        Elevation.Reshenie(wanted: true, elevated: false).Should().Be(ElevationOutcome.Relaunched);
    }

    [Fact]
    public void TryElevate_bez_prosby_nichego_ne_perezapuskaet()
    {
        // The setting is off. Nothing happens, and this is not a failure: a
        // product that elevates without being asked is a product nobody trusts.
        Elevation.TryElevate(wanted: false, args: [], out var ishod, out var prichina)
            .Should().BeTrue(prichina);

        // Исход зависит от того, как запущена оболочка, и версия из плана
        // молча предполагала неповышенную. Под администратором она падала на
        // AlreadyElevated. Названы оба случая: утверждение, ради которого тест
        // написан, от этого не слабеет, потому что перезапуск без просьбы
        // запрещён в обоих.
        ishod.Should().NotBe(
            ElevationOutcome.Relaunched, "без просьбы перезапуска не бывает никогда");

        ishod.Should().Be(Elevation.IsElevated
            ? ElevationOutcome.AlreadyElevated
            : ElevationOutcome.NotRequested);
    }

    [Fact]
    public void TryReadHandedIdentity_chitaet_oba_argumenta()
    {
        string[] argumenty =
        [
            "scan",
            Elevation.ArgumentSid, "S-1-5-21-1111111111-2222222222-3333333333-1000",
            Elevation.ArgumentProfile, @"C:\Users\ExampleUser",
        ];

        Elevation.TryReadHandedIdentity(argumenty, out var sid, out var profil, out var prichina)
            .Should().BeTrue(prichina);

        sid.Should().Be("S-1-5-21-1111111111-2222222222-3333333333-1000");
        profil.Should().Be(@"C:\Users\ExampleUser");
    }

    [Fact]
    public void TryReadHandedIdentity_bez_argumentov_eto_ne_oshibka()
    {
        // A process that was started by a person rather than by us has no handed
        // identity, and that is the normal first launch.
        Elevation.TryReadHandedIdentity(["scan"], out var sid, out var profil, out var prichina)
            .Should().BeFalse();

        sid.Should().BeEmpty();
        profil.Should().BeEmpty();
        prichina.Should().Contain("не передан");
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1000")]
    [InlineData("")]
    public void TryReadHandedIdentity_argument_bez_znacheniya_otkazyvaet(string sid)
    {
        string[] argumenty = string.IsNullOrEmpty(sid)
            ? ["scan", Elevation.ArgumentSid]
            : ["scan", Elevation.ArgumentSid, sid];

        Elevation.TryReadHandedIdentity(argumenty, out _, out _, out var prichina)
            .Should().BeFalse();

        prichina.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryProfileFromRegistry_svoy_SID_daet_svoy_profil()
    {
        // ProfileList lives in HKLM but is readable without elevation, checked on
        // the machine. This is the verification step that makes a handed SID
        // worth anything: a forged one names no registered profile.
        using var lichnost = WindowsIdentity.GetCurrent();

        Elevation.TryProfileFromRegistry(lichnost.User!.Value, out var profil, out var prichina)
            .Should().BeTrue(prichina);

        profil.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(profil).Should().BeTrue("профиль обязан существовать на диске");
    }

    [Fact]
    public void TryProfileFromRegistry_vydumannyy_SID_otkazyvaet()
    {
        Elevation.TryProfileFromRegistry(
            "S-1-5-21-9999999999-9999999999-9999999999-4242", out var profil, out var prichina)
            .Should().BeFalse();

        profil.Should().BeEmpty();
        prichina.Should().Contain("не найден");
    }

    [Fact]
    public void Peremennye_stroyatsya_ot_profilya_a_ne_ot_tekushchego_processa()
    {
        var karta = Elevation.Peremennye(@"D:\Profiles\xd");

        karta["USERPROFILE"].Should().Be(@"D:\Profiles\xd");
        karta["LOCALAPPDATA"].Should().Be(@"D:\Profiles\xd\AppData\Local");
        karta["APPDATA"].Should().Be(@"D:\Profiles\xd\AppData\Roaming");
        karta["TEMP"].Should().Be(@"D:\Profiles\xd\AppData\Local\Temp");
        karta["TMP"].Should().Be(@"D:\Profiles\xd\AppData\Local\Temp");
    }

    [Fact]
    public void Peremennye_ubirayut_zavershayushchiy_razdelitel()
    {
        // ProfileImagePath приезжает из реестра и иногда несёт его на хвосте.
        var karta = Elevation.Peremennye(@"C:\Users\ExampleUser\");

        // Спрашивается именно USERPROFILE: Path.Combine хвостовой разделитель
        // съедает сам, поэтому по производным путям обрезки не видно вовсе, и
        // проверка по ним ничего бы не проверяла.
        karta["USERPROFILE"].Should().Be(@"C:\Users\ExampleUser");
        karta["LOCALAPPDATA"].Should().Be(@"C:\Users\ExampleUser\AppData\Local");
    }

    [Fact]
    public void Peremennye_koren_toma_ne_prevrashchayut_v_otnositelnyy_put()
    {
        // Профилем корень тома не бывает, но обрезка хвоста обычным TrimEnd
        // оставила бы «C:», а это путь ОТНОСИТЕЛЬНО текущего каталога диска, и
        // он потом уходит в правила и в удаление.
        //
        // Спрашивается USERPROFILE, а не производные от него: Path.Combine
        // приписывает разделитель сам, поэтому LOCALAPPDATA из «C:» выходит
        // абсолютным и поломки по нему не видно. Проверено мутацией: по
        // LOCALAPPDATA она выживала.
        var karta = Elevation.Peremennye(@"C:\");

        Path.IsPathFullyQualified(karta["USERPROFILE"]).Should().BeTrue();
        Path.IsPathFullyQualified(karta["LOCALAPPDATA"]).Should().BeTrue();
    }

    [Fact]
    public void TryApplyOriginalProfile_svoy_SID_s_chuzhim_profilem_otkazyvaet()
    {
        // SID настоящий, профиль передан чужой. Это и есть та подмена, ради
        // которой сверка написана: выдуманный SID отсеивается раньше, на
        // ProfileList, и до сравнения путей дело не доходит вовсе. Без этого
        // теста мутация, снимающая сверку, выживала весь набор.
        using var lichnost = WindowsIdentity.GetCurrent();

        string[] argumenty =
        [
            Elevation.ArgumentSid, lichnost.User!.Value,
            Elevation.ArgumentProfile, @"C:\Users\net-takogo-cheloveka",
        ];

        Elevation.TryApplyOriginalProfile(argumenty, out var prichina).Should().BeFalse();
        prichina.Should().Contain("не совпадает");
    }

    [Theory]
    [InlineData(1223, ElevationOutcome.Declined)]
    [InlineData(5, ElevationOutcome.Failed)]
    [InlineData(740, ElevationOutcome.Failed)]
    [InlineData(0, ElevationOutcome.Failed)]
    public void PoKoduOshibki_otkaz_v_UAC_eto_obychnyy_ishod_a_ne_avariya(
        int kod, ElevationOutcome ozhidaemyy)
    {
        // 1223 это ERROR_CANCELLED, документированный код «человек нажал Нет».
        // Внутри catch эта ветка недостижима без настоящего окна UAC, поэтому
        // она вынесена в чистую функцию: утверждение проверяемо, а не заявлено.
        Elevation.PoKoduOshibki(kod).Should().Be(ozhidaemyy);
    }

    [Fact]
    public void CurrentSid_sovpadaet_s_tokenom_processa()
    {
        using var lichnost = WindowsIdentity.GetCurrent();

        Elevation.CurrentSid.Should().Be(lichnost.User!.Value);
    }

    [Fact]
    public void IsElevated_odin_otvet_na_vse_reshenie()
    {
        // RebootDeleteScheduler had its own copy of this check. Two copies of
        // "are we administrator" drift, and the day they disagree is the day one
        // component refuses work the other one already started.
        RebootDeleteScheduler.IsElevated.Should().Be(Elevation.IsElevated);
    }

    [Theory]
    [InlineData(ElevatedCapability.ReadPrefetch, true)]
    [InlineData(ElevatedCapability.DismComponentStore, true)]
    [InlineData(ElevatedCapability.DeleteDriverPackage, true)]
    [InlineData(ElevatedCapability.ReadWindowsApps, true)]
    [InlineData(ElevatedCapability.DeleteMachineRegistryValue, true)]
    [InlineData(ElevatedCapability.ScheduleRebootDelete, true)]
    [InlineData(ElevatedCapability.UninstallMachineProgram, true)]
    [InlineData(ElevatedCapability.CreateRestorePoint, true)]
    [InlineData(ElevatedCapability.CleanWindowsTemp, true)]
    [InlineData(ElevatedCapability.ReadUninstallBranches, false)]
    [InlineData(ElevatedCapability.ReadMsixPackages, false)]
    [InlineData(ElevatedCapability.EnumerateDrivers, false)]
    [InlineData(ElevatedCapability.ScanUserProfile, false)]
    [InlineData(ElevatedCapability.RecycleBin, false)]
    [InlineData(ElevatedCapability.VacuumUserDatabase, false)]
    [InlineData(ElevatedCapability.UninstallUserProgram, false)]
    public void Requires_otvechaet_yavno_na_kazhdyy_punkt(ElevatedCapability chto, bool trebuet)
    {
        Elevation.Requires(chto).Should().Be(trebuet);
    }

    [Fact]
    public void Requires_znaet_pro_kazhdyy_element_perechisleniya()
    {
        // The switch has no default arm on purpose, so a new capability breaks
        // the build. This test says the same thing at runtime, because somebody
        // will eventually add a default arm to make the build pass.
        foreach (var vozmozhnost in Enum.GetValues<ElevatedCapability>())
        {
            var act = () => Elevation.Requires(vozmozhnost);
            act.Should().NotThrow($"для {vozmozhnost} обязан быть записан явный ответ");
        }
    }
}

[Trait("Class", "LiveDestructive")]
public sealed class ElevationZhivyeTests
{
    // Live holds what can only be true on a real machine with the fuse armed:
    // an actual relaunch through runas, which shows a UAC prompt and starts a
    // second process. It cannot run on a developer machine, because a test that
    // pops a consent dialog in the middle of a Sandbox run is a test that gets
    // disabled within a week.

    [Fact]
    public void TryElevate_v_goste_libo_povyshaet_libo_uzhe_povyshen()
    {
        VmFuse.RequireArmed();

        var poluchilos = Elevation.TryElevate(
            wanted: true, args: ["--proba-povysheniya"], out var ishod, out var prichina);

        ishod.Should().BeOneOf(
            ElevationOutcome.AlreadyElevated,
            ElevationOutcome.Relaunched,
            ElevationOutcome.Declined);

        if (ishod == ElevationOutcome.Declined)
        {
            // Refusing UAC is an ordinary outcome and not an incident. The
            // product keeps working with less, and says what it did not check.
            poluchilos.Should().BeTrue("отказ в UAC это обычный исход, а не авария");
            prichina.Should().Contain("отказ");
        }
    }

    [Fact]
    public void Peredannyy_profil_prohodit_sverku_v_goste()
    {
        VmFuse.RequireArmed();

        string[] argumenty =
        [
            Elevation.ArgumentSid, Elevation.CurrentSid,
            Elevation.ArgumentProfile, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ];

        Elevation.TryApplyOriginalProfile(argumenty, out var prichina).Should().BeTrue(prichina);
    }

    [Fact]
    public void Chuzhoy_SID_ostanavlivaet_rabotu_a_ne_menyaet_profil_molcha()
    {
        VmFuse.RequireArmed();

        string[] argumenty =
        [
            Elevation.ArgumentSid, "S-1-5-21-9999999999-9999999999-9999999999-4242",
            Elevation.ArgumentProfile, @"C:\Users\chuzhoy",
        ];

        Elevation.TryApplyOriginalProfile(argumenty, out var prichina).Should().BeFalse();
        prichina.Should().NotBeNullOrWhiteSpace();
    }
}
