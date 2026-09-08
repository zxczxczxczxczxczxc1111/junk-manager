using System.Globalization;
using System.Text;
using JunkManager.Core.Interop;
using JunkManager.Core.Sources.Detect;
using JunkManager.Safety;

namespace JunkManager.Core.Explain;

public sealed record ExplainedBucket(string Name, long Bytes);

public sealed record DiskExplanation(
    string Volume,
    long TotalBytes,
    long FreeBytes,
    long UsedBytes,
    IReadOnlyList<ExplainedBucket> Buckets,
    IReadOnlyList<ExplainedBucket> TopLevelRemainder)
{
    public long ExplainedBytes => Buckets.Sum(b => b.Bytes);

    /// <summary>
    /// Clamped at zero. Sources overlap by design, so the sum can exceed the
    /// used space, and a negative remainder would read as a defect in the disk
    /// rather than in the arithmetic.
    /// </summary>
    public long UnexplainedBytes => Math.Max(0, UsedBytes - ExplainedBytes);

    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.CurrentCulture, $"том {Volume}");
        text.AppendLine(CultureInfo.CurrentCulture, $"  всего   {Gb(TotalBytes)}");
        text.AppendLine(CultureInfo.CurrentCulture, $"  занято  {Gb(UsedBytes)}");
        text.AppendLine(CultureInfo.CurrentCulture, $"  свободно {Gb(FreeBytes)}");
        text.AppendLine("объяснено:");

        foreach (var bucket in Buckets.OrderByDescending(b => b.Bytes))
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"  {bucket.Name,-24} {Gb(bucket.Bytes)}");
        }

        text.AppendLine(CultureInfo.CurrentCulture, $"не объяснено {Gb(UnexplainedBytes)}");

        // Сумма строк ниже БОЛЬШЕ занятого места, и это не ошибка счёта: на
        // машине владельца 140.32 ГБ против 115.90 ГБ занятых. Жёсткие ссылки
        // NTFS (файлы WinSxS и System32 это одни и те же байты) попадают в
        // каждый каталог, где на них ссылаются. Оговорка стоит рядом с
        // числами, иначе человек их сложит и решит, что диск врёт.
        text.AppendLine("крупнейшие каталоги верхнего уровня (логический размер, жёсткие ссылки считаются в каждом):");

        foreach (var bucket in TopLevelRemainder.OrderByDescending(b => b.Bytes).Take(15))
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"  {bucket.Name,-24} {Gb(bucket.Bytes)}");
        }

        return text.ToString();
    }

    private static string Gb(long bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.CurrentCulture) + " ГБ";
}

/// <summary>
/// The measurable form of the depth requirement. Not "we wrote more rules" but
/// "there is less space we cannot account for". The number is meant to be
/// recorded before and after every wave of new rules, otherwise there is nothing
/// to compare against and the requirement becomes a feeling.
/// </summary>
public static class DiskExplainer
{
    /// <param name="extra">
    /// Everything the product accounts for without a finding: program folders,
    /// user profiles, the page file, hibernation, shadow copies. These carry no
    /// path, so nothing can be deduplicated against them, and a caller that
    /// hands in bytes already covered by a finding overstates what is explained.
    /// </param>
    /// <param name="topLevel">
    /// The volume's top-level breakdown, from <see cref="TopLevel"/>. Passed in
    /// rather than measured here because measuring it walks the entire disk, and
    /// a test that only checks the arithmetic must not pay for that.
    /// </param>
    public static DiskExplanation Explain(
        string volumeRoot,
        ScanResult scan,
        IReadOnlyList<ExplainedBucket> extra,
        IReadOnlyList<ExplainedBucket>? topLevel = null)
    {
        ArgumentNullException.ThrowIfNull(scan);

        if (!DiskSpace.TryGetVolume(volumeRoot, out var total, out var free))
        {
            // Zeroes, and they are honest: every number below is derived from
            // these two, and inventing them would make the whole report lie.
            total = 0;
            free = 0;
        }

        var buckets = BezVlozhennyh(scan.Findings)
            .GroupBy(f => f.Source)
            .Select(g => new ExplainedBucket(g.Key.ToString(), g.Sum(f => f.SizeBytes)))
            .Concat(extra)
            .ToList();

        return new DiskExplanation(
            Volume: volumeRoot,
            TotalBytes: total,
            FreeBytes: free,
            UsedBytes: total - free,
            Buckets: buckets,
            TopLevelRemainder: topLevel ?? []);
    }

    /// <summary>
    /// Drops every finding that lies inside another finding, keeping the wider
    /// one, before a single byte is added up.
    /// </summary>
    /// <remarks>
    /// FileScanner deduplicates by nesting too, but only inside one pass over
    /// the rules: findings of DIFFERENT sources meet each other for the first
    /// time here. The overlap is not hypothetical, it is designed in: the
    /// detectors propose Cache and GPUCache subfolders that the browser rules
    /// already take whole. Deletion stays honest either way, because the second
    /// pass finds the files already gone. The preview is what would lie, and the
    /// preview is what a person decides by.
    /// </remarks>
    private static List<Finding> BezVlozhennyh(IReadOnlyList<Finding> findings)
    {
        var razobrannye = new List<(Finding Nahodka, VerifiedPath? Put)>(findings.Count);

        foreach (var nahodka in findings)
        {
            // A handler, a registry value or a path the guard refuses is an
            // identity, not a location: nothing can be said about it containing
            // or being contained, so it is kept exactly as it came.
            razobrannye.Add(
                FindingPath.IsFileSystem(nahodka.Path)
                && SafetyGuard.TryVerify(nahodka.Path, out var put, out _)
                    ? (nahodka, put)
                    : (nahodka, null));
        }

        var prinyatye = new List<VerifiedPath>();
        var otobrannye = new List<Finding>();

        // Widest first, so the wider finding always survives, whatever order the
        // sources happened to be merged in.
        foreach (var (nahodka, put) in razobrannye.OrderBy(p => p.Put?.Value.Length ?? 0))
        {
            if (put is not { } proverennyy)
            {
                otobrannye.Add(nahodka);
                continue;
            }

            if (prinyatye.Exists(shirokiy => SafetyGuard.Contains(shirokiy, proverennyy)))
            {
                continue;
            }

            prinyatye.Add(proverennyy);
            otobrannye.Add(nahodka);
        }

        return otobrannye;
    }

    /// <summary>
    /// Sizes of every top-level directory of the volume. A full disk walk:
    /// minutes on a real machine, which is why it is a separate call and not
    /// part of <see cref="Explain"/>.
    /// </summary>
    public static IReadOnlyList<ExplainedBucket> TopLevel(string volumeRoot)
    {
        var buckets = new List<ExplainedBucket>();

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(volumeRoot);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return buckets;
        }

        foreach (var directory in directories)
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                continue;
            }

            buckets.Add(new ExplainedBucket(
                Path.GetFileName(directory),
                DirectorySize.Measure(directory)));
        }

        return buckets;
    }
}
