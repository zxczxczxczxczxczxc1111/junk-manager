using System.Text.Json;
using System.Text.Json.Serialization;
using JunkManager.Core.Rules;

namespace JunkManager.Core.Sources.VolumeCache;

/// <param name="TierRaw">
/// The tier exactly as the file spelled it, parsed later. Same reason as in
/// RuleDefinition: an unknown value has to produce a named refusal instead of
/// silently deserialising to the default enum member.
/// </param>
public sealed record VolumeCacheDescription(
    [property: JsonPropertyName("keyName")] string KeyName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tier")] string TierRaw,
    [property: JsonPropertyName("consequence")] string Consequence)
{
    // JsonIgnore is load-bearing, not tidiness. Under PropertyNameCaseInsensitive
    // both "Tier" and TierRaw's "tier" bind to the same JSON name, and
    // System.Text.Json refuses the whole type with a duplicate-name error at the
    // first Deserialize call. The same trap is already documented on
    // RuleDefinition.Tier.
    [JsonIgnore]
    public RiskTier Tier { get; init; }
}

public sealed record VolumeCacheFile(
    [property: JsonPropertyName("handlers")] IReadOnlyList<VolumeCacheDescription> Handlers);

/// <summary>
/// A handler with no description does not ship, exactly like a rule with no
/// consequence. The product knows what the space is, so refusing to show it is a
/// deliberate cost: an unexplained row makes a person guess, and guessing is how
/// people delete things they wanted. Undescribed handlers surface in
/// scan --explain-disk instead, where a gap is supposed to be visible.
/// </summary>
public static class VolumeCacheDescriptions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyDictionary<string, VolumeCacheDescription> Load(string rulesDirectory)
    {
        var file = Path.Combine(rulesDirectory, "volume-caches.json");

        if (!File.Exists(file))
        {
            throw new RuleFormatException($"файл описаний обработчиков не найден: {file}");
        }

        return Parse(File.ReadAllText(file));
    }

    internal static IReadOnlyDictionary<string, VolumeCacheDescription> Parse(string text)
    {
        VolumeCacheFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<VolumeCacheFile>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new RuleFormatException($"volume-caches.json не разбирается: {ex.Message}");
        }

        if (parsed?.Handlers is null)
        {
            throw new RuleFormatException("volume-caches.json пуст или не содержит handlers");
        }

        var map = new Dictionary<string, VolumeCacheDescription>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in parsed.Handlers)
        {
            if (string.IsNullOrWhiteSpace(handler.KeyName))
            {
                throw new RuleFormatException("volume-caches.json: описание без keyName");
            }

            var where = $"volume-caches.json, обработчик '{handler.KeyName}'";

            if (VolumeCacheCatalog.Blacklist.Contains(handler.KeyName.Trim()))
            {
                throw new RuleFormatException(
                    $"{where}: обработчик в чёрном списке и описывать его нельзя. " +
                    $"DownloadsFolder это файлы человека, а не мусор");
            }

            if (string.IsNullOrWhiteSpace(handler.Name))
            {
                throw new RuleFormatException($"{where}: пустое name");
            }

            if (string.IsNullOrWhiteSpace(handler.Consequence))
            {
                throw new RuleFormatException(
                    $"{where}: пустое consequence. Находка, которую человек не может " +
                    $"понять, хуже отсутствия находки");
            }

            var tier = handler.TierRaw switch
            {
                "Safe" => RiskTier.Safe,
                "Risk" => RiskTier.Risk,
                _ => throw new RuleFormatException(
                    $"{where}: неизвестная ступень '{handler.TierRaw}', допустимы Safe и Risk"),
            };

            if (!map.TryAdd(handler.KeyName, handler with { Tier = tier }))
            {
                throw new RuleFormatException($"{where}: описан дважды");
            }
        }

        return map;
    }
}
