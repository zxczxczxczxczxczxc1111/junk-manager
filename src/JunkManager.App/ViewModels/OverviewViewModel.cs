using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using System.IO;
using JunkManager.Core;

namespace JunkManager.App.ViewModels;

/// <summary>
/// The overview screen: a volume strip, categories on the left, the findings of
/// the selected category on the right, and a total at the bottom.
/// </summary>
/// <param name="proshloe">
/// Куда положить итог, чтобы его увидел экран «Файлы». Null в тестах, которые
/// проверяют раскладку экрана и про чужие экраны ничего не знают.
/// </param>
internal sealed partial class OverviewViewModel(
    IScanService skaner, PosledniyProhod? proshloe = null)
    : ObservableObject, IScreenViewModel, IDisposable
{
    private const double PorogDoli = 0.015;
    private const int MaksimumStrok = 12;

    private CancellationTokenSource? _otmena;

    public ScreenStateViewModel State { get; } = new();

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

    [ObservableProperty]
    private CategoryViewModel? _current;

    [ObservableProperty]
    private long _foundBytes;

    [ObservableProperty]
    private long _safeBytes;

    [ObservableProperty]
    private long _riskBytes;

    /// <summary>
    /// Сколько байт НЕ проверено. Не ноль при отказе от повышения прав, и это
    /// принципиально другое число, чем «найдено ноль».
    /// </summary>
    [ObservableProperty]
    private long _hiddenBytes;

    [ObservableProperty]
    private long _diskTotalBytes;

    [ObservableProperty]
    private long _diskUsedBytes;

    public void PokazatNachalo()
    {
        ObnovitTom();
        State.Pusto(
            "Начнём с проверки",
            "Найдём ненужные файлы и покажем, что можно убрать. Ты выбираешь, что удалить.",
            "Сканировать диск",
            SkanirovatCommand);
        State.IsWelcome = true;
    }

    [RelayCommand]
    private async Task SkanirovatAsync()
    {
        if (_otmena is not null)
        {
            // CancelAsync, а не Cancel: продолжения отменяемой задачи иначе
            // выполняются прямо здесь, в потоке интерфейса, и окно замирает на
            // столько, сколько они займут (CA1849).
            await _otmena.CancelAsync().ConfigureAwait(true);
            _otmena.Dispose();
        }

        _otmena = new CancellationTokenSource();

        State.Ogranichit(null);
        State.Nachat(cancel: OtmenitCommand);
        ObnovitTom();

        try
        {
            var hod = new Progress<ScanProgress>(p => State.Hod(p.Share, p.Path));
            var itog = await skaner.ScanAsync(hod, _otmena.Token).ConfigureAwait(true);
            Prinyat(itog);
        }
        catch (OperationCanceledException)
        {
            // Отмена это не ошибка и не пустой результат. Экран возвращается
            // туда, откуда его позвали, и молчит.
            State.Pusto(
                "Сканирование остановлено",
                "Ничего не изменено. Запустить заново можно кнопкой",
                "Сканировать диск",
                SkanirovatCommand);
        }
        catch (IOException e)
        {
            State.Oshibka("Сканирование прервано", e.Message, SkanirovatCommand);
        }
        catch (UnauthorizedAccessException e)
        {
            State.Oshibka("Сканирование прервано", e.Message, SkanirovatCommand);
        }
    }

    [RelayCommand]
    private void Otmenit() => _otmena?.Cancel();

    [RelayCommand]
    private void Vybrat(CategoryViewModel? kategoriya)
    {
        if (kategoriya is null || ReferenceEquals(kategoriya, Current))
        {
            return;
        }

        if (Current is not null)
        {
            Current.IsCurrent = false;
        }

        Current = kategoriya;
        Current.IsCurrent = true;
    }

    /// <summary>
    /// Раскладывает результат по экрану. Открыт для тестов намеренно: путь
    /// «пришёл результат, что нарисовалось» это то, что здесь стоит проверять,
    /// и гонять ради этого настоящий обход диска незачем.
    /// </summary>
    public void Prinyat(ScanResult itog)
    {
        ArgumentNullException.ThrowIfNull(itog);

        // Итог отдаётся раньше отрисовки: экран «Файлы» обязан показывать ТОТ
        // ЖЕ проход, а не свой. Два прохода по одному диску это не только
        // вторые двадцать пять секунд, но и второй набор чисел.
        proshloe?.Polozhit(itog);

        Categories.Clear();

        foreach (var kategoriya in CategoryViewModel.Sgruppirovat(
            itog.Findings, PorogDoli, MaksimumStrok, skaner.CategoryByRule))
        {
            Categories.Add(kategoriya);
        }

        FoundBytes = itog.TotalBytes;
        SafeBytes = itog.SafeBytes;
        RiskBytes = itog.RiskBytes;

        Current = Categories.FirstOrDefault();
        if (Current is not null)
        {
            Current.IsCurrent = true;
        }

        if (Categories.Count == 0 && !itog.Cancelled)
        {
            State.Ogranichit(null);
            State.Pusto(
                itog.Skipped.Count == 0 ? "Мусор не найден" : "Проверка завершена с пропусками",
                itog.Skipped.Count == 0
                    ? "В проверенных источниках подходящих файлов нет"
                    : $"Находок нет, но есть пропуски: {itog.Skipped.Count}. "
                      + "Часть путей недоступна или не подходит для очистки. Подробности в разделе «Файлы»",
                "Сканировать заново",
                SkanirovatCommand);
            return;
        }

        // Прерванный проход возвращает меньше находок и выглядит ровно как
        // чистая машина. Плашка это единственное, что отличает одно от другого.
        State.Ogranichit(itog.Cancelled
            ? "Сканирование прервано, показано только найденное до остановки. "
              + "Это «проверено не всё», а не «больше ничего нет»"
            : itog.Skipped.Count > 0
                ? $"Пропуски: {itog.Skipped.Count}. Подробности в разделе «Файлы». "
                  + "Показаны только подтверждённые находки"
                : null);

        if (Categories.Count == 0)
        {
            // Сюда попадает только прерванный проход: чистая машина ушла
            // веткой выше. Пустой список под плашкой это экран, на котором
            // нечего читать, поэтому состояние честно называется своим именем,
            // а плашка снимается: она повторяла бы то же самое второй раз.
            State.Ogranichit(null);
            State.Pusto(
                "Сканирование прервано",
                "До остановки ничего найти не успели. Это «проверено не всё», "
                + "а не «мусора нет»",
                "Сканировать заново",
                SkanirovatCommand);
            return;
        }

        State.Gotovo();
    }

    /// <summary>
    /// Источник отмены это одноразовый ресурс, и владелец обязан его закрыть
    /// (CA1001). Экран живёт столько же, сколько окно, поэтому на практике это
    /// срабатывает один раз, но правило от этого не перестаёт быть правилом.
    /// </summary>
    public void Dispose()
    {
        _otmena?.Cancel();
        _otmena?.Dispose();
        _otmena = null;
    }

    /// <summary>
    /// Перечитывает ТОЛЬКО занятость тома, и ничего больше.
    /// </summary>
    /// <remarks>
    /// Очистка освобождает гигабайты на соседнем экране, а полоса занятости
    /// показывала снимок, снятый при последнем сканировании. Человек удалял и
    /// шёл смотреть, сколько стало, и видел, сколько было.
    ///
    /// Проход по диску отсюда НЕ запускается ни при каких условиях: он идёт
    /// минуты, а переключение раздела это не заявка на работу. Чтение
    /// DriveInfo стоит один системный вызов, и его цена тут единственный довод,
    /// по которому этот экран вообще имеет право подписаться на показ.
    /// </remarks>
    public Task PriPokazeAsync(CancellationToken ct)
    {
        ObnovitTom();
        return Task.CompletedTask;
    }

    private void ObnovitTom()
    {
        // Том системного диска. Не оценка: DriveInfo отдаёт то, что сообщает
        // файловая система, а показывать посчитанное это правило продукта.
        var tom = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

        if (!tom.IsReady)
        {
            return;
        }

        DiskTotalBytes = tom.TotalSize;
        DiskUsedBytes = tom.TotalSize - tom.TotalFreeSpace;
    }
}
