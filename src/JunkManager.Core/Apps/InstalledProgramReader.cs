using System.Globalization;
using Microsoft.Win32;
using JunkManager.Safety;

namespace JunkManager.Core.Apps;

/// <summary>
/// Reads the three Uninstall branches and turns them into a list a person can
/// read. Everything that decides anything is a pure function over
/// <see cref="RawUninstallEntry"/>; the registry is touched in exactly one
/// method, so the rules are testable and the reading is boring.
/// </summary>
public static class InstalledProgramReader
{
    private const string Vetka = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    // Windows hides these itself. Showing them would offer to remove a security
    // update through a string that does not remove security updates.
    private static readonly string[] SkrytyeVidy = ["Security Update", "Update", "Hotfix"];

    private static readonly char[] Razdeliteli = ['\\', '/'];

    /// <summary>Every row from all three branches, hidden ones included.</summary>
    public static IReadOnlyList<RawUninstallEntry> ReadRaw() => ReadRawDetailed().Entries;

    public static (IReadOnlyList<RawUninstallEntry> Entries, IReadOnlyList<SkippedItem> Skipped) ReadRawDetailed()
    {
        var itog = new List<RawUninstallEntry>();
        var skipped = new List<SkippedItem>();
        if (Environment.Is64BitOperatingSystem)
        {
            Sobrat(RegistryHive.LocalMachine, RegistryView.Registry64, Vetka, ProgramScope.Machine64, itog, skipped);
        }

        Sobrat(RegistryHive.LocalMachine, RegistryView.Registry32, Vetka, ProgramScope.Machine32, itog, skipped);
        if (ProgramInventory.CanReadCurrentUser(out var reason))
        {
            Sobrat(RegistryHive.CurrentUser, RegistryView.Registry64, Vetka, ProgramScope.User, itog, skipped);
            // Shared HKCU still has a legacy WOW child, because Windows enjoys archaeology.
            Sobrat(RegistryHive.CurrentUser, RegistryView.Registry32,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", ProgramScope.User32, itog, skipped);
        }
        else { skipped.Add(new("HKCU Uninstall", reason)); }

        return (itog, skipped);
    }

    public static ProgramInventorySnapshot ReadDetailed()
    {
        var raw = ReadRawDetailed();
        return new(Svesti(raw.Entries), raw.Skipped)
        {
            OwnershipPrograms = [.. raw.Entries.SelectMany(entry => Svesti([entry with
            {
                DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.KeyName : entry.DisplayName,
                SystemComponent = 0, ParentKeyName = null, ReleaseType = null,
            }]))],
            IsComplete = raw.Skipped.Count == 0,
        };
    }

    /// <summary>The list as the product shows it.</summary>
    public static IReadOnlyList<InstalledProgram> Read() => Svesti(ReadRaw());

    /// <summary>
    /// Hidden rows, by the same rules Windows uses in its own list. A row with
    /// no display name has nothing to show; SystemComponent and ParentKeyName
    /// mark a piece of something bigger; a release type marks an update.
    /// </summary>
    public static bool Skryta(RawUninstallEntry zapis)
    {
        ArgumentNullException.ThrowIfNull(zapis);

        if (string.IsNullOrWhiteSpace(zapis.DisplayName))
        {
            return true;
        }

        if (zapis.SystemComponent == 1)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(zapis.ParentKeyName))
        {
            return true;
        }

        return zapis.ReleaseType is { } vid
            && SkrytyeVidy.Contains(vid.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Which installer put this here. The order matters: the WindowsInstaller
    /// value is authoritative and outranks the file name, because an MSI package
    /// is perfectly free to name its stub Uninstall.exe.
    /// </summary>
    public static InstallerKind Opredelit(RawUninstallEntry zapis)
    {
        ArgumentNullException.ThrowIfNull(zapis);

        if (string.Equals(zapis.WindowsInstaller?.Trim(), "1", StringComparison.Ordinal))
        {
            return InstallerKind.Msi;
        }

        var stroka = zapis.UninstallString ?? string.Empty;

        if (ImyaFayla(stroka).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.Msi;
        }

        if (stroka.Contains("--uninstall", StringComparison.OrdinalIgnoreCase)
            && stroka.Contains("Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.Squirrel;
        }

        // "Inno Setup: App Path" is written by Inno itself and by nobody else,
        // so it is checked before the file name. unins000.exe is the default
        // name and only the default: a repackaged installer renames it.
        if (!string.IsNullOrWhiteSpace(zapis.InnoAppPath))
        {
            return InstallerKind.InnoSetup;
        }

        var imya = ImyaFayla(stroka);

        // Inno names its stub unins000.exe, digits and all, and the digits are
        // what separates it from NSIS. Checking the bare "unins" prefix instead
        // swallows Uninstall.exe and uninst.exe, which are NSIS conventions, and
        // makes the NSIS branch below unreachable: caught by the table test on
        // 05.09.2026, where Uninstall.exe came back as InnoSetup.
        if (imya.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            && imya.Length > 5
            && char.IsAsciiDigit(imya[5]))
        {
            return InstallerKind.InnoSetup;
        }

        // NSIS leaves no registry marker at all, so this last one IS a guess and
        // is written down as such. The cost of guessing wrong is bounded: /S on
        // something that is not NSIS produces a visible window, and the runner
        // refuses to leave a visible window unattended.
        if (imya.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
            || imya.StartsWith("uninst", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.Nsis;
        }

        return InstallerKind.Unknown;
    }

    /// <summary>
    /// InstallDate, or null. Three formats live on a real machine and one of
    /// them is not a date at all: Discord writes 20253330. The range check is
    /// what separates "parsed" from "believed".
    /// </summary>
    public static DateOnly? RazobratDatu(string? raw, DateOnly segodnya)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ochishcheno = raw.Trim();

        string[] formaty = ["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd"];

        if (!DateOnly.TryParseExact(
                ochishcheno, formaty, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var data))
        {
            return null;
        }

        // Windows 95 shipped in 1995 and nothing on this machine predates it.
        // A date in the future is a broken value by definition.
        if (data.Year < 1995 || data > segodnya)
        {
            return null;
        }

        return data;
    }

    /// <summary>
    /// The list, deduplicated.
    /// </summary>
    /// <remarks>
    /// The collapsing rule, stated once so it cannot drift: rows collapse on the
    /// triple (DisplayName, DisplayVersion, Publisher) and ONLY within the
    /// machine branches. HKCU never collapses into HKLM, because a per-user
    /// install and a machine install of the same version are two installs owned
    /// by two different people, with two different uninstall strings, and
    /// merging them hides one of them from whoever owns it. Version is part of
    /// the key so that two versions installed side by side stay two rows.
    /// The survivor is the row that has something to uninstall with; on a tie
    /// the wider scope wins, because that is the row Windows itself shows.
    /// </remarks>
    public static IReadOnlyList<InstalledProgram> Svesti(IReadOnlyList<RawUninstallEntry> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var luchshie = new Dictionary<string, RawUninstallEntry>(StringComparer.OrdinalIgnoreCase);
        var poryadok = new List<string>();
        var registrations = new Dictionary<string, List<ProgramRegistration>>(StringComparer.OrdinalIgnoreCase);

        foreach (var zapis in raw)
        {
            if (Skryta(zapis))
            {
                continue;
            }

            var klyuch = Klyuch(zapis);
            if (!registrations.TryGetValue(klyuch, out var addresses))
            {
                addresses = [];
                registrations[klyuch] = addresses;
            }

            addresses.Add(new(zapis.Scope, zapis.KeyName));

            if (!luchshie.TryGetValue(klyuch, out var uzhe))
            {
                luchshie[klyuch] = zapis;
                poryadok.Add(klyuch);
                continue;
            }

            if (Silnee(zapis, uzhe))
            {
                luchshie[klyuch] = zapis;
            }
        }

        var itog = new List<InstalledProgram>(poryadok.Count);
        var segodnya = DateOnly.FromDateTime(DateTime.Now);

        foreach (var klyuch in poryadok)
        {
            var zapis = luchshie[klyuch];

            itog.Add(new InstalledProgram(
                Id: $"{zapis.Scope}:{zapis.KeyName}",
                DisplayName: zapis.DisplayName!.Trim(),
                Publisher: zapis.Publisher?.Trim() ?? string.Empty,
                Version: zapis.DisplayVersion?.Trim() ?? string.Empty,
                InstallLocation: Pusto(zapis.InstallLocation) ?? Pusto(zapis.InnoAppPath),
                UninstallString: Pusto(zapis.UninstallString),
                QuietUninstallString: Pusto(zapis.QuietUninstallString),
                Installer: Opredelit(zapis),
                Scope: zapis.Scope,
                InstalledOn: RazobratDatu(zapis.InstallDate, segodnya))
            {
                Registrations = registrations[klyuch],
                UserSid = zapis.Scope is ProgramScope.User or ProgramScope.User32 ? Elevation.CurrentSid : null,
                ExecutablePath = ReadExecutable(zapis),
                EstimatedSizeBytes = zapis.EstimatedSizeBytes,
            });
        }

        return itog;
    }

    private static string Klyuch(RawUninstallEntry zapis)
    {
        // HKCU keeps its own namespace. This single prefix is the whole "never
        // collapse a per-user install into a machine one" rule.
        var oblast = zapis.Scope is ProgramScope.User or ProgramScope.User32 ? "user" : "machine";

        return string.Join('\u001f',
            oblast,
            zapis.DisplayName?.Trim() ?? string.Empty,
            zapis.DisplayVersion?.Trim() ?? string.Empty,
            zapis.Publisher?.Trim() ?? string.Empty,
            zapis.InstallLocation?.Trim().TrimEnd('\\') ?? string.Empty,
            Guid.TryParse(zapis.KeyName, out var product) ? product.ToString("D") :
                (zapis.UninstallString ?? zapis.QuietUninstallString ?? zapis.KeyName).Trim());
    }

    private static bool Silnee(RawUninstallEntry kandidat, RawUninstallEntry tekushchiy)
    {
        var uKandidata = !string.IsNullOrWhiteSpace(kandidat.UninstallString);
        var uTekushchego = !string.IsNullOrWhiteSpace(tekushchiy.UninstallString);

        if (uKandidata != uTekushchego)
        {
            return uKandidata;
        }

        return kandidat.Scope < tekushchiy.Scope;
    }

    private static string? Pusto(string? znachenie) =>
        string.IsNullOrWhiteSpace(znachenie) ? null : znachenie.Trim();

    private static string ImyaFayla(string uninstallString)
    {
        var stroka = uninstallString.Trim().Trim('"');

        if (stroka.Length == 0)
        {
            return string.Empty;
        }

        var konec = stroka.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        var putChasti = konec >= 0 ? stroka[..(konec + 4)] : stroka;
        var razdelitel = putChasti.LastIndexOfAny(Razdeliteli);

        return razdelitel >= 0 ? putChasti[(razdelitel + 1)..] : putChasti;
    }

    private static void Sobrat(
        RegistryHive uley, RegistryView vid, string put, ProgramScope oblast,
        List<RawUninstallEntry> itog, List<SkippedItem> skipped)
    {
        using var koren = RegistryKey.OpenBaseKey(uley, vid);

        RegistryKey? vetka;
        try
        {
            vetka = koren.OpenSubKey(put, writable: false);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A branch we may not read is a branch we do not know about. It is
            // not an empty branch, and the difference reaches the person as
            // "не проверялось" rather than "ничего не найдено".
            skipped.Add(new($"{uley} {vid}\\{put}", ex.Message));
            return;
        }

        if (vetka is null)
        {
            return;
        }

        using (vetka)
        {
            string[] names;
            try { names = vetka.GetSubKeyNames(); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                skipped.Add(new($"{uley} {vid}\\{put}", ex.Message));
                return;
            }

            foreach (var imya in names)
            {
                RegistryKey? zapis;
                try
                {
                    zapis = vetka.OpenSubKey(imya, writable: false);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    skipped.Add(new($"{uley} {vid}\\{put}\\{imya}", ex.Message));
                    continue;
                }

                if (zapis is null)
                {
                    continue;
                }

                using (zapis)
                {
                    try
                    {
                    itog.Add(new RawUninstallEntry(
                        KeyName: imya,
                        Scope: oblast,
                        DisplayName: Stroka(zapis, "DisplayName"),
                        DisplayVersion: Stroka(zapis, "DisplayVersion"),
                        Publisher: Stroka(zapis, "Publisher"),
                        InstallLocation: Stroka(zapis, "InstallLocation"),
                        UninstallString: Stroka(zapis, "UninstallString"),
                        QuietUninstallString: Stroka(zapis, "QuietUninstallString"),
                        InstallDate: Stroka(zapis, "InstallDate"),
                        SystemComponent: Chislo(zapis, "SystemComponent"),
                        ParentKeyName: Stroka(zapis, "ParentKeyName"),
                        ReleaseType: Stroka(zapis, "ReleaseType"),
                        WindowsInstaller: Stroka(zapis, "WindowsInstaller"),
                        InnoAppPath: Stroka(zapis, "Inno Setup: App Path"))
                    {
                        DisplayIcon = Stroka(zapis, "DisplayIcon"),
                        EstimatedSizeBytes = Chislo(zapis, "EstimatedSize") > 0 ? (long)Chislo(zapis, "EstimatedSize") * 1024 : null,
                    });
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                    {
                        skipped.Add(new($"{uley} {vid}\\{put}\\{imya}", ex.Message));
                    }
                }
            }
        }
    }

    public static ProgramPresenceResult Probe(InstalledProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var registrations = program.Registrations;
        if (registrations.Count == 0)
        {
            var colon = program.Id.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || colon == program.Id.Length - 1)
            {
                return new(ProgramPresence.Unknown, "у программы нет точного адреса регистрации");
            }

            registrations = [new(program.Scope, program.Id[(colon + 1)..])];
        }

        string? failure = null;
        foreach (var registration in registrations)
        {
            var user = registration.Scope is ProgramScope.User or ProgramScope.User32;
            if (user && (!ProgramInventory.CanReadCurrentUser(out failure)
                    || (program.UserSid is not null && !string.Equals(program.UserSid, Elevation.CurrentSid, StringComparison.Ordinal))))
            {
                return new(ProgramPresence.Unknown, failure ?? "пользователь регистрации изменился");
            }

            if (registration.KeyName.Contains('\\', StringComparison.Ordinal)
                || registration.Scope == ProgramScope.Msix)
            {
                return new(ProgramPresence.Unknown, "неверный адрес регистрации");
            }

            try
            {
                using var root = RegistryKey.OpenBaseKey(user ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                    registration.Scope is ProgramScope.Machine32 or ProgramScope.User32 ? RegistryView.Registry32 : RegistryView.Registry64);
                var branch = registration.Scope == ProgramScope.User32
                    ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" : Vetka;
                using var key = root.OpenSubKey(branch + "\\" + registration.KeyName);
                if (key is not null) { return new(ProgramPresence.Present); }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                failure = ex.Message;
            }
        }

        return failure is null ? new(ProgramPresence.Absent) : new(ProgramPresence.Unknown, failure);
    }

    private static string? ReadExecutable(RawUninstallEntry entry)
    {
        var icon = entry.DisplayIcon?.Trim();
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var comma = icon.LastIndexOf(',');
            if (comma >= 0 && int.TryParse(icon[(comma + 1)..], CultureInfo.InvariantCulture, out _)) { icon = icon[..comma]; }
            icon = Environment.ExpandEnvironmentVariables(icon.Trim().Trim('"'));
            if (Path.IsPathFullyQualified(icon) && Path.GetExtension(icon).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            { return icon; }
        }
        return Registry.RegistryTargetExtractor.TryExtract(entry.UninstallString, out var executable, out _)
            && Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? executable : null;
    }

    private static string? Stroka(RegistryKey klyuch, string imya) =>
        klyuch.GetValue(imya) switch
        {
            string s => s,
            // A REG_DWORD in a string field happens: WindowsInstaller is written
            // both ways by different installers.
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    private static int Chislo(RegistryKey klyuch, string imya) =>
        klyuch.GetValue(imya) switch
        {
            int i => i,
            string s when int.TryParse(s, CultureInfo.InvariantCulture, out var razobrano) => razobrano,
            _ => 0,
        };
}
