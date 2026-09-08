using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using JunkManager.App.Text;
using JunkManager.Core.Registry;
using JunkManager.Deletion;

namespace JunkManager.App.ViewModels;

/// <summary>
/// Экран реестра. Читает настоящие ветки и называет то, чего не смотрел.
/// </summary>
/// <remarks>
/// Ветка «модуль ещё не приехал» отсюда убрана вместе с заглушкой: модуль
/// приехал. Разница, ради которой экран писался заранее, никуда не делась, она
/// только сменила имя. Теперь «не проверено» это ПРОПУСК: ветка, которую не
/// прочитали, и ветка, в которой чисто, дают на экране один и тот же ноль.
/// </remarks>
internal sealed partial class RegistryViewModel
    : ObservableObject, IScreenViewModel, IDisposable
{
    private readonly IRegistryScanService _reestr;
    private readonly IRegistryCleanupService _ochistka;
    private readonly IRegistryBackupsService _bekapy;
    private readonly bool _elevated;

    private CancellationTokenSource? _otmenaOchistki;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToConfirm))]
    private bool _busy;

    [ObservableProperty]
    private FlowStep _step = FlowStep.Selection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToConfirm))]
    [NotifyPropertyChangedFor(nameof(SelectedLabel))]
    private int _selectedCount;

    /// <summary>Сколько отмеченных уйдёт ключом целиком, а не одной строкой.</summary>
    [ObservableProperty]
    private int _wholeKeyCount;

    /// <summary>Список подтверждения долистан до конца.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UdalitCommand))]
    private bool _confirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportSummary))]
    private RegistryCleanupReport? _report;

    /// <summary>
    /// Что вышло с точкой восстановления. Показывается на ПОДТВЕРЖДЕНИИ, а не в
    /// отчёте: читать про отсутствующую страховку надо до нажатия.
    /// </summary>
    [ObservableProperty]
    private string _restorePointNote = string.Empty;

    [ObservableProperty]
    private string _backupDirectory = string.Empty;

    [ObservableProperty]
    private string _backupNote = string.Empty;

    [ObservableProperty]
    private bool _backupEnabled;

    [ObservableProperty]
    private string _coverageSummary = string.Empty;

    /// <summary>
    /// Панель бэкапов поверх списка. НЕ шаг потока: откат зовут и из выбора, и
    /// из отчёта, а шаг заставил бы выбирать между ними.
    /// </summary>
    [ObservableProperty]
    private bool _backupsOpen;

    /// <summary>Итог последнего отката. Null значит откатов в этом сеансе не было.</summary>
    [ObservableProperty]
    private string? _rollbackNote;

    public RegistryViewModel(
        IRegistryScanService reestr,
        IRegistryCleanupService ochistka,
        IRegistryBackupsService bekapy,
        bool elevated)
    {
        ArgumentNullException.ThrowIfNull(reestr);
        ArgumentNullException.ThrowIfNull(ochistka);
        ArgumentNullException.ThrowIfNull(bekapy);

        _reestr = reestr;
        _ochistka = ochistka;
        _bekapy = bekapy;
        _elevated = elevated;
    }

    public ScreenStateViewModel State { get; } = new();

    public ObservableCollection<RegistryFindingViewModel> Rows { get; } = [];

    /// <summary>Исходы уже пройденных записей. Растёт по ходу очистки.</summary>
    public ObservableCollection<DeleteOutcome> Running { get; } = [];

    /// <summary>
    /// Только отмеченное. Это и есть список, который показывают на
    /// подтверждении, и перестраивает его <c>Pereschitat</c> тем же проходом,
    /// каким считает число: два прохода разошлись бы на первой снятой отметке,
    /// а читают их на одном экране.
    /// </summary>
    public ObservableCollection<RegistryFindingViewModel> SelectedRows { get; } = [];

    /// <summary>Свои бэкапы, крупные первыми. Наполняется при открытии панели.</summary>
    public ObservableCollection<RegistryBackupFile> Backups { get; } = [];

    public bool CanGoToConfirm => SelectedCount > 0 && !Busy && Step == FlowStep.Selection;

    /// <summary>Подпись у кнопок шапки. Пусто, пока ничего не отмечено.</summary>
    public string SelectedLabel => SelectedCount == 0
        ? string.Empty
        : "отмечено " + RussianPlural.Format(SelectedCount, "запись", "записи", "записей");

    public string ReportSummary => Report is null
        ? string.Empty
        : RussianPlural.Format(Report.DeletedCount, "запись удалена", "записи удалено", "записей удалено")
          + $", {Report.SkippedCount} пропущено, {Report.FailedCount} не удалось";

    [RelayCommand]
    public async Task ProveritAsync(CancellationToken ct)
    {
        if (Busy || Step == FlowStep.Running) return;
        Busy = true;
        State.Nachat();
        foreach (var row in Rows) row.PropertyChanged -= PriOtmetke;
        Rows.Clear();
        Pereschitat();
        State.Ogranichit(null);

        RegistryScanResult itog;

        try
        {
            var hod = new Progress<string>(adres => State.Hod(null, adres));
            itog = await _reestr.ScanAsync(hod, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Остановленная проверка это НЕ ошибка и не чистый реестр. Оставить
            // экран в загрузке нельзя тем более: остановленная работа выглядела
            // бы идущей.
            State.Pusto(
                "Проверка остановлена",
                "Проверку можно запустить заново.", "Проверить ещё раз", ProveritCommand);
            return;
        }
        catch (UnauthorizedAccessException e)
        {
            State.Oshibka("Реестр не читается", e.Message, ProveritCommand);
            return;
        }
        catch (IOException e)
        {
            State.Oshibka("Реестр не читается", e.Message, ProveritCommand);
            return;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Security.SecurityException)
        {
            State.Oshibka("Реестр не читается", e.Message, ProveritCommand);
            return;
        }
        finally { Busy = false; }

        Prinyat(itog);
    }

    /// <summary>
    /// Раскладывает готовый итог по экрану. Открыт намеренно: путь «пришёл
    /// результат, что нарисовалось» обязан проверяться без реестра.
    /// </summary>
    public void Prinyat(RegistryScanResult itog)
    {
        ArgumentNullException.ThrowIfNull(itog);
        CoverageSummary = string.Join(" · ", itog.Coverage.Select(item => item.Category + ": " + item.Detail));

        foreach (var byla in Rows)
        {
            byla.PropertyChanged -= PriOtmetke;
        }

        Rows.Clear();

        foreach (var nahodka in itog.Findings)
        {
            var stroka = new RegistryFindingViewModel(nahodka, _elevated);
            stroka.PropertyChanged += PriOtmetke;
            Rows.Add(stroka);
        }

        // Новая проверка возвращает поток к началу. Остаться на подтверждении
        // значило бы предлагать удалить список, которого больше нет.
        Step = FlowStep.Selection;
        Confirmed = false;
        Pereschitat();

        var prichiny = new List<string>();
        var unavailable = itog.Skipped.Where(item => !item.IsExpectedExclusion).ToArray();

        if (itog.Cancelled)
        {
            prichiny.Add(
                "Проверка прервана, показано только найденное до остановки. "
                + "Это «проверено не всё», а не «больше ничего нет»");
        }

        if (unavailable.Length > 0)
        {
            prichiny.Add(
                RussianPlural.Format(unavailable.Length, "запись", "записи", "записей")
                + " не проверено. Эти записи сохранены; причины доступны в подробностях");
        }

        if (!itog.Cancelled && itog.Coverage.Any(item => !item.Supported))
            prichiny.Add("Часть разделов не проверена: "
                + string.Join("; ", itog.Coverage.Where(item => !item.Supported).Select(item => item.Category + ": " + item.Detail)));

        var mashinnyh = Rows.Count(r => !r.CanSelect);

        if (mashinnyh > 0)
        {
            prichiny.Add(
                RussianPlural.Format(mashinnyh, "запись", "записи", "записей")
                + " в ветке машины: отметить их нельзя, для удаления нужны права администратора");
        }

        State.Ogranichit(prichiny.Count == 0 ? null : string.Join(". ", prichiny),
            unavailable.Length == 0 ? null : string.Join(Environment.NewLine,
                unavailable.Select(item => item.Path + ": " + item.Reason).Distinct(StringComparer.Ordinal)));

        if (Rows.Count == 0)
        {
            if (itog.Cancelled)
            {
                State.Pusto(
                    "Проверка остановлена",
                    "До остановки битых ссылок не найдено. Запустите проверку заново, чтобы получить полный результат.",
                    "Проверить ещё раз", ProveritCommand);
                return;
            }

            if (_reestr.BranchCount == 0)
            {
                State.Oshibka("Проверка не выполнена", "Не загружены разделы реестра для проверки.", ProveritCommand);
                return;
            }

            if (unavailable.Length > 0 || itog.Coverage.Any(item => !item.Supported))
            {
                // «Чисто» это доказанное отсутствие. Одну ветку доказать не
                // удалось, значит доказательства нет.
                State.Pusto(
                    "Битых ссылок не найдено",
                    "Проверено не всё: часть записей недоступна или не поддерживается. "
                    + "Найденные ограничения можно посмотреть в подробностях и повторить проверку.",
                    "Проверить ещё раз", ProveritCommand);
                return;
            }

            State.Pusto(
                "Всё в порядке",
                $"Проверено разделов реестра: {_reestr.BranchCount}. В проверенных разделах "
                + "битых ссылок не найдено. Очистка не требуется."
                + (itog.Skipped.Count > 0 ? " Системные записи и записи вне области очистки сохранены." : string.Empty),
                "Проверить ещё раз", ProveritCommand);
            return;
        }

        State.Gotovo();
    }
    [RelayCommand]
    private void OtmetitVsyo()
    {
        // Отмечает только то, что вообще можно отметить. Строки ветки машины
        // без прав остаются нетронутыми, и кнопка не обещает того, чего не
        // сделает.
        foreach (var stroka in Rows.Where(r => r.CanSelect && !r.Source.ManualSelectionOnly))
        {
            stroka.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SnyatVsyo()
    {
        foreach (var stroka in Rows)
        {
            stroka.IsSelected = false;
        }
    }

    [RelayCommand]
    public async Task KPodtverzhdeniyuAsync(CancellationToken ct)
    {
        if (!CanGoToConfirm)
        {
            return;
        }

        // Страховка делается ДО показа подтверждения, а не после нажатия
        // «Удалить». Отчёт приходит, когда решать уже нечего, а решение
        // «удалять ли без точки восстановления» человек принимает здесь.
        Busy = true;
        try { RestorePointNote = await _ochistka.ZastrahovatAsync(ct).ConfigureAwait(true); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { RestorePointNote = "Точка восстановления не создана: " + ex.Message; }
        finally { Busy = false; }
        BackupNote = _ochistka.BackupNote;

        Step = FlowStep.Confirm;
    }

    [RelayCommand]
    private void NazadKVyboru()
    {
        // Согласие снимается вместе с уходом с подтверждения: оно было дано на
        // другой список.
        Confirmed = false;
        Step = FlowStep.Selection;
    }

    [RelayCommand(CanExecute = nameof(Confirmed))]
    public async Task UdalitAsync(CancellationToken ct)
    {
        // Проверка повторяется ВНУТРИ команды, а не только в CanExecute:
        // Execute у команды не спрашивает CanExecute, его спрашивает кнопка.
        if (!Confirmed || Step != FlowStep.Confirm || Busy)
        {
            return;
        }

        if (_otmenaOchistki is { } prezhnyaya)
        {
            await prezhnyaya.CancelAsync().ConfigureAwait(true);
            prezhnyaya.Dispose();
        }

        _otmenaOchistki = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Running.Clear();
        Report = null;
        Step = FlowStep.Running;
        Busy = true;
        State.Hod(0, string.Empty);

        var vybrannye = Rows.Where(r => r.IsSelected).Select(r => r.Source).ToList();

        var hod = new Progress<RegistryCleanupProgress>(p =>
        {
            if (Step != FlowStep.Running) return;
            State.Hod(p.Share, p.Address);

            if (p.Last is { } ishod)
            {
                Running.Add(ishod);
            }
        });

        var token = _otmenaOchistki.Token;
        try
        {
            var itog = await _ochistka.RunAsync(vybrannye, hod, token).ConfigureAwait(true);
            Report = itog.Report;
            RestorePointNote = itog.RestorePointNote;
            BackupDirectory = itog.BackupDirectory;
            BackupEnabled = itog.BackupEnabled;
            BackupNote = itog.BackupNote;
        }

        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Report = new RegistryCleanupReport([.. Running], Cancelled: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            Report = new RegistryCleanupReport([.. Running, new("Очистка реестра", DeleteStatus.Failed, 0,
                "Операция прервана: " + ex.Message + ". Проверьте журнал и выполните новую проверку.")], Cancelled: false);
        }
        finally
        {
            Step = FlowStep.Report;
            // Progress messages can arrive after completion. The receipt, mercifully, already exists.
            Running.Clear();
            if (Report is not null) foreach (var outcome in Report.Outcomes) Running.Add(outcome);
            Busy = false;
        }
    }

    [RelayCommand]
    private void Ostanovit() => _otmenaOchistki?.Cancel();

    [RelayCommand]
    private void Zavershit()
    {
        // Возврат к пустому, а не к прежнему списку: часть записей только что
        // перестала существовать, и показывать их снова значит предлагать
        // удалить удалённое.
        foreach (var byla in Rows)
        {
            byla.PropertyChanged -= PriOtmetke;
        }

        Rows.Clear();
        Running.Clear();
        Report = null;
        Confirmed = false;
        Step = FlowStep.Selection;
        Pereschitat();

        State.Pusto(
            "Очистка завершена",
            "Список устарел: часть записей больше не существует. " + BackupNote
            + " Можно проверить реестр заново.", "Проверить ещё раз", ProveritCommand);
    }

    [RelayCommand]
    private async Task OtkryitBekapyAsync(CancellationToken ct)
    {
        Backups.Clear();
        RollbackNote = null;
        BackupsOpen = true;

        try
        {
            var files = await _bekapy.ListAsync(ct).ConfigureAwait(true);
            foreach (var fayl in files.OrderByDescending(file => file.SizeBytes).ThenByDescending(file => file.WrittenUtc))
                Backups.Add(fayl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { RollbackNote = "Чтение копий остановлено"; return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { RollbackNote = "Каталог копий не прочитан: " + ex.Message; return; }

        if (Backups.Count == 0)
        {
            RollbackNote = $"бэкапов пока нет: каталог {_bekapy.Directory} пуст. "
                + "Копии создаются только при включённом экспорте записей в настройках";
        }
    }

    [RelayCommand]
    private void ZakryitBekapy()
    {
        BackupsOpen = false;
        RollbackNote = null;
    }

    [RelayCommand]
    public async Task VosstanovitAsync(RegistryBackupFile? fayl, CancellationToken ct)
    {
        if (Busy) return;
        if (fayl is null || !fayl.Valid)
        {
            // Кнопка у негодного файла выключена, но Execute у команды не
            // спрашивает CanExecute. Тот же довод, что и у удаления.
            RollbackNote = "этот файл не проходит проверку и не импортируется";
            return;
        }

        RollbackResult itog;
        Busy = true;
        try { itog = await _bekapy.RestoreAsync(fayl, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { RollbackNote = "Восстановление остановлено. Проверьте текущее состояние записи"; return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        { RollbackNote = "Восстановление не завершено: " + ex.Message; return; }
        finally { Busy = false; }

        RollbackNote = itog.Ok
            ? $"запись восстановлена из {fayl.FileName}. Список устарел, нужна новая проверка"
            : $"откат не состоялся: {itog.Reason}";

        if (!itog.Ok)
        {
            return;
        }

        // Список находок после импорта врёт: записи вернулись. Чистить его
        // обязательно, иначе следующее нажатие «Удалить» уносит то, что только
        // что вернули.
        foreach (var byla in Rows)
        {
            byla.PropertyChanged -= PriOtmetke;
        }

        Rows.Clear();
        Confirmed = false;
        Step = FlowStep.Selection;
        Pereschitat();

        State.Pusto(
            "Запись восстановлена",
            "Выбранная запись вернулась из бэкапа. Проверьте реестр заново, чтобы обновить результат.",
            "Проверить ещё раз", ProveritCommand);
    }

    public void Dispose()
    {
        _otmenaOchistki?.Cancel();
        _otmenaOchistki?.Dispose();
        _otmenaOchistki = null;
    }

    private void PriOtmetke(object? otpravitel, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RegistryFindingViewModel.IsSelected))
        {
            Pereschitat();
        }
    }

    /// <summary>
    /// Пересчёт полным проходом, а не прибавлением и вычитанием.
    /// </summary>
    /// <remarks>
    /// Накопительный счётчик расходится с истиной на первой же строке, которую
    /// сняли и поставили обратно, и расходится молча: человек читает итог и
    /// жмёт «Удалить». Тот же довод записан на FilesViewModel.Pereschitat.
    /// </remarks>
    private void Pereschitat()
    {
        var otmechennye = Rows.Where(r => r.IsSelected).ToList();

        SelectedCount = otmechennye.Count;
        WholeKeyCount = otmechennye.Count(r => r.Source.Kind == RegistryEntryKind.Key);

        // Список подтверждения это ОТМЕЧЕННОЕ, а не весь найденный список.
        // Показывать всё значит заставить человека искать своё глазами ровно
        // там, где он соглашается на безвозвратное.
        SelectedRows.Clear();

        foreach (var stroka in otmechennye)
        {
            SelectedRows.Add(stroka);
        }
    }
}
