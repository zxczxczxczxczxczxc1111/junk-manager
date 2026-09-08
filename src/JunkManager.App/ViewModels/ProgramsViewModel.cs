using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using JunkManager.App.Text;
using JunkManager.Core.Apps;
using JunkManager.Deletion;

namespace JunkManager.App.ViewModels;

internal sealed partial class ProgramRowViewModel : ObservableObject
{
    public ProgramRowViewModel(InstalledProgram source, bool elevated)
    {
        Source = source;
        _sizeBytes = source.EstimatedSizeBytes;
        _sizePending = source.EstimatedSizeBytes is null;
        _sizeDetail = source.EstimatedSizeBytes is null ? "Размер будет посчитан по папке приложения" : "Оценка, указанная установщиком";
        if (!elevated && source.Scope is ProgramScope.Machine32 or ProgramScope.Machine64)
            Refusal = "Для удаления нужны права администратора";
        else if (UninstallRunner.IsRunning(source.Id))
            Refusal = "Деинсталлятор ещё работает; обновите список после его завершения";
        else if (source.Installer == InstallerKind.Msix)
        {
            Refusal = string.IsNullOrWhiteSpace(source.PackageFullName) ? "Не определён пакет приложения" : string.Empty;
            RemovalDescription = "Удаление пакета текущего пользователя";
        }
        else if (UninstallCommandBuilder.TryBuild(source, out var command, out var reason))
            RemovalDescription = command.Quiet ? "Штатный деинсталлятор, без мастера" : "Откроется мастер удаления программы";
        else Refusal = reason ?? "Не удалось определить команду удаления";
    }

    public InstalledProgram Source { get; }
    public string Name => Source.DisplayName;
    public string Details => string.Join(" · ", new[] { Source.Publisher, Source.Version, Source.Installer.ToString() }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Size))] private long? _sizeBytes;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Size))] private bool _sizePending;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Size))] private bool _sizePartial;
    [ObservableProperty] private string _sizeDetail;
    public string Size => SizePending ? "Считаем…" : SizeBytes is { } size
        ? (SizePartial ? "≥ " : "≈ ") + ByteSizeFormatter.Format(size) : "Размер недоступен";
    public void SetSize(ProgramSizeEstimate estimate)
    {
        SizeBytes = estimate.Bytes;
        SizePartial = estimate.Partial;
        SizeDetail = estimate.Detail;
        SizePending = false;
    }
    public string Refusal { get; } = string.Empty;
    public string RemovalDescription { get; } = string.Empty;
    public bool CanSelect => Refusal.Length == 0;
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) { if (value && !CanSelect) IsSelected = false; }
}

internal sealed partial class ProgramLeftoverRowViewModel : ObservableObject
{
    public ProgramLeftoverRowViewModel(ProgramLeftoverCandidate candidate) => Candidate = candidate;
    public ProgramLeftoverCandidate Candidate { get; }
    public string Path => Candidate.Item.Path;
    public string Explanation => Candidate.Program.DisplayName + " · " + Candidate.Item.Basis;
    public bool CanSelect => Candidate.Item.CanDelete;
    public string Confidence => CanSelect ? "Принадлежность подтверждена" : "Недостаточно доказательств, удаление недоступно";
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) { if (value && !CanSelect) IsSelected = false; }
}

internal sealed record ProgramResultRow(string Name, string Status, string Detail);
internal enum ProgramsStep { Selection, ConfirmRemoval, Removing, Report, ConfirmLeftovers, CleaningLeftovers }

internal sealed partial class ProgramsViewModel : ObservableObject, IScreenViewModel, IDisposable
{
    private readonly IProgramsService _service;
    private readonly bool _elevated;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly List<ProgramRowViewModel> _all = [];
    private CancellationTokenSource? _operation;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "CalculateSizesAsync owns and disposes its token source in finally; Dispose cancels the work without invalidating its token.")]
    private CancellationTokenSource? _sizeOperation;
    public Task SizeCalculation { get; private set; } = Task.CompletedTask;
    private TaskCompletionSource<bool>? _waitingReply;
    private bool _loaded;
    private bool _disposed;

    public ProgramsViewModel(IProgramsService service, bool elevated)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _elevated = elevated;
        State.Pusto("Программы", "Список установленных программ ещё не прочитан", "Загрузить", RefreshCommand);
    }

    public ScreenStateViewModel State { get; } = new();
    public ObservableCollection<ProgramRowViewModel> Rows { get; } = [];
    public ObservableCollection<ProgramLeftoverRowViewModel> Leftovers { get; } = [];
    public ObservableCollection<ProgramResultRow> Results { get; } = [];
    public IReadOnlyList<string> SortOptions { get; } = ["По названию", "По размеру", "По издателю"];
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private int _sortIndex = 1;
    [ObservableProperty] private ProgramsStep _step;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private bool _waitingForDecision;
    [ObservableProperty] private string _activity = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _notice = string.Empty;
    [ObservableProperty] private string _cleanupNote = string.Empty;
    public bool IsSelection => Step == ProgramsStep.Selection;
    public bool IsConfirmRemoval => Step == ProgramsStep.ConfirmRemoval;
    public bool IsReport => Step == ProgramsStep.Report;
    public bool IsConfirmLeftovers => Step == ProgramsStep.ConfirmLeftovers;
    public bool IsBusy => Loading || Step is ProgramsStep.Removing or ProgramsStep.CleaningLeftovers;
    public bool ShowProgress => Step is ProgramsStep.Removing or ProgramsStep.CleaningLeftovers;
    public bool NoMatches => Rows.Count == 0;
    public int SelectedCount => _all.Count(row => row.IsSelected && row.CanSelect);
    public int SelectedLeftoverCount => Leftovers.Count(row => row.IsSelected && row.CanSelect);
    public string SelectionSummary => $"Показано {Rows.Count} из {_all.Count} · Выбрано {SelectedCount}";
    public IReadOnlyList<ProgramRowViewModel> SelectedPrograms => Sort(_all.Where(row => row.IsSelected && row.CanSelect)).ToArray();
    public IReadOnlyList<ProgramLeftoverRowViewModel> SelectedLeftovers => Leftovers.Where(row => row.IsSelected && row.CanSelect).ToArray();
    public Task PriPokazeAsync(CancellationToken ct) => _loaded || IsBusy ? Task.CompletedTask : LoadAsync(ct);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);
    private bool CanRefresh() => !IsBusy && !_disposed;

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!CanRefresh()) return;
        StopSizeCalculation();
        BeginOperation(ct);
        Loading = true;
        State.Nachat("Читаем программы и пакеты текущего пользователя", CancelCommand);
        try
        {
            var token = _operation!.Token;
            var inventory = await _service.ReadAsync(token).ConfigureAwait(true);
            // Registry command validation does I/O. The UI thread already has enough existential duties.
            var next = await Task.Run(() => inventory.Programs.Select(program => new ProgramRowViewModel(program, _elevated)).ToArray(), token).ConfigureAwait(true);
            var selected = _all.Where(row => row.IsSelected).Select(row => row.Source.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _all) row.PropertyChanged -= RowChanged;
            _all.Clear();
            foreach (var row in next)
            {
                row.IsSelected = selected.Contains(row.Source.Id);
                row.PropertyChanged += RowChanged;
                _all.Add(row);
            }
            _loaded = true;
            var unavailable = inventory.Skipped.Where(item => !item.IsExpectedExclusion).ToArray();
            Notice = unavailable.Length == 0 ? string.Empty : $"Не удалось прочитать часть сведений: {unavailable.Length}. "
                + string.Join("; ", unavailable.Select(item => item.Reason).Distinct(StringComparer.Ordinal).Take(3));
            if (!inventory.IsComplete) Notice = "Список прочитан не полностью. " + Notice;
            Step = ProgramsStep.Selection;
            ApplyFilter();
            State.Gotovo();
            var sizeOperation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _sizeOperation = sizeOperation;
            SizeCalculation = CalculateSizesAsync(next, inventory.OwnershipPrograms, sizeOperation);
        }
        catch (OperationCanceledException) when (_operation!.IsCancellationRequested)
        {
            State.Pusto("Чтение остановлено", "Можно загрузить список заново", "Повторить", RefreshCommand);
        }
        catch (Exception ex) when (IsOperationalError(ex))
        {
            State.Oshibka("Не удалось прочитать программы", ex.Message, RefreshCommand);
        }
        finally { Loading = false; EndOperation(); }
    }

    private async Task CalculateSizesAsync(IReadOnlyList<ProgramRowViewModel> rows,
        IReadOnlyList<InstalledProgram> owners, CancellationTokenSource operation)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var row in rows.Where(row => row.SizePending))
            {
                operation.Token.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(45))
                { row.SetSize(new(null, false, "Подсчёт ограничен по времени. Обнови список, чтобы повторить")); continue; }
                try
                {
                    var estimate = await _service.MeasureSizeAsync(row.Source, owners, operation.Token).ConfigureAwait(true);
                    operation.Token.ThrowIfCancellationRequested();
                    row.SetSize(estimate);
                }
                catch (Exception ex) when (IsOperationalError(ex))
                { row.SetSize(new(null, false, "Не удалось прочитать размер: " + ex.Message)); }
            }
            if (!_disposed && ReferenceEquals(_sizeOperation, operation) && IsSelection) ApplyFilter();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            foreach (var row in rows.Where(row => row.SizePending))
                row.SetSize(new(null, false, "Подсчёт остановлен. Обнови список, чтобы повторить"));
        }
        finally
        {
            if (ReferenceEquals(_sizeOperation, operation)) _sizeOperation = null;
            operation.Dispose();
        }
    }

    private void StopSizeCalculation() => _sizeOperation?.Cancel();

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSortIndexChanged(int value) => ApplyFilter();
    partial void OnLoadingChanged(bool value) => NotifyActions();
    partial void OnStepChanged(ProgramsStep value) => NotifyActions();
    private void ApplyFilter()
    {
        var filtered = _all.Where(row => row.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase)
            || row.Source.Publisher.Contains(Search, StringComparison.CurrentCultureIgnoreCase));
        var next = Sort(filtered).ToArray();
        var visible = next.ToHashSet();
        for (var index = Rows.Count - 1; index >= 0; index--)
            if (!visible.Contains(Rows[index])) Rows.RemoveAt(index);
        for (var index = 0; index < next.Length; index++)
        {
            if (index < Rows.Count && ReferenceEquals(Rows[index], next[index])) continue;
            var previous = Rows.IndexOf(next[index]);
            if (previous >= 0) Rows.Move(previous, index);
            else Rows.Insert(index, next[index]);
        }
        // A measured byte must not bulldoze the checkbox that was just clicked.
        NotifySelection();
    }

    private IOrderedEnumerable<ProgramRowViewModel> Sort(IEnumerable<ProgramRowViewModel> filtered) =>
        SortIndex switch
        {
            1 => filtered.OrderByDescending(row => row.SizeBytes ?? -1).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => filtered.OrderBy(row => row.Source.Publisher, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
        };

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgramRowViewModel.IsSelected)) NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedLeftoverCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(SelectedPrograms));
        OnPropertyChanged(nameof(SelectedLeftovers));
        NotifyActions();
    }
    private void NotifyActions()
    {
        foreach (var property in new[] { nameof(IsSelection), nameof(IsConfirmRemoval), nameof(IsReport), nameof(IsConfirmLeftovers), nameof(IsBusy), nameof(ShowProgress) })
            OnPropertyChanged(property);
        RefreshCommand.NotifyCanExecuteChanged();
        ReviewRemovalCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ReviewLeftoversCommand.NotifyCanExecuteChanged();
        CleanLeftoversCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanReviewRemoval))]
    private void ReviewRemoval() { StopSizeCalculation(); Notice = string.Empty; Step = ProgramsStep.ConfirmRemoval; }
    private bool CanReviewRemoval() => IsSelection && !IsBusy && SelectedCount > 0;
    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => Step = IsConfirmLeftovers ? ProgramsStep.Report : ProgramsStep.Selection;
    private bool CanBack() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        var selected = SelectedPrograms.Select(row => row.Source).ToArray();
        if (!CanRemove()) return;
        BeginOperation(CancellationToken.None);
        Step = ProgramsStep.Removing;
        Activity = "Готовим удаление";
        Results.Clear();
        foreach (var row in Leftovers) row.PropertyChanged -= RowChanged;
        Leftovers.Clear();
        try
        {
            var progress = new Progress<UninstallProgress>(item =>
            {
                if (Step != ProgramsStep.Removing || _disposed) return;
                var name = selected.FirstOrDefault(program => program.Id == item.ProgramId)?.DisplayName ?? item.ProgramId;
                Activity = $"{name} · {item.Elapsed:mm\\:ss} · " + (item.CheckingRegistration ? "Проверяем результат удаления" : "Работает деинсталлятор");
            });
            var report = await _service.RemoveAsync(selected, new UninstallOptions { ContinueWaitingAsync = AskToWaitAsync }, progress, _operation!.Token).ConfigureAwait(true);
            foreach (var result in report.Queue.Results)
                Results.Add(new(selected.FirstOrDefault(program => program.Id == result.ProgramId)?.DisplayName ?? result.ProgramId,
                    OutcomeLabel(result), (result.Reason ?? string.Empty) + (result.RebootRequired ? " Нужна перезагрузка Windows." : string.Empty)));
            foreach (var pending in selected.Where(program => report.Queue.Results.All(result => result.ProgramId != program.Id)))
                Results.Add(new(pending.DisplayName, "Не запускалось", "Очередь остановлена до этой программы"));
            foreach (var item in report.Leftovers.OrderByDescending(item => item.Item.SizeBytes))
            {
                var row = new ProgramLeftoverRowViewModel(item);
                row.PropertyChanged += RowChanged;
                Leftovers.Add(row);
            }
            var confirmed = report.Queue.Results.Count(result => result.RemovalConfirmed);
            Summary = $"Подтверждено отсутствие: {confirmed} из {selected.Length}. " + (report.Queue.Cancelled ? "Очередь остановлена." : "Обработка завершена.");
            Notice = report.Skipped.Count == 0 ? string.Empty : $"Поиск остатков выполнен с ограничениями: {report.Skipped.Count}. "
                + string.Join("; ", report.Skipped.Take(3).Select(item => item.Reason));
            foreach (var result in report.Queue.Results.Where(result => result.RemovalConfirmed))
                if (_all.FirstOrDefault(row => row.Source.Id == result.ProgramId) is { } row) { row.PropertyChanged -= RowChanged; _all.Remove(row); }
            ApplyFilter();
        }
        catch (OperationCanceledException) when (_operation!.IsCancellationRequested)
        {
            Summary = "Наблюдение остановлено. Запущенный деинсталлятор может продолжать работу.";
        }
        catch (Exception ex) when (IsOperationalError(ex))
        {
            Summary = "Удаление завершилось с ошибкой. Обновите список, чтобы проверить состояние программ.";
            Notice = ex.Message;
        }
        finally { EndOperation(); Step = ProgramsStep.Report; NotifySelection(); }
    }
    private bool CanRemove() => IsConfirmRemoval && !IsBusy && SelectedCount > 0;

    private async Task<bool> AskToWaitAsync(UninstallProgress progress, CancellationToken ct)
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => { _waitingReply = reply; WaitingForDecision = true; });
        try { return await reply.Task.WaitAsync(ct).ConfigureAwait(false); }
        finally { Post(() => { if (ReferenceEquals(_waitingReply, reply)) { _waitingReply = null; WaitingForDecision = false; } }); }
    }
    private void Post(Action action)
    {
        if (_context is null) action();
        else _context.Post(_ => action(), null);
    }
    [RelayCommand] private void ContinueWaiting() => _waitingReply?.TrySetResult(true);
    [RelayCommand] private void StopWaiting() => _waitingReply?.TrySetResult(false);
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _waitingReply?.TrySetResult(false);
        _operation?.Cancel();
        Activity = "Останавливаем очередь. Запущенный деинсталлятор может продолжать работу.";
    }
    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanReviewLeftovers))]
    private void ReviewLeftovers() { CleanupNote = _service.CleanupNote; Step = ProgramsStep.ConfirmLeftovers; }
    private bool CanReviewLeftovers() => IsReport && !IsBusy && SelectedLeftoverCount > 0;
    [RelayCommand(CanExecute = nameof(CanCleanLeftovers))]
    private async Task CleanLeftoversAsync()
    {
        if (!CanCleanLeftovers()) return;
        var selected = SelectedLeftovers.Select(row => row.Candidate).ToArray();
        BeginOperation(CancellationToken.None);
        Step = ProgramsStep.CleaningLeftovers;
        Activity = "Повторно проверяем выбранные остатки";
        try
        {
            if (selected.Any(item => item.Item.RegistryFinding is not null))
                Notice = await _service.PrepareLeftoversAsync(_operation!.Token).ConfigureAwait(true);
            var progress = new Progress<string>(path => { if (Step == ProgramsStep.CleaningLeftovers) Activity = path; });
            var outcomes = await _service.CleanLeftoversAsync(selected, progress, _operation!.Token).ConfigureAwait(true);
            foreach (var outcome in outcomes)
                Results.Add(new(outcome.Path, outcome.Status == DeleteStatus.Deleted ? "Остаток удалён" : "Остаток сохранён", outcome.Reason ?? string.Empty));
            foreach (var row in Leftovers.Where(row => outcomes.Any(outcome => outcome.Status == DeleteStatus.Deleted
                && outcome.Path.Equals(row.Path, StringComparison.OrdinalIgnoreCase))).ToArray())
            { row.PropertyChanged -= RowChanged; Leftovers.Remove(row); }
            Summary = $"Удалено остатков: {outcomes.Count(item => item.Status == DeleteStatus.Deleted)}. "
                + (_operation.IsCancellationRequested ? "Очистка остановлена." : "Выбранные объекты обработаны.");
        }
        catch (OperationCanceledException) when (_operation!.IsCancellationRequested) { Summary = "Очистка остатков остановлена"; }
        catch (Exception ex) when (IsOperationalError(ex)) { Summary = "Не удалось закончить очистку остатков"; Notice = ex.Message; }
        finally { EndOperation(); Step = ProgramsStep.Report; NotifySelection(); }
    }
    private bool CanCleanLeftovers() => IsConfirmLeftovers && !IsBusy && SelectedLeftoverCount > 0;
    private void BeginOperation(CancellationToken ct) { _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(ct); }
    private void EndOperation() { _operation?.Dispose(); _operation = null; WaitingForDecision = false; }
    private static bool IsOperationalError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or InvalidOperationException or System.Security.SecurityException or System.ComponentModel.Win32Exception;
    private static string OutcomeLabel(UninstallResult result) => result.Outcome switch
    {
        UninstallOutcome.Removed when result.RemovalConfirmed => "Удалена",
        UninstallOutcome.AlreadyAbsent when result.RemovalConfirmed => "Уже отсутствует",
        UninstallOutcome.StillRunning or UninstallOutcome.TimedOut => "Деинсталлятор ещё работает",
        UninstallOutcome.Cancelled => "Отменено",
        UninstallOutcome.Busy => "Другой установщик занят",
        UninstallOutcome.Refused => "Запуск отклонён",
        UninstallOutcome.Failed => "Ошибка удаления",
        _ => "Удаление не подтверждено",
    };
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSizeCalculation();
        _waitingReply?.TrySetResult(false);
        _operation?.Cancel();
        foreach (var row in _all) row.PropertyChanged -= RowChanged;
        foreach (var row in Leftovers) row.PropertyChanged -= RowChanged;
        // The active operation owns disposal; closing a window must not pull its token's rug away.
        if (!IsBusy) EndOperation();
    }
}
