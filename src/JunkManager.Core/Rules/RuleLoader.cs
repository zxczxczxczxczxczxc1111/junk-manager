using System.Text.Json;

namespace JunkManager.Core.Rules;

/// <summary>
/// Loads the rule files. Fail-closed everywhere: a broken file stops the whole
/// load with the filename in the message. Skipping it would make a whole
/// category vanish, and on screen that looks exactly like "there is no junk
/// here", which is the one wrong answer nobody investigates.
/// </summary>
public static class RuleLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<RuleDefinition> Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            // A missing directory and an empty rule set look identical on screen.
            // Telling them apart is the loader's job, not the reader's.
            throw new RuleFormatException($"каталог правил не найден: {directory}");
        }

        return LoadDocuments(Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal).Select(file => (file, ReadFile(file))));
    }

    internal static IReadOnlyList<RuleDefinition> LoadDocuments(IEnumerable<(string Name, string Text)> documents)
    {
        var all = new List<RuleDefinition>();
        var seenIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, text) in documents)
        {
            var parsed = Parse(file, text);

            foreach (var rule in parsed.Rules)
            {
                Validate(rule, file);

                if (seenIds.TryGetValue(rule.Id, out var firstSeenIn))
                {
                    throw new RuleFormatException(
                        $"дублирующийся id правила '{rule.Id}': " +
                        $"{Path.GetFileName(firstSeenIn)} и {Path.GetFileName(file)}");
                }

                seenIds[rule.Id] = file;
                all.Add(rule with
                {
                    Category = parsed.Category,
                    Tier = ParseTier(rule.TierRaw, rule.Id),
                });
            }
        }

        if (all.Count == 0)
        {
            // Тот же довод, что и у отсутствующего каталога, доведённый до
            // конца. Каталог есть, а правил в нём ноль: проход отчитается
            // «просмотрено, найдено ноль», и человек прочитает это как «на
            // диске чисто». Поставка, в которую правила не доехали, обязана
            // падать громко, а не работать тихо и вхолостую.
            throw new RuleFormatException(
                "в каталоге правил не нашлось ни одного правила");
        }

        return all;
    }

    private static string ReadFile(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            throw new RuleFormatException(
                $"файл правил {Path.GetFileName(file)} не читается: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RuleFormatException(
                $"файл правил {Path.GetFileName(file)} не читается: {ex.Message}", ex);
        }

    }

    private static RuleFile Parse(string file, string text)
    {
        RuleFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RuleFile>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new RuleFormatException(
                $"файл правил {Path.GetFileName(file)} не разбирается: {ex.Message}", ex);
        }

        if (parsed is null)
        {
            throw new RuleFormatException($"файл правил {Path.GetFileName(file)} пуст");
        }

        if (parsed.Rules is null)
        {
            throw new RuleFormatException(
                $"файл правил {Path.GetFileName(file)}: нет раздела rules");
        }

        if (string.IsNullOrWhiteSpace(parsed.Category))
        {
            throw new RuleFormatException(
                $"файл правил {Path.GetFileName(file)}: пустая category");
        }

        return parsed;
    }

    private static void Validate(RuleDefinition rule, string file)
    {
        var fileName = Path.GetFileName(file);

        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            throw new RuleFormatException($"{fileName}: правило без id");
        }

        var where = $"{fileName}, правило '{rule.Id}'";

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new RuleFormatException($"{where}: пустое name");
        }

        if (rule.Paths is null || rule.Paths.Count == 0)
        {
            throw new RuleFormatException($"{where}: пустой список paths");
        }

        if (rule.Paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new RuleFormatException($"{where}: пустая строка в списке paths");
        }

        if (string.IsNullOrWhiteSpace(rule.Consequence))
        {
            throw new RuleFormatException(
                $"{where}: пустое consequence. Находка, которую человек не может понять, " +
                "хуже отсутствия находки");
        }

        if (rule.OlderThanDays < 0)
        {
            throw new RuleFormatException($"{where}: olderThanDays отрицательный");
        }

        if (rule.ProcessNames is null || rule.ProcessNames.Any(name =>
            string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\\', '/', '*', '?', ':']) >= 0
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw new RuleFormatException($"{where}: processNames должен содержать имена процессов без пути и .exe");
        }
    }

    private static RiskTier ParseTier(string raw, string ruleId) => raw switch
    {
        "Safe" => RiskTier.Safe,
        "Risk" => RiskTier.Risk,
        _ => throw new RuleFormatException(
            $"правило '{ruleId}': неизвестная ступень '{raw}'. Допустимы только Safe и Risk. " +
            "Ступени Refill и Review отменены 05.09.2026"),
    };
}
