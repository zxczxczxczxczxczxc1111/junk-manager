using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunkManager.Core.Reporting;

/// <summary>
/// The machine-readable form of a scan. It lives in Core rather than in the CLI
/// because its shape is a contract the interface and the acceptance run both
/// read, and a contract that only exists inside a Main method cannot be tested.
/// </summary>
public static class ScanJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Russian text goes out as readable Russian, not as \uXXXX escapes. The
        // output is read by people at least as often as by programs.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(ScanResult result) =>
        JsonSerializer.Serialize(ScanReport.From(result), Options);

    public static ScanReport? Deserialize(string json) =>
        JsonSerializer.Deserialize<ScanReport>(json, Options);
}

public sealed record ScanReport(
    bool Cancelled,
    long TotalBytes,
    long SafeBytes,
    long RiskBytes,
    IReadOnlyList<FindingReport> Findings,
    IReadOnlyList<SkippedReport> Skipped)
{
    public static ScanReport From(ScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ScanReport(
            result.Cancelled,
            result.TotalBytes,
            result.SafeBytes,
            result.RiskBytes,
            [.. result.Findings
                .OrderByDescending(f => f.SizeBytes)
                .Select(f => new FindingReport(
                    f.Name, f.Path, f.SizeBytes, f.Tier, f.Consequence, f.Source, f.RuleId, f.LastUsedDays,
                    f.Scope, f.DeletionTargets.Count))],
            [.. result.Skipped.Select(s => new SkippedReport(s.Path, s.Reason, s.HoldingProcess))]);
    }
}

/// <param name="Scope">
/// Whole means the path goes with everything in it. SelectedEntries means only
/// <paramref name="TargetCount"/> entries inside it go and the directory stays.
/// A confirmation screen that does not show this difference is asking a person
/// to approve something other than what happens.
/// </param>
public sealed record FindingReport(
    string Name,
    string Path,
    long SizeBytes,
    RiskTier Tier,
    string Consequence,
    FindingSource Source,
    string? RuleId,
    int? LastUsedDays,
    DeleteScope Scope = DeleteScope.Whole,
    int TargetCount = 1);

public sealed record SkippedReport(string Path, string Reason, string? HoldingProcess);
