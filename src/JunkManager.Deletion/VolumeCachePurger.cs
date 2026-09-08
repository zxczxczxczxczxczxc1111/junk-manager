using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using JunkManager.Core.Interop;
using JunkManager.Core.Sources.VolumeCache;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>
/// What one Windows cleanup handler did. Refused and Failed are kept apart on
/// purpose: "there was nothing to delete" is the answer seven handlers give on a
/// clean machine, and reporting that as a failure would bury the handlers that
/// really did break.
/// </summary>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined so an unassigned outcome cannot read as 'Purged' or as any " +
        "other real state. Same reasoning as DeleteStatus and RiskTier.")]
public enum PurgeOutcome
{
    /// <summary>Windows emptied the handler. Reason is null.</summary>
    Purged = 1,

    /// <summary>The handler declined: it had nothing to delete.</summary>
    Refused = 2,

    /// <summary>Tried and could not. Reason names the HRESULT.</summary>
    Failed = 3,
}

/// <param name="Reason">Null only when the outcome is <see cref="PurgeOutcome.Purged"/>.</param>
public sealed record PurgeResult(string KeyName, PurgeOutcome Outcome, string? Reason);

/// <summary>
/// The only place in the solution that calls IEmptyVolumeCache::Purge.
/// </summary>
/// <remarks>
/// <para>
/// Предохранителя ВМ здесь БОЛЬШЕ НЕТ, решение владельца 06.09.2026. Он делал
/// главную способность продукта недоступной на живой машине: корзину, журналы
/// обновления и вытесненные компоненты нельзя было очистить нигде, кроме
/// стенда, хотя спека раздел «Источник 1» прямо называет находку обработчика
/// обычной находкой. При этом файловый удалитель, который безвозвратно уносит
/// произвольные каталоги, предохранителя не требовал никогда, то есть
/// закрытым оказалось как раз более безопасное действие.
/// </para>
/// <para>
/// Ворота, которые остались и которые настоящие: чёрный список (папка
/// «Загрузки» не очищается никогда и ничем), предпросмотр со ступенью риска и
/// последствием у каждой находки, и явное подтверждение человеком. Очистку
/// выполняет сама Windows своим документированным интерфейсом, тем же, которым
/// пользуется её собственная «Очистка диска».
/// </para>
/// </remarks>
/// <remarks>
/// DllImport rather than LibraryImport, same trade as everywhere else in this
/// solution: the generator emits unsafe code and demands AllowUnsafeBlocks
/// across the whole project. DefaultDllImportSearchPaths with System32 is
/// mandatory (CA5392): without it the loader may pick up an ole32 or advapi32
/// sitting next to the executable, which on a tool that deletes files is a
/// hijack, not a warning.
/// </remarks>
public static class VolumeCachePurger
{
    private const uint ClsctxServer = 0x5; // INPROC_SERVER | LOCAL_SERVER
    private const int SFalse = 1;
    private const int KeyRead = 0x20019;

    private static readonly Guid IidEvc = new("8FCE5227-04DA-11d1-A004-00805F8ABE06");
    private static readonly Guid IidEvc2 = new("02B7E3BA-4DB3-11D2-B2D9-00C04F8EEC8C");
    private static readonly nint HkeyLocalMachine = unchecked((nint)(int)0x80000002);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(
        in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegOpenKeyEx(
        nint key, string subKey, int options, int rights, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegCloseKey(nint key);

    /// <summary>
    /// Hands one handler to Windows to empty. Both gates are checked here and
    /// nowhere else, in this order and on purpose.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The handler is blacklisted, or the fuse is not armed. Both are refusals
    /// to start, not failures of the run, so they are loud rather than a
    /// <see cref="PurgeResult"/> a caller could log and walk past.
    /// </exception>
    public static PurgeResult Purge(VolumeCacheEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Чёрный список это ПЕРВОЕ, что происходит в методе, до открытия ключа и
        // до создания объекта. Он и есть настоящие ворота: имя обработчика может
        // прийти откуда угодно, включая чужой код, а «Загрузки» это файлы
        // человека при любом стечении обстоятельств.
        if (VolumeCacheCatalog.Blacklist.Contains(entry.KeyName.Trim()))
        {
            throw new InvalidOperationException(
                $"обработчик '{entry.KeyName.Trim()}' в чёрном списке и не запускается никогда: " +
                $"это файлы человека, а не мусор системы");
        }

        var subKey = $@"{VolumeCacheCatalog.RegistryPath}\{entry.KeyName}";

        if (RegOpenKeyEx(HkeyLocalMachine, subKey, 0, KeyRead, out var hkey) != 0)
        {
            return new PurgeResult(entry.KeyName, PurgeOutcome.Failed, "ключ обработчика не открывается");
        }

        try
        {
            return Run(entry, hkey, ct);
        }
        catch (COMException ex)
        {
            // A handler failing is expected. Taking the whole cleanup down with
            // it is not: the next handler may still have gigabytes to give.
            return new PurgeResult(entry.KeyName, PurgeOutcome.Failed, $"обработчик отказал: {ex.Message}");
        }
        finally
        {
            // The close code is discarded through a discard rather than a
            // suppression, same as in the probe: a failed close of a read-only
            // handle during unwinding is not actionable, and throwing out of a
            // finally would replace a real purge result with a useless
            // exception.
            _ = RegCloseKey(hkey);
        }
    }

    private static PurgeResult Run(VolumeCacheEntry entry, nint hkey, CancellationToken ct)
    {
        var hr = CoCreateInstance(entry.Clsid, nint.Zero, ClsctxServer, IidEvc2, out var ptr);
        var hasEvc2 = hr == 0;

        if (!hasEvc2)
        {
            hr = CoCreateInstance(entry.Clsid, nint.Zero, ClsctxServer, IidEvc, out ptr);
        }

        if (hr != 0)
        {
            return new PurgeResult(entry.KeyName, PurgeOutcome.Failed, $"объект не создаётся: 0x{hr:X8}");
        }

        try
        {
            // Never null. Same reason as in the probe: at least one handler
            // dereferences the callback without checking, and the resulting
            // AccessViolationException is not catchable.
            var callback = new VolumeCacheProgress(ct, null);
            var flags = 0;
            int hrInit;

            if (hasEvc2)
            {
                var handler = (IEmptyVolumeCache2)Marshal.GetTypedObjectForIUnknown(
                    ptr, typeof(IEmptyVolumeCache2));
                hrInit = handler.InitializeEx(
                    hkey, VolumeCacheCatalog.SystemVolume, entry.KeyName,
                    out _, out _, out _, ref flags);

                return hrInit == 0
                    ? Finish(entry, handler.Purge(ulong.MaxValue, callback))
                    : Refused(entry, hrInit);
            }

            var legacy = (IEmptyVolumeCache)Marshal.GetTypedObjectForIUnknown(
                ptr, typeof(IEmptyVolumeCache));
            hrInit = legacy.Initialize(
                hkey, VolumeCacheCatalog.SystemVolume, out _, out _, ref flags);

            return hrInit == 0
                ? Finish(entry, legacy.Purge(ulong.MaxValue, callback))
                : Refused(entry, hrInit);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    private static PurgeResult Finish(VolumeCacheEntry entry, int hrPurge) =>
        hrPurge >= 0
            ? new PurgeResult(entry.KeyName, PurgeOutcome.Purged, null)
            : new PurgeResult(entry.KeyName, PurgeOutcome.Failed, $"очистка не удалась: 0x{hrPurge:X8}");

    /// <summary>
    /// S_FALSE from Initialize means "nothing to delete here" and is the normal
    /// answer from seven handlers on a live machine. It is a refusal to run, not
    /// a failure, and reporting it as a failure would fill the report with noise
    /// on every clean machine.
    /// </summary>
    private static PurgeResult Refused(VolumeCacheEntry entry, int hrInit) =>
        hrInit == SFalse
            ? new PurgeResult(entry.KeyName, PurgeOutcome.Refused, "удалять нечего")
            : new PurgeResult(
                entry.KeyName, PurgeOutcome.Failed, $"инициализация не удалась: 0x{hrInit:X8}");
}
