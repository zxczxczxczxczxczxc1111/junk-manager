using System.Text.Json;
using System.Text.Json.Serialization;
using JunkManager.Core;
using JunkManager.Core.Rules;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Как именно кладётся посев. Вид определяет механизм, а не только содержимое:
/// «занятый файл» и «файл» отличаются не байтами, а тем, что первый держат
/// открытым, и обнаружитель обязан вести себя с ними по-разному.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Ноль оставлен неопределённым намеренно, как в RiskTier. Опись читается из файла; " +
        "запись без вида должна падать на разборе, а не тихо превращаться в 'File' или 'None' " +
        "и сеять не то, что описано.")]
public enum SeedKind
{
    /// <summary>Файл нужного размера и возраста.</summary>
    File = 1,

    /// <summary>Каталог и три файла внутри, суммарно нужного размера.</summary>
    Directory = 2,

    /// <summary>Файл, который держат открытым до конца прогона.</summary>
    LockedFile = 3,

    /// <summary>Значение автозапуска в HKCU.</summary>
    RegistryValue = 4,

    /// <summary>Файл, отправленный в корзину.</summary>
    RecycledFile = 5,

    /// <summary>База SQLite со свободными страницами. Сеется вместе с источником Vacuum.</summary>
    SqliteWithFreePages = 6,

    /// <summary>Плотная база SQLite. Ловушка, сеется вместе с источником Vacuum.</summary>
    SqliteNoFreePages = 7,

    /// <summary>Пакет драйвера через pnputil. Сеется вместе с источником PlatformTool.</summary>
    DriverPackage = 8,

    /// <summary>Дамп с проверяемым заголовком MDMP.</summary>
    CrashDump = 9,
}

/// <summary>
/// Одна запись описи посева.
/// </summary>
/// <remarks>
/// Поле <see cref="Why"/> обязательно и проверяется загрузчиком: посев, про
/// который непонятно, что он доказывает, через месяц удалят как непонятный, и
/// вместе с ним уйдёт проверка, ради которой он заводился.
/// </remarks>
public sealed class SeedEntry
{
    public string Id { get; init; } = string.Empty;

    public SeedKind Kind { get; init; }

    public string Path { get; init; } = string.Empty;

    public long Bytes { get; init; }

    public int AgeDays { get; init; }

    /// <summary>Какой механизм ОБЯЗАН найти приманку. У ловушек не заполняется.</summary>
    public FindingSource? ExpectedSource { get; init; }

    public string? ExpectedRuleId { get; init; }

    /// <summary>Приманка (true) или ловушка (false).</summary>
    public bool MustFind { get; init; }

    /// <summary>
    /// Промах по такой приманке роняет приёмку. У приманок на ещё не
    /// построенные источники стоит false: их промах это измерение глубины, а не
    /// поломка.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// false означает бутафорию: посев проверяет правило (раскрытие пути и
    /// совпадение маски), а не механизм. Считается отдельно, иначе бутафория
    /// маскирует дыру в механизме.
    /// </summary>
    public bool Genuine { get; init; } = true;

    public string Why { get; init; } = string.Empty;

    /// <summary>Путь с раскрытыми переменными окружения.</summary>
    public string ExpandedPath => PathExpander.ExpandEnvironment(Path);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Читает опись. Отказ громкий и на любой кривизне: опись, которая молча
    /// прочиталась наполовину, даёт зелёную приёмку при половине непосеянного.
    /// </summary>
    public static IReadOnlyList<SeedEntry> Load(string file)
    {
        if (!System.IO.File.Exists(file))
        {
            throw new InvalidOperationException(
                $"описи посева нет по пути {file}. Она обязана доехать до гостя вместе с набором тестов");
        }

        var soderzhimoe = System.IO.File.ReadAllText(file);

        OpisPosev? opis;
        try
        {
            opis = JsonSerializer.Deserialize<OpisPosev>(soderzhimoe, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"опись посева {file} не разбирается: {ex.Message}", ex);
        }

        if (opis?.Seeds is null || opis.Seeds.Count == 0)
        {
            throw new InvalidOperationException($"опись посева {file} пуста");
        }

        var vidennye = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var zapis in opis.Seeds)
        {
            if (string.IsNullOrWhiteSpace(zapis.Id))
            {
                throw new InvalidOperationException($"в описи {file} есть запись без id");
            }

            if (!vidennye.Add(zapis.Id))
            {
                throw new InvalidOperationException($"в описи {file} id '{zapis.Id}' встречается дважды");
            }

            if (string.IsNullOrWhiteSpace(zapis.Path))
            {
                throw new InvalidOperationException($"посев '{zapis.Id}' без пути");
            }

            if (string.IsNullOrWhiteSpace(zapis.Why))
            {
                throw new InvalidOperationException(
                    $"посев '{zapis.Id}' без поля why. Посев, про который непонятно, что он доказывает, "
                    + "через месяц удалят как непонятный");
            }

            if (zapis.MustFind && zapis.ExpectedSource is null)
            {
                throw new InvalidOperationException(
                    $"приманка '{zapis.Id}' без expectedSource. Приманка без указанного источника "
                    + "не отличает «нашли правильно» от «нашли случайно»");
            }

            if (!zapis.MustFind && zapis.Required)
            {
                throw new InvalidOperationException(
                    $"ловушка '{zapis.Id}' помечена required. Обязательной может быть только приманка");
            }
        }

        return opis.Seeds;
    }

    /// <remarks>
    /// Создаётся только десериализатором, поэтому анализатор не видит ни одного
    /// вызова конструктора. Правило CA1812 подавлено адресно: сделать тип
    /// статическим нельзя, он и есть форма файла.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Экземпляр создаёт System.Text.Json при разборе описи посева.")]
    private sealed class OpisPosev
    {
        public List<SeedEntry>? Seeds { get; init; }
    }
}
