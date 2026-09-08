namespace JunkManager.Core;

/// <summary>
/// Not every finding is a path on disk. A Windows cleanup handler, a DISM
/// component store, a registry value and an MSIX package all need an identity in
/// <see cref="Finding.Path"/>, and every one of them would look like a broken
/// path to a check that assumes otherwise. The seeded acceptance run runs every
/// finding path through SafetyGuard, so the distinction has to be explicit
/// rather than guessed from the shape of the string.
/// </summary>
public static class FindingPath
{
    public const string VolumeCacheScheme = "volumecache:";
    public const string PlatformToolScheme = "platformtool:";
    public const string RegistryScheme = "registry:";
    public const string MsixScheme = "msix:";

    private static readonly string[] Schemes =
        [VolumeCacheScheme, PlatformToolScheme, RegistryScheme, MsixScheme];

    /// <summary>
    /// True when the string names a place on disk that the guard can and must
    /// judge. False for the schemes above, which are identities, not locations.
    /// </summary>
    public static bool IsFileSystem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var scheme in Schemes)
        {
            if (path.StartsWith(scheme, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
