using System.Text.RegularExpressions;

namespace JunkManager.Core.Apps;

/// <summary>
/// "Last run N days ago", from Prefetch and from nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// UserAssist was checked on a live machine and rejected: nine Count subkeys,
/// zero values in all of them, verified by a recursive walk in a 64-bit process.
/// LastAccessTime was rejected too, DisableLastAccess=1 makes it equal to
/// creation time. The LastWriteTime of the registry key is the time of the last
/// auto-update, which matched Brave, Chrome and WinSCP file updates to the
/// second.
/// </para>
/// <para>
/// What this answers is "the file was started", not "a person opened a window":
/// the scheduler starts things too. And an unknown age is null, never zero.
/// </para>
/// </remarks>
public static partial class PrefetchUsageReader
{
    private static readonly string Katalog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");

    [GeneratedRegex(@"^[0-9A-F]{8}$", RegexOptions.IgnoreCase)]
    private static partial Regex Hesh();

    /// <summary>
    /// Whether the folder can actually be read, established by reading it.
    /// </summary>
    public static bool IsAvailable(out string? reason)
    {
        try
        {
            _ = Directory.EnumerateFiles(Katalog, "*.pf").Take(1).ToList();
            reason = null;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = $"каталог {Katalog} требует прав администратора";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            reason = $"каталога {Katalog} нет, предзагрузка отключена";
            return false;
        }
        catch (IOException ex)
        {
            reason = $"каталог {Katalog} не читается: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Days since anything inside the install location was last started, or null
    /// when that cannot be established.
    /// </summary>
    public static int? DaysSinceLastRun(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        if (!IsAvailable(out _))
        {
            return null;
        }

        List<string> imenaPrefetch;
        try
        {
            // A lambda rather than a method group on purpose: the method group
            // converts to Func<string, string?> and loses the NotNullIfNotNull
            // annotation, which turns an always-present file name into a warning
            // and then into a null-forgiving operator nobody can justify.
            imenaPrefetch = [.. Directory.EnumerateFiles(Katalog, "*.pf").Select(fayl => Path.GetFileName(fayl))];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        DateTime? pozdneyshiy = null;

        var nastroyki = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        IEnumerable<string> ispolnyaemye;
        try
        {
            ispolnyaemye = Directory.EnumerateFiles(installLocation, "*.exe", nastroyki);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        foreach (var exe in ispolnyaemye)
        {
            foreach (var sled in PodobratFayly(Path.GetFileName(exe), imenaPrefetch))
            {
                try
                {
                    var zapisan = File.GetLastWriteTimeUtc(Path.Combine(Katalog, sled));

                    if (pozdneyshiy is null || zapisan > pozdneyshiy)
                    {
                        pozdneyshiy = zapisan;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // One unreadable trace does not make the rest unknown.
                }
            }
        }

        return Dney(pozdneyshiy, DateTime.UtcNow);
    }

    /// <summary>
    /// Prefetch files belonging to one executable. Exact name plus an eight
    /// character hash, never a prefix: TELEGRAM.EXE and TELEGRAMDESKTOP.EXE are
    /// different programs, and prefix matching would report the wrong one as
    /// recently used.
    /// </summary>
    public static IReadOnlyList<string> PodobratFayly(string exeName, IReadOnlyList<string> imenaFaylov)
    {
        ArgumentNullException.ThrowIfNull(imenaFaylov);

        if (string.IsNullOrWhiteSpace(exeName))
        {
            return [];
        }

        var iskomoe = exeName.Trim();
        var itog = new List<string>();

        foreach (var imya in imenaFaylov)
        {
            if (!imya.EndsWith(".pf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bezRasshireniya = imya[..^3];
            var defis = bezRasshireniya.LastIndexOf('-');

            if (defis <= 0)
            {
                continue;
            }

            if (!Hesh().IsMatch(bezRasshireniya[(defis + 1)..]))
            {
                continue;
            }

            if (bezRasshireniya[..defis].Equals(iskomoe, StringComparison.OrdinalIgnoreCase))
            {
                itog.Add(imya);
            }
        }

        return itog;
    }

    /// <summary>
    /// Whole days, or null. Null is the entire point of this method existing
    /// separately: an unknown age displayed as zero reads as "used today".
    /// </summary>
    public static int? Dney(DateTime? lastRunUtc, DateTime nowUtc)
    {
        if (lastRunUtc is null)
        {
            return null;
        }

        var dney = (int)Math.Floor((nowUtc - lastRunUtc.Value).TotalDays);
        return dney < 0 ? 0 : dney;
    }
}
