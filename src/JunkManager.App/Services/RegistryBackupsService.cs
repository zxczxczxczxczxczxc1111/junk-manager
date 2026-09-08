using System.IO;
using JunkManager.Deletion;
using Microsoft.Win32;

namespace JunkManager.App.Services;

/// <summary>
/// Каталог бэкапов реестра как список и как единственный источник импорта.
/// </summary>
internal sealed class RegistryBackupsService : IRegistryBackupsService
{
    public RegistryBackupsService(string? katalog = null) =>
        Directory = katalog ?? RegistryBackup.DefaultDirectory;

    public string Directory { get; }

    public Task<IReadOnlyList<RegistryBackupFile>> ListAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<RegistryBackupFile>>(Sobrat, ct);

    public async Task<RollbackResult> RestoreAsync(RegistryBackupFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!Svoy(file.FilePath))
        {
            // Единственная защита от импорта чужого файла. Проверяется по
            // канонизированному пути, а не по строке: ..\..\ прошёл бы
            // сравнение строк и вышел бы из каталога.
            return new RollbackResult(
                false, file.FilePath,
                $"импорт не начинался: файл не из каталога бэкапов {Directory}");
        }

        // The captured view belongs to the entry, not to whichever process clicked Restore.
        return await RegistryRollback
            .ImportAsync(file.FilePath, file.View, ct)
            .ConfigureAwait(true);
    }

    private bool Svoy(string put)
    {
        try
        {
            var polnyy = Path.GetFullPath(put);
            var koren = Path.GetFullPath(Directory);

            return polnyy.StartsWith(
                koren.EndsWith(Path.DirectorySeparatorChar) ? koren : koren + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
            when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private List<RegistryBackupFile> Sobrat()
    {
        var itog = new List<RegistryBackupFile>();

        string[] fayly;
        try
        {
            fayly = System.IO.Directory.GetFiles(Directory, "*.reg");
        }
        catch (DirectoryNotFoundException)
        {
            // До первой очистки реестра каталога нет вовсе. Это норма, а не
            // отказ, и пустой список тут честен: бэкапов действительно нет.
            return itog;
        }

        foreach (var fayl in fayly)
        {
            var svedeniya = new FileInfo(fayl);
            var godnyy = RegistryBackupEntry.TryRead(fayl, out var entry, out var prichina);

            itog.Add(new RegistryBackupFile(
                fayl,
                svedeniya.Name,
                new DateTimeOffset(svedeniya.LastWriteTimeUtc, TimeSpan.Zero),
                svedeniya.Length,
                godnyy,
                prichina)
            {
                Address = entry is null ? "Нет точных сведений для восстановления одной записи"
                    : JunkManager.Safety.RegistryAddress.Format(entry.Snapshot.Hive, entry.Snapshot.SubKey,
                        entry.Snapshot.WholeKey ? null : entry.Snapshot.ValueName),
                View = entry?.Snapshot.View ?? RegistryView.Default,
            });
        }

        // Свежие первыми: человеку, которому нужен откат, нужен последний.
        itog.Sort((a, b) => b.WrittenUtc.CompareTo(a.WrittenUtc));
        return itog;
    }
}
