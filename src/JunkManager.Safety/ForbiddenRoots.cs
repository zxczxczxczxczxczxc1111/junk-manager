namespace JunkManager.Safety;

/// <summary>
/// The three lists the guard decides by. Every entry is derived from the running
/// system rather than typed as a literal: this code has to reach the same verdict
/// on a machine where Windows sits on another letter and the account is named
/// something else.
/// </summary>
internal static class ForbiddenRoots
{
    /// <summary>
    /// Refused only when the path IS one of these, never when it merely lies
    /// under one. Volume roots live here: deleting "C:\" is never a plan, but
    /// denying everything below it would deny the whole disk and make the guard
    /// useless.
    /// </summary>
    internal static IReadOnlyList<string> DeniedExact { get; } = BuildDeniedExact();

    /// <summary>
    /// Refused for the root itself and for everything under it, minus the
    /// carve-outs in <see cref="Allowed"/>.
    /// </summary>
    internal static IReadOnlyList<string> DeniedTrees { get; } = BuildDeniedTrees();

    /// <summary>
    /// Real junk that happens to live under a denied tree. A carve-out only wins
    /// when it is deeper than the denial it overrides, so a broad exception can
    /// never silently unlock a whole root.
    /// </summary>
    internal static IReadOnlyList<string> Allowed { get; } = BuildAllowed();

    private static List<string> BuildDeniedExact()
    {
        var list = new List<string>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            Add(list, drive.RootDirectory.FullName);
        }

        return list;
    }

    private static List<string> BuildDeniedTrees()
    {
        var windows = Folder(Environment.SpecialFolder.Windows);
        var list = new List<string>();

        Add(list, windows);
        Add(list, Folder(Environment.SpecialFolder.ProgramFiles));
        Add(list, Folder(Environment.SpecialFolder.ProgramFilesX86));
        Add(list, Folder(Environment.SpecialFolder.CommonStartMenu));
        Add(list, Folder(Environment.SpecialFolder.StartMenu));

        // "C:\Users" has no SpecialFolder of its own: it is the parent of the
        // current profile. Deriving it beats a literal, which would be wrong on
        // any machine where profiles were relocated.
        var profile = Folder(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
        {
            Add(list, Path.GetDirectoryName(profile) ?? string.Empty);
        }

        // WinSxS and Installer are already covered by the Windows tree. They are
        // named anyway so a future carve-out under Windows cannot expose them by
        // accident, and so the refusal reason names the thing that actually matters.
        if (windows.Length > 0)
        {
            Add(list, Path.Combine(windows, "WinSxS"));
            Add(list, Path.Combine(windows, "Installer"));
            Add(list, Path.Combine(windows, "System32", "config"));
            Add(list, Path.Combine(windows, "assembly"));
        }

        return list;
    }

    private static List<string> BuildAllowed()
    {
        var windows = Folder(Environment.SpecialFolder.Windows);
        var list = new List<string>();

        if (windows.Length > 0)
        {
            Add(list, Path.Combine(windows, "Temp"));
            Add(list, Path.Combine(windows, "SystemTemp"));
            Add(list, Path.Combine(windows, "Prefetch"));
            Add(list, Path.Combine(windows, "Logs"));
            Add(list, Path.Combine(windows, "Panther"));
            Add(list, Path.Combine(windows, "System32", "LogFiles"));
            Add(list, Path.Combine(windows, "SoftwareDistribution", "Download"));
        }

        // The parts of the profile that are ours to clean. Everything else under
        // the profile stays denied, "Загрузки" included: Windows considers that
        // folder cleanable, we do not.
        Add(list, Folder(Environment.SpecialFolder.LocalApplicationData));
        Add(list, Folder(Environment.SpecialFolder.ApplicationData));
        Add(list, Path.GetTempPath());

        return list;
    }

    private static string Folder(Environment.SpecialFolder which) =>
        Environment.GetFolderPath(which);

    /// <summary>
    /// Adds a root in the exact shape the guard compares against: trimmed of a
    /// trailing separator, except for a bare volume root where the separator is
    /// part of the path. Empty entries are dropped rather than stored, because an
    /// empty root would match everything.
    /// </summary>
    private static void Add(List<string> list, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var normalized = root.Length > 3
            ? root.TrimEnd(Path.DirectorySeparatorChar)
            : root;

        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(normalized);
        }
    }
}
