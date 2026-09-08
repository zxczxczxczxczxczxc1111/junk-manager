using FluentAssertions;
using JunkManager.Safety;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryGuardTests
{
    private const string RunHkcu = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunHklm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    [Theory]
    [InlineData(@"Software\Classes\CLSID\{16A296C3-C083-46EE-8FF8-9C78AC586389}\InprocServer32")]
    [InlineData(@"Software\Classes\CLSID\{16A296C3-C083-46EE-8FF8-9C78AC586389}\LocalServer32")]
    [InlineData(@"Software\Classes\JunkManager.Sample\shell\open\command")]
    public void Cautious_leaf_allows_only_user_default_value_and_never_whole_key(string path)
    {
        // One default value is not a deed to the entire COM registration.
        RegistryGuard.TryVerifyValue(RegistryHive.CurrentUser, path, string.Empty, RegistryView.Default,
            out var value, out _).Should().BeTrue();
        value.IsEmpty.Should().BeFalse();
        RegistryGuard.TryVerifyValue(RegistryHive.CurrentUser, path, "ThreadingModel", RegistryView.Default,
            out _, out _).Should().BeFalse();
        RegistryGuard.TryVerifyKey(RegistryHive.CurrentUser, path, RegistryView.Default, out _, out _).Should().BeFalse();
        RegistryGuard.TryVerifyValue(RegistryHive.LocalMachine, path, string.Empty, RegistryView.Default,
            out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"Software\Classes\CLSID\not-a-guid\InprocServer32")]
    [InlineData(@"Software\Classes\*\shell\open\command")]
    [InlineData(@"Software\Classes\.txt\shell\open\command")]
    [InlineData(@"Software\Classes\Directory\shell\open\command")]
    [InlineData(@"Software\Classes\AppX.Test\shell\open\command")]
    public void Cautious_guard_refuses_shell_wide_and_invalid_registrations(string path)
    {
        // Familiar words do not turn a shell-wide setting into one abandoned application.
        RegistryGuard.TryVerifyValue(RegistryHive.CurrentUser, path, string.Empty, RegistryView.Default,
            out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerifyValue_razreshennaya_vetka_daet_propusk()
    {
        var est = RegistryGuard.TryVerifyValue(
            RegistryHive.CurrentUser, RunHkcu, "OneDrive", RegistryView.Default,
            out var propusk, out var otkaz);

        est.Should().BeTrue(otkaz);
        propusk.ValueName.Should().Be("OneDrive");
        propusk.Address.Should().Contain("HKCU").And.Contain("OneDrive");
    }

    [Fact]
    public void Avtozapusk_mashiny_razreshen_naravne_s_polzovatelskim()
    {
        // Ветка автозапуска есть и у пользователя, и у машины. Разрешить только
        // пользовательскую значит не находить половину мёртвых записей, причём
        // именно ту половину, которую ставят установщики.
        RegistryGuard.TryVerifyValue(
            RegistryHive.LocalMachine, RunHklm, "SomeApp", RegistryView.Registry64,
            out var propusk, out var otkaz)
            .Should().BeTrue(otkaz);

        propusk.Address.Should().Contain("HKLM");
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion")]
    [InlineData(@"SOFTWARE")]
    [InlineData(@"SOFTWARE\Classes")]
    public void TryVerifyValue_ne_perechislennaya_vetka_otklonyaetsya(string vetka)
    {
        // Разрешено ТОЛЬКО перечисленное. Соседняя ветка, родительская ветка и
        // улей целиком отклоняются одинаково.
        var est = RegistryGuard.TryVerifyValue(
            RegistryHive.LocalMachine, vetka, "chto-to", RegistryView.Registry64,
            out _, out var otkaz);

        est.Should().BeFalse();
        otkaz.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("SYSTEM")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services")]
    [InlineData("SECURITY")]
    [InlineData("SAM")]
    [InlineData(@"SAM\SAM\Domains")]
    public void TryVerifyValue_SYSTEM_SECURITY_i_SAM_zapreshcheny_navsegda(string vetka)
    {
        var est = RegistryGuard.TryVerifyValue(
            RegistryHive.LocalMachine, vetka, "chto-to", RegistryView.Registry64,
            out _, out var otkaz);

        est.Should().BeFalse();
        otkaz.Should().Contain("навсегда",
            "причина обязана называть безусловный запрет, а не отсутствие в списке разрешённых");
    }

    [Fact]
    public void Zapret_pobezhdaet_razreshenie_dazhe_esli_vetku_razreshili_po_oshibke()
    {
        // Проверяется ПОРЯДОК: запреты смотрятся раньше разрешений. Ветка
        // Policies лежит под SOFTWARE, и если однажды кто-то разрешит SOFTWARE
        // целиком, Policies обязана остаться закрытой.
        var est = RegistryGuard.TryVerifyValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableLUA", RegistryView.Registry64, out _, out var otkaz);

        est.Should().BeFalse();
        otkaz.Should().Contain("навсегда");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TryVerifyValue_znachenie_po_umolchaniyu_ne_propuskaetsya(string? imya)
    {
        // У ключа значение по умолчанию одно, и оно и есть смысл ключа.
        var est = RegistryGuard.TryVerifyValue(
            RegistryHive.CurrentUser, RunHkcu, imya!, RegistryView.Default,
            out _, out var otkaz);

        est.Should().BeFalse();
        otkaz.Should().Contain("по умолчанию");
    }

    [Fact]
    public void Vlozhennost_schitaetsya_po_segmentam_a_ne_po_prefiksu()
    {
        // Строка "Run" это префикс строки "RunOnce", но RunOnce это другой
        // ключ. Файловый двойник этой ошибки стоил отказа на C:\WindowsApps,
        // см. SafetyGuard.IsAtOrUnder.
        var podKornem = RegistryGuard.TryVerifyValue(
            RegistryHive.CurrentUser, RunHkcu + @"\Vlozhennyy", "a",
            RegistryView.Default, out _, out _);

        var soseddZaPrefiksom = RegistryGuard.TryVerifyBranch(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\RunSomethingElse",
            RegistryView.Default, out _, out _);

        podKornem.Should().BeTrue("вложенный ключ разрешённой ветки разрешён");
        soseddZaPrefiksom.Should().BeFalse("совпадение по префиксу строки это не вложенность");
    }

    [Theory]
    [InlineData(@"  Software\Microsoft\Windows\CurrentVersion\Run  ")]
    [InlineData(@"\Software\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"Software\\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"software\microsoft\windows\currentversion\run")]
    public void Zapis_vetki_normalizuetsya_i_spisok_ne_obhoditsya_napisaniem(string vetka)
    {
        RegistryGuard.TryVerifyBranch(
            RegistryHive.CurrentUser, vetka, RegistryView.Default, out var chistaya, out var otkaz)
            .Should().BeTrue(otkaz);

        chistaya.Should().Be(@"Software\Microsoft\Windows\CurrentVersion\Run",
            "нормализованная форма одна, иначе адрес в журнале зависит от написания в файле");
    }

    [Theory]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run\..\..\..\..\SYSTEM")]
    [InlineData(@"Software\.\Microsoft")]
    public void Perehody_tochkami_otklonyayutsya(string vetka)
    {
        RegistryGuard.TryVerifyBranch(
            RegistryHive.CurrentUser, vetka, RegistryView.Default, out _, out var otkaz)
            .Should().BeFalse();

        otkaz.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(RegistryHive.ClassesRoot)]
    [InlineData(RegistryHive.Users)]
    [InlineData(RegistryHive.CurrentConfig)]
    [InlineData(RegistryHive.PerformanceData)]
    public void Chuzhie_uli_otklonyayutsya_celikom(RegistryHive uley)
    {
        RegistryGuard.TryVerifyValue(uley, RunHkcu, "a", RegistryView.Default, out _, out var otkaz)
            .Should().BeFalse();

        otkaz.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryVerifyKey_razreshennyy_koren_celikom_ne_udalyaetsya()
    {
        // App Paths\foo.exe это запись. App Paths это контейнер, который ведёт
        // Windows, и продукт, умеющий удалить контейнер, находится в одной
        // ошибке от снятия регистрации у всех программ сразу.
        var koren = RegistryGuard.TryVerifyKey(
            RegistryHive.LocalMachine, AppPaths, RegistryView.Registry64, out _, out var otkaz);

        var zapis = RegistryGuard.TryVerifyKey(
            RegistryHive.LocalMachine, AppPaths + @"\netu-takoy.exe", RegistryView.Registry64,
            out var propusk, out var vtoroyOtkaz);

        koren.Should().BeFalse();
        otkaz.Should().Contain("не удаляется");
        zapis.Should().BeTrue(vtoroyOtkaz);
        propusk.SubKey.Should().EndWith("netu-takoy.exe");
    }

    [Fact]
    public void Propusk_nelzya_sobrat_snaruzhi()
    {
        // Конструктор internal, как у VerifiedPath. Тест сторожит намерение:
        // компилятор ответит на вопрос «а это точно проверяли», только пока
        // собрать пропуск снаружи нельзя.
        var ctor = typeof(VerifiedRegistryValue).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        ctor.Should().BeEmpty("публичный конструктор пропуска превращает пропуск в обычную запись");
    }

    [Fact]
    public void Postavlyaemyy_fayl_vetok_celikom_prohodit_guard()
    {
        // Файл умеет только сужать список из кода. Ветка, которую guard не
        // пропускает, в поставке означает мёртвую строку, а не расширение прав.
        var fayl = Path.Combine(AppContext.BaseDirectory, "rules", "sources", "registry-branches.json");

        File.Exists(fayl).Should().BeTrue("файл веток обязан доехать до выходного каталога");

        var soderzhimoe = File.ReadAllText(fayl);

        soderzhimoe.Should().NotContain("JunkManagerTests",
            "тестовая ветка разрешена guard-ом, но в поставку не входит");
        soderzhimoe.Should().NotContain("SYSTEM");
    }
}
