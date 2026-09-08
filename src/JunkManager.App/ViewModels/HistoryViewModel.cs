using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;

namespace JunkManager.App.ViewModels;

/// <summary>
/// The history screen: what was removed, when, and what was not.
/// </summary>
/// <remarks>
/// Журнал это НЕ откат. Удаление безвозвратно, и ни одна кнопка здесь ничего не
/// возвращает. Экран существует, чтобы у вопроса «что эта штука удалила вчера»
/// был ответ.
/// </remarks>
internal sealed partial class HistoryViewModel(IHistoryService zhurnal)
    : ObservableObject, IScreenViewModel
{
    public ScreenStateViewModel State { get; } = new();

    public ObservableCollection<HistoryRun> Runs { get; } = [];

    [ObservableProperty]
    private HistoryRun? _current;

    /// <summary>
    /// Перечитывает журнал на каждый показ раздела.
    /// </summary>
    /// <remarks>
    /// Единственный экран продукта, чьи данные меняются, пока на него не
    /// смотрят: очистка идёт на «Файлах» и дописывает сюда новый файл. Чтение
    /// дешёвое, каталог свой, и уходит оно с потока окна внутри
    /// <see cref="IHistoryService.ReadAsync"/>.
    /// </remarks>
    public Task PriPokazeAsync(CancellationToken ct) => ZagruzitAsync(ct);

    public async Task ZagruzitAsync(CancellationToken ct)
    {
        State.Nachat();

        try
        {
            var stranica = await zhurnal.ReadAsync(ct).ConfigureAwait(true);

            Runs.Clear();
            foreach (var progon in stranica.Runs.OrderByDescending(run => run.BytesFreed).ThenByDescending(run => run.StartedUtc))
            {
                Runs.Add(progon with { Outcomes = progon.Outcomes.OrderByDescending(outcome => outcome.BytesFreed).ToArray() });
            }

            Current = Runs.FirstOrDefault();

            // Битые записи не прячутся и не отменяют целые. Плашка называет
            // файл и причину, а список показывает всё, что открылось.
            State.Ogranichit(stranica.Errors.Count > 0
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"Не открылась запись {Path.GetFileName(stranica.Errors[0].FilePath)}: "
                    + $"{stranica.Errors[0].Reason}. Остальные записи открылись")
                : null);

            if (Runs.Count == 0)
            {
                State.Pusto(
                    "Очисток ещё не было",
                    "Здесь появится, что удалено, сколько освобождено и что пропущено. "
                    + "Журнал не средство отката: удаление безвозвратно");
                return;
            }

            State.Gotovo();
        }
        catch (OperationCanceledException)
        {
            // Окно закрывают. Показывать ошибку тому, кто ушёл, некому.
        }
        catch (IOException e)
        {
            State.Oshibka("Журнал не читается", e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            State.Oshibka("Журнал не читается", e.Message);
        }
    }

    /// <summary>
    /// Открывает папку журналов проводником.
    /// </summary>
    /// <remarks>
    /// Единственное место в продукте, где запускается чужой процесс, и
    /// запускается он БЕЗ аргументов от человека: путь свой, из настроек
    /// службы. Ввод пользователя сюда не попадает ни в каком виде.
    /// </remarks>
    [RelayCommand]
    private void Otkryt()
    {
        if (!Directory.Exists(zhurnal.Directory))
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = zhurnal.Directory,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // Проводник не поднялся. Ронять окно из-за этого нельзя: журнал
            // прекрасно читается и внутри программы.
            State.Ogranichit($"Проводник не открылся: {e.Message}");
        }
    }
}
