using System.Diagnostics.CodeAnalysis;
using JunkManager.Core.Sources.Detect;
using JunkManager.Safety;

namespace JunkManager.Core.Apps;

[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification = "Тот же довод, что и у FindingSource: неопределённый вид следа не должен " +
        "читаться как каталог, иначе ветка реестра уедет в удаление файлов.")]
public enum LeftoverKind
{
    Directory = 1,
    RegistryKey = 2,
    RegistryValue = 3,
    Shortcut = 4,
    File = 5,
}

/// <param name="Basis">
/// Why this is considered a trace, in one sentence a person can argue with. The
/// spec requires it on every trace: this is the one place in the product that
/// guesses, and a guess without its reasoning cannot be checked.
/// </param>
public sealed record Leftover(LeftoverKind Kind, string Path, long SizeBytes, string Basis)
{
    public LeftoverConfidence Confidence { get; init; } = LeftoverConfidence.Weak;
    public IReadOnlyList<LeftoverEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<ProgramFileStamp> Snapshot { get; init; } = [];
    public Registry.RegistryFinding? RegistryFinding { get; init; }
    public string? TargetPath { get; init; }
    public bool CanDelete => Confidence == LeftoverConfidence.Strong;
    public bool IsSelectedByDefault { get; }
}

public enum LeftoverConfidence { Unknown, Weak, Strong, Blocked }
public sealed record LeftoverEvidence(string Code, string Description, bool Negative = false);

public sealed record LeftoverSearch(
    IReadOnlyList<Leftover> Found, IReadOnlyList<SkippedItem> Skipped);

/// <summary>
/// Traces a program left behind. The only guessing component in the product, and
/// everything about it is arranged so the guess is visible: Risk tier, an
/// explicit basis, never pre-selected, and every path through SafetyGuard.
/// </summary>
public static partial class LeftoverFinder
{

    // Tokens that match half the machine. Desktop is the one that cost the most:
    // matching Telegram Desktop hit com.vault.desktop and %ProgramData%\Desktop.
    private static readonly HashSet<string> StopSlova = new(StringComparer.OrdinalIgnoreCase)
    {
        "Desktop", "App", "Data", "Update", "Cache", "Launcher", "Software",
        "Windows", "Microsoft", "Inc", "Ltd", "LLC", "GmbH", "Corp", "Corporation",
        "Limited", "Company", "Studio", "Studios", "Games", "Program", "Programs",
        "Setup", "Installer", "Client", "Service", "Services", "Common", "Files",
        "User", "Users", "Local", "Roaming", "Team", "Group", "Technologies",
    };

    // Directories that are junk by their own name, whatever program made them.
    // "Crash Reports" is NOT here on purpose: that name belongs to the pattern
    // scan, and claiming it would report a correct path under the wrong source.
    private static readonly HashSet<string> Metki = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Cache_Data", "Code Cache", "GPUCache", "ShaderCache",
        "DawnCache", "logs", "Crashpad", "CachedData", "tmp",
    };

    private static readonly char[] RazdeliteliTokenov =
        [' ', '-', '_', '.', ',', '(', ')', '[', ']', '/', '\\', '+', '&', '\''];

    /// <summary>
    /// Traces of a program that has just been removed, matched by publisher and
    /// product name.
    /// </summary>
    /// <remarks>
    /// Runs only after the uninstaller exited successfully, and returns nothing
    /// otherwise. That is not caution, it is the difference between a trace and
    /// a working directory: while the program is installed, everything it owns
    /// looks exactly like a leftover.
    /// </remarks>
    public static LeftoverSearch FindAfterRemoval(InstalledProgram removed, UninstallResult result)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not (UninstallOutcome.Removed or UninstallOutcome.AlreadyAbsent))
        {
            return new LeftoverSearch([], [new SkippedItem(
                removed.DisplayName,
                $"удаление не завершилось успехом ({result.Outcome}), следы не ищутся: "
                + "пока программа стоит, её каталоги это не следы")]);
        }

        return FindAfterRemovalAsync(removed, result).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Data directories with no installed program behind them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second entry point, and the one the seeded acceptance run reaches.
    /// The basis is different from FindAfterRemoval: not "the name matches a
    /// program we just removed" but "no uninstall record and no package claims
    /// this name at all".
    /// </para>
    /// <para>
    /// Three limits, each one taken from a trap in polygon/posev.json rather
    /// than from taste. Only %LOCALAPPDATA% is walked, because Roaming holds
    /// settings. Only a marked subdirectory is proposed, never the vendor
    /// directory itself, because %LOCALAPPDATA%\Google holds Login Data,
    /// History and Bookmarks next to the cache. And "Crash Reports" is not a
    /// marker, because that path belongs to the pattern scan.
    /// </para>
    /// </remarks>
    /// <param name="koren">
    /// Where to look. Null means the real %LOCALAPPDATA%, which is what the
    /// product uses. A parameter because the alternative is tests that write
    /// into the developer's own profile and clean up in a finally: they worked,
    /// but they could not check the exclusion list at all, since the names on it
    /// (VirtualStore, Package Cache) already exist there and belong to Windows.
    /// A mutation removing the exclusion check survived exactly because of that.
    /// </param>
    public static LeftoverSearch FindOrphans(
        IReadOnlyList<InstalledProgram> installed,
        IReadOnlyList<MsixPackage> packages,
        int idleDays,
        string? koren = null)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(packages);

        var izvestnye = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var programma in installed)
        {
            foreach (var token in Tokeny(programma.Publisher, programma.DisplayName))
            {
                izvestnye.Add(token);
            }
        }

        foreach (var paket in packages)
        {
            foreach (var token in Tokeny(paket.Publisher, paket.DisplayName))
            {
                izvestnye.Add(token);
            }
        }

        var naydeno = new List<Leftover>();
        var propushcheno = new List<SkippedItem>();
        var lokalnyy = koren ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var seychas = DateTime.UtcNow;

        IEnumerable<string> katalogi;
        try
        {
            katalogi = Directory.GetDirectories(lokalnyy, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return new LeftoverSearch([], [new SkippedItem(lokalnyy, $"каталог не читается: {ex.Message}")]);
        }

        foreach (var katalog in katalogi)
        {
            var imya = Path.GetFileName(katalog);

            // A dotted folder belongs to AbandonedFolderDetector, and a name
            // that matches something installed belongs to that program.
            if (imya.StartsWith('.')
                || DetectorExclusions.IsExcluded(imya)
                || new DirectoryInfo(katalog).LinkTarget is not null)
            {
                continue;
            }

            if (SovpadaetImya(imya, [.. izvestnye]))
            {
                continue;
            }

            if (installed.Any(p => ProgramLeftoverGuard.IsAtOrUnder(katalog, p.InstallLocation)
                    || ProgramLeftoverGuard.IsAtOrUnder(p.InstallLocation, katalog))
                || packages.Any(p => ProgramLeftoverGuard.IsAtOrUnder(katalog, p.RootFolder)
                    || ProgramLeftoverGuard.IsAtOrUnder(p.RootFolder, katalog))
                || ProgramLeftoverGuard.IsAtOrUnder(AppContext.BaseDirectory, katalog)) { continue; }

            IEnumerable<string> vlozhennye;
            try
            {
                vlozhennye = Directory.GetDirectories(katalog, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                propushcheno.Add(new SkippedItem(katalog, $"каталог не читается: {ex.Message}"));
                continue;
            }

            var bylaMetka = false;

            foreach (var metka in vlozhennye)
            {
                if (!YavlyaetsyaMetkoy(Path.GetFileName(metka))
                    || new DirectoryInfo(metka).LinkTarget is not null)
                {
                    continue;
                }

                if (!SafetyGuard.TryVerify(metka, out var propusk, out var otkaz))
                {
                    propushcheno.Add(new SkippedItem(metka, otkaz));
                    continue;
                }

                var pozdneyshiy = PozdneyshayaZapis(propusk.Value);

                if (pozdneyshiy is null || (seychas - pozdneyshiy.Value).TotalDays < idleDays)
                {
                    continue;
                }

                var bayt = Razmer(propusk.Value);

                if (!ProgramLeftoverGuard.TrySnapshot(metka, metka, false, out _, out var unsafeReason))
                {
                    propushcheno.Add(new(metka, unsafeReason));
                    continue;
                }

                bylaMetka = true;

                naydeno.Add(new Leftover(
                    LeftoverKind.Directory,
                    propusk.Value,
                    bayt,
                    $"нет записи деинсталляции с именем '{imya}', "
                        + $"каталог не менялся {(int)(seychas - pozdneyshiy.Value).TotalDays} дней"));
            }

            if (!bylaMetka)
            {
                DobavitBezMetki(katalog, imya, idleDays, seychas, naydeno, propushcheno);
            }
        }

        return new LeftoverSearch(naydeno, propushcheno);
    }

    /// <summary>
    /// Second arm of the search: a vendor folder with no recognised marker
    /// inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by the seeded acceptance 05.09.2026 as a gap between two
    /// mechanisms. Dotted folders in the profile belong to
    /// AbandonedFolderDetector by its own design; dotless ones with a marker
    /// inside belong to the loop above; a dotless folder with no marker belonged
    /// to nobody. On a live machine that was seven directories and 62 MB, one of
    /// them Opera Software untouched for 403 days.
    /// </para>
    /// <para>
    /// The whole directory is proposed here, not a subdirectory, because there
    /// is no marker to narrow down to. That is a bigger claim than the arm
    /// above makes, so it costs more evidence: nothing installed answers to the
    /// name, the name is not one Windows owns, nothing has been written for the
    /// whole idle period, and the size threshold is four times higher. Risk tier
    /// either way, and nothing here selects anything for deletion.
    /// </para>
    /// </remarks>
    private static void DobavitBezMetki(
        string katalog,
        string imya,
        int idleDays,
        DateTime seychas,
        List<Leftover> naydeno,
        List<SkippedItem> propushcheno)
    {
        if (!SafetyGuard.TryVerify(katalog, out var propusk, out var otkaz))
        {
            propushcheno.Add(new SkippedItem(katalog, otkaz));
            return;
        }

        var pozdneyshiy = PozdneyshayaZapis(propusk.Value);

        if (pozdneyshiy is null || (seychas - pozdneyshiy.Value).TotalDays < idleDays)
        {
            return;
        }

        var bayt = Razmer(propusk.Value);

        if (!ProgramLeftoverGuard.TrySnapshot(katalog, katalog, false, out _, out var unsafeReason))
        {
            propushcheno.Add(new(katalog, unsafeReason));
            return;
        }

        propushcheno.Add(new(propusk.Value,
            $"каталог '{imya}' не менялся {(int)(seychas - pozdneyshiy.Value).TotalDays} дней; "
            + $"размер {bayt} байт, но возраст и отсутствие записи не доказывают, что это мусор"));
    }

    /// <summary>
    /// Words from publisher and product worth matching on. Everything shorter
    /// than five characters and every stop word is dropped: those match half the
    /// machine and each false positive here is somebody's file.
    /// </summary>
    public static IReadOnlyList<string> Tokeny(string publisher, string product)
    {
        var itog = new List<string>();

        foreach (var istochnik in new[] { publisher ?? string.Empty, product ?? string.Empty })
        {
            foreach (var kusok in istochnik.Split(RazdeliteliTokenov, StringSplitOptions.RemoveEmptyEntries))
            {
                if (kusok.Length < 5 || StopSlova.Contains(kusok))
                {
                    continue;
                }

                if (!itog.Contains(kusok, StringComparer.OrdinalIgnoreCase))
                {
                    itog.Add(kusok);
                }
            }
        }

        return itog;
    }

    /// <summary>
    /// Whether a directory name matches one of the tokens as a WHOLE word.
    /// Substring matching would take TelegramBotFarm for Telegram, and that is a
    /// different program belonging to a different person's afternoon.
    /// </summary>
    public static bool SovpadaetImya(string directoryName, IReadOnlyList<string> tokeny)
    {
        ArgumentNullException.ThrowIfNull(tokeny);

        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        var chasti = directoryName.Split(RazdeliteliTokenov, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokeny)
        {
            foreach (var chast in chasti)
            {
                if (chast.Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a subdirectory name is junk by its own name. Deliberately short
    /// and deliberately without "Crash Reports": that one belongs to the pattern
    /// scan, and two mechanisms claiming one path make the acceptance run fail
    /// on the source rather than on the count.
    /// </summary>
    public static bool YavlyaetsyaMetkoy(string childName) =>
        !string.IsNullOrWhiteSpace(childName) && Metki.Contains(childName.Trim());

    /// <summary>
    /// A trace as the rest of the product sees it. Always Risk, always carrying
    /// its basis, and never pre-selected by anything downstream.
    /// </summary>
    public static Finding ToFinding(Leftover leftover)
    {
        ArgumentNullException.ThrowIfNull(leftover);

        return new Finding(
            Name: Path.GetFileName(leftover.Path.TrimEnd(Path.DirectorySeparatorChar)),
            Path: leftover.Path,
            SizeBytes: leftover.SizeBytes,
            Tier: RiskTier.Risk,
            Consequence: $"Похоже на след удалённой программы: {leftover.Basis}. "
                + "Что там лежит, продукт не знает, и вернуть удалённое будет неоткуда.",
            Source: FindingSource.Program,
            RuleId: null,
            LastUsedDays: null);
    }

    private static IReadOnlyList<string> Korni() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    private static DateTime? PozdneyshayaZapis(string katalog)
    {
        DateTime? pozdneyshiy = null;

        var nastroyki = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            foreach (var fayl in Directory.EnumerateFiles(katalog, "*", nastroyki))
            {
                var zapisan = File.GetLastWriteTimeUtc(fayl);

                if (pozdneyshiy is null || zapisan > pozdneyshiy)
                {
                    pozdneyshiy = zapisan;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        return pozdneyshiy;
    }

    private static long Razmer(string katalog)
    {
        var nastroyki = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        long itog = 0;

        try
        {
            foreach (var fayl in Directory.EnumerateFiles(katalog, "*", nastroyki))
            {
                try
                {
                    itog += new FileInfo(fayl).Length;
                }
                catch (FileNotFoundException)
                {
                    // Vanished mid-walk. A smaller number, not an error.
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return itog;
        }

        return itog;
    }
}
