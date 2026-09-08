using JunkManager.Core.Interop;
using JunkManager.Core.Rules;

namespace JunkManager.Core.Sources.VolumeCache;

/// <summary>
/// Turns Windows own cleanup handlers into ordinary findings. The point of the
/// source is that the biggest categories on a real machine are cleaned by
/// Windows itself, so the product never has to decide which file inside WinSxS
/// is spare.
/// </summary>
public sealed class VolumeCacheSource
{
    public Task<ScanResult> ScanAsync(IProgress<string>? progress, CancellationToken ct) =>
        ScanAsync(null, progress, ct);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification =
            "Источник находок это объект, а не набор функций. Их несколько, вызывающая " +
            "сторона держит их списком и перебирает единообразно, а статический метод " +
            "в такой список не кладётся. Состояния у источника пока нет, и это " +
            "временно: настройки прохода приедут сюда же.")]
    public Task<ScanResult> ScanAsync(
        string? rulesDirectory, IProgress<string>? progress, CancellationToken ct) =>
        Task.Run(() => Scan(rulesDirectory, progress, ct), CancellationToken.None);

    private static ScanResult Scan(
        string? rulesDirectory, IProgress<string>? progress, CancellationToken ct)
    {
        var described = rulesDirectory is null
            ? BuiltInCatalog.LoadVolumeCacheDescriptions()
            : VolumeCacheDescriptions.Load(rulesDirectory);
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();

        foreach (var entry in VolumeCacheCatalog.Enumerate())
        {
            if (ct.IsCancellationRequested)
            {
                return new ScanResult(findings, skipped, Cancelled: true);
            }

            var id = FindingPath.VolumeCacheScheme + entry.KeyName;
            progress?.Report(entry.DisplayName);

            if (!described.TryGetValue(entry.KeyName, out var description))
            {
                // Loud in the journal, invisible in the list. A new Windows build
                // that adds a handler must show up as an explicit gap, not as
                // silently unclaimed gigabytes.
                skipped.Add(new SkippedItem(
                    id, $"обработчик '{entry.DisplayName}' не описан в volume-caches.json"));
                continue;
            }

            var probe = EmptyVolumeCacheInterop.Probe(entry, ct, progress);

            if (probe.Failure is not null)
            {
                skipped.Add(new SkippedItem(id, probe.Failure));
                continue;
            }

            if (probe.SpaceUsedBytes <= 0)
            {
                continue;
            }

            findings.Add(new Finding(
                Name: description.Name,
                Path: id,
                SizeBytes: probe.SpaceUsedBytes,
                Tier: description.Tier,
                Consequence: description.Consequence,
                Source: FindingSource.VolumeCache));
        }

        return new ScanResult(findings, skipped);
    }
}
