using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace JunkManager.Safety;

public static partial class RegistryGuard
{
    private const string SharedDlls = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
    private static readonly HashSet<string> ProtectedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLSID", "Interface", "TypeLib", "Installer", "Applications", "SystemFileAssociations",
        "Directory", "Drive", "Folder", "AllFilesystemObjects", "LibraryFolder", "AppX",
    };

    /// <summary>Read-only roots. They never grant permission to remove the container.</summary>
    public static bool TryVerifyScanBranch(RegistryHive hive, string subKey, RegistryView view,
        out string normalized, [NotNullWhen(false)] out string? reason)
    {
        if (TryVerifyBranch(hive, subKey, view, out normalized, out reason)) return true;
        if (!Enum.IsDefined(view) || string.IsNullOrWhiteSpace(subKey) || subKey.Any(char.IsControl)) return false;
        var path = Normalize(subKey);
        if (hive == RegistryHive.LocalMachine && path.Equals(SharedDlls, StringComparison.OrdinalIgnoreCase)
            || hive == RegistryHive.CurrentUser && (path.Equals(@"Software\Classes", StringComparison.OrdinalIgnoreCase)
                || path.Equals(@"Software\Classes\CLSID", StringComparison.OrdinalIgnoreCase)))
        {
            normalized = path;
            reason = null;
            return true;
        }
        return false;
    }

    private static bool IsCautiousValue(RegistryHive hive, string? subKey, string? name, RegistryView view)
    {
        if (!Enum.IsDefined(view) || string.IsNullOrWhiteSpace(subKey) || name is null
            || subKey.Any(char.IsControl) || name.Any(char.IsControl)) return false;
        var path = Normalize(subKey);
        if (hive == RegistryHive.LocalMachine && path.Equals(SharedDlls, StringComparison.OrdinalIgnoreCase))
            return name.Length > 3 && char.IsAsciiLetter(name[0]) && name[1] == ':' && name[2] == '\\'
                && Path.IsPathFullyQualified(name) && Path.GetExtension(name).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && !name.Split('\\').Any(part => part is "." or "..");
        if (hive != RegistryHive.CurrentUser || name.Length != 0) return false;
        var parts = path.Split('\\');
        if (parts.Length < 5 || !parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("Classes", StringComparison.OrdinalIgnoreCase)) return false;
        if (parts.Length == 5 && parts[2].Equals("CLSID", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(parts[3], "B", out _))
            return parts[4].Equals("InprocServer32", StringComparison.OrdinalIgnoreCase)
                || parts[4].Equals("LocalServer32", StringComparison.OrdinalIgnoreCase);
        // Only one application's open command. Shell-wide registrations stay out of this bargain.
        return parts.Length == 6 && parts[2].Contains('.', StringComparison.Ordinal)
            && !parts[2].StartsWith('.') && !parts[2].Any(character => character is '*' or '?' or '/' or ':')
            && !ProtectedClasses.Contains(parts[2]) && !parts[2].StartsWith("AppX", StringComparison.OrdinalIgnoreCase)
            && parts[3].Equals("shell", StringComparison.OrdinalIgnoreCase)
            && parts[4].Equals("open", StringComparison.OrdinalIgnoreCase)
            && parts[5].Equals("command", StringComparison.OrdinalIgnoreCase);
    }
}
