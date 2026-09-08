using System.IO.Enumeration;
using JunkManager.Core.Scanning;

namespace JunkManager.Core.Sources.Pattern;

/// <param name="MaxDepth">
/// Levels below the root, root itself being 1. A pattern scan without a depth
/// limit is a full disk walk with a nicer name.
/// </param>
public sealed record PatternScanOptions(
    IReadOnlyList<string> Roots, IReadOnlyList<string> Masks, int MaxDepth, int MinAgeDays)
{
    /// <summary>
    /// Directory hints for inspecting crash files, never permission to remove the whole directory. A crash-report
    /// store is named by the framework and filled by the application, so the
    /// file names inside are unknowable while the folder name is not: this is
    /// the one case where the directory carries more information than its
    /// contents. Optional, and empty by default at the record level so an
    /// explicitly constructed options object gains nothing it did not ask for.
    /// </summary>
    public IReadOnlyList<string> DirectoryMasks { get; init; } = DirectoryMasksPoUmolchaniyu;

    /// <summary>
    /// Crash-report folders only. Every entry here is a store some framework
    /// writes to and nothing reads: Chromium and Electron write "Crash Reports"
    /// and "Crashpad", Windows Error Reporting writes "CrashDumps" per
    /// application. Widening this list past crash reports would turn a
    /// mask search into a guess about somebody's data.
    /// </summary>
    public static IReadOnlyList<string> DirectoryMasksPoUmolchaniyu { get; } =
    [
        "Crash Reports", "CrashReports", "CrashDumps", "Crashpad", "crashes",
    ];

    /// <summary>
    /// Off by default at the call site, not here: this only says what the scan
    /// looks like once a person turns it on.
    /// </summary>
    public static PatternScanOptions Default() => new(
        Roots:
        [
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.GetTempPath(),
        ],
        Masks: ["*.tmp", "*.bak", "*.old", "*.dmp", "*.chk", "Thumbs.db"],
        MaxDepth: 6,
        MinAgeDays: 30);
}

/// <summary>
/// Finds junk nobody wrote a rule for. Deliberately the least trusted source in
/// the product: everything it produces is Risk, because a mask cannot know what
/// a file is for. The lesson is borrowed from BleachBit, which needed this mode
/// on top of 106 files of path rules.
/// </summary>
/// <param name="time">
/// The clock, injectable for the same reason FileScanner takes one: the age
/// cut-off is the only thing standing between this source and every fresh file
/// on the disk, and a cut-off tested by shifting real timestamps around is a
/// cut-off tested by luck.
/// </param>
public sealed class PatternScanner(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<ScanResult> ScanAsync(PatternScanOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MinAgeDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDepth, 1);
        var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-options.MinAgeDays);
        return Task.Run(() => Scan(options, progress, cutoff, ct), CancellationToken.None);
    }

    private static ScanResult Scan(PatternScanOptions options, IProgress<string>? progress,
        DateTime cutoff, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in options.Roots)
        {
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((root, 1));
            while (stack.TryPop(out var current) && !ct.IsCancellationRequested)
            {
                if (!CleanupPathPolicy.TryVerify(current.Path, out var verified, out var reason))
                {
                    skipped.Add(new SkippedItem(current.Path, reason!));
                    continue;
                }
                progress?.Report(current.Path);
                try
                {
                    var crashFolder = options.DirectoryMasks.Any(mask => FileSystemName.MatchesSimpleExpression(mask, Path.GetFileName(current.Path), true));
                    var crashFiles = new List<string>();
                    var crashSnapshots = new Dictionary<string, CleanupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
                    long crashBytes = 0;
                    foreach (var file in Directory.EnumerateFiles(verified.Value))
                    {
                        if (ct.IsCancellationRequested) break;
                        var matchesFile = options.Masks.Any(mask => FileSystemName.MatchesSimpleExpression(mask, Path.GetFileName(file), true));
                        if (!matchesFile && !crashFolder) continue;
                        if (!seen.Add(file)) continue;
                        if (!CleanupPathPolicy.TryVerify(file, out _, out reason))
                        {
                            skipped.Add(new SkippedItem(file, reason!));
                            continue;
                        }
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc > cutoff) continue;
                        if (!HasEvidence(file, matchesFile, out var basis))
                        {
                            skipped.Add(new SkippedItem(file, "совпали имя и возраст, но назначение файла не подтверждено"));
                            continue;
                        }
                        if (crashFolder)
                        {
                            crashFiles.Add(file);
                            crashSnapshots[file] = CleanupFileSnapshot.Capture(info);
                            crashBytes += info.Length;
                            continue;
                        }
                        findings.Add(new Finding(info.Name, file, info.Length, RiskTier.Risk,
                            basis + " Удаление необратимо; старые временные копии и диагностика станут недоступны.",
                            FindingSource.PatternScan)
                        { FileSnapshots = new Dictionary<string, CleanupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
                            { [file] = CleanupFileSnapshot.Capture(info) } });
                    }
                    if (crashFiles.Count > 0)
                        findings.Add(new Finding(Path.GetFileName(current.Path), verified.Value, crashBytes, RiskTier.Risk,
                            "Выбранные старые файлы диагностики станут недоступны для разбора сбоев. Остальное содержимое каталога сохранится.",
                            FindingSource.PatternScan, Scope: DeleteScope.SelectedEntries, Targets: crashFiles)
                        { FileSnapshots = crashSnapshots });
                    if (current.Depth >= options.MaxDepth) continue;
                    foreach (var directory in Directory.EnumerateDirectories(verified.Value))
                    {
                        if (ct.IsCancellationRequested) break;
                        stack.Push((directory, current.Depth + 1));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new SkippedItem(current.Path, $"не удалось прочитать: {ex.Message}"));
                }
            }
        }
        return new ScanResult(findings, skipped, ct.IsCancellationRequested);
    }

    private static bool HasEvidence(string file, bool allowTempEvidence, out string basis)
    {
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (allowTempEvidence && file.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            basis = "Файл из системной временной папки пользователя, отобран по маске и возрасту.";
            return true;
        }
        // A .dmp suffix alone is as persuasive as a fake moustache on a database.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[8];
        var count = stream.Read(header);
        if (count >= 4 && header[..4].SequenceEqual("MDMP"u8))
        {
            basis = "Подтверждён заголовок дампа памяти Windows; файл нужен для разбора прошлого сбоя.";
            return true;
        }
        basis = string.Empty;
        return false;
    }
}
