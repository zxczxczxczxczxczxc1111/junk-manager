using JunkManager.Core.Registry;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <param name="RestorePointNote">
/// Что вышло с точкой восстановления. НИКОГДА не пустая и никогда не null:
/// вопрос поднимается при каждой очистке, и «про точку ничего не сказано»
/// человек читает как «точка была».
/// </param>
/// <param name="BackupDirectory">
/// Куда лёг единственный путь отката. Адресом, а не словами: тому, кому нужен
/// откат, нужен файл.
/// </param>
internal sealed record RegistryCleanupOutcome(
    RegistryCleanupReport Report, string RestorePointNote, string BackupDirectory)
{
    public bool BackupEnabled { get; init; }
    public string BackupNote { get; init; } = "Экспорт записей выключен.";
}

/// <summary>
/// Один проход очистки реестра, от списка находок до отчёта.
/// </summary>
/// <remarks>
/// Договор существует ради проверок экрана: настоящая служба трогает реестр, а
/// проверка потока «отметил, подтвердил, пошло, кончилось» обязана идти без
/// реестра. Та же причина, по которой есть <see cref="ICleanupService"/>.
/// </remarks>
internal interface IRegistryCleanupService
{
    string BackupNote => "Экспорт записей выключен. Удалённые записи нельзя восстановить из Junk Manager.";
    /// <summary>
    /// Делает попытку страховки, если её ещё не делали в этом сеансе, и
    /// возвращает словами, что вышло.
    /// </summary>
    /// <remarks>
    /// Отдельный метод, а не свойство: создание точки восстановления это
    /// секунды работы VSS, и свойство с таким побочным действием однажды
    /// прочитают из привязки, заморозив окно. Зовётся ПЕРЕД показом
    /// подтверждения: читать про отсутствующую страховку надо до нажатия, а не
    /// в отчёте после него.
    /// </remarks>
    Task<string> ZastrahovatAsync(CancellationToken ct);

    Task<RegistryCleanupOutcome> RunAsync(
        IReadOnlyList<RegistryFinding> findings,
        IProgress<RegistryCleanupProgress>? hod,
        CancellationToken ct);
}
