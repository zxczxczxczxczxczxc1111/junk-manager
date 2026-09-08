using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <param name="Valid">
/// Прошёл ли файл те же четыре проверки, что стоят на импорте. Показывается
/// ДО выбора: узнать, что бэкап негоден, в момент отката поздно.
/// </param>
internal sealed record RegistryBackupFile(
    string FilePath,
    string FileName,
    DateTimeOffset WrittenUtc,
    long SizeBytes,
    bool Valid,
    string? Reason)
{
    public string Address { get; init; } = "Старая копия без точных сведений о выбранной записи";
    public Microsoft.Win32.RegistryView View { get; init; }
}

/// <summary>
/// Свои бэкапы реестра и откат по одному из них.
/// </summary>
/// <remarks>
/// Список СВОИХ файлов, а не системный диалог выбора. Раздел 15 спеки запрещает
/// стандартные контролы, но главное не это: импорт .reg это ЗАПИСЬ в реестр, и
/// продукт, импортирующий файл, который написал не он, перестаёт быть продуктом,
/// удаляющим только доказанное.
/// </remarks>
internal interface IRegistryBackupsService
{
    string Directory { get; }

    Task<IReadOnlyList<RegistryBackupFile>> ListAsync(CancellationToken ct);

    Task<RollbackResult> RestoreAsync(RegistryBackupFile file, CancellationToken ct);
}
