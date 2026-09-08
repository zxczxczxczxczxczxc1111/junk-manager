using JunkManager.Safety;

namespace JunkManager.Core.Scanning;

/// <summary>Merges sources without counting the same files twice. Earlier findings keep precedence.</summary>
public static class SliyanieIstochnikov
{
    public static ScanResult Slit(ScanResult osnova, IReadOnlyList<Finding> dobavlyaemye,
        IReadOnlyList<SkippedItem> propuski, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(osnova);
        ArgumentNullException.ThrowIfNull(dobavlyaemye);
        ArgumentNullException.ThrowIfNull(propuski);
        var findings = new List<Finding>(osnova.Findings);
        var skipped = new List<SkippedItem>(osnova.Skipped);
        skipped.AddRange(propuski);
        var index = new CoverageIndex(ct);
        try
        {
            foreach (var finding in osnova.Findings) index.Add(finding);
            foreach (var candidate in dobavlyaemye)
            {
                ct.ThrowIfCancellationRequested();
                if (candidate.Scope == DeleteScope.SelectedEntries && candidate.Source != FindingSource.Vacuum)
                {
                    var targets = new List<string>();
                    foreach (var target in candidate.DeletionTargets)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!index.Find(target, includeChildren: false, out _)) targets.Add(target);
                    }
                    if (targets.Count == candidate.DeletionTargets.Count)
                    {
                        Include(candidate);
                        continue;
                    }
                    skipped.Add(new(candidate.Path, "часть файлов уже посчитана другим источником"));
                    if (targets.Count == 0) continue;
                    var readable = new List<string>();
                    long bytes = 0;
                    foreach (var target in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (!CleanupPathPolicy.TryVerify(target, out _, out var reason))
                            { skipped.Add(new(target, reason!)); continue; }
                            bytes += new FileInfo(target).Length;
                            readable.Add(target);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { skipped.Add(new(target, ex.Message)); }
                    }
                    if (readable.Count > 0) Include(candidate with { Targets = readable, SizeBytes = bytes });
                    continue;
                }

                var overlaps = candidate.Source == FindingSource.Vacuum
                    ? index.Vacuums.TryGetValue(candidate.Path, out var owner)
                    : index.Find(candidate.Path, includeChildren: true, out owner);
                if (overlaps)
                    skipped.Add(new(candidate.Path, $"уже посчитан по пути {owner}: два источника на один путь удваивают цифру освобождаемого места"));
                else Include(candidate);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Publish only whole findings. Half a target list with a full size is accounting cosplay.
            return new(findings, skipped, Cancelled: true);
        }
        return new(findings, skipped, osnova.Cancelled || ct.IsCancellationRequested);

        void Include(Finding finding)
        {
            findings.Add(finding);
            index.Add(finding);
        }
    }

    private sealed class CoverageIndex(CancellationToken ct)
    {
        private readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _ancestors = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Vacuums { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(Finding finding)
        {
            ct.ThrowIfCancellationRequested();
            if (finding.Source == FindingSource.Vacuum)
            { Vacuums.TryAdd(finding.Path, finding.Path); return; }
            foreach (var target in finding.DeletionTargets)
            {
                ct.ThrowIfCancellationRequested();
                _exact.TryAdd(target, finding.Path);
                if (!FindingPath.IsFileSystem(target) || !SafetyGuard.TryVerify(target, out var verified, out _)) continue;
                _roots.TryAdd(verified.Value, finding.Path);
                for (var path = verified.Value; path is not null; path = Path.GetDirectoryName(path))
                {
                    ct.ThrowIfCancellationRequested();
                    // Shared parents are already indexed all the way up. No family reunion per file.
                    if (!_ancestors.TryAdd(path, finding.Path)) break;
                }
            }
        }

        public bool Find(string target, bool includeChildren, out string? owner)
        {
            ct.ThrowIfCancellationRequested();
            if (_exact.TryGetValue(target, out owner)) return true;
            if (!FindingPath.IsFileSystem(target) || !SafetyGuard.TryVerify(target, out var verified, out _)) return false;
            for (var path = verified.Value; path is not null; path = Path.GetDirectoryName(path))
            {
                ct.ThrowIfCancellationRequested();
                if (_roots.TryGetValue(path, out owner)) return true;
            }
            return includeChildren && _ancestors.TryGetValue(verified.Value, out owner);
        }
    }
}
