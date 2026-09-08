using FluentAssertions;
using JunkManager.Core.Registry;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

/// <summary>
/// Which branches WOW64 really splits in two, and which it shares.
/// </summary>
/// <remarks>
/// <para>
/// Разбор чужих чистильщиков 05.09.2026 указал на дефект в наших правилах:
/// `App Paths` под HKLM это ОБЩИЙ ключ, а не две проекции, и правило на
/// 32-битный вид давало вторую находку с тем же адресом. Сканер дубли не
/// склеивает намеренно (у него два прохода по правилам), поэтому человек видел
/// бы одну и ту же запись дважды.
/// </para>
/// <para>
/// Проверки спрашивают СИСТЕМУ, а не помнят документацию: список общих ключей
/// это решение Windows, оно менялось между версиями и может измениться снова.
/// Сравнивается СОДЕРЖИМОЕ, а не имена ключей: имя это логический путь, в обоих
/// видах оно одинаково по определению, и проверка на именах не падает никогда.
/// </para>
/// <para>
/// Класс LiveRead: реестр только читается, ничего не меняется.
/// </para>
/// </remarks>
[Trait("Class", "LiveRead")]
public sealed class ObshchieVetkiTests
{
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string AppPathsWow =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Udalenie = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UdalenieWow =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Roditel = @"SOFTWARE\Microsoft\Windows\CurrentVersion";

    [Fact]
    public void App_Paths_odin_i_tot_zhe_klyuch_v_oboih_proekciyah()
    {
        var shestdesyat = Podklyuchi(RegistryHive.LocalMachine, RegistryView.Registry64, AppPaths);
        var tridcat = Podklyuchi(RegistryHive.LocalMachine, RegistryView.Registry32, AppPaths);

        shestdesyat.Should().NotBeEmpty("иначе сравнивать нечего и проверка пуста");
        tridcat.Should().BeEquivalentTo(
            shestdesyat,
            "App Paths под HKLM это общий ключ, и второе правило на 32-битный вид "
            + "давало бы вторую находку с тем же адресом");
    }

    [Fact]
    public void V_WOW6432Node_App_Paths_net_svoih_zapisey()
    {
        // Обратная сторона удаления правила: вместе с дублем могло уйти и
        // покрытие. По буквальному пути WOW6432Node не должно лежать ничего
        // сверх общего ключа, иначе убранное правило было единственным, кто
        // туда смотрел. Проверено 06.09.2026: на рабочем ПК 31 из 31, в госте
        // 17 из 17, пересечение полное.
        var obshchiy = Podklyuchi(RegistryHive.LocalMachine, RegistryView.Registry64, AppPaths);
        var poBukve = Podklyuchi(RegistryHive.LocalMachine, RegistryView.Registry64, AppPathsWow);

        obshchiy.Should().NotBeEmpty("иначе сравнивать нечего и проверка пуста");
        poBukve.Should().BeSubsetOf(
            obshchiy,
            "запись, которой нет в общем ключе, после удаления правила не увидит никто");
    }

    [Fact]
    public void Run_v_32_bitnom_vide_eto_Wow6432Node()
    {
        // Ворота против «уберём все 32-битные правила заодно»: у Run
        // перенаправление настоящее, и потерянное правило означает
        // ненайденный автозапуск x86-программы.
        var cherez64 = Znacheniya(RegistryHive.LocalMachine, RegistryView.Registry64, Run);
        var cherez32 = Znacheniya(RegistryHive.LocalMachine, RegistryView.Registry32, Run);
        var poBukve = Znacheniya(RegistryHive.LocalMachine, RegistryView.Registry64, RunWow);

        cherez64.Should().NotBeEmpty(
            "у Windows в HKLM Run всегда есть свой автозапуск, иначе сравнивать нечего");

        cherez32.Should().BeEquivalentTo(
            poBukve, "32-битный вид Run ведёт ровно в WOW6432Node");

        cherez32.Should().NotBeEquivalentTo(
            cherez64, "перенаправление настоящее, и правило на 32-битный вид это не дубль");
    }

    [Fact]
    public void Vetka_udaleniya_v_HKCU_ne_perenapravlyaetsya()
    {
        // Читатель установленных программ берёт HKCU в ОДНОМ представлении.
        // Это полно ровно до тех пор, пока ветка не перенаправляется: иначе
        // 32-битные программы, поставленные «только для меня», пропадут из
        // списка целиком, и продукт не сможет предложить их удалить.
        //
        // Непустоты у самой ветки удаления проверка НЕ требует. Свежий профиль
        // отдаёт её пустой совершенно законно, и населённость там это гонка с
        // самой Windows: 06.09.2026 в госте ветка была пуста во время прогона и
        // содержала `OneDriveSetup.exe` через минуту после него, потому что
        // OneDriveSetup доустанавливается под пользователя в фоне при первом
        // входе. Проверка, предписывающая машине населённость, шаткая по
        // построению.
        //
        // Якорь берётся на родителе: `CurrentVersion` под HKCU есть в любом
        // профиле (в госте 65 подключей, на ПК свои), и если бы HKCU\SOFTWARE
        // перенаправлялся, 32-битное представление отдало бы другой набор.
        var roditel64 = Podklyuchi(RegistryHive.CurrentUser, RegistryView.Registry64, Roditel);
        var roditel32 = Podklyuchi(RegistryHive.CurrentUser, RegistryView.Registry32, Roditel);
        var v64 = Podklyuchi(RegistryHive.CurrentUser, RegistryView.Registry64, Udalenie);
        var v32 = Podklyuchi(RegistryHive.CurrentUser, RegistryView.Registry32, Udalenie);
        var poBukve = Podklyuchi(RegistryHive.CurrentUser, RegistryView.Registry64, UdalenieWow);

        Console.WriteLine(
            $"HKCU CurrentVersion: {roditel64.Length}/{roditel32.Length}, "
            + $"удаление: {v64.Length}/{v32.Length}, по букве WOW6432Node: {poBukve.Length}");

        roditel64.Should().NotBeEmpty("этот ключ есть в любом профиле, иначе проверка пуста");
        roditel32.Should().BeEquivalentTo(
            roditel64, "HKCU\\SOFTWARE не перенаправляется, и оба представления ведут в одно место");

        v32.Should().BeEquivalentTo(
            v64,
            "если ветка станет перенаправляемой, читатель обязан завести второй проход");

        poBukve.Should().BeSubsetOf(
            v64,
            "запись по буквальному пути WOW6432Node, которой нет в общей ветке, "
            + "не увидит никто: читатель ходит в HKCU один раз");
    }

    [Fact]
    public void V_pravilah_net_vtorogo_vida_dlya_obshchey_vetki()
    {
        var pravila = RegistryScanRules.Load(PutKPravilam());

        var appPaths = pravila
            .Where(p => p.Hive == RegistryHive.LocalMachine)
            .Where(p => p.SubKey.Equals(AppPaths, StringComparison.OrdinalIgnoreCase))
            .ToList();

        appPaths.Should().ContainSingle(
            "App Paths под HKLM общий: второе правило на другую проекцию читает тот же "
            + "ключ и даёт дубль находки");
    }

    private static string[] Podklyuchi(RegistryHive uley, RegistryView vid, string put)
    {
        using var baza = RegistryKey.OpenBaseKey(uley, vid);
        using var klyuch = baza.OpenSubKey(put);

        return klyuch?.GetSubKeyNames() ?? [];
    }

    private static string[] Znacheniya(RegistryHive uley, RegistryView vid, string put)
    {
        using var baza = RegistryKey.OpenBaseKey(uley, vid);
        using var klyuch = baza.OpenSubKey(put);

        return klyuch?.GetValueNames() ?? [];
    }

    private static string PutKPravilam() =>
        Path.Combine(AppContext.BaseDirectory, "rules", "sources");
}
