using System.Collections.ObjectModel;
using JunkManager.App.Services;
using JunkManager.Core;
using JunkManager.Deletion;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Очистка, которая ничего не удаляет и всё помнит.
/// </summary>
/// <remarks>
/// Нужна, чтобы проверять ПОТОК экрана: подтвердил, пошло, кончилось,
/// остановил. Настоящая служба для этого не годится в принципе: она трогает
/// диск, а проверка потока обязана идти без диска. Что именно уносится с
/// диска, проверяется отдельно, в CleanupRunnerTests.
/// </remarks>
public sealed class OchistkaZaglushka : ICleanupService
{
    public DeleteMode Mode { get; set; } = DeleteMode.Permanent;

    /// <summary>Что именно отдали на очистку. Порядок сохраняется.</summary>
    public Collection<Finding> Poluchennye { get; } = [];

    /// <summary>
    /// Зовётся перед каждой находкой. Отсюда проверка отменяет прогон, не
    /// подстраиваясь под настоящие задержки диска.
    /// </summary>
    public Action<int>? PeredKazhdoy { get; set; }

    /// <summary>Чем кончается каждая находка. По умолчанию удалена.</summary>
    public DeleteStatus Ishod { get; set; } = DeleteStatus.Deleted;

    /// <summary>
    /// Кто держал файл. Заполненный держатель разворачивает на итоге блок
    /// занятых файлов с тремя кнопками, а он живёт в СВОЁМ шаблоне, который без
    /// этого поля не разворачивается ни в одной проверке сборки окна.
    /// </summary>
    public string? Derzhatel { get; set; }

    public Task<CleanupReport> RunAsync(
        IReadOnlyList<Finding> findings,
        IProgress<CleanupProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        foreach (var nahodka in findings)
        {
            Poluchennye.Add(nahodka);
        }

        var ishody = new List<DeleteOutcome>();
        var osvobozhdeno = 0L;

        for (var i = 0; i < findings.Count; i++)
        {
            PeredKazhdoy?.Invoke(i);

            if (ct.IsCancellationRequested)
            {
                return Task.FromResult(new CleanupReport(ishody, osvobozhdeno, Cancelled: true));
            }

            var nahodka = findings[i];
            var bayt = Ishod == DeleteStatus.Deleted ? nahodka.SizeBytes : 0;
            var ishod = new DeleteOutcome(
                nahodka.Path, Ishod, bayt,
                Ishod == DeleteStatus.Deleted ? null : "заглушка",
                Ishod == DeleteStatus.Deleted ? null : Derzhatel);

            ishody.Add(ishod);
            osvobozhdeno += bayt;

            progress?.Report(new CleanupProgress(
                ishody.Count, findings.Count, osvobozhdeno, nahodka.Path, ishod));
        }

        return Task.FromResult(new CleanupReport(ishody, osvobozhdeno, Cancelled: false));
    }
}
