using System.Globalization;
using System.IO;
using System.Text.Json;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <summary>
/// Reads the journal directory: one .jsonl file is one cleanup.
/// </summary>
/// <remarks>
/// A broken file never takes the others down with it. The journal is read when
/// something has already gone wrong, and refusing to open any of it because one
/// line is truncated is refusing at the worst possible moment.
/// </remarks>
internal sealed class HistoryService(string? katalog = null, TimeProvider? chasy = null)
    : IHistoryService
{
    /// <summary>Запасной разбор времени, когда шапки в файле нет.</summary>
    private const string FormatImeni = "yyyyMMdd-HHmmss-fff";

    private readonly TimeProvider _chasy = chasy ?? TimeProvider.System;

    public string Directory { get; } = katalog ?? JsonlOperationLog.DefaultDirectory;

    public Task<HistoryPage> ReadAsync(CancellationToken ct) =>
        // Чтение уходит с потока окна целиком. Каталог журналов лежит в профиле
        // пользователя, то есть на диске, который прямо сейчас чистят: один
        // медленный ответ файловой системы это застывшее окно.
        Task.Run(() => Prochitat(ct), ct);

    /// <summary>
    /// Removes journals older than the retention setting says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Возраст берётся по последней записи файла, а не по имени. Имя это
    /// удобство: файл можно переименовать или принести с другой машины, и
    /// разбор чужого имени дал бы ему нулевую дату, то есть удалил бы его
    /// первым.
    /// </para>
    /// <para>
    /// Трогаются только <c>*.jsonl</c> в своём каталоге. Занятый файл
    /// пропускается молча: журнал ИДУЩЕЙ очистки открыт на запись, и уборка,
    /// падающая на нём, не убрала бы ничего.
    /// </para>
    /// </remarks>
    public Task<int> UbratStaryeAsync(int dney, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dney);

        // Ноль это «хранить всё». Считать его нулевым сроком значило бы стереть
        // журнал целиком у того, кто попросил хранить его вечно.
        return dney == 0 ? Task.FromResult(0) : Task.Run(() => Ubrat(dney, ct), ct);
    }

    private int Ubrat(int dney, CancellationToken ct)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        var otsechka = _chasy.GetUtcNow().AddDays(-dney).UtcDateTime;
        var ubrano = 0;

        foreach (var fayl in System.IO.Directory.GetFiles(Directory, "*.jsonl"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.GetLastWriteTimeUtc(fayl) >= otsechka)
                {
                    continue;
                }

                File.Delete(fayl);
                ubrano++;
            }
            catch (IOException)
            {
                // Занят. Останется до следующего запуска, и это лучше, чем
                // уборка, останавливающаяся на первом же открытом файле.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return ubrano;
    }

    private HistoryPage Prochitat(CancellationToken ct)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            // Каталог создаётся первой очисткой. До неё его нет, и это пусто,
            // а не сбой чтения.
            return new HistoryPage([], []);
        }

        var progony = new List<HistoryRun>();
        var oshibki = new List<HistoryReadError>();

        foreach (var fayl in System.IO.Directory.GetFiles(Directory, "*.jsonl"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var progon = JsonlOperationLog.ReadRun(fayl);

                progony.Add(new HistoryRun(
                    fayl,
                    progon.StartedUtc ?? VremyaIzImeni(fayl),
                    progon.Outcomes));
            }
            catch (JsonException e)
            {
                oshibki.Add(new HistoryReadError(fayl, e.Message));
            }
            catch (IOException e)
            {
                oshibki.Add(new HistoryReadError(fayl, e.Message));
            }
            catch (UnauthorizedAccessException e)
            {
                oshibki.Add(new HistoryReadError(fayl, e.Message));
            }
        }

        // От новых к старым: журнал открывают, чтобы посмотреть последнюю
        // очистку, и она обязана быть первой строкой.
        return new HistoryPage([.. progony.OrderByDescending(p => p.StartedUtc)], oshibki);
    }

    /// <summary>
    /// Время из имени файла. Нужно только файлам без шапки.
    /// </summary>
    /// <returns>
    /// <see cref="DateTimeOffset.MinValue"/> для чужого имени. Приписать такому
    /// файлу текущее время значит поднять его наверх списка как самую свежую
    /// очистку, поэтому он уезжает в самый низ.
    /// </returns>
    private static DateTimeOffset VremyaIzImeni(string fayl) =>
        DateTimeOffset.TryParseExact(
            Path.GetFileNameWithoutExtension(fayl), FormatImeni, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var kogda)
            ? kogda
            : DateTimeOffset.MinValue;
}
