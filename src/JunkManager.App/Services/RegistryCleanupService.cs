using JunkManager.Core.Registry;
using JunkManager.Deletion;
using JunkManager.Safety;

namespace JunkManager.App.Services;

/// <summary>
/// Точка восстановления один раз за сеанс, затем перебор находок.
/// </summary>
/// <remarks>
/// <para>
/// Журнал открывается на прогон, а не на процесс: имя файла несёт время начала,
/// и экран «Журнал» читает один файл как одну очистку. Общий на процесс журнал
/// слил бы неделю прогонов в одну запись. То же самое сделано в CleanupService.
/// </para>
/// <para>
/// Точка восстановления и экспорт записей включаются отдельно. При включённом
/// экспорте ошибка резервирования сохраняет исходную запись в реестре.
/// </para>
/// </remarks>
internal sealed class RegistryCleanupService : IRegistryCleanupService
{
    private const string OpisanieTochki = "Junk Manager: перед очисткой реестра";

    private readonly ISettingsService _nastroyki;
    private readonly Func<string, RestorePointResult> _tochka;
    private readonly string? _katalogBekapov;
    private readonly string? _katalogZhurnala;

    private bool _tochkuUzheProbovali;
    private string _zametka = "точка восстановления в этом сеансе не потребовалась";

    /// <param name="tochka">
    /// Null означает настоящую точку восстановления. Параметр, потому что
    /// настоящая требует прав администратора и включённой защиты системы, а
    /// проверять учёт «один раз за сеанс» надо на машине, где нет ни того, ни
    /// другого.
    /// </param>
    /// <param name="katalogBekapov">Null означает <c>RegistryBackup.DefaultDirectory</c>.</param>
    /// <param name="katalogZhurnala">
    /// Null означает <c>JsonlOperationLog.DefaultDirectory</c>. Шов ради
    /// песочных проверок: без него они писали бы в настоящий журнал очисток на
    /// машине человека, а класс Sandbox трогает только свой временный каталог.
    /// </param>
    public RegistryCleanupService(
        ISettingsService nastroyki,
        Func<string, RestorePointResult>? tochka = null,
        string? katalogBekapov = null,
        string? katalogZhurnala = null)
    {
        ArgumentNullException.ThrowIfNull(nastroyki);

        _nastroyki = nastroyki;
        _tochka = tochka ?? SystemRestorePoint.Create;
        _katalogBekapov = katalogBekapov;
        _katalogZhurnala = katalogZhurnala;
    }

    public async Task<RegistryCleanupOutcome> RunAsync(
        IReadOnlyList<RegistryFinding> findings,
        IProgress<RegistryCleanupProgress>? hod,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var katalog = _katalogBekapov ?? RegistryBackup.DefaultDirectory;

        if (findings.Count == 0)
        {
            // Точка восстановления перед ничем это вытесненная из списка чужая
            // точка и ноль пользы.
            return new RegistryCleanupOutcome(
                new RegistryCleanupReport([], Cancelled: false), _zametka, katalog);
        }

        // Зовётся ещё раз, хотя экран уже звал перед подтверждением. Экран мог
        // и не позвать: удаление приходит и из проверок, и из будущего CLI, а
        // страховка обязана быть попыткой, а не текстом на кнопке.
        await ZastrahovatAsync(ct).ConfigureAwait(true);

        using var zhurnal = JsonlOperationLog.CreateForRun(_katalogZhurnala);
        var backupEnabled = _nastroyki.Current.BackupRegistryBeforeCleanup;
        var backupNote = BackupNote;
        var perebor = new RegistryCleanupRunner(
            new RegistryExecutor(zhurnal, backupEnabled
                ? new RegistryBackup(_katalogBekapov) : null));

        var otchet = await perebor.RunAsync(findings, hod, ct).ConfigureAwait(true);

        return new RegistryCleanupOutcome(otchet, _zametka, katalog)
        { BackupEnabled = backupEnabled, BackupNote = backupNote };
    }

    /// <summary>
    /// Одна попытка за сеанс. Попытка считается сделанной независимо от исхода:
    /// иначе на машине с выключенной защитой продукт звал бы систему перед
    /// каждым удалением и каждый раз получал бы тот же отказ.
    /// </summary>
    /// <remarks>
    /// Настоящий вызов уходит в <see cref="Task.Run(Action)"/>: SRSetRestorePointW
    /// работает через VSS и занимает секунды, а зовут его из потока окна.
    /// Замороженное на пять секунд окно человек читает как сломанное.
    /// </remarks>
    public async Task<string> ZastrahovatAsync(CancellationToken ct)
    {
        if (_tochkuUzheProbovali)
        {
            return _zametka;
        }

        if (!_nastroyki.Current.RestorePointBeforeRegistry)
        {
            _zametka = "точка восстановления выключена в настройках";
            return _zametka;
        }

        ct.ThrowIfCancellationRequested();
        _tochkuUzheProbovali = true;
        var itog = await Task.Run(() => _tochka(OpisanieTochki), ct).ConfigureAwait(true);

        // Номер печатается, только если он есть. Windows 11 сборки 26200 его не
        // возвращает вовсе (замер в SystemRestorePointTests), а подпись
        // «номер 0» человек читает как сбой при исправно созданной точке.
        _zametka = itog.Ok
            ? (itog.SequenceNumber > 0
                ? $"точка восстановления создана, номер {itog.SequenceNumber}"
                : "точка восстановления создана. Номер система не назвала, "
                  + "искать её в списке восстановления по описанию и времени")
            : (itog.Reason ?? "точка восстановления не создана, причина не названа")
              + ". " + BackupNote;

        return _zametka;
    }

    public string BackupNote => _nastroyki.Current.BackupRegistryBeforeCleanup
        ? "Экспорт выбранных записей включён. При ошибке резервирования запись сохранится в реестре."
        : "Экспорт записей выключен. Удалённые записи нельзя восстановить из Junk Manager.";
}
