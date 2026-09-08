using System.Diagnostics;
using System.Globalization;
using JunkManager.Deletion;
using JunkManager.Safety;

namespace JunkManager.App.Services;

/// <summary>
/// The real three ways out of "file is in use".
/// </summary>
/// <remarks>
/// <para>
/// Все три механизма были написаны и покрыты проверками задолго до этого класса
/// и не имели НИ ОДНОГО вызова из прода: `LockedFileInspector` только называл
/// держателя, `RebootDeleteScheduler` не звался ниоткуда вовсе. Человек читал
/// «держит процесс Spotify (PID 13248)» и на этом упирался в стену.
/// </para>
/// <para>
/// Держатели переспрашиваются в момент ДЕЙСТВИЯ, а не берутся из отчёта об
/// очистке. Между отчётом и нажатием проходит сколько угодно времени, программа
/// могла закрыться сама, а могла и смениться другой с тем же номером процесса.
/// Убивать по устаревшему номеру значит убить не то.
/// </para>
/// </remarks>
internal sealed class LockedFileService : ILockedFileService
{
    public Task<LockedFileActionResult> PoprositZakrytsyaAsync(string put, CancellationToken ct) =>
        // Уходит с потока окна: за вызовом стоит ожидание чужих программ, а окно
        // в этот момент обязано остаться живым.
        Task.Run(() => Poprosit(put), CancellationToken.None);

    public Task<LockedFileActionResult> OtlozhitNaZagruzkuAsync(string put, CancellationToken ct) =>
        Task.Run(() => Otlozhit(put), CancellationToken.None);

    public Task<LockedFileActionResult> ZavershitAsync(string put, CancellationToken ct) =>
        Task.Run(() => Zavershit(put), CancellationToken.None);

    private static LockedFileActionResult Poprosit(string put) =>
        LockedFileInspector.TryAskToClose(put, out var prichina)
            ? new LockedFileActionResult(true, "Программа закрылась по просьбе")
            : new LockedFileActionResult(false, prichina ?? "закрыть не удалось, причина не названа");

    private static LockedFileActionResult Otlozhit(string put)
    {
        if (!SafetyGuard.TryVerifyForDeletion(put, out var proverennyy, out var otkaz))
        {
            return new LockedFileActionResult(false, otkaz);
        }

        return RebootDeleteScheduler.TryScheduleOnReboot(proverennyy, out var prichina)
            ? new LockedFileActionResult(
                true, "Windows удалит файл при следующей загрузке. Сейчас он остаётся на месте")
            : new LockedFileActionResult(
                false, prichina ?? "отложить не удалось, причина не названа");
    }

    private static LockedFileActionResult Zavershit(string put)
    {
        if (!LockedFileInspector.TryGetHolders(put, out var derzhateli, out var prichina))
        {
            return new LockedFileActionResult(
                false, $"кто держит файл, выяснить не удалось: {prichina}");
        }

        if (derzhateli.Count == 0)
        {
            // Освободился сам, пока человек читал экран. Это успех, а не ошибка:
            // цель была в том, чтобы файл никто не держал.
            return new LockedFileActionResult(true, "Файл уже никто не держит");
        }

        var kriticheskiy = derzhateli.FirstOrDefault(d => d.Kind == HolderKind.Critical);

        if (kriticheskiy is not null)
        {
            // Критический процесс не завершается даже принудительно. Его
            // завершение это синий экран, а не освобождённый файл.
            return new LockedFileActionResult(
                false,
                $"файл держит {kriticheskiy.Describe()}, и это критический процесс системы: "
                + "продукт его не завершает ни при каких условиях");
        }

        var ubito = new List<string>();
        var otkazy = new List<string>();

        foreach (var derzhatel in derzhateli)
        {
            if (Ubit(derzhatel, out var oshibka))
            {
                ubito.Add(derzhatel.Describe());
            }
            else
            {
                otkazy.Add($"{derzhatel.Describe()}: {oshibka}");
            }
        }

        if (otkazy.Count > 0)
        {
            return new LockedFileActionResult(
                false, "завершить не удалось: " + string.Join("; ", otkazy));
        }

        return new LockedFileActionResult(
            true,
            string.Create(
                CultureInfo.CurrentCulture,
                $"Завершено: {string.Join(", ", ubito)}. Файл можно удалять"));
    }

    /// <summary>
    /// Завершает один процесс и говорит, если не смог.
    /// </summary>
    /// <remarks>
    /// Дерево процессов завершается целиком: держателем часто оказывается
    /// дочерний процесс, а закрытие одного его родителя оставляет файл занятым и
    /// выглядит как «нажал, и ничего не произошло».
    /// </remarks>
    private static bool Ubit(FileHolder derzhatel, out string? oshibka)
    {
        try
        {
            using var process = Process.GetProcessById(derzhatel.ProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);

            oshibka = process.HasExited ? null : "процесс не завершился за десять секунд";
            return oshibka is null;
        }
        catch (ArgumentException)
        {
            // Процесса уже нет. Цель достигнута чужими руками, и это успех.
            oshibka = null;
            return true;
        }
        catch (InvalidOperationException)
        {
            oshibka = null;
            return true;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // Чаще всего это чужой сеанс или процесс с более высокими правами.
            oshibka = e.Message;
            return false;
        }
        catch (NotSupportedException e)
        {
            oshibka = e.Message;
            return false;
        }
    }
}
