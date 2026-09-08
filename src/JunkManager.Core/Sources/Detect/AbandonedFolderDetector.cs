using JunkManager.Core.Scanning;

namespace JunkManager.Core.Sources.Detect;

/// <summary>
/// Dot-folders in the profile that nothing has written to in a long time. This
/// is a guess and is labelled as one: Risk tier, an explicit basis in the
/// consequence line, and never checked by default. The exclusions are what keep
/// the guess from being dangerous.
/// </summary>
/// <param name="time">
/// The clock, injectable for the same reason FileScanner and PatternScanner take
/// one: the idle cut-off is the whole detector, and a cut-off tested only by
/// shifting real timestamps around is a cut-off tested by luck.
/// </param>
public sealed class AbandonedFolderDetector(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> roots, int idleDays, CancellationToken ct)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        return Task.Run(() => Scan(roots, idleDays, nowUtc, ct), CancellationToken.None);
    }

    private static ScanResult Scan(
        IReadOnlyList<string> roots, int idleDays, DateTime nowUtc, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();

        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested)
            {
                return new ScanResult(findings, skipped, Cancelled: true);
            }

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.GetDirectories(root, ".*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                skipped.Add(new SkippedItem(root, $"каталог не читается: {ex.Message}"));
                continue;
            }

            foreach (var candidate in candidates)
            {
                var name = Path.GetFileName(candidate);

                if (DetectorExclusions.IsExcluded(name)
                    || new DirectoryInfo(candidate).LinkTarget is not null)
                {
                    continue;
                }

                if (!CleanupPathPolicy.TryVerify(candidate, out var verified, out var reason))
                {
                    skipped.Add(new SkippedItem(candidate, reason!));
                    continue;
                }
                var idle = (nowUtc - DirectorySize.NewestWriteUtc(candidate)).TotalDays;
                if (idle < idleDays) continue;

                skipped.Add(new SkippedItem(verified.Value,
                    $"каталог не менялся {(int)idle} дн.; возраст не доказывает отсутствие нужных данных, удаление не предлагается"));
            }
        }

        return new ScanResult(findings, skipped, ct.IsCancellationRequested);
    }
}
