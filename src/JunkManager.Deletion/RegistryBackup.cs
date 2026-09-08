using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using JunkManager.Core.Sources.Platform;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Deletion;

/// <param name="FilePath">
/// Where the backup was supposed to go. Filled even on failure, so a person
/// looking for the file knows which name to look for and not find.
/// </param>
public sealed record BackupResult(bool Ok, string FilePath, string? Reason);

/// <summary>
/// Exports a registry branch before anything touches it.
/// </summary>
/// <remarks>
/// An interface, and not because of layering fashion: the one requirement the
/// spec states outright for this module is "a failed export cancels the
/// deletion", and there is no honest way to make the real reg.exe fail on a
/// branch the guard would allow. The test substitutes a backup that refuses.
/// </remarks>
public interface IRegistryBackup
{
    Task<BackupResult> ExportAsync(
        RegistryHive hive, string subKey, RegistryView view, CancellationToken ct);
}

/// <summary>
/// One call to reg.exe. The exe name is not a parameter on purpose: this type
/// runs reg.exe and nothing else, and a runner that chooses its own executable
/// is a runner somebody points at something else.
/// </summary>
public delegate Task<ToolRun> RegExeRunner(
    IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);

/// <summary>
/// reg.exe export plus the check that the export actually happened. The check
/// is the point: a backup nobody verified is a file with a .reg extension, and
/// this is the only path back after a registry deletion.
/// </summary>
public sealed class RegistryBackup : IRegistryBackup
{
    internal const string Zagolovok = "Windows Registry Editor Version 5.00";

    private static readonly TimeSpan Terpenie = TimeSpan.FromMinutes(2);

    private readonly string _katalog;
    private readonly TimeProvider _vremya;
    private readonly RegExeRunner _zapusk;

    /// <param name="directory">Null means <see cref="DefaultDirectory"/>.</param>
    /// <param name="runner">
    /// Null means the real reg.exe. Substituted only by tests, and it cannot be
    /// used to fake a backup: whatever the runner answers, the result still has
    /// to survive <see cref="Proverit"/>, which reads the file off the disk.
    /// That is the whole reason the seam is here. The real reg.exe cannot be
    /// made to exit 0 without writing a file, so without the seam the claim
    /// "exit code 0 proves nothing" is untestable, and an untestable claim in a
    /// backup is a claim nobody will notice going false.
    /// </param>
    public RegistryBackup(
        string? directory = null, TimeProvider? time = null, RegExeRunner? runner = null)
    {
        _katalog = directory ?? DefaultDirectory;
        _vremya = time ?? TimeProvider.System;
        _zapusk = runner
            ?? ((arguments, timeout, ct) => ProcessRunner.RunAsync(ProcessRunner.SystemTool("reg.exe"), arguments, timeout, ct));
    }

    /// <summary>
    /// <c>%LOCALAPPDATA%\JunkManager\registry-backups</c>, section 4 of the spec.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JunkManager",
        "registry-backups");

    public async Task<BackupResult> ExportAsync(
        RegistryHive hive, string subKey, RegistryView view, CancellationToken ct)
    {
        string fayl;
        try
        {
            Directory.CreateDirectory(_katalog);
            fayl = Path.Combine(_katalog, Imya(hive, subKey));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupResult(false, string.Empty, $"каталог бэкапов не готов: {ex.Message}");
        }

        var argumenty = new List<string>
        {
            "export",
            RegistryAddress.RegExeHive(hive) + "\\" + subKey,
            fayl,
            "/y",
        };

        // Проекция передаётся явно. Экспорт 32-битной ветки из 64-битного вида
        // вернул бы содержимое соседней ветки, а откат положил бы его не туда.
        if (view == RegistryView.Registry32)
        {
            argumenty.Add("/reg:32");
        }
        else if (view == RegistryView.Registry64)
        {
            argumenty.Add("/reg:64");
        }

        ToolRun run;
        try
        {
            run = await _zapusk(argumenty, Terpenie, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new BackupResult(false, fayl, "экспорт реестра отменён или не уложился в срок");
        }
        catch (Win32Exception ex)
        {
            return new BackupResult(false, fayl, $"reg.exe не запустился: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new BackupResult(false, fayl, $"reg.exe не запустился: {ex.Message}");
        }

        if (run.ExitCode != 0)
        {
            // Проверено на машине DERMO 05.09.2026: экспорт несуществующей ветки
            // это код 1 и текст в stderr.
            return new BackupResult(false, fayl, $"reg.exe вернул {run.ExitCode}: {Odna(run)}");
        }

        // Код 0 сам по себе ничего не доказывает. Доказывает файл.
        return Proverit(fayl, out var prichina)
            ? new BackupResult(true, fayl, null)
            : new BackupResult(false, fayl, prichina);
    }

    /// <summary>
    /// The four checks from section 9.2 of the spec: the file is there, it is
    /// not empty, it reads, and its first line is the header. Internal so the
    /// tests can drive each failure separately without a real reg.exe.
    /// </summary>
    /// <summary>
    /// The four checks from section 9.2 of the spec: the file is there, it is
    /// not empty, it reads, and its first line is the header.
    /// </summary>
    /// <remarks>
    /// Public, and not because tests need it: they see internals already. The
    /// restore panel shows whether each backup file can be imported BEFORE the
    /// person picks one, and asking that question needs the same four checks
    /// that gate the import itself. A second copy of them would answer
    /// differently on the day one of the two is changed.
    ///
    /// Имя менять нельзя: строка `return Proverit(fayl, out var prichina)`
    /// стоит образцом в `scripts/mutations/08-reestr-udalenie.ps1`, и
    /// переименование тихо превратило бы её в мутацию, которая не применяется
    /// и потому ничего не проверяет.
    /// </remarks>
    public static bool Proverit(string file, [NotNullWhen(false)] out string? reason)
    {
        long dlina;
        try
        {
            var svedeniya = new FileInfo(file);

            if (!svedeniya.Exists)
            {
                reason = $"файла бэкапа нет: {file}";
                return false;
            }

            dlina = svedeniya.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            reason = $"файл бэкапа {file} не читается: {ex.Message}";
            return false;
        }

        if (dlina == 0)
        {
            reason = $"файл бэкапа пустой: {file}";
            return false;
        }

        byte[] nachalo;
        try
        {
            using var potok = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            nachalo = new byte[(int)Math.Min(4096, potok.Length)];
            potok.ReadExactly(nachalo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"файл бэкапа {file} не читается: {ex.Message}";
            return false;
        }

        if (nachalo.Length < 2 || nachalo[0] != 0xFF || nachalo[1] != 0xFE)
        {
            // reg.exe пишет UTF-16LE с меткой FF FE, проверено 05.09.2026. Файл
            // без метки написан не тем, чем мы думаем, и импорт такого файла
            // это импорт неизвестно чего.
            reason = $"у файла бэкапа {file} нет метки UTF-16LE (FF FE)";
            return false;
        }

        var tekst = Encoding.Unicode.GetString(nachalo, 2, nachalo.Length - 2);

        if (!tekst.StartsWith(Zagolovok, StringComparison.Ordinal))
        {
            reason = $"первая строка файла бэкапа {file} не '{Zagolovok}'";
            return false;
        }

        reason = null;
        return true;
    }

    private string Imya(RegistryHive hive, string subKey)
    {
        var moment = _vremya.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var hvost = RegistryAddress.ShortHive(hive) + "-" + subKey;

        foreach (var plohoy in Path.GetInvalidFileNameChars())
        {
            hvost = hvost.Replace(plohoy, '-');
        }

        // Обрезка нужна: ветка App Paths с длинным именем программы легко даёт
        // имя файла длиннее, чем принимает файловая система.
        if (hvost.Length > 80)
        {
            hvost = hvost[..80];
        }

        return $"{moment}-{hvost}-{Guid.NewGuid():N}.reg";
    }

    private static string Odna(ToolRun run)
    {
        var tekst = string.IsNullOrWhiteSpace(run.StandardError)
            ? run.StandardOutput
            : run.StandardError;

        return tekst.ReplaceLineEndings(" ").Trim();
    }
}
