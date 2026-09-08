using Microsoft.Win32;

namespace JunkManager.Safety;

/// <summary>
/// One spelling of a registry address for the whole solution. The journal, the
/// refusal reason and the backup file name all name the same entry the same
/// way, so a person comparing two of them is comparing one string and not two
/// dialects.
/// </summary>
public static class RegistryAddress
{
    /// <summary>Short hive name, the one a person types into regedit.</summary>
    public static string ShortHive(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.Users => "HKU",
        RegistryHive.CurrentConfig => "HKCC",
        RegistryHive.PerformanceData => "HKPD",
        _ => "HK?",
    };

    /// <summary>
    /// Full hive name, the only form reg.exe accepts on its command line.
    /// Throws for a hive reg.exe cannot export: building an export command for
    /// it would produce a backup file that silently is not one.
    /// </summary>
    public static string RegExeHive(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.Users => "HKEY_USERS",
        RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
        _ => throw new ArgumentOutOfRangeException(
            nameof(hive), hive, "этот улей reg.exe не экспортирует"),
    };

    /// <summary>
    /// The address of a key, or of one value inside it. Square brackets around
    /// the value name are the same notation .reg files use, so a person reading
    /// a refusal and a person reading a backup see the same shape.
    /// </summary>
    public static string Format(RegistryHive hive, string? subKey, string? valueName)
    {
        var vetka = $@"{ShortHive(hive)}\{subKey ?? string.Empty}";

        return string.IsNullOrEmpty(valueName) ? vetka : $"{vetka} [{valueName}]";
    }
}
