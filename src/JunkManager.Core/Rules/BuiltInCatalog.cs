using JunkManager.Core.Registry;
using JunkManager.Core.Sources.Platform;
using JunkManager.Core.Sources.VolumeCache;

namespace JunkManager.Core.Rules;

/// <summary>The product catalog, embedded in Core and validated by the fixture loaders.</summary>
public static class BuiltInCatalog
{
    private const string Prefix = "JunkManager.Catalog.";

    public static IReadOnlyList<RuleDefinition> LoadRules() =>
        RuleLoader.LoadDocuments(
            typeof(BuiltInCatalog).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)
                    && name.EndsWith(".json", StringComparison.Ordinal)
                    && !name.StartsWith(Prefix + "sources.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(name => (name[Prefix.Length..], ReadText(name[Prefix.Length..]))));

    public static IReadOnlyList<RegistryScanRule> LoadRegistryRules() =>
        RegistryScanRules.Parse(ReadText("sources.registry-branches.json"));

    public static IReadOnlyDictionary<string, VolumeCacheDescription> LoadVolumeCacheDescriptions() =>
        VolumeCacheDescriptions.Parse(ReadText("sources.volume-caches.json"));

    public static PlatformToolTexts LoadPlatformToolTexts() =>
        PlatformToolSource.ParseTexts(ReadText("sources.platform-tools.json"));

    private static string ReadText(string name)
    {
        // No disk fallback: a neighboring JSON file is not the product's brain.
        using var stream = typeof(BuiltInCatalog).Assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new RuleFormatException($"встроенный каталог: ресурс {name} не найден");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
