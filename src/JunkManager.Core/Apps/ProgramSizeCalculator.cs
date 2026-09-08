using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Core.Apps;

/// <summary>
/// How much a program actually occupies, measured rather than estimated.
/// </summary>
/// <remarks>
/// EstimatedSize from the registry is never read. It is written once by the
/// installer and never updated, and on this machine Telegram Desktop understates
/// itself by a factor of 13.5 because it writes its cache into the install
/// directory. A forecast printed next to the word "освободится" is a lie; this
/// walks the tree instead, and when it cannot, it says so.
/// </remarks>
public static class ProgramSizeCalculator
{
    // InstallLocation on a live machine is sometimes one of these. Measuring
    // them would attribute every program on the machine to whichever one asked
    // first, which is how a 40 MB program comes to claim 60 GB. Derived from the
    // running system rather than typed as literals, for the same reason as
    // ForbiddenRoots: Windows does not always sit on C.
    private static readonly string[] Obshchie = SobratObshchie();

    public static bool TryMeasure(string? installLocation, out long bytes, out string? reason, CancellationToken ct = default)
    {
        bytes = 0;

        if (string.IsNullOrWhiteSpace(installLocation))
        {
            reason = "каталог установки не записан в реестре, размер неизвестен";
            return false;
        }

        // The shared-root check runs BEFORE the guard, and the order is the
        // point. The guard refuses C:\ and C:\Windows in its own words, which
        // say nothing about why a size is missing next to a program name; and it
        // lets C:\ProgramData through entirely, so this check has to exist here
        // regardless. One check in one place with one answer.
        if (ObshchiyKoren(installLocation, out var obshchiy))
        {
            reason = $"каталог установки это общий корень {obshchiy}, "
                + "его содержимое принадлежит не одной программе";
            return false;
        }

        // SafetyGuard здесь НЕ спрашивается, и это отступление от плана в пользу
        // спеки, раздел 10.2. Guard это дверь в УДАЛЕНИЕ, а замер это чтение:
        // отсюда ничего не удаляется, а программу сносит её собственный
        // деинсталлятор, а не путь, посчитанный этим методом. Спросить у guard
        // разрешения значит оставить без размера всё, что стоит в
        // C:\Program Files, то есть две трети списка вместе с Chrome, чьи
        // посчитанные 999 МБ прямо записаны в спеке как образец. Проверяется то,
        // что нужно чтению: абсолютный локальный путь, не общий корень.
        if (!Put(installLocation, out var kanonicheskiy, out var otkaz))
        {
            reason = otkaz;
            return false;
        }

        if (!Directory.Exists(kanonicheskiy))
        {
            reason = $"каталог {kanonicheskiy} не существует";
            return false;
        }

        if (new DirectoryInfo(kanonicheskiy).LinkTarget is not null)
        {
            reason = "каталог установки оказался ссылкой; размер неизвестен";
            return false;
        }

        long itog = 0;

        // Explicit stack rather than SearchOption.AllDirectories, exactly as in
        // FileScanner: that overload walks straight through a junction, and a
        // program folder with a junction into System32 would report the size of
        // Windows.
        var stek = new Stack<string>();
        stek.Push(kanonicheskiy);

        while (stek.Count > 0)
        {
            if (ct.IsCancellationRequested) { reason = "замер размера отменён"; return false; }
            var tekushchiy = stek.Pop();

            try
            {
                foreach (var vlozhennyy in Directory.EnumerateDirectories(tekushchiy))
                {
                    if (new DirectoryInfo(vlozhennyy).LinkTarget is null)
                    {
                        stek.Push(vlozhennyy);
                    }
                }

                foreach (var fayl in Directory.EnumerateFiles(tekushchiy))
                {
                    var svedeniya = new FileInfo(fayl);

                    if (svedeniya.LinkTarget is not null)
                    {
                        continue;
                    }

                    itog += svedeniya.Length;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // WindowsApps is the usual case here. A partial number shown as
                // a whole one is worse than an admitted gap.
                reason = $"каталог {tekushchiy} не читается: {ex.Message}. Нужны права администратора";
                bytes = 0;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                // Vanished mid-walk. Normal on a live machine, and the number is
                // simply smaller.
            }
            catch (IOException ex)
            {
                reason = $"каталог {tekushchiy} не читается: {ex.Message}";
                bytes = 0;
                return false;
            }
        }

        bytes = itog;
        reason = null;
        return true;
    }

    /// <summary>
    /// What a read needs from a path: absolute, local, and parseable. Nothing
    /// here decides whether the path may be deleted; that question belongs to
    /// SafetyGuard and is asked at deletion time, by the code that deletes.
    /// </summary>
    private static bool Put(
        string syroy, out string kanonicheskiy, [NotNullWhen(false)] out string? otkaz)
    {
        kanonicheskiy = string.Empty;
        var ochishchennyy = syroy.Trim();

        if (ochishchennyy.StartsWith(@"\\", StringComparison.Ordinal))
        {
            otkaz = "сетевые пути не обрабатываются, размер неизвестен";
            return false;
        }

        if (!Path.IsPathFullyQualified(ochishchennyy))
        {
            otkaz = $"путь установки не абсолютный: '{ochishchennyy}'";
            return false;
        }

        try
        {
            kanonicheskiy = Path.GetFullPath(ochishchennyy);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            otkaz = $"путь установки не разбирается: {ex.Message}";
            return false;
        }

        if (kanonicheskiy.Length > 3)
        {
            kanonicheskiy = kanonicheskiy.TrimEnd(Path.DirectorySeparatorChar);
        }

        otkaz = null;
        return true;
    }

    /// <summary>
    /// Whether the path IS one of the shared roots, never whether it lies under
    /// one: everything a program installs lies under Program Files, and refusing
    /// all of it would leave the product without a single measured size.
    /// </summary>
    private static bool ObshchiyKoren(string put, [NotNullWhen(true)] out string? koren)
    {
        string kanonicheskiy;

        try
        {
            kanonicheskiy = Path.GetFullPath(put.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Unparseable here means unparseable for the guard too, and the
            // guard is the one that words that refusal.
            koren = null;
            return false;
        }

        if (kanonicheskiy.Length > 3)
        {
            kanonicheskiy = kanonicheskiy.TrimEnd(Path.DirectorySeparatorChar);
        }

        foreach (var kandidat in Obshchie)
        {
            if (kanonicheskiy.Equals(kandidat, StringComparison.OrdinalIgnoreCase))
            {
                koren = kandidat;
                return true;
            }
        }

        koren = null;
        return false;
    }

    private static string[] SobratObshchie()
    {
        var itog = new List<string>();

        Dobavit(itog, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Dobavit(itog, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Dobavit(itog, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Dobavit(itog, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        // "C:\Users" has no SpecialFolder of its own: it is the parent of the
        // current profile, and deriving it beats a literal on any machine where
        // profiles were relocated.
        var profil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (profil.Length > 0)
        {
            Dobavit(itog, Path.GetDirectoryName(profil) ?? string.Empty);
        }

        // A bare volume root is the widest shared root there is. It reaches this
        // list through a real InstallLocation of "C:\", which exists on this
        // machine, and measuring it would put the whole disk behind one name.
        foreach (var disk in DriveInfo.GetDrives())
        {
            Dobavit(itog, disk.RootDirectory.FullName);
        }

        return [.. itog];
    }

    private static void Dobavit(List<string> spisok, string koren)
    {
        if (string.IsNullOrWhiteSpace(koren))
        {
            return;
        }

        // Same shape the comparison uses: trailing separator dropped, except on
        // a bare volume root where it is part of the path.
        var normalizovannyy = koren.Length > 3
            ? koren.TrimEnd(Path.DirectorySeparatorChar)
            : koren;

        if (!spisok.Contains(normalizovannyy, StringComparer.OrdinalIgnoreCase))
        {
            spisok.Add(normalizovannyy);
        }
    }
}
