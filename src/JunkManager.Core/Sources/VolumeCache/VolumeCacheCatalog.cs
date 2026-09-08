using JunkManager.Core.Interop;
using Microsoft.Win32;

namespace JunkManager.Core.Sources.VolumeCache;

/// <param name="DisplayName">
/// Best name available: the resolved "display" value when there is one, the key
/// name otherwise. Never empty, because a nameless row in the list is worse than
/// an ugly one.
/// </param>
public sealed record VolumeCacheEntry(string KeyName, Guid Clsid, string DisplayName);

/// <summary>
/// Reads the list of disk cleanup handlers Windows registers for itself. Reading
/// only: creating the COM objects is a separate step, because the blacklist has
/// to be provable without touching a single handler.
/// </summary>
public static class VolumeCacheCatalog
{
    /// <summary>
    /// Public because JunkManager.Deletion opens the same key before handing a
    /// handler to Purge, and a second copy of this string in another assembly is
    /// the kind of duplicate that quietly stops matching after a Windows change.
    /// One constant, one place to fix.
    /// </summary>
    public const string RegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    /// <summary>
    /// Handlers we refuse to offer, BY KEY NAME. Not by CLSID: verified on
    /// 05.09.2026 that {C0E13E61-0CC6-11d1-BBB6-0060978B2AE6} is shared by
    /// twelve keys, so a CLSID blacklist would take out eleven working handlers
    /// along with DownloadsFolder and nobody would notice the loss.
    /// </summary>
    /// <summary>
    /// The volume the handlers are initialised for.
    /// </summary>
    /// <remarks>
    /// Asked of Windows rather than written down as "C:". On a machine whose
    /// Windows lives on another drive the hardcoded letter measures one volume
    /// and empties another, and the handler reports success either way, because
    /// from its side the request was perfectly valid.
    /// </remarks>
    public static string SystemVolume { get; } =
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        ?? throw new InvalidOperationException(
            "системный том не определяется: каталог Windows без корня");

    public static IReadOnlySet<string> Blacklist { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows считает "Загрузки" очищаемой папкой. Мы не считаем.
            "DownloadsFolder",
        };

    public static IReadOnlyList<VolumeCacheEntry> Enumerate()
    {
        // Fully qualified on purpose. The namespace JunkManager.Core.Registry
        // shadows the simple name Registry here, and a global using alias
        // does not help: a namespace member beats an alias at the same level.
        using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryPath);

        if (root is null)
        {
            // Missing key is a documented possibility, not a failure: the
            // interface is marked legacy by Microsoft.
            return [];
        }

        var found = new List<VolumeCacheEntry>();

        foreach (var keyName in root.GetSubKeyNames())
        {
            using var sub = root.OpenSubKey(keyName);

            if (sub?.GetValue(null) is not string clsidText
                || !Guid.TryParse(clsidText, out var clsid)
                || clsid == Guid.Empty)
            {
                // A key without a usable CLSID is skipped, not fatal. Seen in the
                // wild after partial uninstalls of third-party handlers.
                continue;
            }

            var display = keyName;

            if (ShellStrings.TryResolve(sub.GetValue("display") as string, out var resolved))
            {
                display = resolved;
            }

            found.Add(new VolumeCacheEntry(keyName, clsid, display));
        }

        return Filter(found);
    }

    public static IReadOnlyList<VolumeCacheEntry> Filter(IEnumerable<VolumeCacheEntry> raw) =>
        [.. raw.Where(e => !Blacklist.Contains(e.KeyName.Trim()))];
}
