using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using Microsoft.Win32;

namespace JunkManager.Safety;

[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification = "Ноль оставлен неопределённым: незаполненный исход повышения не должен " +
        "читаться как AlreadyElevated, иначе продукт начнёт работу, считая, что права у него есть.")]
public enum ElevationOutcome
{
    /// <summary>Rights were already there. Nothing was restarted.</summary>
    AlreadyElevated = 1,

    /// <summary>A second, elevated process was started. This one exits.</summary>
    Relaunched = 2,

    /// <summary>The person said no in the UAC dialog. An ordinary outcome.</summary>
    Declined = 3,

    /// <summary>Could not even ask. Reason is filled.</summary>
    Failed = 4,

    /// <summary>Nobody asked for elevation, so nothing was attempted.</summary>
    NotRequested = 5,
}

/// <summary>
/// Everything the product might want to do that may need administrator rights.
/// </summary>
/// <remarks>
/// A list rather than a boolean, because the interface has to say "не
/// проверялось" for the specific things it could not check. "Ничего не найдено"
/// in place of "нечем было посмотреть" is the single most misleading sentence a
/// disk cleaner can print.
/// </remarks>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification = "Ноль оставлен неопределённым намеренно: неназванная возможность не должна " +
        "получать ответ «прав не требует» по умолчанию.")]
public enum ElevatedCapability
{
    ReadPrefetch = 1,
    DismComponentStore = 2,
    DeleteDriverPackage = 3,
    ReadWindowsApps = 4,
    DeleteMachineRegistryValue = 5,
    ScheduleRebootDelete = 6,
    UninstallMachineProgram = 7,
    CreateRestorePoint = 8,
    CleanWindowsTemp = 9,
    ReadUninstallBranches = 10,
    ReadMsixPackages = 11,
    EnumerateDrivers = 12,
    ScanUserProfile = 13,
    RecycleBin = 14,
    VacuumUserDatabase = 15,
    UninstallUserProgram = 16,
}

/// <summary>
/// Elevation at startup, before the window and before any scanning.
/// </summary>
/// <remarks>
/// <para>
/// The manifest stays asInvoker and requireAdministrator is never written into
/// it. That is not stubbornness. If the signed-in person is not an administrator
/// and elevation happens under a different account, %LOCALAPPDATA%, %APPDATA%,
/// %TEMP% and HKCU start pointing at the administrator's profile. Every rule in
/// this product is written through those variables, so the product would clean a
/// profile belonging to somebody who is not sitting at the machine, and nothing
/// would look wrong while it did.
/// </para>
/// <para>
/// So the original SID and profile are handed to the elevated process on the
/// command line and verified there against ProfileList in HKLM, which is
/// readable without rights and writable only with them. Until that check passes,
/// scanning does not start.
/// </para>
/// </remarks>
public static class Elevation
{
    /// <summary>Documented Win32 code for "the person said no in the UAC dialog".</summary>
    private const int OtkazPolzovatelya = 1223;

    private const string ProfileList =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public const string ArgumentSid = "--ishodnyy-sid";

    public const string ArgumentProfile = "--ishodnyy-profil";

    /// <summary>
    /// Whether this process runs with administrator rights. The single answer
    /// for the whole solution: RebootDeleteScheduler delegates here rather than
    /// keeping its own copy.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            using var lichnost = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(lichnost).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>SID of the account this process runs as.</summary>
    public static string CurrentSid
    {
        get
        {
            using var lichnost = WindowsIdentity.GetCurrent();
            return lichnost.User?.Value ?? string.Empty;
        }
    }

    /// <summary>
    /// What to do, decided without touching anything, so every branch is
    /// reachable from a test on a machine that is not elevated.
    /// </summary>
    public static ElevationOutcome Reshenie(bool wanted, bool elevated) => (wanted, elevated) switch
    {
        (_, true) => ElevationOutcome.AlreadyElevated,
        (false, false) => ElevationOutcome.NotRequested,
        (true, false) => ElevationOutcome.Relaunched,
    };

    /// <summary>
    /// Restarts this process elevated, once, at startup.
    /// </summary>
    /// <returns>
    /// True for every ordinary outcome, including a refusal in the UAC dialog.
    /// False only when we could not even ask. Refusing rights is a choice, not a
    /// fault, and turning it into a failure trains people to click yes.
    /// </returns>
    public static bool TryElevate(
        bool wanted, IReadOnlyList<string> args, out ElevationOutcome outcome, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(args);

        outcome = Reshenie(wanted, IsElevated);

        if (outcome != ElevationOutcome.Relaunched)
        {
            reason = null;
            return true;
        }

        var ispolnyaemyy = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(ispolnyaemyy))
        {
            outcome = ElevationOutcome.Failed;
            reason = "путь к собственному исполняемому файлу неизвестен, перезапуск невозможен";
            return false;
        }

        var psi = new ProcessStartInfo(ispolnyaemyy)
        {
            // UseShellExecute is required for the runas verb. It is the only
            // place in this solution that sets it.
            UseShellExecute = true,
            Verb = "runas",
        };

        foreach (var argument in args)
        {
            // The handed identity is rebuilt below, so an argument pair that came
            // in from outside never propagates unchecked.
            if (argument.Equals(ArgumentSid, StringComparison.Ordinal)
                || argument.Equals(ArgumentProfile, StringComparison.Ordinal))
            {
                continue;
            }

            psi.ArgumentList.Add(argument);
        }

        psi.ArgumentList.Add(ArgumentSid);
        psi.ArgumentList.Add(CurrentSid);
        psi.ArgumentList.Add(ArgumentProfile);
        psi.ArgumentList.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        try
        {
            using var process = Process.Start(psi);

            if (process is null)
            {
                outcome = ElevationOutcome.Failed;
                reason = "повышенный процесс не запустился и не сказал почему";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Win32Exception ex)
        {
            outcome = PoKoduOshibki(ex.NativeErrorCode);

            if (outcome == ElevationOutcome.Declined)
            {
                reason = "отказ в запросе прав администратора: работаем с тем, что доступно";
                return true;
            }

            reason = $"повышение не удалось, код {ex.NativeErrorCode}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// What a failed ShellExecute means.
    /// </summary>
    /// <remarks>
    /// Split out of the catch block on purpose. The claim it carries is "saying
    /// no to UAC is an ordinary outcome, not a fault", and inside the catch that
    /// claim was untestable: reaching it needs a real consent dialog, so a
    /// mutation swapping Declined for Failed survived the whole suite. Here it
    /// is a pure function of an error code, and the claim has a test.
    /// </remarks>
    internal static ElevationOutcome PoKoduOshibki(int nativeErrorCode) =>
        nativeErrorCode == OtkazPolzovatelya
            ? ElevationOutcome.Declined
            : ElevationOutcome.Failed;

    /// <summary>
    /// The SID and profile handed over by the process that asked for elevation.
    /// </summary>
    /// <returns>
    /// False when nothing was handed over, which is the normal first launch, and
    /// false when the arguments are malformed, which is not. The reason tells
    /// them apart.
    /// </returns>
    public static bool TryReadHandedIdentity(
        IReadOnlyList<string> args,
        out string sid,
        out string profile,
        [NotNullWhen(false)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(args);

        sid = string.Empty;
        profile = string.Empty;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(ArgumentSid, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    reason = $"аргумент {ArgumentSid} без значения";
                    return false;
                }

                sid = args[i + 1];
            }
            else if (args[i].Equals(ArgumentProfile, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    reason = $"аргумент {ArgumentProfile} без значения";
                    return false;
                }

                profile = args[i + 1];
            }
        }

        if (sid.Length == 0 && profile.Length == 0)
        {
            reason = "исходный профиль не передан: процесс запущен человеком, а не повышением";
            return false;
        }

        if (sid.Length == 0 || profile.Length == 0)
        {
            reason = "передана половина исходной личности: нужны оба аргумента сразу";
            sid = string.Empty;
            profile = string.Empty;
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Where Windows says that SID's profile lives.
    /// </summary>
    /// <remarks>
    /// This is what makes a handed SID worth trusting. ProfileList is readable
    /// without rights and writable only with them, so a forged SID names no
    /// registered profile and the check fails. A forged SID that DOES name a
    /// registered profile could only be produced by somebody already running as
    /// that person, which is the boundary this design accepts and names.
    /// </remarks>
    public static bool TryProfileFromRegistry(
        string sid, out string profilePath, [NotNullWhen(false)] out string? reason)
    {
        profilePath = string.Empty;

        if (string.IsNullOrWhiteSpace(sid))
        {
            reason = "SID пустой";
            return false;
        }

        using var koren = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        RegistryKey? zapis;
        try
        {
            zapis = koren.OpenSubKey($@"{ProfileList}\{sid}", writable: false);
        }
        catch (System.Security.SecurityException ex)
        {
            reason = $"ветка профилей не читается: {ex.Message}";
            return false;
        }

        if (zapis is null)
        {
            reason = $"профиль для {sid} не найден в реестре";
            return false;
        }

        using (zapis)
        {
            if (zapis.GetValue("ProfileImagePath") is not string put || string.IsNullOrWhiteSpace(put))
            {
                reason = $"у профиля {sid} нет ProfileImagePath";
                return false;
            }

            profilePath = Environment.ExpandEnvironmentVariables(put);
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Points this process's profile variables back at the person who asked for
    /// elevation, after verifying that the handed identity is real.
    /// </summary>
    /// <remarks>
    /// Called before anything scans. When it returns false, scanning does not
    /// start: cleaning the wrong profile is worse than cleaning nothing, and it
    /// is not recoverable.
    /// </remarks>
    public static bool TryApplyOriginalProfile(
        IReadOnlyList<string> args, [NotNullWhen(false)] out string? reason)
    {
        if (!TryReadHandedIdentity(args, out var sid, out var peredannyy, out var chtenie))
        {
            // Nothing handed over means nobody elevated us, and the ambient
            // profile is already the right one.
            reason = chtenie;
            return false;
        }

        if (!TryProfileFromRegistry(sid, out var izReestra, out var otkaz))
        {
            reason = otkaz;
            return false;
        }

        if (!SafetyGuard.IsAtOrUnder(peredannyy, izReestra)
            && !izReestra.Equals(peredannyy, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"переданный профиль {peredannyy} не совпадает с записанным в реестре {izReestra}";
            return false;
        }

        if (!Directory.Exists(izReestra))
        {
            reason = $"каталог профиля {izReestra} не существует";
            return false;
        }

        // Set on the process, not on the user: the change lives exactly as long
        // as this run, and no other process inherits a rewritten environment.
        foreach (var (imya, znachenie) in Peremennye(izReestra))
        {
            Environment.SetEnvironmentVariable(imya, znachenie);
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// The five profile variables every rule in this product is written
    /// through, built from a profile directory rather than from the current
    /// process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept separate from <see cref="TryApplyOriginalProfile"/> so the mapping
    /// has a test that does not rewrite the environment of the test run itself.
    /// </para>
    /// <para>
    /// This is also where the interface plan's ProfileEnvironment.Map ended up.
    /// A second copy of this mapping in the application project would have been
    /// a second answer to "whose profile are we cleaning", and the whole point
    /// of section 11 of the spec is that there is exactly one.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Peremennye(string profilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        // ProfileImagePath comes out of the registry and sometimes carries a
        // trailing separator. TrimEndingDirectorySeparator and not TrimEnd:
        // trimming a bare drive root leaves just the drive letter, and
        // Path.Combine then builds a RELATIVE path against the current
        // directory of that drive.
        var koren = Path.TrimEndingDirectorySeparator(profilePath);
        var mestnye = Path.Combine(koren, "AppData", "Local");
        var vremennye = Path.Combine(mestnye, "Temp");

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = koren,
            ["LOCALAPPDATA"] = mestnye,
            ["APPDATA"] = Path.Combine(koren, "AppData", "Roaming"),
            ["TEMP"] = vremennye,
            ["TMP"] = vremennye,
        };
    }

    /// <summary>
    /// Whether a capability needs administrator rights. Every value answered
    /// explicitly, and there is deliberately no default arm: a new capability
    /// must break the build rather than quietly inherit "no rights needed".
    /// </summary>
    public static bool Requires(ElevatedCapability capability) => capability switch
    {
        ElevatedCapability.ReadPrefetch => true,
        ElevatedCapability.DismComponentStore => true,
        ElevatedCapability.DeleteDriverPackage => true,
        ElevatedCapability.ReadWindowsApps => true,
        ElevatedCapability.DeleteMachineRegistryValue => true,
        ElevatedCapability.ScheduleRebootDelete => true,
        ElevatedCapability.UninstallMachineProgram => true,
        ElevatedCapability.CreateRestorePoint => true,
        ElevatedCapability.CleanWindowsTemp => true,
        ElevatedCapability.ReadUninstallBranches => false,
        ElevatedCapability.ReadMsixPackages => false,
        ElevatedCapability.EnumerateDrivers => false,
        ElevatedCapability.ScanUserProfile => false,
        ElevatedCapability.RecycleBin => false,
        ElevatedCapability.VacuumUserDatabase => false,
        ElevatedCapability.UninstallUserProgram => false,
        _ => throw new ArgumentOutOfRangeException(
            nameof(capability), capability,
            "для этой возможности не записано, требует ли она прав администратора"),
    };
}
