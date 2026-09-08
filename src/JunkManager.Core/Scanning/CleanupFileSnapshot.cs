namespace JunkManager.Core.Scanning;

public sealed record CleanupFileSnapshot(long Length, DateTime LastWriteUtc, DateTime CreatedUtc)
{
    public static CleanupFileSnapshot Capture(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new(file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
    }

    public bool Matches(string path)
    {
        // A filename is not a lifetime contract with yesterday's cache.
        var file = new FileInfo(path);
        return file.Exists && file.LinkTarget is null && file.Length == Length
            && file.LastWriteTimeUtc == LastWriteUtc && file.CreationTimeUtc == CreatedUtc;
    }
}
