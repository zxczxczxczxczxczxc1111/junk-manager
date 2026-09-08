using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Safety;

public sealed record ProgramFileStamp(string Path, bool IsDirectory, long Length, DateTime LastWriteUtc);

/// <summary>Narrow path checks for one explicitly removed program, never a Program Files whitelist.</summary>
public static class ProgramLeftoverGuard
{
    private static readonly HashSet<string> UserDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Documents", "Downloads", "Pictures", "Videos", "Music", "Desktop", "OneDrive",
        "Saved Games", "User Data", "Local Storage", "Session Storage", "Profiles", "Passwords",
    };
    private static readonly HashSet<string> UserExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".rtf",
        ".odt", ".ods", ".csv", ".kdbx", ".key", ".pem", ".pfx", ".wallet", ".db", ".sqlite",
        ".jpg", ".jpeg", ".mp3", ".mp4", ".zip", ".7z", ".rar",
    };
    private static readonly string[] SharedNames = ["Common Files", "WindowsApps", "Package Cache", "Packages"];
    private static readonly string[] UserFileNames = ["Login Data", "Bookmarks", "Cookies", "History", "key4.db", "logins.json"];

    public static bool IsAtOrUnder(string? candidate, string? root)
    {
        if (!Normalize(candidate, out var c) || !Normalize(root, out var r)) { return false; }
        return SafetyGuard.IsAtOrUnder(c, r);
    }

    public static bool IsUserData(string path, bool shortcut = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (shortcut && Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) { return false; }
        return path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(UserDirectories.Contains)
            || UserExtensions.Contains(Path.GetExtension(path))
            || UserFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryVerify(string path, string installRoot, bool shortcut,
        out VerifiedPath verified, [NotNullWhen(false)] out string? reason)
    {
        verified = default;
        if (!Normalize(path, out var canonical) || !Normalize(installRoot, out var root))
        { reason = "путь остатка не является полным локальным путём"; return false; }
        if (shortcut)
        {
            var allowed = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu };
            if (!Path.GetExtension(canonical).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || !allowed.Any(folder => IsAtOrUnder(canonical, Environment.GetFolderPath(folder))))
            { reason = "ярлык находится вне известных каталогов ярлыков"; return false; }
        }
        else if (!IsAtOrUnder(canonical, root))
        { reason = "остаток находится вне точного каталога установки"; return false; }
        if (IsAtOrUnder(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            || root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
            || SharedNames.Any(name => root.Split(Path.DirectorySeparatorChar).Contains(name, StringComparer.OrdinalIgnoreCase)
                || canonical.Split(Path.DirectorySeparatorChar).Contains(name, StringComparer.OrdinalIgnoreCase))
            || SharedRoots().Any(shared => string.Equals(Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(shared), StringComparison.OrdinalIgnoreCase)))
        { reason = "общий или системный каталог не является остатком одной программы"; return false; }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var users = Path.GetDirectoryName(profile);
        if (IsAtOrUnder(root, users) && !IsAtOrUnder(root, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            && !IsAtOrUnder(root, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
            && !IsAtOrUnder(root, Path.GetTempPath()))
        { reason = "каталог установки относится к пользовательским файлам или другому профилю"; return false; }
        if (IsUserData(root) || IsUserData(canonical, shortcut))
        { reason = "путь содержит пользовательские документы, данные или секреты"; return false; }
        if (!PathResolver.TryResolveFinalPath(canonical, out var resolved, out var error))
        { reason = "остаток не открывается: " + error; return false; }
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) { resolved = resolved[4..]; }
        if (!Normalize(resolved, out resolved) || !resolved.Equals(canonical, StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(canonical) & FileAttributes.ReparsePoint) != 0)
        { reason = "остаток или его родитель оказался ссылкой"; return false; }
        verified = new(canonical, resolved);
        reason = null;
        return true;
    }

    public static bool TrySnapshot(string path, string installRoot, bool shortcut,
        out IReadOnlyList<ProgramFileStamp> files, [NotNullWhen(false)] out string? reason)
    {
        var result = new List<ProgramFileStamp>();
        files = [];
        var pending = new Stack<string>();
        pending.Push(path);
        try
        {
            while (pending.TryPop(out var item))
            {
                if (result.Count >= 100000) { reason = "слишком много объектов для безопасного снимка остатков"; return false; }
                if (!TryVerify(item, installRoot, shortcut, out var verified, out reason)) { return false; }
                var directory = Directory.Exists(verified.Value);
                result.Add(new(verified.Value, directory, directory ? 0 : new FileInfo(verified.Value).Length,
                    File.GetLastWriteTimeUtc(verified.Value)));
                if (directory)
                {
                    foreach (var child in Directory.GetFileSystemEntries(verified.Value)) { pending.Push(child); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { reason = "не удалось проверить все объекты остатка: " + ex.Message; return false; }
        files = [.. result.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
        reason = null;
        return true;
    }

    private static string[] SharedRoots() =>
    [
        Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath(),
    ];

    private static bool Normalize(string? value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)
            || value.StartsWith(@"\\", StringComparison.Ordinal) || value.Any(char.IsControl)
            || value.AsSpan().IndexOfAny("<>|\"*?".AsSpan()) >= 0) { return false; }
        try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
