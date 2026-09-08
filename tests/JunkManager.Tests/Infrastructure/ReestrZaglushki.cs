using JunkManager.App.Services;
using JunkManager.Core.Registry;
using JunkManager.Deletion;
using Microsoft.Win32;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Служба реестра, отдающая заранее заготовленный итог.
/// </summary>
/// <remarks>
/// Общая на весь набор, а не приватная внутри одного файла: те же заглушки
/// нужны и проверкам модели, и проверкам сборки окна. Две копии разошлись бы
/// молча, и вторая проверяла бы уже не то, что первая.
/// </remarks>
public sealed class ReestrZaglushka(RegistryScanResult itog, int vetok = 3) : IRegistryScanService
{
    /// <remarks>
    /// Число веток СВОЁ и с поставляемым файлом не связано: файл правится без
    /// пересборки, и проверка, сверяющая его число, покраснела бы от
    /// добавленной ветки, ничего при этом не поймав.
    /// </remarks>
    public int BranchCount => vetok;

    public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
        Task.FromResult(itog);
}

/// <summary>Реестр, который не читается: отказ в правах.</summary>
public sealed class PadayushchiyReestr : IRegistryScanService
{
    public int BranchCount => 3;

    public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
        throw new UnauthorizedAccessException("отказано в доступе к HKLM");
}

/// <summary>Проверка, которую остановили.</summary>
public sealed class OtmenennyyReestr : IRegistryScanService
{
    public int BranchCount => 3;

    public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
        throw new OperationCanceledException();
}

/// <summary>
/// Находки-образцы. Собираются здесь, потому что у находки ядра десять полей, и
/// набранные заново в каждом файле они разъедутся по мелочи.
/// </summary>
public static class ReestrObraztsy
{
    public static RegistryFinding Nahodka(RegistryHive uley, string imya) => new(
        Hive: uley,
        SubKey: uley == RegistryHive.CurrentUser
            ? @"Software\Microsoft\Windows\CurrentVersion\Run"
            : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        ValueName: imya,
        View: RegistryView.Default,
        Kind: RegistryEntryKind.Value,
        RawValue: @"C:\Program Files\Ushedshee\run.exe --minimized",
        MissingTarget: @"C:\Program Files\Ushedshee\run.exe",
        Name: "Автозапуск",
        Consequence: "Windows пытается запустить это при каждом входе",
        RuleId: "run-hkcu");

    /// <summary>Находка-ключ: у неё удаляется ключ целиком, а не строка.</summary>
    public static RegistryFinding Klyuch(RegistryHive uley, string podvetka) => new(
        Hive: uley,
        SubKey: @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + podvetka,
        ValueName: string.Empty,
        View: RegistryView.Registry64,
        Kind: RegistryEntryKind.Key,
        RawValue: @"C:\Program Files\Ushedshee\shell.dll",
        MissingTarget: @"C:\Program Files\Ushedshee\shell.dll",
        Name: "Пути к программам",
        Consequence: "Проводник предлагает открывать этим то, чего нет",
        RuleId: "app-paths-hkcu");
}

/// <summary>
/// Очистка реестра, которая ничего не чистит и всё запоминает.
/// </summary>
/// <remarks>
/// Нужна там, где проверяется ЧТЕНИЕ и переходы потока: модель требует службу
/// очистки, а подсовывать ей настоящую значит проверять экран на живом реестре.
/// Считает и страховки: порядок «страховка до подтверждения, а не после
/// нажатия» проверяется только счётчиком.
/// </remarks>
// internal, а не public: RegistryCleanupOutcome лежит в JunkManager.App и
// виден набору только по InternalsVisibleTo. Открытый метод не имеет права
// возвращать менее доступный тип, это CS0050.
internal sealed class OchistkaReestraZaglushka : IRegistryCleanupService
{
    private const string Zametka = "точка восстановления создана, номер 42";

    public List<RegistryFinding> Prinyatye { get; } = [];

    public int Strahovok { get; private set; }

    public Task<string> ZastrahovatAsync(CancellationToken ct)
    {
        Strahovok++;
        return Task.FromResult(Zametka);
    }

    public Task<RegistryCleanupOutcome> RunAsync(
        IReadOnlyList<RegistryFinding> findings,
        IProgress<RegistryCleanupProgress>? hod,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        Prinyatye.AddRange(findings);

        var ishody = new List<DeleteOutcome>(findings.Count);

        foreach (var nahodka in findings)
        {
            var ishod = new DeleteOutcome(nahodka.Address, DeleteStatus.Deleted, 0);
            ishody.Add(ishod);

            hod?.Report(new RegistryCleanupProgress(
                ishody.Count, findings.Count, nahodka.Address, ishod));
        }

        return Task.FromResult(new RegistryCleanupOutcome(
            new RegistryCleanupReport(ishody, Cancelled: false), Zametka, @"C:\bekapy"));
    }
}

/// <summary>
/// Каталог бэкапов в памяти. Отдаёт то, что положили, и помнит, что просили
/// восстановить.
/// </summary>
/// <remarks>
/// internal по той же причине, что и очистка: RegistryBackupFile лежит в
/// JunkManager.App, а открытый метод не имеет права возвращать менее доступный
/// тип.
/// </remarks>
internal sealed class BekapyZaglushka(params RegistryBackupFile[] fayly) : IRegistryBackupsService
{
    public string Directory => @"C:\bekapy";

    public List<RegistryBackupFile> Vosstanovlennye { get; } = [];

    /// <summary>Что ответит откат. По умолчанию удача.</summary>
    public RollbackResult? Otvet { get; set; }

    public Task<IReadOnlyList<RegistryBackupFile>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RegistryBackupFile>>(fayly);

    public Task<RollbackResult> RestoreAsync(RegistryBackupFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        Vosstanovlennye.Add(file);

        return Task.FromResult(Otvet ?? new RollbackResult(true, file.FilePath, null));
    }
}
