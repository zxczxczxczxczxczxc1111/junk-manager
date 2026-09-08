using System.Globalization;
using System.Text;

namespace JunkManager.Core.Explain;

// Usage rows deliberately have no deletion target. A large number is not a demolition permit.
public sealed record StorageItem(string Group, string Path, long Bytes, string Note, bool Partial = false)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
}

public sealed record StorageAnalysis(IReadOnlyList<StorageItem> Items, IReadOnlyList<string> Issues, bool Cancelled)
{
    public string ToCsv()
    {
        static string Quote(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var output = new StringBuilder("Группа;Путь;Байт;Примечание;Проверено полностью\r\n");
        foreach (var row in Items)
            output.AppendLine(string.Join(';', Quote(row.Group), Quote(row.Path), row.Bytes.ToString(CultureInfo.InvariantCulture),
                Quote(row.Note), row.Partial ? "Нет" : "Да"));
        return output.ToString();
    }
}

public sealed record StorageAnalysisOptions(string Profile, string Local, string Roaming, string Windows, string ProgramData,
    string ProgramsX86, string Volume, bool DeepScan = false)
{
    public static StorageAnalysisOptions Current(bool deepScan = false)
    {
        static string Folder(Environment.SpecialFolder kind) => Environment.GetFolderPath(kind);
        var windows = Folder(Environment.SpecialFolder.Windows);
        return new(Folder(Environment.SpecialFolder.UserProfile), Folder(Environment.SpecialFolder.LocalApplicationData),
            Folder(Environment.SpecialFolder.ApplicationData), windows, Folder(Environment.SpecialFolder.CommonApplicationData),
            Folder(Environment.SpecialFolder.ProgramFilesX86), Path.GetPathRoot(windows)!, deepScan);
    }
}

public static class StorageAnalyzer
{
    public static Task<StorageAnalysis> AnalyzeAsync(StorageAnalysisOptions options, IProgress<string>? progress, CancellationToken ct) =>
        Task.Run(() => Analyze(options, progress, ct), CancellationToken.None);

    private static StorageAnalysis Analyze(StorageAnalysisOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rows = new Dictionary<string, StorageItem>(StringComparer.OrdinalIgnoreCase);
        var measured = new Dictionary<string, (long Bytes, bool Partial)>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var disks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var largest = new PriorityQueue<StorageItem, long>();
        var lastProgress = DateTime.MinValue;
        void Problem(string path, string reason)
        {
            // Thousands of inaccessible files should not produce a second disk-sized report.
            if (issues.Count < 100) issues.Add(path + ": " + reason);
        }
        void Report(string path)
        {
            if ((DateTime.UtcNow - lastProgress).TotalMilliseconds < 200) return;
            lastProgress = DateTime.UtcNow;
            progress?.Report(path);
        }
        (long Bytes, bool Partial) Measure(string root, bool collectLargest = false)
        {
            if (!collectLargest && measured.TryGetValue(root, out var cached)) return cached;
            long bytes = 0;
            var partial = false;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var path) && !ct.IsCancellationRequested)
            {
                Report(path);
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    { partial = true; Problem(path, "ссылка пропущена"); continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        foreach (var child in Directory.EnumerateFileSystemEntries(path))
                        { if (ct.IsCancellationRequested) break; pending.Push(child); }
                    }
                    else
                    {
                        var size = new FileInfo(path).Length;
                        bytes += size;
                        if (Path.GetExtension(path).Equals(".vhdx", StringComparison.OrdinalIgnoreCase)
                            || Path.GetExtension(path).Equals(".vhd", StringComparison.OrdinalIgnoreCase)) disks[path] = size;
                        if (collectLargest)
                        {
                            largest.Enqueue(new("Крупные файлы", path, size, "Размер файла, не признак мусора."), size);
                            if (largest.Count > 25) largest.Dequeue();
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { partial = true; Problem(path, ex.Message); }
            }
            var result = (bytes, partial || ct.IsCancellationRequested);
            measured[root] = result;
            return result;
        }
        void Add(string group, string path, string note)
        {
            if (ct.IsCancellationRequested || rows.ContainsKey(path)) return;
            try { _ = File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Problem(path, ex.Message); rows[path] = new(group, path, 0, "Размер недоступен. " + note, true); return; }
            var size = Measure(path);
            rows[path] = new(group, path, size.Bytes, note, size.Partial);
        }
        void Children(string root, string group, bool dotOnly = false)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                foreach (var path in Directory.EnumerateDirectories(root))
                {
                    if (ct.IsCancellationRequested) break;
                    if (dotOnly && !Path.GetFileName(path).StartsWith('.')) continue;
                    Add(group, path, dotOnly
                        ? "Данные инструмента: настройки, история или кэш. Проверь, пользуешься ли им."
                        : "Полный размер папки. Кэш для удаления показан отдельно в разделе «Файлы».");
                }
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Problem(root, ex.Message); }
        }

        Children(options.Local, "AppData · Local");
        Children(options.Roaming, "AppData · Roaming");
        Children(options.Profile, "Данные инструментов", dotOnly: true);
        Add("Данные инструментов", Path.Combine(options.Profile, "go"), "Исходники и зависимости Go. Кэш модулей показан отдельной строкой.");
        foreach (var relative in new[] { "projects", "shell-snapshots", "statsig", "logs", "todos", "downloads", "plugins", "history.jsonl" })
            Add("Claude Code", Path.Combine(options.Profile, ".claude", relative), "История, настройки или файлы инструмента. Временные данные для очистки показаны в разделе «Файлы».");
        foreach (var relative in new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys", "Windows.old", "$Windows.~BT", "$Windows.~WS", "$WinREAgent" })
            Add("Система", Path.Combine(options.Volume, relative), "Системные данные. Доступную очистку определяет Windows; целиком удалять нельзя.");
        foreach (var relative in new[] { "WinSxS", @"System32\DriverStore\FileRepository", "Temp", "Panther", "Logs", "Prefetch", "LiveKernelReports", "Minidump", @"SoftwareDistribution\Download" })
            Add("Windows", Path.Combine(options.Windows, relative), "Логический размер: общие файлы Windows могут учитываться повторно. Это не объём доступной очистки.");
        Add("Отчёты сбоев", Path.Combine(options.ProgramData, @"Microsoft\Windows\WER"), "Журналы и дампы прошлых ошибок.");
        foreach (var relative in new[] { @".ollama\models", @".cache\huggingface", @"go\pkg\mod", @"miniconda3\pkgs", @"anaconda3\pkgs" })
            Add("Модели и зависимости", Path.Combine(options.Profile, relative), "Может содержать локальные модели или зависимости, которых нет в интернете. Проверь содержимое.");
        Add("Виртуальные машины", Path.Combine(options.Local, @"Docker\wsl"), "Диски Docker могут содержать образы, контейнеры и базы данных. Очистка и сжатие выполняются через Docker.");
        Add("Незавершённые загрузки", Path.Combine(options.ProgramsX86, @"Steam\steamapps\downloading"), "Загрузка игр ещё не завершена. Управляй ей через Steam.");
        Add("Battle.net", Path.Combine(options.ProgramData, @"Battle.net\Agent"), "Здесь находится и программа обновления Battle.net, а не только кэш.");
        foreach (var root in new[] { Path.Combine(options.Local, "AnthropicClaude"), Path.Combine(options.Local, "Riot Games") })
            Children(root, "Файлы приложений");
        try
        {
            if (Directory.Exists(options.Local))
                foreach (var path in Directory.EnumerateDirectories(options.Local, "*-updater"))
                    Add("Обновления приложений", path, "В папке может находиться сам обновлятор. Проверь содержимое перед удалением.");
            var downloads = Path.Combine(options.Profile, "Downloads");
            if (Directory.Exists(downloads))
                foreach (var file in Directory.EnumerateFiles(downloads))
                {
                    if (ct.IsCancellationRequested) break;
                    if (new[] { ".exe", ".msi", ".zip", ".iso", ".7z", ".rar" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                        && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-90))
                        Add("Старые установщики и архивы", file, "Архив старше 90 дней. Возраст не доказывает, что файл больше не нужен.");
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem(options.Profile, ex.Message); }
        if (options.DeepScan && !ct.IsCancellationRequested) Measure(options.Volume, collectLargest: true);
        else
        {
            // A VM disk can live outside AppData; its extension is not a teleportation device.
            var directories = new Stack<(string Path, int Depth)>();
            directories.Push((options.Volume, 0));
            while (directories.TryPop(out var directory) && !ct.IsCancellationRequested)
            {
                Report(directory.Path);
                try
                {
                    if ((File.GetAttributes(directory.Path) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                    {
                        if (ct.IsCancellationRequested) break;
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        { if (directory.Depth < 6) directories.Push((entry, directory.Depth + 1)); }
                        else if (Path.GetExtension(entry).Equals(".vhdx", StringComparison.OrdinalIgnoreCase)
                            || Path.GetExtension(entry).Equals(".vhd", StringComparison.OrdinalIgnoreCase))
                            disks[entry] = new FileInfo(entry).Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem(directory.Path, ex.Message); }
            }
        }
        foreach (var pair in disks) rows[pair.Key] = new("Виртуальные диски", pair.Key, pair.Value, "Внутри может находиться целая система и её данные. Размер файла не равен мусору.");
        foreach (var item in largest.UnorderedItems) rows.TryAdd(item.Element.Path, item.Element);
        return new(rows.Values.OrderByDescending(row => row.Bytes).ThenBy(row => row.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            issues, ct.IsCancellationRequested);
    }
}
