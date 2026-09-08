using JunkManager.Core;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>
/// Estimates free database pages from an explicitly supplied candidate catalog.
/// Compaction is a manual operation, never a preselected safe cleanup.
/// </summary>
/// <remarks>
/// The estimate itself only reads. LockedFileInspector supplies the name of
/// a process preventing access; this keeps the existing public source contract.
/// </remarks>
public sealed class SqliteCompactSource
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification =
            "Источник находок это объект, а не набор функций. Их несколько, вызывающая " +
            "сторона держит их списком и перебирает единообразно, а статический метод " +
            "в такой список не кладётся. То же решение, что и у VolumeCacheSource.")]
    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> candidates, IProgress<string>? progress, CancellationToken ct) =>
        Task.Run(() => Scan(candidates, progress, ct), CancellationToken.None);

    private static ScanResult Scan(
        IReadOnlyList<string> candidates, IProgress<string>? progress, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
            {
                return new ScanResult(findings, skipped, Cancelled: true);
            }

            if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason))
            {
                skipped.Add(new SkippedItem(candidate, reason));
                continue;
            }

            progress?.Report(verified.Value);
            var estimate = SqliteCompactor.Measure(verified.Value);

            if (estimate.Failure is not null)
            {
                skipped.Add(new SkippedItem(
                    verified.Value, estimate.Failure, KtoDerzhit(verified.Value)));
                continue;
            }

            if (estimate.ReclaimableBytes == 0)
            {
                continue;
            }

            findings.Add(new Finding(
                Name: $"Сжатие базы {Path.GetFileName(verified.Value)}",
                Path: verified.Value,
                SizeBytes: estimate.ReclaimableBytes,
                Tier: RiskTier.Risk,
                Consequence: "База будет перезаписана для освобождения пустых страниц. Закройте приложение. Выигрыш приблизительный; неподдерживаемая схема будет пропущена.",
                Source: FindingSource.Vacuum) { IsSizeEstimate = true });
        }

        return new ScanResult(findings, skipped, ct.IsCancellationRequested);
    }

    /// <summary>
    /// Who is holding the database, or null when nobody is or when the Restart
    /// Manager could not say. Asked for every failure rather than only for the
    /// lock code on purpose: sqlite answers CantOpen for a permission problem
    /// too, and the honest way to tell the two apart is to look.
    /// </summary>
    private static string? KtoDerzhit(string path) =>
        LockedFileInspector.TryGetHolders(path, out var kto, out _) && kto.Count > 0
            ? string.Join(", ", kto.Select(k => k.Describe()))
            : null;
}
