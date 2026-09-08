using System.Diagnostics;
using System.Text;
using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.Core.Registry;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Live;

/// <summary>
/// Модуль реестра целиком, через службы ОКНА, в настоящем реестре гостя.
/// </summary>
/// <remarks>
/// <para>
/// Не Sandbox: тут настоящая ветка автозапуска профиля, настоящий экспорт
/// reg.exe и настоящая точка восстановления. На машине человека этому места
/// нет, и предохранитель это первое, что здесь спрашивается.
/// </para>
/// <para>
/// Зовутся именно СЛУЖБЫ, а не ядро напрямую. Ровно на этом продукт спотыкался
/// 05.09.2026: пять источников были собраны, покрыты зелёными тестами и не
/// вызывались ниоткуда. Проверка, собирающая сканер своими руками, проверяет
/// свою копию продукта.
/// </para>
/// </remarks>
[Trait("Class", "LiveDestructive")]
[Collection(ObshcheeSostoyanieMashiny.Imya)]
public sealed class ReestrVOkneTests
{
    private const string VetkaRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ImyaZapisi = "JunkManagerZhivayaProverka";
    private const string MertvayaCel = @"C:\net-takoy-programmy-jm\zapusk.exe";

    [Fact]
    public void Tochka_vosstanovleniya_sozdaetsya_i_vidna_sisteme()
    {
        VmFuse.RequireArmed();

        var opisanie = "Junk Manager: живая проверка " + Guid.NewGuid().ToString("N")[..8];

        var bylo = Spisok();
        var itog = SystemRestorePoint.Create(opisanie);

        // Если защита системы в госте выключена, тут будет внятный отказ, а не
        // загадка. Читать его надо буквально: снимок гостя собран без защиты,
        // чинить надо снимок, а не проверку.
        itog.Ok.Should().BeTrue(itog.Reason);

        // Доказательством служит ТОЧКА В СПИСКЕ, а не номер: Windows 11 сборки
        // 26200 номера не возвращает вовсе, замер записан в
        // SystemRestorePointTests. Список спрашивается у ЧУЖОГО инструмента:
        // свой же вызов, повторённый второй раз, доказывает только то, что он
        // одинаково отвечает.
        var stalo = Spisok();

        stalo.Should().Contain(s => s.Contains(opisanie, StringComparison.Ordinal),
            "точка обязана быть видна в списке восстановления, а не только в нашем ответе");
        stalo.Length.Should().BeGreaterThan(bylo.Length, "точек обязано стать больше на одну");
    }

    [Fact]
    public async Task Polnyy_krug_cherez_sluzhby_okna_nayti_udalit_vernut()
    {
        VmFuse.RequireArmed();
        var nastroyki = new SettingsService();
        var original = await nastroyki.LoadAsync(TestContext.Current.CancellationToken);

        // Приманка кладётся в НАСТОЯЩИЙ автозапуск профиля: ветка HKCU\Run
        // разрешена guard-ом и стоит в поставляемом файле веток. Проверять
        // модуль на тестовой ветке значило бы проверять, что тестовая ветка
        // работает.
        using (var vetka = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VetkaRun, writable: true))
        {
            vetka!.SetValue(ImyaZapisi, MertvayaCel, RegistryValueKind.String);
        }

        try
        {
            var skaner = new RegistryScanService();
            var nayti = await skaner.ScanAsync(null, TestContext.Current.CancellationToken);

            var nasha = nayti.Findings.SingleOrDefault(
                f => string.Equals(f.ValueName, ImyaZapisi, StringComparison.Ordinal));

            nasha.Should().NotBeNull(
                "запись автозапуска на несуществующий файл это ровно то, что модуль обязан находить");
            nasha!.MissingTarget.Should().Be(MertvayaCel);

            // Настройки читаются НАСТОЯЩИЕ и до прогона: у SettingsService
            // свойство Current заполняется только LoadAsync, и служба без него
            // работала бы со значениями по умолчанию, то есть проверяла бы не
            // то, что стоит у человека.
            await nastroyki.SaveAsync(original with { BackupRegistryBeforeCleanup = true, RestorePointBeforeRegistry = false },
                TestContext.Current.CancellationToken);

            var ochistka = new RegistryCleanupService(nastroyki);
            var itog = await ochistka.RunAsync(
                [nasha], null, TestContext.Current.CancellationToken);

            itog.Report.DeletedCount.Should().Be(1, "бэкап проверен, значит удаление обязано состояться");
            Est(ImyaZapisi).Should().BeFalse("запись обязана уйти из настоящего автозапуска");

            // Откат. Критерий готовности этапа 5 звучит дословно так: «Откат
            // импортом бэкапа восстанавливает ветку».
            var bekapy = new RegistryBackupsService(itog.BackupDirectory);
            var spisok = await bekapy.ListAsync(TestContext.Current.CancellationToken);

            var svezhiy = spisok.FirstOrDefault(b => b.Valid);
            svezhiy.Should().NotBeNull("бэкап делается перед КАЖДОЙ записью, и он обязан быть годным");

            var otkat = await bekapy.RestoreAsync(svezhiy!, TestContext.Current.CancellationToken);
            otkat.Ok.Should().BeTrue(otkat.Reason);

            // Сверяется СОДЕРЖИМОЕ, а не наличие: вернуться обязано ровно то,
            // что ушло, байт в байт.
            RegistryRollback.ValueRestored(
                RegistryHive.CurrentUser, VetkaRun, ImyaZapisi, RegistryView.Default, MertvayaCel)
                .Should().BeTrue();
        }
        finally
        {
            // Прибирает за собой при любом исходе. Оставленная запись в
            // настоящем автозапуске это чужая проблема на следующем прогоне.
            using var vetka = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VetkaRun, writable: true);
            vetka?.DeleteValue(ImyaZapisi, throwOnMissingValue: false);
            await nastroyki.SaveAsync(original, CancellationToken.None);
        }
    }

    /// <summary>Описания точек восстановления так, как их видит система.</summary>
    /// <remarks>
    /// Спрашивается ЧУЖОЙ инструмент, а не наш же P/Invoke второй раз: две
    /// одинаковые лжи согласуются между собой, а список восстановления оболочки
    /// про наш код ничего не знает.
    /// </remarks>
    private static string[] Spisok()
    {
        var psi = new ProcessStartInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            RedirectStandardOutput = true,
            // Обе стороны, и своя, и чужая. Консоль гостя живёт в кодовой
            // странице 866, поэтому powershell без явного переключения отдаёт
            // кириллицу в ней, и описание точки приезжает вопросительными
            // знаками. Проверено прогоном в госте 07.09.2026: точка была
            // создана, а сравнение падало на кодировке.
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; "
            + "Get-ComputerRestorePoint | ForEach-Object { $_.Description }");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("powershell не запустился");

        var vyvod = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return vyvod.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool Est(string imya)
    {
        using var vetka = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VetkaRun, writable: false);
        return vetka?.GetValueNames().Contains(imya, StringComparer.OrdinalIgnoreCase) == true;
    }
}
