using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JunkManager.Core.Sources.Platform;

/// <param name="HasDevices">
/// True when at least one device is bound to this package. This is the honest
/// answer to "is this version in use": a version number cannot tell you, because
/// a machine can perfectly well run an older package while a newer one sits
/// unbound in the store.
/// </param>
public sealed record DriverPackage(
    string PublishedName, string OriginalName, string Provider, string Version, bool HasDevices);

public static partial class PnpDriverStore
{
    private const string InfDirectory = @"C:\Windows\INF";
    private const string RepositoryDirectory = @"C:\Windows\System32\DriverStore\FileRepository";

    // Published names are exactly oemNN.inf. Anything else in this position is
    // an argument the product did not produce, and it does not reach pnputil.
    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedNamePattern();

    public static IReadOnlyList<string> EnumerateArguments() =>
        ["/enum-drivers", "/devices", "/format", "xml"];

    public static IReadOnlyList<string> DeleteArguments(string publishedName)
    {
        if (publishedName is null || !PublishedNamePattern().IsMatch(publishedName))
        {
            throw new ArgumentException(
                $"'{publishedName}' не похоже на опубликованное имя пакета вида oemNN.inf",
                nameof(publishedName));
        }

        // No /force and no /uninstall. Both tear a driver out from under a live
        // device, and the product only ever removes packages nothing is using.
        return ["/delete-driver", publishedName];
    }

    public static IReadOnlyList<DriverPackage> Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            // A broken answer means we know nothing about drivers this run. It
            // does not mean the scan stops: the other four sources are fine.
            return [];
        }

        var packages = new List<DriverPackage>();

        foreach (var driver in document.Descendants("Driver"))
        {
            var published = (string?)driver.Attribute("DriverName");

            if (string.IsNullOrWhiteSpace(published))
            {
                continue;
            }

            var devices = driver.Element("Devices");

            packages.Add(new DriverPackage(
                PublishedName: published,
                OriginalName: (string?)driver.Element("OriginalName") ?? string.Empty,
                Provider: (string?)driver.Element("ProviderName") ?? string.Empty,
                Version: (string?)driver.Element("DriverVersion") ?? string.Empty,
                HasDevices: devices is not null && devices.Elements("Device").Any()));
        }

        return packages;
    }

    /// <summary>
    /// A package is removable only when nothing is bound to it AND a sibling
    /// package with the same original INF name IS bound. The second half is what
    /// makes it "superseded" rather than merely "unused": an unused lone package
    /// might be an optional driver waiting for hardware.
    /// </summary>
    public static IReadOnlyList<DriverPackage> SelectRemovable(IReadOnlyList<DriverPackage> all)
    {
        ArgumentNullException.ThrowIfNull(all);

        var activeNames = all
            .Where(p => p.HasDevices)
            .Select(p => p.OriginalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. all.Where(p => !p.HasDevices && activeNames.Contains(p.OriginalName))];
    }

    /// <summary>
    /// Size of the package, measured by walking its folder in the driver store.
    /// The folder is found by content, not by name: C:\Windows\INF\oemNN.inf is
    /// a byte-for-byte copy of the INF inside the repository folder, verified by
    /// SHA256 on 05.09.2026, and the folder name carries a hash we cannot derive.
    /// </summary>
    public static bool TryMeasure(DriverPackage package, out long bytes, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(package);

        bytes = 0;
        var infPath = Path.Combine(InfDirectory, package.PublishedName);

        if (!File.Exists(infPath))
        {
            reason = $"файл {package.PublishedName} не найден в {InfDirectory}";
            return false;
        }

        byte[] wanted;
        try
        {
            wanted = SHA256.HashData(File.ReadAllBytes(infPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"{package.PublishedName} не читается: {ex.Message}";
            return false;
        }

        var prefix = Path.GetFileNameWithoutExtension(package.OriginalName);

        foreach (var folder in SafeEnumerate(RepositoryDirectory, prefix + ".inf_*"))
        {
            var candidate = Path.Combine(folder, package.OriginalName);

            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                if (!SHA256.HashData(File.ReadAllBytes(candidate)).AsSpan().SequenceEqual(wanted))
                {
                    continue;
                }

                bytes = Measure(folder);
                reason = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = $"папка пакета не читается: {ex.Message}. Нужны права администратора";
                return false;
            }
        }

        reason = $"папка пакета {package.OriginalName} в хранилище не найдена, размер не посчитан";
        return false;
    }

    /// <summary>Live enumeration for the acceptance test. Read only.</summary>
    public static IReadOnlyList<DriverPackage> LoadLive()
    {
        var run = ProcessRunner
            .RunAsync(ProcessRunner.SystemTool("pnputil.exe"), EnumerateArguments(), TimeSpan.FromMinutes(2), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return run.ExitCode == 0 ? Parse(run.StandardOutput) : [];
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long Measure(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        long total = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (FileNotFoundException)
            {
                // Vanished mid-walk. Not an error, just a smaller number.
            }
        }

        return total;
    }
}
