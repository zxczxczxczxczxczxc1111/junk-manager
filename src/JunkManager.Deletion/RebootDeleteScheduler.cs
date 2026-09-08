using System.Runtime.InteropServices;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Deletion;

/// <summary>
/// Schedules a path to be removed by Windows during the next boot, before
/// anything has it open.
/// </summary>
/// <remarks>
/// This is the honest answer to a file that is locked by something that cannot
/// be closed: an antivirus filter, a running service, a driver. The alternative
/// on offer elsewhere is to kill the holder, which trades a full disk for a
/// corrupted one.
/// </remarks>
public static class RebootDeleteScheduler
{
    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    private const string SessionManagerKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private const string PendingValue = "PendingFileRenameOperations";

    /// <summary>
    /// Whether this process runs with administrator rights.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="Elevation.IsElevated"/> rather than asking again.
    /// Two copies of this check drift, and the day they disagree is the day one
    /// component refuses work that another one has already started.
    /// </remarks>
    public static bool IsElevated => Elevation.IsElevated;

    /// <summary>
    /// Asks Windows to delete the path on the next boot.
    /// </summary>
    /// <returns>
    /// False with a filled <paramref name="reason"/> when it could not be
    /// scheduled. Elevation is checked before the call rather than after: the
    /// Win32 error for "you are not an administrator" here is a bare access
    /// denial, which reads to a person as "the file is protected".
    /// </returns>
    public static bool TryScheduleOnReboot(VerifiedPath path, out string? reason)
    {
        reason = Otkaz(IsElevated, path.Value);
        if (reason is not null)
        {
            return false;
        }

        if (!SafetyGuard.TryVerifyForDeletion(path.Value, out var svezhiy, out var prichina))
        {
            reason = prichina;
            return false;
        }

        if (!MoveFileEx(svezhiy.Value, null, MOVEFILE_DELAY_UNTIL_REBOOT))
        {
            var kod = Marshal.GetLastWin32Error();
            reason = $"MoveFileEx отказал, код {kod}";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// The refusal, worked out without touching the system, so both answers are
    /// reachable from a test. Null means there is no reason to refuse.
    /// </summary>
    internal static string? Otkaz(bool elevated, string put)
    {
        if (string.IsNullOrWhiteSpace(put))
        {
            return "пустой пропуск: планировать нечего";
        }

        if (!elevated)
        {
            return "отложенное удаление требует прав администратора: "
                + "запись делается в общесистемную ветку реестра, "
                + "и без повышения прав она недоступна";
        }

        return null;
    }

    /// <summary>
    /// What is already queued for the next boot, as Windows stores it. Read-only
    /// on purpose: the value belongs to the system and routinely holds entries
    /// put there by Windows Update, and rewriting it to remove one line is how
    /// somebody else's pending update gets lost.
    /// </summary>
    public static IReadOnlyList<string> Scheduled()
    {
        using var vetka = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SessionManagerKey, writable: false);
        if (vetka?.GetValue(PendingValue) is not string[] znacheniya)
        {
            return [];
        }

        // Значения идут парами: источник, назначение. Пустое назначение это
        // удаление, непустое это переименование. Нас интересуют источники.
        var itog = new List<string>();
        for (var i = 0; i < znacheniya.Length; i += 2)
        {
            var istochnik = znacheniya[i];
            if (string.IsNullOrEmpty(istochnik))
            {
                continue;
            }

            itog.Add(OchistitZapis(istochnik));
        }

        return itog;
    }

    /// <summary>
    /// Turns one raw queue entry into a plain path.
    /// </summary>
    /// <remarks>
    /// The NT namespace prefix is NOT always at position zero, and assuming it
    /// was cost a red test in the polygon. Windows 11 build 26200 writes the
    /// source as <c>*1\??\C:\...</c>; the hex confirms it, the leading bytes
    /// being 2A 00 31 00 before the prefix. What the marker means is not
    /// documented anywhere reachable from here, so it is not interpreted: the
    /// prefix is located rather than assumed, and everything after it is the
    /// path. An entry with no prefix at all is returned untouched.
    /// </remarks>
    internal static string OchistitZapis(string istochnik)
    {
        const string PrefiksNt = @"\??\";

        var indeks = istochnik.IndexOf(PrefiksNt, StringComparison.Ordinal);
        return indeks >= 0 ? istochnik[(indeks + PrefiksNt.Length)..] : istochnik;
    }

    /// <remarks>
    /// DllImport rather than LibraryImport: the generator wants AllowUnsafeBlocks
    /// for the whole project, same as in PathResolver and LockedFileInspector.
    /// </remarks>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string lpExistingFileName, string? lpNewFileName, uint dwFlags);
}
