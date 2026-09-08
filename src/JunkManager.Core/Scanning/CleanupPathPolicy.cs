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

    public static bool IsProtected(string path) => IsProtected(path,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static bool IsProtected(string path, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var spotify = Path.Combine(localAppData, "Spotify", "Storage");
        var spotifyStorage = !string.IsNullOrWhiteSpace(localAppData)
            && (normalized.Equals(spotify, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(spotify + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var storageIndex = spotify.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        for (var index = 0; index < segments.Length; index++)
        {
            // Only Spotify's exact media store gets this exception. Other Storage folders keep their doors locked.
            if (spotifyStorage && index == storageIndex && segments[index].Equals("Storage", StringComparison.OrdinalIgnoreCase)) continue;
            if (ProtectedNames.Contains(segments[index])) return true;
        }
        return false;
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
