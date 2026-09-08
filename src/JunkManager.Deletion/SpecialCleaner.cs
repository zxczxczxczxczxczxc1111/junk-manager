using JunkManager.Core;
using JunkManager.Core.Interop;
using JunkManager.Core.Sources.Platform;
using JunkManager.Core.Sources.VolumeCache;

namespace JunkManager.Deletion;

/// <summary>
/// Removes what lives behind an identity rather than behind a path.
/// </summary>
/// <remarks>
/// <para>
/// Не всякая находка это место на диске. Обработчик очистки Windows несёт адрес
/// `volumecache:Recycle Bin`, платформенная утилита несёт `platformtool:pnputil/oem12.inf`,
/// и удалять их файловым удалителем нечем: за адресом стоит чужой механизм.
/// </para>
/// <para>
/// До 06.09.2026 такого разделения не было вовсе. Перебор гнал КАЖДУЮ находку в
/// файловый удалитель, предохранитель путей отвечал «путь не абсолютный», и
/// человек читал это как поломку продукта. Заодно выяснилось, что
/// `VolumeCachePurger` и `PlatformToolExecutor` не имели ни одного вызова из
/// прода: механизмы были написаны, покрыты проверками и не подключены.
/// </para>
/// </remarks>
public sealed class SpecialCleaner : ISpecialCleaner
{
    private const string DismId = "dism/component-store";
    private const string DriverPrefix = "pnputil/";

    public Task<DeleteOutcome> CleanAsync(Finding finding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ct.ThrowIfCancellationRequested();
        if ((finding.Source == FindingSource.VolumeCache && !finding.Path.StartsWith(FindingPath.VolumeCacheScheme, StringComparison.Ordinal))
            || (finding.Source == FindingSource.PlatformTool && !finding.Path.StartsWith(FindingPath.PlatformToolScheme, StringComparison.Ordinal)))
            return Task.FromResult(new DeleteOutcome(finding.Path, DeleteStatus.Skipped, 0, "адрес не соответствует источнику очистки"));

        return finding.Source switch
        {
            FindingSource.VolumeCache => Task.FromResult(Obrabotchik(finding, ct)),
            FindingSource.PlatformTool => UtilitaAsync(finding, ct),

            // Не «на всякий случай удалим как файл»: механизма нет, и продукт
            // обязан сказать это словами, а не молча пропустить строку.
            _ => Task.FromResult(new DeleteOutcome(
                finding.Path,
                DeleteStatus.Skipped,
                0,
                $"адрес '{finding.Path}' пришёл от источника {finding.Source}, "
                + "и механизма удаления для такого источника у продукта нет")),
        };
    }

    /// <summary>
    /// Hands one Windows cleanup handler to Windows itself.
    /// </summary>
    private static DeleteOutcome Obrabotchik(Finding nahodka, CancellationToken ct)
    {
        var klyuch = nahodka.Path[FindingPath.VolumeCacheScheme.Length..];

        // Чёрный список спрашивается ЗДЕСЬ, хотя чистильщик спросит его снова.
        // Перечисление обработчиков уже отсеивает такие ключи, значит найти
        // запись не выйдет, и без этой ветки продукт ответил бы «больше не
        // зарегистрирован» про обработчик, который зарегистрирован прекрасно.
        // Неверная причина хуже отсутствующей: по ней человек пойдёт чинить
        // Windows.
        if (VolumeCacheCatalog.Blacklist.Contains(klyuch.Trim()))
        {
            return new DeleteOutcome(
                nahodka.Path,
                DeleteStatus.Skipped,
                0,
                $"обработчик '{klyuch}' в чёрном списке продукта и не запускается никогда: "
                + "это файлы человека, а не мусор системы");
        }

        var zapis = Nayti(klyuch);

        if (zapis is null)
        {
            return new DeleteOutcome(
                nahodka.Path,
                DeleteStatus.Skipped,
                0,
                $"обработчик '{klyuch}' больше не зарегистрирован в Windows: "
                + "список читался при проходе, с тех пор он изменился");
        }

        PurgeResult itog;
        var before = EmptyVolumeCacheInterop.Probe(zapis, ct);
        if (before.Failure is not null)
            return new DeleteOutcome(nahodka.Path, DeleteStatus.Skipped, 0,
                "текущее состояние обработчика не прочитано: " + before.Failure);

        try
        {
            itog = VolumeCachePurger.Purge(zapis, ct);
        }
        catch (InvalidOperationException e)
        {
            // Чёрный список и предохранитель это ОТКАЗЫ ЗАПУСКАТЬ, и чистильщик
            // объявляет их исключением намеренно. Здесь они становятся обычным
            // пропуском с текстом самого отказа: человек читает, что именно
            // помешало, а не «путь не абсолютный».
            return new DeleteOutcome(nahodka.Path, DeleteStatus.Skipped, 0, e.Message);
        }

        return itog.Outcome switch
        {
            PurgeOutcome.Purged => new DeleteOutcome(
                nahodka.Path, DeleteStatus.Deleted, Osvobozhdeno(before.SpaceUsedBytes, zapis, ct)),

            // «Удалять нечего» это штатный ответ семи обработчиков на чистой
            // машине, а не поломка. Провалом это называть нельзя: провалы читают
            // как список того, что надо чинить.
            PurgeOutcome.Refused => new DeleteOutcome(
                nahodka.Path, DeleteStatus.Skipped, 0, itog.Reason),

            _ => new DeleteOutcome(
                nahodka.Path, DeleteStatus.Failed, 0, itog.Reason ?? "обработчик отказал молча"),
        };
    }

    /// <summary>
    /// Сколько унесла очистка обработчика.
    /// </summary>
    /// <remarks>
    /// Fresh measurements bracket the purge. Concurrent changes can still
    /// affect the estimate, so the reported difference is bounded by the first measurement.
    /// </remarks>
    private static long Osvobozhdeno(long before, VolumeCacheEntry zapis, CancellationToken ct)
    {
        var ostalos = EmptyVolumeCacheInterop.Probe(zapis, ct);

        if (ostalos.Failure is not null)
        {
            // Замерить не вышло. Ноль честнее выдуманного числа: обработчик
            // отработал, и это сказано статусом, а размер остался неизвестен.
            return 0;
        }

        return Math.Clamp(before - ostalos.SpaceUsedBytes, 0, before);
    }

    private static VolumeCacheEntry? Nayti(string klyuch)
    {
        foreach (var zapis in VolumeCacheCatalog.Enumerate())
        {
            if (string.Equals(zapis.KeyName, klyuch, StringComparison.OrdinalIgnoreCase))
            {
                return zapis;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the destructive half of a platform utility.
    /// </summary>
    private static async Task<DeleteOutcome> UtilitaAsync(Finding nahodka, CancellationToken ct)
    {
        var hvost = nahodka.Path[FindingPath.PlatformToolScheme.Length..];

        try
        {
            if (string.Equals(hvost, DismId, StringComparison.Ordinal))
            {
                var run = await PlatformToolExecutor.CleanComponentStoreAsync(ct)
                    .ConfigureAwait(false);

                return PoKoduVyhoda(nahodka, run, "dism");
            }

            if (hvost.StartsWith(DriverPrefix, StringComparison.Ordinal))
            {
                var run = await PlatformToolExecutor
                    .DeleteDriverAsync(hvost[DriverPrefix.Length..], ct).ConfigureAwait(false);

                return PoKoduVyhoda(nahodka, run, "pnputil");
            }
        }
        catch (InvalidOperationException e)
        {
            return new DeleteOutcome(nahodka.Path, DeleteStatus.Skipped, 0, e.Message);
        }
        catch (ArgumentException e)
        {
            return new DeleteOutcome(nahodka.Path, DeleteStatus.Skipped, 0, e.Message);
        }

        return new DeleteOutcome(
            nahodka.Path,
            DeleteStatus.Skipped,
            0,
            $"адрес '{hvost}' не называет ни одну известную платформенную утилиту");
    }

    /// <summary>
    /// Turns a utility exit code into an outcome.
    /// </summary>
    /// <remarks>
    /// These utilities do not report freed bytes. A scan estimate is not an
    /// actual deletion measurement, so success carries an explicit unknown-size note.
    /// </remarks>
    private static DeleteOutcome PoKoduVyhoda(Finding nahodka, ToolRun run, string utilita) =>
        run.ExitCode == 0
            ? new DeleteOutcome(nahodka.Path, DeleteStatus.Deleted, 0, "Очистка завершена. Освобождённый объём утилита не сообщает")
            : new DeleteOutcome(
                nahodka.Path,
                DeleteStatus.Failed,
                0,
                $"{utilita} вышел с кодом {run.ExitCode}: "
                + Korotko(run.StandardError, run.StandardOutput));

    /// <summary>
    /// Первая содержательная строка вывода утилиты.
    /// </summary>
    /// <remarks>
    /// Целиком вывод DISM это десятки строк с полосой процентов, и в строке
    /// отчёта она превращается в кашу. Пустой вывод при ненулевом коде тоже
    /// бывает, и тогда честнее сказать, что утилита промолчала.
    /// </remarks>
    private static string Korotko(string oshibki, string vyvod)
    {
        foreach (var istochnik in new[] { oshibki, vyvod })
        {
            foreach (var stroka in istochnik.Split('\n'))
            {
                var chistaya = stroka.Trim();

                if (chistaya.Length > 0)
                {
                    return chistaya;
                }
            }
        }

        return "утилита ничего не сказала";
    }
}
