namespace JunkManager.Core;

/// <summary>
/// Everything one scan produced. Skipped items travel with the findings on
/// purpose: a scan that quietly drops what it could not read reports a smaller,
/// prettier number and hides the part a person most needs to see.
/// </summary>
/// <param name="Cancelled">
/// True when the scan stopped early. It exists so a partial result cannot be
/// read as a complete one: a cancelled scan that returns fewer findings looks
/// exactly like a clean machine, and that is the one wrong answer nobody
/// investigates.
/// </param>
public sealed record ScanResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<SkippedItem> Skipped,
    bool Cancelled = false)
{
    public static ScanResult Empty { get; } = new([], []);

    public long TotalBytes => Findings.Sum(f => f.SizeBytes);

    public long SafeBytes => Findings.Where(f => f.Tier == RiskTier.Safe).Sum(f => f.SizeBytes);

    public long RiskBytes => Findings.Where(f => f.Tier == RiskTier.Risk).Sum(f => f.SizeBytes);
}
