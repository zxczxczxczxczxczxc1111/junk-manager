using System.Text.Json;
using System.Text.Json.Serialization;
using JunkManager.Core.Rules;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Core.Registry;

/// <summary>
/// Where the path lives inside a branch: in the values of one key, or in the
/// default value of each subkey. Two shapes and not one because Run and
/// App Paths genuinely differ, and pretending otherwise means deleting the
/// wrong thing in one of them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Ноль оставлен неопределённым намеренно, как в RiskTier и FindingSource. " +
        "Вид читается из файла, и запись без вида обязана падать на разборе, а не тихо " +
        "становиться Value и удалять значение там, где записью был ключ.")]
public enum RegistryEntryKind
{
    /// <summary>Запись это ЗНАЧЕНИЕ внутри ветки. Удаляется значение.</summary>
    Value = 1,

    /// <summary>
    /// Запись это ПОДКЛЮЧ ветки, путь лежит в его значении по умолчанию.
    /// Удаляется подключ целиком.
    /// </summary>
    Key = 2,
}

/// <param name="View">
/// Registry32 and Registry64 are different sets of entries under HKLM, not two
/// spellings of one. A cleaner that walks only the native view leaves every x86
/// program's autorun entry unseen.
/// </param>
public enum RegistryScanMode
{
    Standard,
    SharedDlls,
    ComServers,
    FileAssociations,
}

public sealed record RegistryScanRule(
    string Id,
    RegistryHive Hive,
    string SubKey,
    RegistryView View,
    RegistryEntryKind Kind,
    string Name,
    string Consequence)
{
    public RegistryScanMode Mode { get; init; }
}

/// <summary>
/// Reads rules/sources/registry-branches.json. The file can only NARROW what
/// RegistryGuard already allows: a branch the guard refuses stops the whole
/// load with the branch named, rather than being skipped.
/// </summary>
/// <remarks>
/// Fail-closed for the same reason RuleLoader is: a silently dropped branch
/// looks on screen exactly like "the registry is clean", and that is the one
/// wrong answer nobody investigates.
/// </remarks>
public static class RegistryScanRules
{
    public const string FileName = "registry-branches.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static IReadOnlyList<RegistryScanRule> Load(string sourcesDirectory)
    {
        var fayl = Path.Combine(sourcesDirectory, FileName);

        if (!File.Exists(fayl))
        {
            throw new RuleFormatException($"файл веток реестра не найден: {fayl}");
        }

        return Parse(File.ReadAllText(fayl));
    }

    internal static IReadOnlyList<RegistryScanRule> Parse(string text)
    {
        OpisVetok? opis;
        try
        {
            opis = JsonSerializer.Deserialize<OpisVetok>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new RuleFormatException($"{FileName} не разбирается: {ex.Message}", ex);
        }

        if (opis?.Branches is null || opis.Branches.Count == 0)
        {
            throw new RuleFormatException($"{FileName}: ни одной ветки");
        }

        var vidennye = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itog = new List<RegistryScanRule>(opis.Branches.Count);

        foreach (var vetka in opis.Branches)
        {
            if (string.IsNullOrWhiteSpace(vetka.Id))
            {
                throw new RuleFormatException($"{FileName}: есть ветка без id");
            }

            if (!vidennye.Add(vetka.Id))
            {
                throw new RuleFormatException($"{FileName}: id '{vetka.Id}' встречается дважды");
            }

            if (string.IsNullOrWhiteSpace(vetka.Name))
            {
                throw new RuleFormatException($"{FileName}: у ветки '{vetka.Id}' пустое name");
            }

            if (string.IsNullOrWhiteSpace(vetka.Consequence))
            {
                // Тот же довод, что и у правил файлов: находка без объяснения
                // последствия человеку не показывается никогда.
                throw new RuleFormatException($"{FileName}: у ветки '{vetka.Id}' пустое consequence");
            }

            if (!Enum.IsDefined(vetka.Mode) || !Enum.IsDefined(vetka.Kind))
                throw new RuleFormatException($"{FileName}: неизвестный режим или вид записи '{vetka.Id}'");

            if (!RegistryGuard.TryVerifyScanBranch(
                    vetka.Hive,
                    vetka.SubKey ?? string.Empty,
                    vetka.View,
                    out var chistaya,
                    out var otkaz))
            {
                throw new RuleFormatException(
                    $"{FileName}: ветка '{vetka.Id}' отклонена guard-ом: {otkaz}");
            }

            itog.Add(new RegistryScanRule(
                vetka.Id, vetka.Hive, chistaya, vetka.View, vetka.Kind, vetka.Name, vetka.Consequence)
                { Mode = vetka.Mode });
        }

        return itog;
    }

    /// <remarks>
    /// Экземпляр создаёт только System.Text.Json, поэтому анализатор не видит
    /// ни одного вызова конструктора. Подавление адресное: сделать тип
    /// статическим нельзя, он и есть форма файла.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Экземпляр создаёт System.Text.Json при разборе файла веток.")]
    private sealed class OpisVetok
    {
        public List<ZapisVetki>? Branches { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Экземпляр создаёт System.Text.Json при разборе файла веток.")]
    private sealed class ZapisVetki
    {
        public string Id { get; init; } = string.Empty;

        public RegistryHive Hive { get; init; }

        public string? SubKey { get; init; }

        public RegistryView View { get; init; }

        public RegistryEntryKind Kind { get; init; }

        public RegistryScanMode Mode { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Consequence { get; init; } = string.Empty;
    }
}
