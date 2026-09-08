using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>One exact deletion, never an invitation to import its neighbouring values.</summary>
public sealed record RegistryBackupEntry(
    int Version, string State, string ExportHash, string OwnerSid, RegistryEntrySnapshot Snapshot)
{
    public static string MetadataPath(string exportFile) => exportFile + ".entry.json";

    internal static RegistryBackupEntry Prepare(string exportFile, RegistryEntrySnapshot snapshot)
    {
        if (!SafeFile(exportFile)) throw new IOException("путь экспорта содержит ссылку или файл отсутствует");
        var entry = new RegistryBackupEntry(1, "Prepared", Hash(exportFile), CurrentSid(), snapshot);
        Write(exportFile, entry);
        return entry;
    }

    internal void MarkDeleted(string exportFile) => Write(exportFile, this with { State = "Deleted" });

    public static bool TryRead(
        string exportFile, [NotNullWhen(true)] out RegistryBackupEntry? entry,
        [NotNullWhen(false)] out string? reason)
    {
        entry = null;
        try
        {
            var metadata = MetadataPath(exportFile);
            if (!SafeFile(exportFile) || !SafeFile(metadata)
                || !RegistryBackup.Proverit(exportFile, out reason))
            {
                reason = "файл или точные сведения резервирования отсутствуют либо небезопасны";
                return false;
            }

            if (new FileInfo(metadata).Length > 16 * 1024 * 1024)
            {
                reason = "сведения резервирования превышают допустимый размер";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<RegistryBackupEntry>(File.ReadAllText(metadata));
            if (parsed is null || parsed.Version != 1 || parsed.State != "Deleted"
                || parsed.Snapshot is null || parsed.OwnerSid != CurrentSid()
                || !string.Equals(parsed.ExportHash, Hash(exportFile), StringComparison.Ordinal))
            {
                reason = "нет подтверждённого удаления, экспорт изменён или резервирование принадлежит другому пользователю";
                return false;
            }

            entry = parsed;
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            reason = $"точные сведения резервирования не читаются: {ex.Message}";
            return false;
        }
    }

    internal static bool SafeFile(string file)
    {
        var full = Path.GetFullPath(file);
        if (!File.Exists(full))
        {
            return false;
        }

        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new IOException("SID пользователя не определён");
    }

    private static void Write(string exportFile, RegistryBackupEntry entry)
    {
        var path = MetadataPath(exportFile);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Flush before deletion; a buffered promise is not a backup.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entry);
            if (stream.Position > 16 * 1024 * 1024)
                throw new IOException("точный снимок слишком велик для поддерживаемого восстановления; удаление отменено");
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
