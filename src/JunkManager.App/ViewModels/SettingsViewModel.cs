using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using JunkManager.App.Text;
using JunkManager.Deletion;

namespace JunkManager.App.ViewModels;

/// <summary>Один вариант выпадающего списка: подпись и значение.</summary>
internal sealed record VariantVybora<T>(string Podpis, T Znachenie);

/// <summary>
/// The settings screen. Seven values, each of which reaches something.
/// </summary>
/// <remarks>
/// <paramref name="primenit"/> is the whole point of the class. Writing the file
/// and calling it done would leave the running process on the old delete mode
/// until the next launch, which is a switch that does nothing at the moment it
/// is switched.
/// </remarks>
internal sealed partial class SettingsViewModel(
    ISettingsService nastroyki, Action<AppSettings>? primenit = null)
    : ObservableObject, IScreenViewModel
{
    private bool _loaded;

    public Task PendingSave { get; private set; } = Task.CompletedTask;

    public ScreenStateViewModel State { get; } = new();

    public ObservableCollection<VariantVybora<DeleteMode>> RezhimVarianty { get; } =
    [
        new("Безвозвратно", DeleteMode.Permanent),
        new("В корзину", DeleteMode.RecycleBin),
    ];

    public ObservableCollection<VariantVybora<int>> HranenieVarianty { get; } =
    [
        new("30 дней", 30),
        new("90 дней", 90),
        new("Бессрочно", 0),
    ];

    [ObservableProperty]
    private bool _elevateOnStart = true;

    [ObservableProperty]
    private VariantVybora<DeleteMode>? _rezhim;

    [ObservableProperty]
    private VariantVybora<int>? _hranenie;

    [ObservableProperty]
    private bool _restorePointBeforeRegistry;

    [ObservableProperty]
    private bool _backupRegistryBeforeCleanup;

    [ObservableProperty]
    private bool _detectorsEnabled = true;

    [ObservableProperty]
    private bool _deepScan;

    [ObservableProperty]
    private bool _leftoverSearch = true;

    /// <summary>
    /// Пункты, которым нужно повышение. Без прав они неактивны, и рядом стоит
    /// плашка, а не тишина.
    /// </summary>
    [ObservableProperty]
    private bool _elevated;

    /// <summary>
    /// Записано ли показанное на экране. Снимается любой правкой, поэтому
    /// отметка не может пережить изменение, которого ещё нет в файле.
    /// </summary>
    [ObservableProperty]
    private bool _sohraneno;

    public async Task ZagruzitAsync(bool elevated, CancellationToken ct)
    {
        _loaded = false;
        await PendingSave.ConfigureAwait(true);
        Elevated = elevated;
        State.Nachat();

        try
        {
            var znacheniya = await nastroyki.LoadAsync(ct).ConfigureAwait(true);

            ElevateOnStart = znacheniya.ElevateOnStart;
            RestorePointBeforeRegistry = znacheniya.RestorePointBeforeRegistry;
            BackupRegistryBeforeCleanup = znacheniya.BackupRegistryBeforeCleanup;
            DetectorsEnabled = znacheniya.DetectorsEnabled;
            DeepScan = znacheniya.DeepScan;
            LeftoverSearch = znacheniya.LeftoverSearch;

            Rezhim = RezhimVarianty.FirstOrDefault(v => v.Znachenie == znacheniya.Mode)
                ?? RezhimVarianty[0];
            Hranenie = PodobratHranenie(znacheniya.HistoryRetentionDays);

            State.Ogranichit(elevated
                ? null
                : "Часть настроек требует прав администратора и сейчас неактивна");

            State.Gotovo();
            _loaded = true;
        }
        catch (JsonException e)
        {
            State.Oshibka(
                "Настройки не читаются",
                $"{Path.GetFileName(nastroyki.FilePath)}: {e.Message}. Текущие значения не изменились");
        }
        catch (IOException e)
        {
            State.Oshibka("Настройки не читаются", $"{e.Message}. Текущие значения не изменились");
        }
        catch (UnauthorizedAccessException e)
        {
            State.Oshibka("Настройки не читаются", $"{e.Message}. Текущие значения не изменились");
        }
    }

    /// <summary>
    /// Любая правка снимает отметку «сохранено». Без этого отметка остаётся
    /// висеть над изменённым и не записанным.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);

        if (!string.Equals(e.PropertyName, nameof(Sohraneno), StringComparison.Ordinal))
        {
            Sohraneno = false;
        }

        if (_loaded && (e.PropertyName is nameof(BackupRegistryBeforeCleanup) or nameof(RestorePointBeforeRegistry)))
        {
            // Save after the dirty flag, or a synchronous write gets punished for being fast.
            _ = SohranitAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Вариант для значения из файла. Незнакомое значение НЕ подменяется
    /// ближайшим: подмена соврала бы на экране и записала бы враньё в файл при
    /// первом же сохранении.
    /// </summary>
    private VariantVybora<int> PodobratHranenie(int dney)
    {
        if (HranenieVarianty.FirstOrDefault(v => v.Znachenie == dney) is { } gotovyy)
        {
            return gotovyy;
        }

        var svoy = new VariantVybora<int>(
            RussianPlural.Format(dney, "день", "дня", "дней"), dney);
        HranenieVarianty.Insert(0, svoy);
        return svoy;
    }

    [RelayCommand]
    private Task SohranitAsync(CancellationToken ct)
    {
        var novye = new AppSettings
        {
            ElevateOnStart = ElevateOnStart,
            Mode = Rezhim?.Znachenie ?? DeleteMode.Permanent,
            RestorePointBeforeRegistry = RestorePointBeforeRegistry,
            BackupRegistryBeforeCleanup = BackupRegistryBeforeCleanup,
            DetectorsEnabled = DetectorsEnabled,
            DeepScan = DeepScan,
            LeftoverSearch = LeftoverSearch,
            HistoryRetentionDays = Hranenie?.Znachenie ?? 90,
        };

        PendingSave = SaveAfterAsync(PendingSave, novye, ct);
        return PendingSave;
    }

    private async Task SaveAfterAsync(Task previous, AppSettings novye, CancellationToken ct)
    {
        // Two quick toggles must queue, not wrestle over settings.json.tmp.
        await previous.ConfigureAwait(true);
        try
        {
            await nastroyki.SaveAsync(novye, ct).ConfigureAwait(true);
        }
        catch (IOException e)
        {
            State.Oshibka(
                "Настройки не сохранились",
                $"{Path.GetFileName(nastroyki.FilePath)}: {e.Message}. Текущие значения не изменились");
            return;
        }
        catch (UnauthorizedAccessException e)
        {
            State.Oshibka(
                "Настройки не сохранились",
                $"{Path.GetFileName(nastroyki.FilePath)}: {e.Message}. Текущие значения не изменились");
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            State.Oshibka("Сохранение отменено", "Текущие значения не изменились");
            return;
        }

        // Применение идёт ТОЛЬКО после удачной записи. Иначе служба очистки
        // работает по режиму, которого нет в файле, и следующий запуск молча
        // возвращает прежний.
        primenit?.Invoke(novye);
        State.Gotovo();
        Sohraneno = true;
    }
}
