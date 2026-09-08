using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunkManager.App.Services;

/// <summary>
/// settings.json next to the journal, written atomically.
/// </summary>
/// <remarks>
/// <para>
/// A broken file throws instead of quietly returning defaults. Silently
/// defaulting would flip the delete mode from recycle bin back to permanent
/// without telling anybody, and the person would find out by losing a file.
/// </para>
/// <para>
/// Каталог тот же, что у журнала, и вычисляется он тем же способом:
/// <c>Environment.GetFolderPath</c>. Второй способ определения профиля означал
/// бы два разных ответа на вопрос «чей профиль мы чистим», а весь раздел 11
/// спеки написан ради того, чтобы ответ был один.
/// </para>
/// </remarks>
internal sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsService(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JunkManager",
            "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath))
        {
            // Файла нет до первого сохранения. Это не сбой, это первый запуск.
            Current = new AppSettings();
            return Current;
        }

        var tekst = await File.ReadAllTextAsync(FilePath, ct).ConfigureAwait(false);

        Current = JsonSerializer.Deserialize<AppSettings>(tekst, Options)
            ?? throw new JsonException($"пустой файл настроек: {FilePath}");

        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.HistoryRetentionDays);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Пишем во временный и переименовываем. Прямая запись поверх оставляет
        // половину файла, если питание пропало посреди неё, и следующий запуск
        // не читает настройки вовсе.
        var vremennyy = FilePath + ".tmp";
        var tekst = JsonSerializer.Serialize(settings, Options);

        await File.WriteAllTextAsync(vremennyy, tekst, ct).ConfigureAwait(false);

        // overwrite обязателен: второе сохранение приходит на уже существующий
        // файл, и без него падает ровно то действие, которое человек делает
        // чаще первого.
        File.Move(vremennyy, FilePath, overwrite: true);

        Current = settings;
    }
}
