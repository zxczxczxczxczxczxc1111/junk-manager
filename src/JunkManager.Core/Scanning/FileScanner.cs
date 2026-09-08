using System.IO.Enumeration;
using JunkManager.Core.Rules;
using JunkManager.Safety;

namespace JunkManager.Core.Scanning;

/// <summary>Snapshots eligible files. A directory name is not permission to delete its future occupants.</summary>
public sealed class FileScanner(TimeProvider? time = null,
    Func<IReadOnlyList<string>, string?>? processRefusal = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<IReadOnlyList<string>, string?> _processRefusal = processRefusal ?? CleanupProcessGuard.Refusal;

    public Task<ScanResult> ScanAsync(IReadOnlyList<RuleDefinition> rules,
        IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        return Task.Run(() => Scan(rules, progress, nowUtc, ct), CancellationToken.None);
    }

    private ScanResult Scan(IReadOnlyList<RuleDefinition> rules, IProgress<string>? progress,
        DateTime nowUtc, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report(rule.Name);
            var processReason = _processRefusal(rule.ProcessNames);
            foreach (var pattern in rule.Paths)
            {
                if (ct.IsCancellationRequested) break;
                List<string> expanded;
                try { expanded = [.. PathExpander.Expand(pattern)]; }
                catch (Exception ex) when (ex is RuleFormatException or IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new SkippedItem(pattern, ex.Message));
                    continue;
                }
                foreach (var path in expanded)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!CleanupPathPolicy.TryVerify(path, out var root, out var reason))
                    {
                        skipped.Add(new SkippedItem(path, reason!));
                        continue;
                    }
                    var targets = new List<string>();
                    var snapshots = new Dictionary<string, CleanupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
                    long bytes = 0;
                    DateTime? newest = null;
                    var pending = new Stack<string>();
                    pending.Push(root.Value);
                    while (pending.TryPop(out var current) && !ct.IsCancellationRequested)
                    {
                        if (!CleanupPathPolicy.TryVerify(root.Value, out var freshRoot, out reason)
                            || !CleanupPathPolicy.TryVerify(current, out var checkedPath, out reason)
                            || !SafetyGuard.Contains(freshRoot, checkedPath))
                        {
                            skipped.Add(new SkippedItem(current, reason ?? "цель вне корня находки"));
                            continue;
                        }
                        try
                        {
                            if (Directory.Exists(current))
                            {
                                foreach (var child in Directory.EnumerateFileSystemEntries(current))
                                {
                                    if (ct.IsCancellationRequested) break;
                                    pending.Push(child);
                                }
                                continue;
                            }
                            var info = new FileInfo(current);
                            if (!info.Exists) throw new FileNotFoundException("файл исчез во время сканирования", current);
                            var written = info.LastWriteTimeUtc;
                            if (rule.OlderThanDays > 0 && Days(written, nowUtc) < rule.OlderThanDays) continue;
                            if (rule.FileFilter is { Length: > 0 } filter
                                && !FileSystemName.MatchesSimpleExpression(filter, info.Name, ignoreCase: true)) continue;
                            var length = info.Length;
                            sizes[current] = length;
                            bytes += length;
                            targets.Add(current);
                            snapshots[current] = CleanupFileSnapshot.Capture(info);
                            if (newest is null || written > newest) newest = written;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skipped.Add(new SkippedItem(current, $"не удалось прочитать: {ex.Message}"));
                        }
                    }
                    if (targets.Count == 0)
                    {
                        continue;
                    }
                    // Zero bytes is a size, not a witness protection programme for empty files.
                    // Reading sizes is allowed while an app runs; deletion still checks its process guard.
                    var consequence = processReason is null ? rule.Consequence : rule.Consequence + " Перед очисткой: " + processReason;
                    findings.Add(new Finding(rule.Name, root.Value, bytes, rule.Tier, consequence,
                        FindingSource.Rule, rule.Id, newest is null ? null : Days(newest.Value, nowUtc),
                        Directory.Exists(root.Value) ? DeleteScope.SelectedEntries : DeleteScope.Whole, targets)
                    {
                        RequiredStoppedProcesses = rule.ProcessNames,
                        FileSnapshots = snapshots,
                    });
                }
            }
        }
        return new ScanResult(Merge(findings, sizes, skipped), skipped, ct.IsCancellationRequested);
    }

    private static List<Finding> Merge(List<Finding> findings, Dictionary<string, long> sizes, List<SkippedItem> skipped)
    {
        var merged = new List<Finding>();
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in findings.OrderBy(item => item.Path.Length)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.RuleId, StringComparer.Ordinal))
        {
            var remaining = new List<string>();
            var absorbed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in finding.DeletionTargets.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!owners.TryGetValue(target, out var index)) { remaining.Add(target); continue; }
                var owner = merged[index];
                absorbed.Add(owner.Path);
                // Deduplication must not launder a risky rule into a safe checkbox. Accounting is not absolution.
                merged[index] = owner with
                {
                    Tier = owner.Tier == RiskTier.Risk || finding.Tier == RiskTier.Risk ? RiskTier.Risk : RiskTier.Safe,
                    RequiredStoppedProcesses = owner.RequiredStoppedProcesses.Concat(finding.RequiredStoppedProcesses)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Consequence = owner.Tier != RiskTier.Risk && finding.Tier == RiskTier.Risk
                        ? owner.Consequence + " " + finding.Consequence : owner.Consequence,
                };
            }
            if (absorbed.Count > 0) skipped.Add(new(finding.Path, "файлы уже посчитаны по пути " + string.Join(", ", absorbed)));
            if (remaining.Count == 0) continue;
            foreach (var target in remaining) owners.Add(target, merged.Count);
            merged.Add(finding with { Targets = remaining, SizeBytes = remaining.Sum(target => sizes[target]) });
        }
        return merged;
    }

    private static int Days(DateTime whenUtc, DateTime nowUtc) =>
        Math.Max(0, (int)Math.Floor((nowUtc - whenUtc).TotalDays));
}
