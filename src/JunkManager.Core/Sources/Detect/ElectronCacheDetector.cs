using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;

namespace JunkManager.Core.Sources.Detect;

/// <summary>Known Electron owners with cache layout checks. A familiar folder name is not ownership evidence.</summary>
public sealed class ElectronCacheDetector
{
    private readonly FileScanner _scanner = new();
    private static readonly string[][] Shapes =
    [
        ["Cache", "Cache_Data"], ["Code Cache"], ["GPUCache"],
    ];

    private static readonly Dictionary<string, string[]> Owners = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"] = ["Discord"], ["discordptb"] = ["DiscordPTB"],
        ["discordcanary"] = ["DiscordCanary"], ["discorddevelopment"] = ["DiscordDevelopment"],
        ["Slack"] = ["slack"], ["Code"] = ["Code"], ["Code - Insiders"] = ["Code - Insiders"],
    };

    public async Task<ScanResult> ScanAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var rules = new List<RuleDefinition>();
        var skipped = new List<SkippedItem>();
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) break;
            if (!CleanupPathPolicy.TryVerify(root, out _, out var reason))
            {
                skipped.Add(new SkippedItem(root, reason!));
                continue;
            }
            try
            {
                foreach (var application in Directory.EnumerateDirectories(root))
                {
                    if (ct.IsCancellationRequested) break;
                    if (DetectorExclusions.IsExcluded(Path.GetFileName(application))) continue;
                    if (!CleanupPathPolicy.TryVerify(application, out _, out reason))
                    {
                        skipped.Add(new SkippedItem(application, reason!));
                        continue;
                    }
                    foreach (var shape in Shapes)
                    {
                        var candidate = Path.Combine([application, .. shape]);
                        if (!Directory.Exists(candidate)) continue;
                        var name = Path.GetFileName(application);
                        if (!Owners.TryGetValue(name, out var processes))
                        {
                            skipped.Add(new SkippedItem(candidate,
                                "похож на кэш Electron, но владелец и допустимость удаления не подтверждены"));
                            continue;
                        }
                        rules.Add(new RuleDefinition("electron-" + rules.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "Кэш " + name, [candidate], "Risk",
                            "Известное приложение Electron. Закройте его перед очисткой. Кэш загрузится заново; локальные избранные GIF могут пропасть.")
                        { Tier = RiskTier.Risk, ProcessNames = processes });
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedItem(root, ex.Message));
            }
        }
        var result = await _scanner.ScanAsync(rules, null, ct).ConfigureAwait(false);
        return result with
        {
            Findings = [.. result.Findings.Select(finding => finding with { Source = FindingSource.Detector, RuleId = null })],
            Skipped = [.. skipped, .. result.Skipped],
        };
    }
}

/// <summary>Shared walk used by both detectors. Reads, never follows a link.</summary>
internal static class DirectorySize
{
    internal static long Measure(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        long total = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (FileNotFoundException)
                {
                    // Vanished mid-walk. The number gets smaller, nothing breaks.
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return total;
        }

        return total;
    }

    internal static DateTime NewestWriteUtc(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var newest = Directory.Exists(directory)
            ? Directory.GetLastWriteTimeUtc(directory)
            : DateTime.MinValue;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                try
                {
                    var written = File.GetLastWriteTimeUtc(file);

                    if (written > newest)
                    {
                        newest = written;
                    }
                }
                catch (FileNotFoundException)
                {
                    // Same as above.
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return newest;
        }

        return newest;
    }
}
