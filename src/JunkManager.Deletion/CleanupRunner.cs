using JunkManager.Core;
using JunkManager.Core.Scanning;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <param name="Total">Сколько находок в пакете. Ноль сюда не приходит.</param>
/// <param name="Last">
/// Чем кончилась ровно эта находка. Без исхода экран хода показывал бы только
/// движение полосы, то есть «что-то происходит», а человеку в этот момент нужно
/// «что именно унесли и что не смогли». Null допускается ради вызовов, где
/// исхода ещё нет.
/// </param>
public sealed record CleanupProgress(
    int Done, int Total, long BytesFreed, string Path, DeleteOutcome? Last = null)
{
    public double Share => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <param name="Cancelled">
/// True when the person stopped it. Without this flag a run that was stopped
/// after two of three thousand items looks exactly like a small successful one,
/// and the summary line would say so.
/// </param>
public sealed record CleanupReport(
    IReadOnlyList<DeleteOutcome> Outcomes,
    long BytesFreed,
    bool Cancelled)
{
    public int DeletedCount => Outcomes.Count(o => o.Status == DeleteStatus.Deleted);

    public int SkippedCount => Outcomes.Count(o => o.Status == DeleteStatus.Skipped);

    public int FailedCount => Outcomes.Count(o => o.Status == DeleteStatus.Failed);

    public int CancelledCount => Outcomes.Count(o => o.Status == DeleteStatus.Cancelled);
}

/// <summary>
/// Runs a whole batch of findings through <see cref="FileDeleter"/>, reporting
/// progress and collecting one report at the end.
/// </summary>
/// <remarks>
/// <para>
/// The routing between <see cref="FileDeleter.DeleteAsync"/> and
/// <see cref="FileDeleter.DeleteSelectedAsync"/> lives here and nowhere else.
/// It is one switch on <see cref="Finding.Scope"/>, and getting it wrong once
/// means taking a directory the person was told would stay.
/// </para>
/// <para>
/// Findings not reached before cancellation get no outcome at all. Writing a
/// Cancelled line for each of them would put thousands of records in the
/// journal describing things that never happened. DeleteStatus.Cancelled comes
/// from the deleter, for the item that was actually interrupted mid-way.
/// </para>
/// </remarks>
public sealed class CleanupRunner
{
    private readonly FileDeleter _udalitel;
    private readonly ISpecialCleaner _osobyy;

    /// <param name="special">
    /// Механизм для находок, чей путь это АДРЕС, а не место на диске. Null берёт
    /// настоящий: подставляют его только проверки, потому что за ним стоят
    /// необратимые операции чужими руками.
    /// </param>
    public CleanupRunner(FileDeleter deleter, ISpecialCleaner? special = null)
    {
        ArgumentNullException.ThrowIfNull(deleter);
        _udalitel = deleter;
        _osobyy = special ?? new SpecialCleaner();
    }

    /// <summary>
    /// Runs the batch off the caller's thread and reports progress as it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `Task.Run` тут не украшение. Само удаление синхронное целиком: обход
    /// каталога, `File.Delete`, `Directory.Delete`. Без переноса весь пакет
    /// исполнялся бы на потоке вызывающего, а зовут отсюда поток окна, и окно
    /// замирало на всё время очистки: полоса хода не перерисовывалась, кнопка
    /// «Остановить» не нажималась, и продукт выглядел зависшим ровно в тот
    /// момент, когда человек больше всего хочет видеть, что происходит.
    /// </para>
    /// <para>
    /// Тем же приёмом и по тому же доводу работают `RegistryScanner.ScanAsync`,
    /// `VolumeCacheSource.ScanAsync` и `HistoryService.ReadAsync`. Поток очистки
    /// был единственным, кто это не делал.
    /// </para>
    /// <para>
    /// Токен в `Task.Run` НЕ передаётся намеренно. Отменённый до старта `Task.Run`
    /// не запускает тело вовсе и бросает отменой, то есть пакет не оставил бы ни
    /// одного исхода и ни одной строки журнала. Отмена обязана обрабатываться
    /// ВНУТРИ перебора, где она даёт честный отчёт с пометкой `Cancelled`.
    /// </para>
    /// </remarks>
    public Task<CleanupReport> RunAsync(
        IReadOnlyList<Finding> findings,
        DeleteMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return Task.Run(
            () => PerebratAsync(findings, mode, progress, ct), CancellationToken.None);
    }

    private async Task<CleanupReport> PerebratAsync(
        IReadOnlyList<Finding> findings,
        DeleteMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken ct)
    {
        var ishody = new List<DeleteOutcome>(findings.Count);
        var osvobozhdeno = 0L;
        var otmeneno = false;

        foreach (var nahodka in findings)
        {
            if (ct.IsCancellationRequested)
            {
                otmeneno = true;
                break;
            }

            var ishod = await OdnaAsync(nahodka, mode, ct).ConfigureAwait(false);

            ishody.Add(ishod);
            osvobozhdeno += ishod.BytesFreed;

            if (ishod.Status == DeleteStatus.Cancelled)
            {
                otmeneno = true;
            }

            progress?.Report(new CleanupProgress(
                ishody.Count, findings.Count, osvobozhdeno, nahodka.Path, ishod));
        }

        return new CleanupReport(ishody, osvobozhdeno, otmeneno || ct.IsCancellationRequested);
    }

    private async Task<DeleteOutcome> OdnaAsync(
        Finding nahodka, DeleteMode mode, CancellationToken ct)
    {
        if (CleanupProcessGuard.Refusal(nahodka.RequiredStoppedProcesses) is { } processRefusal)
            return await _udalitel.RecordRefusalAsync(nahodka.Path, processRefusal).ConfigureAwait(false);

        // A database is not a file-deletion request. The extension did not sign a waiver.
        if (nahodka.Source == FindingSource.Vacuum)
        {
            if (!CleanupPathPolicy.TryVerify(nahodka.Path, out var database, out var refusal))
                return await _udalitel.RecordRefusalAsync(nahodka.Path, refusal).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
                return await _udalitel.RecordAsync(new(nahodka.Path, DeleteStatus.Cancelled, 0, "отменено до сжатия")).ConfigureAwait(false);
            var compacted = SqliteVacuumExecutor.TryCompact(database, out var freed, out var reason);
            return await _udalitel.RecordAsync(new(nahodka.Path, compacted ? DeleteStatus.Deleted : DeleteStatus.Skipped,
                freed, compacted ? "База сжата, файл сохранён" : reason)).ConfigureAwait(false);
        }
        // Развилка ПЕРВАЯ, до предохранителя путей. Обработчик очистки Windows
        // и платформенная утилита несут адрес (`volumecache:Recycle Bin`), а не
        // место на диске, и предохранителю его показывать нельзя: он честно
        // отвечает «путь не абсолютный», а человек читает это как поломку
        // продукта и остаётся без единого способа очистить корзину.
        if (!FindingPath.IsFileSystem(nahodka.Path))
        {
            var chuzhimi = await _osobyy.CleanAsync(nahodka, ct).ConfigureAwait(false);

            // Журнал пишет удалитель и только он: две точки записи разъезжаются
            // на первом же исходе, который одна из них не знает.
            return await _udalitel.RecordAsync(chuzhimi).ConfigureAwait(false);
        }

        // Проверка идёт по КОРНЮ находки в обоих режимах. Для поэлементного
        // удаления DeleteSelectedAsync проверяет каждую цель отдельно и
        // отказывается от всего, что вышло за корень.
        if (!SafetyGuard.TryVerifyForDeletion(nahodka.Path, out var proverennyy, out var prichina))
        {
            // Отказ произошёл ДО удалителя, который обычно ведёт запись сам.
            // Без этой строки в журнале была бы дыра ровно на отказах.
            return await _udalitel.RecordRefusalAsync(nahodka.Path, prichina)
                .ConfigureAwait(false);
        }

        if (!CleanupPathPolicy.TryVerify(nahodka.Path, out _, out prichina))
            return await _udalitel.RecordRefusalAsync(nahodka.Path, prichina).ConfigureAwait(false);

        return nahodka.Scope switch
        {
            DeleteScope.Whole when nahodka.FileSnapshots is null =>
                await _udalitel.DeleteAsync(proverennyy, mode, ct).ConfigureAwait(false),

            DeleteScope.SelectedEntries or DeleteScope.Whole =>
                await _udalitel.DeleteCleanupSnapshotAsync(
                    proverennyy, nahodka.DeletionTargets, mode, nahodka.RequiredStoppedProcesses, nahodka.FileSnapshots, ct).ConfigureAwait(false),

            _ => throw new InvalidOperationException(
                $"неизвестная область удаления {nahodka.Scope} у находки {nahodka.Path}"),
        };
    }
}
