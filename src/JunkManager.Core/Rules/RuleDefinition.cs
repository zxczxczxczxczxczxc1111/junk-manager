using System.Text.Json.Serialization;

namespace JunkManager.Core.Rules;

/// <summary>
/// Thrown for anything wrong with a rule file. Always names the file, because a
/// message that does not is a message a person cannot act on.
/// </summary>
public sealed class RuleFormatException : Exception
{
    public RuleFormatException(string message) : base(message) { }

    public RuleFormatException() { }

    public RuleFormatException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed record RuleFile(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("rules")] IReadOnlyList<RuleDefinition> Rules);

/// <param name="TierRaw">
/// The tier exactly as the file spelled it, parsed later. Kept as a string so an
/// unknown value produces a named refusal instead of silently deserialising to
/// the default enum member, which would be Safe.
/// </param>
public sealed record RuleDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths,
    [property: JsonPropertyName("tier")] string TierRaw,
    [property: JsonPropertyName("consequence")] string Consequence,
    [property: JsonPropertyName("olderThanDays")] int OlderThanDays = 0,
    [property: JsonPropertyName("fileFilter")] string? FileFilter = null)
{
    [JsonPropertyName("processNames")]
    public IReadOnlyList<string> ProcessNames { get; init; } = [];

    // Both of these are filled by the loader, never by the file. They carry
    // [JsonIgnore] for a concrete reason and not for tidiness: without it,
    // "Tier" and "tier" both bind to a property under case-insensitive matching,
    // and System.Text.Json refuses the whole type with a duplicate-name error.

    [JsonIgnore]
    public string Category { get; init; } = string.Empty;

    [JsonIgnore]
    public RiskTier Tier { get; init; }
}
