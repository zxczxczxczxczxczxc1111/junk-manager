using JunkManager.Safety;

namespace JunkManager.Core.Scanning;

/// <summary>
/// Merges findings from a second source into a scan, dropping anything whose
/// bytes are already counted.
/// </summary>
/// <remarks>
/// <para>
/// Deduplication by nesting exists inside <see cref="FileScanner"/>, but it runs
/// during the pass over the rules and therefore sees rules only. Between sources
/// there was nothing at all, and the closest collision is real: LeftoverFinder
/// proposes a Cache subdirectory while the browser rules already take that exact
/// path. Counted twice, 5000 bytes on disk are shown as 10000, and a person
/// decides by that number.
/// </para>
/// <para>
/// On any overlap the added finding loses, in both directions. That is not
/// symmetry for its own sake: what gets added this way is a guess (a trace), and
/// what is already in the list came from a rule that knows what it takes. The
/// dropped path is named in Skipped together with the path that absorbed it,
/// because a silently dropped finding is indistinguishable from a source that
/// found nothing, and those two need opposite fixes.
/// </para>
/// </remarks>
public static class SliyanieIstochnikov
{
    public static ScanResult Slit(
        ScanResult osnova,
        IReadOnlyList<Finding> dobavlyaemye,
        IReadOnlyList<SkippedItem> propuski)
    {
        ArgumentNullException.ThrowIfNull(osnova);
        ArgumentNullException.ThrowIfNull(dobavlyaemye);
        ArgumentNullException.ThrowIfNull(propuski);

        var nahodki = new List<Finding>(osnova.Findings);
        var propushchennye = new List<SkippedItem>(osnova.Skipped);
        propushchennye.AddRange(propuski);

        foreach (var kandidat in dobavlyaemye)
        {
            if (kandidat.Scope == DeleteScope.SelectedEntries && kandidat.Source != FindingSource.Vacuum)
            {
                var targets = kandidat.DeletionTargets.Where(target =>
                    !nahodki.Any(existing => existing.Source != FindingSource.Vacuum
                        && existing.DeletionTargets.Any(covered => Covers(covered, target)))).ToArray();
                if (targets.Length == kandidat.DeletionTargets.Count)
                {
                    nahodki.Add(kandidat);
                    continue;
                }
                propushchennye.Add(new SkippedItem(kandidat.Path, "часть файлов уже посчитана другим источником"));
                if (targets.Length == 0) continue;
                var readable = new List<string>();
                long bytes = 0;
                foreach (var target in targets)
                {
                    try
                    {
                        if (!CleanupPathPolicy.TryVerify(target, out _, out var reason))
                        {
                            propushchennye.Add(new SkippedItem(target, reason!));
                            continue;
                        }
                        bytes += new FileInfo(target).Length;
                        readable.Add(target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        propushchennye.Add(new SkippedItem(target, ex.Message));
                    }
                }
                if (readable.Count > 0) nahodki.Add(kandidat with { Targets = readable, SizeBytes = bytes });
                continue;
            }
            if (!Peresekaetsya(kandidat, nahodki, out var pogloshchayushchiy))
            {
                nahodki.Add(kandidat);
                continue;
            }

            propushchennye.Add(new SkippedItem(
                kandidat.Path,
                $"уже посчитан по пути {pogloshchayushchiy}: два источника на один путь "
                + "удваивают цифру освобождаемого места"));
        }

        return new ScanResult(nahodki, propushchennye, osnova.Cancelled);
    }

    /// <summary>
    /// Whether the candidate shares bytes with anything already in the list.
    /// Non-filesystem identities (a registry value, a cleanup handler) never
    /// overlap a directory and are compared by string only.
    /// </summary>
    private static bool Peresekaetsya(
        Finding kandidat, List<Finding> nahodki, out string? pogloshchayushchiy)
    {
        foreach (var uzhe in nahodki)
        {
            if (kandidat.Source == FindingSource.Vacuum || uzhe.Source == FindingSource.Vacuum)
            {
                if (kandidat.Source == uzhe.Source && kandidat.Path.Equals(uzhe.Path, StringComparison.OrdinalIgnoreCase))
                {
                    pogloshchayushchiy = uzhe.Path;
                    return true;
                }
                continue;
            }
            if (uzhe.Scope == DeleteScope.SelectedEntries)
            {
                if (uzhe.DeletionTargets.Any(target => Covers(kandidat.Path, target) || Covers(target, kandidat.Path)))
                {
                    pogloshchayushchiy = uzhe.Path;
                    return true;
                }
                continue;
            }
            if (kandidat.Path.Equals(uzhe.Path, StringComparison.OrdinalIgnoreCase))
            {
                pogloshchayushchiy = uzhe.Path;
                return true;
            }

            if (!FindingPath.IsFileSystem(kandidat.Path) || !FindingPath.IsFileSystem(uzhe.Path))
            {
                continue;
            }

            if (!SafetyGuard.TryVerify(kandidat.Path, out var propuskKandidata, out _)
                || !SafetyGuard.TryVerify(uzhe.Path, out var propuskUzhe, out _))
            {
                // A path the guard refuses is a path nothing will delete anyway.
                // Containment between two such strings is not worth guessing at.
                continue;
            }

            if (SafetyGuard.Contains(propuskUzhe, propuskKandidata)
                || SafetyGuard.Contains(propuskKandidata, propuskUzhe))
            {
                pogloshchayushchiy = uzhe.Path;
                return true;
            }
        }

        pogloshchayushchiy = null;
        return false;
    }

    private static bool Covers(string root, string path) =>
        root.Equals(path, StringComparison.OrdinalIgnoreCase)
        || (FindingPath.IsFileSystem(root) && FindingPath.IsFileSystem(path)
            && SafetyGuard.TryVerify(root, out var checkedRoot, out _)
            && SafetyGuard.TryVerify(path, out var checkedPath, out _)
            && SafetyGuard.Contains(checkedRoot, checkedPath));
}
