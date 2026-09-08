using System.Diagnostics;

namespace JunkManager.Core.Apps;

public sealed record ProgramSizeEstimate(long? Bytes, bool Partial, string Detail);

/// <summary>Read-only fallback when an installer declines to do basic arithmetic.</summary>
public static class ProgramSizeReader
{
    public static Task<ProgramSizeEstimate> ReadAsync(InstalledProgram program,
        IReadOnlyList<InstalledProgram> owners, CancellationToken ct) =>
        Task.Run(() => Read(program, owners, ct), ct);

    public static ProgramSizeEstimate Read(InstalledProgram program, IReadOnlyList<InstalledProgram> owners,
        CancellationToken ct, int maxEntries = 100_000, TimeSpan? timeLimit = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ct.ThrowIfCancellationRequested();
        if (program.EstimatedSizeBytes is > 0)
            return new(program.EstimatedSizeBytes, false, "Оценка размера, указанная установщиком");
        var candidates = new[] { program.InstallLocation, Parent(program.ExecutablePath) };
        var reason = "Установщик не сообщил размер и отдельную папку приложения";
        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!TryRoot(candidate!, program, owners, out var root, out reason)) continue;
                var measured = Measure(root, maxEntries, timeLimit ?? TimeSpan.FromSeconds(2), ct);
                return string.Equals(candidate, program.InstallLocation, StringComparison.OrdinalIgnoreCase) ? measured
                    : measured with { Partial = true, Detail = "Размер папки с EXE. Остальные файлы программы могут находиться в других папках. " + measured.Detail };
            }
            catch (Exception ex) when (IsReadError(ex))
            { reason = ex is UnauthorizedAccessException or System.Security.SecurityException
                ? "Нет доступа к папке приложения. Подсчёт не меняет права доступа" : "Папка приложения недоступна: " + ex.Message; }
        }
        return new(null, false, reason);
    }

    private static string? Parent(string? executable)
    {
        try { return string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable); }
        catch (Exception ex) when (IsReadError(ex)) { return null; }
    }

    private static bool TryRoot(string candidate, InstalledProgram program, IReadOnlyList<InstalledProgram> owners,
        out string root, out string reason)
    {
        root = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        reason = "Не определена отдельная локальная папка приложения";
        if (root.Length < 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != (char)92
            || !Path.IsPathFullyQualified(root)) return false;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedRoot = root;
        var boundaries = new[] { Path.GetPathRoot(root),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (boundaries.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path => AtOrUnder(path!, normalizedRoot))) return false;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (AtOrUnder(root, windows) || root.Equals(Path.Combine(local, "Programs"), StringComparison.OrdinalIgnoreCase)
            || root.Equals(Path.Combine(local, "Packages"), StringComparison.OrdinalIgnoreCase)
            || root.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"), StringComparison.OrdinalIgnoreCase))
        { reason = "Общие системные файлы нельзя приписать размеру одной программы"; return false; }
        foreach (var owner in owners.Where(owner => owner.Id != program.Id && !string.IsNullOrWhiteSpace(owner.InstallLocation)))
        {
            // A vendor directory is not one application, even if the registry insists loudly.
            string other;
            try { other = Path.GetFullPath(Environment.ExpandEnvironmentVariables(owner.InstallLocation!.Trim().Trim('"'))); }
            catch (Exception ex) when (IsReadError(ex)) { continue; }
            if (boundaries.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path => AtOrUnder(path!, other))) continue;
            if (AtOrUnder(other, root) || AtOrUnder(root, other))
            { reason = "Папка общая для нескольких программ; отдельный размер недоступен"; return false; }
        }
        for (DirectoryInfo? current = new(root); current is not null; current = current.Parent)
        {
            if ((current.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
            { reason = "Папка содержит ссылку или недоступна локально; переход по ссылкам отключён"; return false; }
        }
        return true;
    }

    private static bool AtOrUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        var normalized = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static ProgramSizeEstimate Measure(string root, int maxEntries, TimeSpan timeLimit, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var pending = new Stack<string>();
        pending.Push(root);
        long bytes = 0;
        var entries = 0;
        var partial = false;
        var limited = false;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { partial = true; continue; }
                foreach (var item in info.EnumerateFileSystemInfos("*", new EnumerationOptions
                    { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++entries > maxEntries || watch.Elapsed >= timeLimit) { limited = true; break; }
                    try
                    {
                        if ((item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { partial = true; continue; }
                        if (item is DirectoryInfo) pending.Push(item.FullName);
                        else if (item is FileInfo file) bytes = checked(bytes + file.Length);
                    }
                    catch (Exception ex) when (IsReadError(ex)) { partial = true; }
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { partial = true; }
            if (limited) break;
        }
        partial |= limited;
        return new(partial && bytes == 0 ? null : bytes, partial,
            partial ? "Посчитана только доступная часть папки: достигнут лимит времени или есть недоступные файлы и ссылки"
                : "Объём файлов папки установки. Данные пользователя и общие зависимости не включены");
    }

    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ArgumentException or NotSupportedException or OverflowException;
}