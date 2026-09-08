using System.Globalization;
using JunkManager.Core.Rules;

namespace JunkManager.Core.Sources.Platform;

public sealed record ComponentStoreReport(
    long ActualSizeBytes, long BackupsBytes, int ReclaimablePackages, bool CleanupRecommended);

/// <summary>
/// Reads what DISM says about the component store and builds the two commands
/// the product is allowed to run. The analyse command reads. The cleanup command
/// removes superseded components and NOTHING ELSE: /ResetBase is not an option
/// that happens to be off, it is a string that never enters the argument list,
/// and there is a test that says so.
/// </summary>
public static class DismComponentStore
{
    private const string MarkerActual = "Actual Size of Component Store";
    private const string MarkerBackups = "Backups and Disabled Features";
    private const string MarkerPackages = "Number of Reclaimable Packages";
    private const string MarkerRecommended = "Component Store Cleanup Recommended";

    public static IReadOnlyList<string> AnalyzeArguments() =>
        ["/Online", "/English", "/Cleanup-Image", "/AnalyzeComponentStore"];

    public static IReadOnlyList<string> CleanupArguments() =>
        ["/Online", "/English", "/Cleanup-Image", "/StartComponentCleanup"];

    public static ComponentStoreReport Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var actual = FindValue(output, MarkerActual);
        var backups = FindValue(output, MarkerBackups);
        var packages = FindValue(output, MarkerPackages);
        var recommended = FindValue(output, MarkerRecommended);

        if (backups is null)
        {
            // Fail-closed. Zero here would read as "the store is clean", and the
            // product would report an honest-looking nothing forever.
            throw new RuleFormatException(
                $"вывод DISM не содержит метку '{MarkerBackups}'. " +
                $"Разбор не состоялся, показывать ноль нельзя");
        }

        return new ComponentStoreReport(
            ActualSizeBytes: actual is null ? 0 : ParseSize(actual),
            BackupsBytes: ParseSize(backups),
            ReclaimablePackages: packages is not null
                && int.TryParse(packages, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : 0,
            CleanupRecommended: string.Equals(recommended, "Yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindValue(string output, string marker)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();

            if (!line.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var colon = line.IndexOf(':', marker.Length - 1);

            if (colon < 0)
            {
                continue;
            }

            // Trim, not Substring(colon + 2): DISM prints a second space before
            // "0 bytes" and a fixed offset silently loses the digit.
            return line[(colon + 1)..].Trim();
        }

        return null;
    }

    /// <summary>
    /// "11.25 GB" to bytes. DISM with /English prints a dot as the decimal
    /// separator regardless of the system locale, so parsing is invariant.
    /// </summary>
    internal static long ParseSize(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new RuleFormatException($"размер от DISM не разбирается: '{text}'");
        }

        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "BYTES" or "BYTE" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => throw new RuleFormatException($"неизвестная единица размера от DISM: '{parts[1]}'"),
        };

        return (long)(value * multiplier);
    }
}
