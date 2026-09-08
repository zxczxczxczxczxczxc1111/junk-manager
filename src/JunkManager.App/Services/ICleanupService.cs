using JunkManager.Core;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <summary>
/// Один проход очистки, от списка находок до отчёта.
/// </summary>
/// <remarks>
/// Интерфейс существует ради проверок экрана: настоящая служба трогает диск, а
/// проверка потока «подтвердил, пошло, кончилось» обязана идти без диска. Это
/// та же причина, по которой у сканирования есть <see cref="IScanService"/>.
/// </remarks>
internal interface ICleanupService
{
    /// <summary>
    /// Безвозвратно или в корзину. Читается из настроек, по умолчанию
    /// безвозвратно: спека говорит, что через корзину медленнее и не всё туда
    /// влезает.
    /// </summary>
    DeleteMode Mode { get; set; }

    Task<CleanupReport> RunAsync(
        IReadOnlyList<Finding> findings,
        IProgress<CleanupProgress>? progress,
        CancellationToken ct);
}
