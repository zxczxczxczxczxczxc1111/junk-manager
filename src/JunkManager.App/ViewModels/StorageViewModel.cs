using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.Core.Explain;
using Microsoft.Win32;

namespace JunkManager.App.ViewModels;

internal sealed partial class StorageViewModel : ObservableObject, IScreenViewModel, IDisposable
{
    private readonly Func<bool, IProgress<string>, CancellationToken, Task<StorageAnalysis>> _scan;
    private CancellationTokenSource? _cancel;
    private StorageAnalysis? _last;
    public ScreenStateViewModel State { get; } = new();

    public StorageViewModel(Func<bool, IProgress<string>, CancellationToken, Task<StorageAnalysis>>? scan = null)
    {
        _scan = scan ?? ScanCurrentAsync;
        State.Gotovo();
    }

    private static async Task<StorageAnalysis> ScanCurrentAsync(bool deep, IProgress<string> progress, CancellationToken ct)
    {
        var options = StorageAnalysisOptions.Current(deep);
        var analysis = await StorageAnalyzer.AnalyzeAsync(options, progress, ct).ConfigureAwait(false);
        return await SystemStorageAnalysis.AppendAsync(analysis, options.Windows, progress, ct).ConfigureAwait(false);
    }

    [ObservableProperty]
    private bool _deepScan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private string _search = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows))]
    private IReadOnlyList<StorageItem> _rows = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private bool _hasAnalyzed;
    [ObservableProperty]
    private string _status = "Нажми «Посчитать». Покажем размеры папок приложений, инструментов и Windows. Ничего не удаляем.";

    public IReadOnlyList<StorageItem> VisibleRows => string.IsNullOrWhiteSpace(Search) ? Rows
        : Rows.Where(row => row.Path.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || row.Group.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToArray();

    public string EmptyMessage => !HasAnalyzed ? "Здесь будет видно, куда ушло место. Нажми «Посчитать», чтобы начать."
        : string.IsNullOrWhiteSpace(Search) ? "Подходящих папок и файлов не найдено." : "По этому запросу ничего не найдено. Попробуй другое имя или путь.";

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (_cancel is not null) return;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        State.Ogranichit(null);
        State.Nachat("Подсчёт размеров", CancelCommand);
        try
        {
            _last = await _scan(DeepScan, new Progress<string>(path => State.Hod(null, path)), cancel.Token).ConfigureAwait(true);
            Rows = _last.Items;
            HasAnalyzed = true;
            Status = _last.Cancelled ? "Подсчёт остановлен. Ниже то, что успели проверить."
                : Rows.Count == 0 ? "Подходящих папок и файлов не найдено." : $"Показано объектов: {Rows.Count}. Крупные сверху.";
            if (_last.Issues.Count > 0)
                State.Ogranichit("Часть данных проверить не удалось. Некоторые размеры могут быть неполными.", string.Join(Environment.NewLine, _last.Issues));
            State.Gotovo();
        }
        catch (OperationCanceledException)
        { Status = "Подсчёт остановлен. Ничего не изменено."; State.Gotovo(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { State.Oshibka("Не удалось завершить подсчёт", ex.Message, AnalyzeCommand); }
        finally { _cancel = null; }
    }

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    private void OpenFolder(StorageItem? item)
    {
        if (item is null) return;
        try
        {
            var directory = Directory.Exists(item.Path) ? item.Path : Path.GetDirectoryName(item.Path);
            if (directory is null || !Directory.Exists(directory)) { Status = "Папка больше не существует."; return; }
            // Only Explorer receives this path. A filename never auditions as an executable.
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            { UseShellExecute = false };
            info.ArgumentList.Add(directory);
            using var process = Process.Start(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Status = "Не удалось открыть папку: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_last is null) { Status = "Сначала посчитай размеры."; return; }
        var dialog = new SaveFileDialog { FileName = "JunkManager-disk.csv", Filter = "Таблица CSV|*.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, _last.ToCsv(), new System.Text.UTF8Encoding(true)).ConfigureAwait(true);
            Status = "Отчёт сохранён: " + dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "Не удалось сохранить отчёт: " + ex.Message; }
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }
}
