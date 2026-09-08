using JunkManager.Safety;
using JunkManager.Core.Scanning;

namespace JunkManager.Deletion;

/// <summary>
/// The only type in the solution allowed to remove anything from disk. That is
/// not a convention: <c>ArchitectureTests</c> reads the compiled assemblies and
/// fails the build if <c>File.Delete</c> or <c>Directory.Delete</c> shows up
/// anywhere else.
/// </summary>
public sealed partial class FileDeleter
{
    private readonly IOperationLog _zhurnal;

    public FileDeleter(IOperationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _zhurnal = log;
    }

    /// <summary>
    /// Removes one verified path and reports what happened. Never throws for a
    /// path it could not take: a locked file, a vanished directory and a denied
    /// permission are ordinary results here, and turning them into exceptions
    /// would mean the first one aborts the rest of the clean.
    /// </summary>
    public async Task<DeleteOutcome> DeleteAsync(
        VerifiedPath path,
        DeleteMode mode,
        CancellationToken ct = default)
    {
        var itog = Vypolnit(path, mode, ct);

        // Журнал пишется всегда и с CancellationToken.None. Отменённая запись
        // означала бы удалённый файл, о котором не осталось следа, то есть ровно
        // ту дыру, ради закрытия которой журнал и заведён.
        await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);

        return itog;
    }

    /// <summary>
    /// Removes exactly the listed entries inside <paramref name="root"/> and
    /// leaves the root itself alone. This is what a rule with an age cut-off or
    /// a name mask produces: it counted some of the bytes in the directory, so
    /// it may only take those.
    /// </summary>
    /// <remarks>
    /// The root is never deleted here under any circumstance, including an empty
    /// list of targets. In this mode the root can be the user's %TEMP%, and a
    /// path that means "everything currently running is writing here" must not
    /// have a code path that removes it.
    /// </remarks>
    /// <returns>
    /// One aggregate outcome for the finding. The journal, meanwhile, gets a
    /// line per entry: the aggregate is for the person reading a summary, the
    /// journal is the record of what actually left the disk.
    /// </returns>
    public Task<DeleteOutcome> DeleteSelectedAsync(
        VerifiedPath root,
        IReadOnlyList<string> targets,
        DeleteMode mode,
        CancellationToken ct = default) => DeleteSelectedCoreAsync(root, targets, mode, false, [], null, ct);

    internal Task<DeleteOutcome> DeleteCleanupSnapshotAsync(VerifiedPath root, IReadOnlyList<string> targets,
        DeleteMode mode, IReadOnlyList<string> requiredStoppedProcesses,
        IReadOnlyDictionary<string, CleanupFileSnapshot>? snapshots, CancellationToken ct) =>
        DeleteSelectedCoreAsync(root, targets, mode, true, requiredStoppedProcesses, snapshots, ct);

    private async Task<DeleteOutcome> DeleteSelectedCoreAsync(VerifiedPath root, IReadOnlyList<string> targets,
        DeleteMode mode, bool filesOnly, IReadOnlyList<string> requiredStoppedProcesses,
        IReadOnlyDictionary<string, CleanupFileSnapshot>? snapshots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var koren = root.Value;

        if (string.IsNullOrEmpty(koren))
        {
            return new DeleteOutcome(
                string.Empty, DeleteStatus.Skipped, 0, "пустой пропуск: удалять нечего");
        }

        if (targets.Count == 0)
        {
            // Ноль целей это ноль удалений. Соблазн «раз список пуст, значит
            // берём каталог» стоил бы пользовательского %TEMP% целиком.
            return new DeleteOutcome(
                koren, DeleteStatus.Skipped, 0, "список целей пуст: удалять нечего");
        }

        long bayt = 0;
        var udaleno = 0;
        var otmena = false;
        string? prichina = null;
        string? derzhatel = null;
        var roditeli = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cel in targets)
        {
            if (ct.IsCancellationRequested)
            {
                otmena = true;
                break;
            }

            if (!SafetyGuard.TryVerifyUnder(root, cel, out var propusk, out var otkaz))
            {
                var propushchena = new DeleteOutcome(cel, DeleteStatus.Skipped, 0, otkaz);
                await _zhurnal.RecordAsync(propushchena, CancellationToken.None).ConfigureAwait(false);
                prichina ??= otkaz;
                continue;
            }

            DeleteOutcome itog;
            if (filesOnly)
            {
                var processReason = CleanupProcessGuard.Refusal(requiredStoppedProcesses);
                if (processReason is null && snapshots is not null)
                {
                    try
                    {
                        if (!snapshots.TryGetValue(cel, out var snapshot) || !snapshot.Matches(cel))
                            processReason = "файл изменился после проверки; выполните новое сканирование";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { processReason = "состояние файла не подтверждено: " + ex.Message; }
                }
                if (processReason is not null || !CleanupPathPolicy.TryVerify(cel, out _, out otkaz) || Directory.Exists(cel))
                {
                    itog = await RecordRefusalAsync(cel, processReason ?? otkaz ?? "вместо показанного файла появился каталог").ConfigureAwait(false);
                }
                else
                {
                    // A former file must never become permission for recursive deletion. Nice try, filesystem.
                    itog = mode == DeleteMode.Permanent ? UdalitFayl(cel, ct)
                        : mode == DeleteMode.RecycleBin ? RecycleBinDeleter.Delete(propusk)
                        : throw new ArgumentOutOfRangeException(nameof(mode));
                    await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);
                }
            }
            else itog = await DeleteAsync(propusk, mode, ct).ConfigureAwait(false);

            if (itog.Status == DeleteStatus.Deleted)
            {
                udaleno++;
                bayt += itog.BytesFreed;
                Zapomnit(roditeli, propusk.Value, koren);
                continue;
            }

            otmena |= itog.Status == DeleteStatus.Cancelled;
            prichina ??= itog.Reason;
            derzhatel ??= itog.HoldingProcess;
        }

        await UbratOpustevshie(roditeli, ct).ConfigureAwait(false);

        if (otmena)
        {
            return new DeleteOutcome(koren, DeleteStatus.Cancelled, bayt, "отменено, унесено не всё");
        }

        if (udaleno == targets.Count)
        {
            return new DeleteOutcome(koren, DeleteStatus.Deleted, bayt);
        }

        return new DeleteOutcome(
            koren,
            DeleteStatus.Failed,
            bayt,
            $"удалено {udaleno} из {targets.Count}: {prichina ?? "причина не названа"}",
            derzhatel);
    }

    /// <summary>
    /// Записывает отказ, случившийся ДО удаления, и возвращает его исходом.
    /// </summary>
    /// <remarks>
    /// Нужен тому, кто проверяет путь предохранителем сам: такой отказ до
    /// удалителя не доходит вовсе, и без этой записи в журнале была бы дыра
    /// ровно на отказах, то есть «не стали брать» стало бы неотличимо от «не
    /// нашли». Внутри поэлементного удаления такая же запись уже пишется на
    /// каждую отвергнутую цель, и разнобой между двумя уровнями был бы враньём
    /// журнала о самом себе.
    /// </remarks>
    public async Task<DeleteOutcome> RecordRefusalAsync(string path, string reason)
    {
        var otkaz = new DeleteOutcome(path, DeleteStatus.Skipped, 0, reason);

        // CancellationToken.None по той же причине, что и в DeleteAsync: запись
        // об отказе, которую отменили, это отказ без следа.
        await _zhurnal.RecordAsync(otkaz, CancellationToken.None).ConfigureAwait(false);

        return otkaz;
    }

    /// <summary>
    /// Записывает исход, полученный ЧУЖИМИ руками, и возвращает его как есть.
    /// </summary>
    /// <remarks>
    /// Нужен находкам, за которыми стоит не файл, а механизм: обработчик очистки
    /// Windows, DISM, pnputil. Удалять их этот класс не умеет и не должен, но
    /// точка записи в журнал обязана остаться одна. Две точки записи разъезжаются
    /// на первом же исходе, который одна из них не знает, и разъезжаются молча.
    /// </remarks>
    public async Task<DeleteOutcome> RecordAsync(DeleteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        await _zhurnal.RecordAsync(outcome, CancellationToken.None).ConfigureAwait(false);

        return outcome;
    }

    /// <summary>
    /// Records every directory between a removed entry and the root, so the
    /// ones that are now empty can be swept. Strictly between: the root stays
    /// whatever happens, and directories off to the side are never touched
    /// because nobody was shown them.
    /// </summary>
    private static void Zapomnit(HashSet<string> roditeli, string cel, string koren)
    {
        var tekushchiy = Path.GetDirectoryName(cel);

        while (!string.IsNullOrEmpty(tekushchiy)
            && !tekushchiy.Equals(koren, StringComparison.OrdinalIgnoreCase)
            && roditeli.Add(tekushchiy))
        {
            tekushchiy = Path.GetDirectoryName(tekushchiy);
        }
    }

    /// <summary>
    /// Removes the directories that held nothing but the entries just deleted.
    /// </summary>
    /// <remarks>
    /// Non-recursive Delete on purpose, and this is the whole point of the
    /// method: it throws when the directory is not empty, so a file that
    /// appeared in the gap between the check and the call survives instead of
    /// being swept up unpreviewed.
    /// </remarks>
    private async Task UbratOpustevshie(HashSet<string> roditeli, CancellationToken ct)
    {
        // Глубокие первыми: иначе внешний каталог ещё не пуст в момент попытки.
        var poGlubine = roditeli
            .OrderByDescending(k => k.Count(c => c == Path.DirectorySeparatorChar));

        foreach (var katalog in poGlubine)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!SafetyGuard.TryVerifyForDeletion(katalog, out _, out _)) continue;
                Directory.Delete(katalog, recursive: false);
            }
            catch (IOException)
            {
                // Не пуст: там осталось то, что человек хотел сохранить.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            await _zhurnal.RecordAsync(
                new DeleteOutcome(katalog, DeleteStatus.Deleted, 0),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static DeleteOutcome Vypolnit(VerifiedPath path, DeleteMode mode, CancellationToken ct)
    {
        // Неизвестный режим это ошибка вызывающего, а не исход операции: тихо
        // удалять «как-нибудь» тут нельзя.
        switch (mode)
        {
            case DeleteMode.Permanent:
                break;

            case DeleteMode.RecycleBin:
                return RecycleBinDeleter.Delete(path);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mode), mode, "режим удаления не поддерживается");
        }

        var zayavlennyy = path.Value;

        if (string.IsNullOrEmpty(zayavlennyy))
        {
            // VerifiedPath это структура, значит default() снаружи получить можно
            // всегда. Пустой пропуск не пропуск.
            return new DeleteOutcome(
                string.Empty, DeleteStatus.Skipped, 0, "пустой пропуск: удалять нечего");
        }

        if (!File.Exists(zayavlennyy) && !Directory.Exists(zayavlennyy))
        {
            return new DeleteOutcome(
                zayavlennyy, DeleteStatus.Skipped, 0, "путь исчез между проверкой и удалением");
        }

        // Перепроверка непосредственно перед удалением, и она не формальность.
        // Пропуск выдан во время скана; к этому моменту под тем же именем может
        // оказаться ссылка на чужое, и удаление обязано это увидеть.
        if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina))
        {
            return new DeleteOutcome(zayavlennyy, DeleteStatus.Skipped, 0, prichina);
        }

        var put = svezhiy.Value;

        if (File.Exists(put))
        {
            return UdalitFayl(put, ct);
        }

        if (Directory.Exists(put))
        {
            return UdalitKatalog(put, ct);
        }

        return new DeleteOutcome(
            put, DeleteStatus.Skipped, 0, "путь исчез между проверкой и удалением");
    }

    /// <summary>
    /// The single cancellation check the whole deleter uses. One expression and
    /// not four copies: with copies, a test reaches one of them and a mutation
    /// that removes any of the other three still passes.
    /// </summary>
    private static bool Otmeneno(CancellationToken ct, Schet? schet = null)
    {
        if (!ct.IsCancellationRequested)
        {
            return false;
        }

        if (schet is not null)
        {
            schet.Otmeneno = true;
        }

        return true;
    }

    private static DeleteOutcome UdalitFayl(string put, CancellationToken ct)
    {
        if (Otmeneno(ct))
        {
            return new DeleteOutcome(put, DeleteStatus.Cancelled, 0, "отменено до удаления");
        }

        try
        {
            var svedeniya = new FileInfo(put);

            // Ссылка на файл удаляется как ссылка, и её байты не наши: за ней
            // лежит чужой файл, который останется на месте.
            var razmer = svedeniya.LinkTarget is null ? svedeniya.Length : 0;

            SnyatTolkoDlyaChteniya(svedeniya);
            File.Delete(put);

            return new DeleteOutcome(put, DeleteStatus.Deleted, razmer);
        }
        catch (IOException ex)
        {
            // Failed, а не Skipped, и по тому же доводу, что у каталога:
            // «пропущено» значит «решили не брать», а решения тут не было, была
            // неудача. Занятый файл лежал в отчёте пропуском до 06.09.2026, и
            // человек читал «пропущено» как «продукт так решил» и не искал
            // выхода, хотя выход есть и стоит рядом.
            //
            // Отказ без имени держателя превращает решаемую ситуацию в пожатие
            // плечами, поэтому имя спрашивается прямо тут.
            return new DeleteOutcome(
                put, DeleteStatus.Failed, 0, "файл занят: " + ex.Message, Derzhatel(put));
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DeleteOutcome(
                put, DeleteStatus.Failed, 0, "отказано в доступе: " + ex.Message);
        }
    }

    /// <summary>
    /// Names whoever is holding the file, or null when nobody can be named.
    /// </summary>
    /// <remarks>
    /// Asked only at the point of refusal and never speculatively: every call
    /// opens and closes a Restart Manager session, and a folder with five
    /// hundred locked files would otherwise open five hundred of them to say the
    /// same thing five hundred times.
    /// </remarks>
    private static string? Derzhatel(string put)
    {
        if (LockedFileInspector.TryGetHolders(put, out var kto, out _) && kto.Count > 0)
        {
            return string.Join(", ", kto.Select(k => k.Describe()));
        }

        return null;
    }

    /// <summary>
    /// Clears the read-only flag so the file can go. Installers set it on caches
    /// routinely; it is a hint to Explorer, not a protection, and treating it as
    /// one would leave a large and common class of junk permanently unremovable.
    /// </summary>
    private static void SnyatTolkoDlyaChteniya(FileInfo svedeniya)
    {
        if ((svedeniya.Attributes & FileAttributes.ReadOnly) != 0)
        {
            svedeniya.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    private static DeleteOutcome UdalitKatalog(string koren, CancellationToken ct)
    {
        var schet = new Schet();
        var polnostyu = Obhod(koren, schet, ct);

        if (schet.Otmeneno)
        {
            return new DeleteOutcome(
                koren, DeleteStatus.Cancelled, schet.Bayt, "отменено до удаления");
        }

        if (polnostyu)
        {
            return new DeleteOutcome(koren, DeleteStatus.Deleted, schet.Bayt);
        }

        // Каталог ушёл частично. Это именно Failed, а не Skipped: пропуск это
        // решение, а тут решения не было, была неудача, и байты в отчёте
        // настоящие, а не обещанные.
        return new DeleteOutcome(
            koren,
            DeleteStatus.Failed,
            schet.Bayt,
            schet.Prichina ?? "каталог удалён не полностью",
            schet.Derzhatel);
    }

    /// <summary>
    /// Removes a directory bottom-up by hand. Never
    /// <c>Directory.Delete(recursive: true)</c> and never
    /// <c>SearchOption.AllDirectories</c>: both walk straight through a junction,
    /// and a junction inside a cache folder points at somebody else's files.
    /// </summary>
    /// <returns>True only if the directory itself is gone.</returns>
    private static bool Obhod(string katalog, Schet schet, CancellationToken ct)
    {
        if (Otmeneno(ct, schet))
        {
            return false;
        }

        var polnostyu = true;

        if (!Perechislit(katalog, katalogi: true, schet, out var podkatalogi))
        {
            return false;
        }

        foreach (var pod in podkatalogi)
        {
            if (Otmeneno(ct, schet))
            {
                return false;
            }

            var svedeniya = new DirectoryInfo(pod);

            if (svedeniya.LinkTarget is not null)
            {
                // Точка повторного разбора снимается как ссылка: Delete по ней
                // убирает саму точку и не трогает то, куда она ведёт.
                if (!Poprobovat(() => svedeniya.Delete(), pod, schet))
                {
                    polnostyu = false;
                }

                continue;
            }

            if (!Obhod(pod, schet, ct))
            {
                polnostyu = false;
            }

            if (schet.Otmeneno)
            {
                return false;
            }
        }

        if (!Perechislit(katalog, katalogi: false, schet, out var fayly))
        {
            return false;
        }

        foreach (var fayl in fayly)
        {
            if (Otmeneno(ct, schet))
            {
                return false;
            }

            var svedeniya = new FileInfo(fayl);
            var razmer = svedeniya.LinkTarget is null ? Dlina(svedeniya) : 0;

            if (Poprobovat(
                    () =>
                    {
                        SnyatTolkoDlyaChteniya(svedeniya);
                        File.Delete(fayl);
                    },
                    fayl,
                    schet))
            {
                schet.Bayt += razmer;
            }
            else
            {
                polnostyu = false;
            }
        }

        if (!polnostyu)
        {
            return false;
        }

        return Poprobovat(() => Directory.Delete(katalog, recursive: false), katalog, schet);
    }

    private static long Dlina(FileInfo svedeniya)
    {
        try
        {
            return svedeniya.Length;
        }
        catch (IOException)
        {
            // Файл исчез между перечислением и замером. Считать его размер
            // выдуманным числом хуже, чем не считать вовсе.
            return 0;
        }
    }

    private static bool Perechislit(
        string katalog,
        bool katalogi,
        Schet schet,
        out List<string> rezultat)
    {
        try
        {
            rezultat = katalogi
                ? [.. Directory.EnumerateDirectories(katalog)]
                : [.. Directory.EnumerateFiles(katalog)];
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Каталог убрали параллельно: перечислять нечего, и это не отказ.
            rezultat = [];
            return true;
        }
        catch (IOException ex)
        {
            schet.Prichina ??= katalog + ": " + ex.Message;
            rezultat = [];
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            schet.Prichina ??= katalog + ": отказано в доступе, " + ex.Message;
            rezultat = [];
            return false;
        }
    }

    private static bool Poprobovat(Action deystvie, string put, Schet schet)
    {
        try
        {
            deystvie();
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Уже нет: цель достигнута чужими руками.
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (IOException ex)
        {
            if (schet.Prichina is null)
            {
                schet.Prichina = put + ": " + ex.Message;
                schet.Derzhatel = Derzhatel(put);
            }

            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            schet.Prichina ??= put + ": отказано в доступе, " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Running totals for one directory walk. First reason wins: the tenth
    /// locked file in a folder explains nothing the first one did not.
    /// </summary>
    private sealed class Schet
    {
        public long Bayt { get; set; }

        public string? Prichina { get; set; }

        /// <summary>Кто держал первый неподдавшийся файл, если его удалось назвать.</summary>
        public string? Derzhatel { get; set; }

        public bool Otmeneno { get; set; }
    }
}
