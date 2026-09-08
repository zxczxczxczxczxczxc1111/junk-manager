using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <summary>
/// The product settings that reach an actual operation.
/// </summary>
/// <remarks>
/// Каждое значение обязано куда-то доезжать. Настройка, которую негде
/// применить, это переключатель, который ничего не переключает, и человек
/// узнаёт об этом, только пересчитав результат руками.
/// </remarks>
internal sealed record AppSettings
{
    /// <summary>Спека, раздел 11: включено по умолчанию, повышение идёт до появления окна.</summary>
    public bool ElevateOnStart { get; init; } = true;

    public DeleteMode Mode { get; init; } = DeleteMode.Permanent;

    /// <summary>
    /// Необязательная точка восстановления перед первым удалением в реестре
    /// за сеанс. Не заменяет экспорт отдельных удаляемых записей.
    /// </summary>
    public bool RestorePointBeforeRegistry { get; init; }

    /// <summary>
    /// Необязательный экспорт удаляемых записей реестра для их восстановления.
    /// </summary>
    public bool BackupRegistryBeforeCleanup { get; init; }

    /// <summary>Обнаружители без списка правил: кэши Electron, заброшенные папки.</summary>
    public bool DetectorsEnabled { get; init; } = true;

    public bool DeepScan { get; init; }

    /// <summary>Поиск следов удалённых программ.</summary>
    public bool LeftoverSearch { get; init; } = true;

    /// <summary>Ноль значит бессрочно.</summary>
    public int HistoryRetentionDays { get; init; } = 90;
}

internal interface ISettingsService
{
    /// <summary>Где лежит файл. Называется в сообщении об ошибке чтения.</summary>
    string FilePath { get; }

    /// <summary>
    /// Последнее прочитанное или записанное. Читают те, кто не держит своей
    /// копии: решение о повышении при запуске и служба очистки.
    /// </summary>
    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken ct);

    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
