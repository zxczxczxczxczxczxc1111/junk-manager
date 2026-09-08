using FluentAssertions;
using JunkManager.Core.Registry;
using JunkManager.Core.Rules;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryScannerTests : IDisposable
{
    private readonly RegistrySandbox _vetka = new();

    public void Dispose() => _vetka.Dispose();

    private RegistryScanRule Pravilo(RegistryEntryKind kind = RegistryEntryKind.Value) => new(
        Id: "test-vetka",
        Hive: RegistryHive.CurrentUser,
        SubKey: _vetka.SubKey,
        View: RegistryView.Default,
        Kind: kind,
        Name: "Тестовая ветка",
        Consequence: "Уйдёт тестовая запись, которая ни на что не влияет.");

    private static Task<RegistryScanResult> Skan(
        RegistryScanRule pravilo, Func<string, TargetState>? proba = null) =>
        new RegistryScanner(proba).ScanAsync([pravilo], null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ScanAsync_znachenie_na_otsutstvuyushchiy_fayl_eto_nahodka()
    {
        _vetka.SetString("mertvaya", @"C:\jm-net-takogo-kataloga\zapusk.exe");

        var itog = await Skan(Pravilo());

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].ValueName.Should().Be("mertvaya");
        itog.Findings[0].MissingTarget.Should().Be(@"C:\jm-net-takogo-kataloga\zapusk.exe");
        itog.Findings[0].RuleId.Should().Be("test-vetka");
        itog.Findings[0].Consequence.Should().NotBeNullOrWhiteSpace();
        itog.Findings[0].Address.Should().Contain("HKCU").And.Contain("mertvaya");
        itog.Findings[0].Snapshot.Should().NotBeNull("удаление должно сравнить данные со снимком при поиске");
    }

    [Fact]
    public async Task RunOnce_prefixes_remain_part_of_the_value_name_and_snapshot()
    {
        // The punctuation belongs to Windows, not to our command parser's imagination.
        _vetka.SetExpandString("!*delayed", @"C:\jm-absent\later.exe --finish");
        var result = await Skan(Pravilo());
        var found = result.Findings.Should().ContainSingle().Subject;
        found.ValueName.Should().Be("!*delayed");
        found.Snapshot!.Values.Single().Kind.Should().Be(RegistryValueKind.ExpandString);
        found.Snapshot.Values.Single().Decode().Should().Be(@"C:\jm-absent\later.exe --finish");
    }

    [Fact]
    public async Task ScanAsync_zhivoe_znachenie_ne_nahodka_i_ne_propusk()
    {
        // Живая запись не упоминается нигде. Пропуск это решение не трогать, а
        // тут решать было нечего.
        _vetka.SetString("zhivaya", Environment.ProcessPath ?? @"C:\Windows\notepad.exe");

        var itog = await Skan(Pravilo());

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_sushchestvovanie_proveryaetsya_po_faylovoy_sisteme_a_ne_po_stroke()
    {
        // Тот же самый путь, ответ пробы разный. Если сканер судит по виду
        // строки, оба прогона дают одно и то же, и тест краснеет.
        _vetka.SetString("odna_i_ta_zhe", @"C:\jm-odna-i-ta-zhe\app.exe");

        var kakBudtoEst = await Skan(Pravilo(), _ => TargetState.Exists);
        var kakBudtoNet = await Skan(Pravilo(), _ => TargetState.Missing);

        kakBudtoEst.Findings.Should().BeEmpty();
        kakBudtoNet.Findings.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAsync_neizvestnoe_sostoyanie_celi_eto_propusk_a_ne_nahodka()
    {
        // "Нет прав на чтение каталога" это НЕ "файла нет". Разница в том, что
        // первое даёт ложную находку на живой программе.
        _vetka.SetString("nechitaemaya", @"C:\jm-nechitaemyy\app.exe");

        var itog = await Skan(Pravilo(), _ => TargetState.Unknown);

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("проверить не удалось");
    }

    [Fact]
    public async Task ScanAsync_znachenie_po_umolchaniyu_ne_rassmatrivaetsya()
    {
        _vetka.SetOwnDefault(@"C:\jm-net-takogo\po-umolchaniyu.exe");

        var itog = await Skan(Pravilo());

        itog.Findings.Should().BeEmpty("значение по умолчанию не удаляется никогда");
        itog.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("по умолчанию");
    }

    [Fact]
    public async Task ScanAsync_ne_stroka_eto_propusk_s_prichinoy()
    {
        _vetka.SetDword("chislo", 42);

        var itog = await Skan(Pravilo());

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("DWord");
    }

    [Fact]
    public async Task ScanAsync_nerazobrannoe_znachenie_eto_propusk_a_ne_nahodka()
    {
        // Продукт, предлагающий удалить то, чего он не понял, врёт человеку про
        // то, что он проверил.
        _vetka.SetString("nerazbornaya", "rundll32.exe shell32.dll,Control_RunDLL");

        var itog = await Skan(Pravilo());

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("не разобрано");
    }

    [Fact]
    public async Task ScanAsync_vetka_vne_spiska_razreshennyh_eto_propusk_a_ne_isklyuchenie()
    {
        var chuzhaya = Pravilo() with { SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion" };

        var itog = await Skan(chuzhaya);

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("отклонена");
    }

    [Fact]
    public async Task ScanAsync_otsutstvuyushchaya_vetka_eto_norma()
    {
        // RunOnce заводится только когда в него что-то положили. Пустой отчёт и
        // ни одной жалобы.
        var netu = Pravilo() with { SubKey = RegistrySandbox.Koren + @"\net-takoy-vetki-vovse" };

        var itog = await Skan(netu);

        itog.Findings.Should().BeEmpty();
        itog.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_podklyuch_s_mertvym_znacheniem_po_umolchaniyu_eto_nahodka_klyucha()
    {
        // Форма App Paths: запись это подключ, а путь лежит в его значении по
        // умолчанию.
        _vetka.SetSubKeyDefault("jm-net-takoy.exe", @"C:\jm-net-takogo\jm-net-takoy.exe");

        var itog = await Skan(Pravilo(RegistryEntryKind.Key));

        itog.Findings.Should().ContainSingle();
        itog.Findings[0].Kind.Should().Be(RegistryEntryKind.Key);
        itog.Findings[0].ValueName.Should().BeEmpty("удаляется подключ целиком, а не значение");
        itog.Findings[0].SubKey.Should().EndWith("jm-net-takoy.exe");
    }

    [Fact]
    public async Task ScanAsync_otmena_vidna_v_rezultate()
    {
        _vetka.SetString("mertvaya", @"C:\jm-net-takogo\zapusk.exe");

        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var itog = await new RegistryScanner().ScanAsync([Pravilo()], null, otmena.Token);

        itog.Cancelled.Should().BeTrue(
            "прерванный обход, выглядящий как чистый реестр, это единственный неверный ответ, "
            + "который никто не идёт проверять");
    }

    [Fact]
    public void Load_nastoyashchiy_fayl_vetok_gruzitsya_i_ves_prohodit_guard()
    {
        var katalog = Path.Combine(AppContext.BaseDirectory, "rules", "sources");

        var vetki = RegistryScanRules.Load(katalog);

        vetki.Select(rule => rule.Mode).Should().Contain(RegistryScanMode.SharedDlls)
            .And.Contain(RegistryScanMode.ComServers).And.Contain(RegistryScanMode.FileAssociations);
        foreach (var rule in vetki)
            JunkManager.Safety.RegistryGuard.TryVerifyScanBranch(rule.Hive, rule.SubKey, rule.View, out _, out _).Should().BeTrue();
        vetki.Select(v => v.Id).Should().OnlyHaveUniqueItems();
        vetki.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Consequence));
        vetki.Should().Contain(v => v.Kind == RegistryEntryKind.Key,
            "App Paths это подключи, и без них DeleteSubKeyTree некому вызывать");
        vetki.Should().Contain(v => v.View == RegistryView.Registry32,
            "32-битная проекция HKLM это отдельные записи, а не другое написание тех же");
    }

    [Fact]
    public void Load_vetka_kotoruyu_ne_propuskaet_guard_ronyaet_zagruzku()
    {
        var katalog = Path.Combine(Path.GetTempPath(), "jm-vetki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(katalog);

        try
        {
            File.WriteAllText(Path.Combine(katalog, "registry-branches.json"), """
            { "branches": [
              { "id": "sistema", "hive": "LocalMachine", "view": "Registry64", "kind": "Value",
                "subKey": "SYSTEM\\CurrentControlSet\\Services",
                "name": "Службы", "consequence": "машина перестанет загружаться" } ] }
            """);

            var vyzov = () => RegistryScanRules.Load(katalog);

            vyzov.Should().Throw<RuleFormatException>().WithMessage("*навсегда*");
        }
        finally
        {
            Directory.Delete(katalog, recursive: true);
        }
    }

    [Fact]
    public void Load_vetka_bez_consequence_ne_gruzitsya()
    {
        var katalog = Path.Combine(Path.GetTempPath(), "jm-vetki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(katalog);

        try
        {
            File.WriteAllText(Path.Combine(katalog, "registry-branches.json"), """
            { "branches": [
              { "id": "run", "hive": "CurrentUser", "view": "Default", "kind": "Value",
                "subKey": "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                "name": "Автозапуск", "consequence": "   " } ] }
            """);

            var vyzov = () => RegistryScanRules.Load(katalog);

            vyzov.Should().Throw<RuleFormatException>().WithMessage("*consequence*");
        }
        finally
        {
            Directory.Delete(katalog, recursive: true);
        }
    }

    [Fact]
    public async Task Zapis_iz_32_bitnogo_vida_sveryaetsya_s_blizhnecom()
    {
        // Запись 32-битной программы говорит про свои каталоги: её
        // «Program Files» это «Program Files (x86)». Наш процесс 64-битный,
        // раскрывает по-своему, файла не находит и без сверки объявил бы
        // мусором ЖИВОЙ автозапуск. Найдено разбором чужих чистильщиков
        // 05.09.2026.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var kak_vidit_64 = Path.Combine(programFiles, "JmVydumannaya", "zapusk.exe");
        var kak_vidit_32 = Path.Combine(x86, "JmVydumannaya", "zapusk.exe");

        _vetka.SetString("tridcatdva", $"\"{kak_vidit_64}\"");

        var pravilo = Pravilo() with { View = RegistryView.Registry32 };

        // Проба отвечает «есть» ТОЛЬКО про 32-битный путь: так и выглядит
        // машина, где программа стоит и работает.
        var itog = await Skan(
            pravilo,
            put => put.Equals(kak_vidit_32, StringComparison.OrdinalIgnoreCase)
                ? TargetState.Exists
                : TargetState.Missing);

        itog.Findings.Should().BeEmpty(
            "программа стоит в 32-битном каталоге, и это не мусор");
    }

    [Fact]
    public async Task Zapis_iz_64_bitnogo_vida_s_blizhnecom_ne_sveryaetsya()
    {
        // Обратные ворота: сверка только для 32-битного вида. Иначе она
        // прикрывала бы настоящие находки, у которых случайно есть тёзка в
        // каталоге для 32-битных программ.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var kak_vidit_64 = Path.Combine(programFiles, "JmVydumannaya", "zapusk.exe");
        var kak_vidit_32 = Path.Combine(x86, "JmVydumannaya", "zapusk.exe");

        _vetka.SetString("shestdesyat", $"\"{kak_vidit_64}\"");

        var itog = await Skan(
            Pravilo() with { View = RegistryView.Registry64 },
            put => put.Equals(kak_vidit_32, StringComparison.OrdinalIgnoreCase)
                ? TargetState.Exists
                : TargetState.Missing);

        itog.Findings.Should().ContainSingle("64-битная запись про 64-битный каталог и говорит");
    }
}
