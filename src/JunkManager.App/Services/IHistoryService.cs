using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <param name="StartedUtc">
/// Время начала прогона. Берётся из шапки записи, а не из имени файла: имя это
/// удобство, файл можно переименовать или принести с другой машины.
/// </param>
internal sealed record HistoryRun(
    string FilePath,
    DateTimeOffset StartedUtc,
    IReadOnlyList<DeleteOutcome> Outcomes)
{
    public long BytesFreed => Outcomes.Sum(o => o.BytesFreed);

    public int DeletedCount => Outcomes.Count(o => o.Status == DeleteStatus.Deleted);

    public int SkippedCount => Outcomes.Count(o => o.Status == DeleteStatus.Skipped);

    public int FailedCount => Outcomes.Count(o => o.Status == DeleteStatus.Failed);

    public int CancelledCount => Outcomes.Count(o => o.Status == DeleteStatus.Cancelled);

    /// <summary>
    /// Сколько строк прогона кончились НЕ удалением: пропущено, не удалось,
    /// остановлено, вместе.
    /// </summary>
    /// <remarks>
    /// Нужно карточке прогона. Без этого числа прогон, где не удалилось всё,
    /// выглядит как «0 объектов удалено» и ничем не отличается от прогона, в
    /// котором нечего было удалять. Найдено снимком живого окна 05.09.2026:
    /// четыре строки в прогоне, а карточка говорила про одну.
    /// </remarks>
    public int UnfinishedCount => Outcomes.Count - DeletedCount;
}

/// <param name="Reason">
/// Что именно не разобралось, с позицией. «Файл повреждён» это не действие,
/// а «неожиданный символ на позиции 1208» это место, куда можно посмотреть.
/// </param>
internal sealed record HistoryReadError(string FilePath, string Reason);

internal sealed record HistoryPage(
    IReadOnlyList<HistoryRun> Runs,
    IReadOnlyList<HistoryReadError> Errors);

internal interface IHistoryService
{
    /// <summary>Где лежат журналы. Показывается кнопкой «Открыть папку».</summary>
    string Directory { get; }

    Task<HistoryPage> ReadAsync(CancellationToken ct);

    /// <summary>
    /// Убирает записи старше срока. Ноль значит хранить всё.
    /// </summary>
    /// <returns>Сколько файлов убрано.</returns>
    /// <remarks>
    /// Реализация по умолчанию не убирает ничего: заглушки в проверках экрана
    /// не имеют каталога вовсе, а обещание «старые записи удаляются» держит
    /// настоящая служба. Пустое тело здесь честнее, чем обязанность, которую
    /// каждая заглушка исполняла бы по-своему.
    /// </remarks>
    Task<int> UbratStaryeAsync(int dney, CancellationToken ct) => Task.FromResult(0);
}
