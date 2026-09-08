using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using JunkManager.Safety;

namespace JunkManager.Core.Scanning;

/// <summary>Shared exclusions: another detector is not a second opinion about private keys.</summary>
public static class CleanupPathPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".config", ".git",
        ".yarn", ".pnpm-store", "node_modules", "OfficeFileCache", "UnsavedFiles",
        "Service Worker", "CacheStorage", "IndexedDB", "Local Storage", "Session Storage",
        "Sessions", "SessionStore", "sessionstore-backups", "storage", "bookmarks",
        "Bookmarks.bak", "Login Data", "Login Data For Account", "Cookies", "Web Data",
        "History", "Preferences", "Secure Preferences", "Local State", "logins.json",
        "key3.db", "key4.db", "cert9.db", "places.sqlite", "favicons.sqlite", "cookies.sqlite",
        "prefs.js", "user.js", "sessionstore.jsonlz4", "recovery.jsonlz4",
        "Documents", "Downloads", "Desktop", "Pictures", "Videos", "Music", "Saved Games",
    };

    public static bool IsProtected(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(ProtectedNames.Contains);
    }

    /// <summary>Rechecks every existing component. This narrows races; it does not pin directory handles.</summary>
    public static bool TryVerify(string raw, out VerifiedPath verified, [NotNullWhen(false)] out string? reason)
    {
        if (!SafetyGuard.TryVerify(raw, out verified, out reason)) return false;
        if (IsProtected(verified.Value))
        {
            reason = "защищённые пользовательские данные или состояние приложения";
            verified = default;
            return false;
        }

        var current = verified.Value;
        try
        {
            while (current is not null)
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reason = $"ссылка или reparse point в пути: {current}";
                    verified = default;
                    return false;
                }
                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"путь не читается: {ex.Message}";
            verified = default;
            return false;
        }

        reason = null;
        return true;
    }
}

public static class CleanupProcessGuard
{
    public static string? Refusal(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 0) return $"сначала закройте {name}: приложение может менять эти данные";
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return $"не удалось проверить процесс {name}: {ex.Message}";
            }
        }
        return null;
    }
}
