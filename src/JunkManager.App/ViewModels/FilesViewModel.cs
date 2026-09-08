using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using JunkManager.App.Text;
using JunkManager.Core;
using JunkManager.Deletion;

namespace JunkManager.App.ViewModels;

/// <summary>
/// The four steps of one flow: choose, confirm, run, report.
/// </summary>
/// <remarks>
/// Отдельного раздела «Подтверждение и очистка» в боковой панели нет и не
/// будет. Подтверждение по смыслу совпадает с выбором и делит один поток
/// надвое, а пункт меню оставляет человека там, где он выбирал файлы.
/// </remarks>
internal enum FlowStep
{
    Selection = 1,
    Confirm = 2,
    Running = 3,
    Report = 4,
}

/// <summary>
/// The Files screen: one flat list of everything found, ticks, and the totals
/// that decide whether the flow can move on.
/// </summary>
internal sealed partial class FilesViewModel : ObservableObject, IScreenViewModel, IDisposable
{
    private readonly PosledniyProhod _proshloe;
    private readonly ICleanupService _ochistka;
    private readonly ILockedFileService _zanyatye;
    private CancellationTokenSource? _otmenaOchistki;

    /// <param name="ochistka">
    /// Обязательный параметр, а не значение по умолчанию. Служба по умолчанию
    /// означала бы модель, которая в проверке молча трогает настоящий диск.
    /// </param>
    /// <param name="zanyatye">
    /// Выходы из «файл занят». Обязательный параметр, и с 06.09.2026 это
    /// вопрос безопасности, а не стиля. Первый выход продукт делает САМ, без
    /// нажатия: служба по умолчанию означала бы проверку, которая по дороге
    /// просит закрыться настоящие программы на машине, где её запустили.
    /// </param>
    public FilesViewModel(
        PosledniyProhod proshloe,
        ICleanupService ochistka,
        ILockedFileService zanyatye)
    {
        ArgumentNullException.ThrowIfNull(proshloe);
        ArgumentNullException.ThrowIfNull(ochistka);
        ArgumentNullException.ThrowIfNull(zanyatye);

        _zanyatye = zanyatye;
        _ochistka = ochistka;

        // Подписка, а не ручное уведомление на каждой правке списка. Список
        // чистится из трёх мест, и забытое уведомление оставило бы на экране
        // блок с кнопками поверх пустоты.
        Stuck.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasStuck));
            OnPropertyChanged(nameof(HasStuckActions));
        };

        _proshloe = proshloe;
        _proshloe.Obnovilsya += (_, itog) => Prinyat(itog);

        if (_proshloe.Itog is { } uzhe)
        {
            Prinyat(uzhe);
        }
        else
        {
            PokazatBezProhoda();
        }
    }

    public ScreenStateViewModel State { get; } = new();

    /// <summary>
    /// Все находки одним плоским списком, без разбивки по категориям. Разбивка
    /// живёт на обзоре: там выбирают, ЧТО смотреть, здесь отмечают, что удалить,
    /// и группы заставляли бы разворачивать каждую, чтобы ничего не пропустить.
    /// </summary>
    public ObservableCollection<FindingViewModel> Rows { get; } = [];

    /// <summary>
    /// Исходы уже пройденных находок, по одной строке. Растёт по ходу очистки.
    /// </summary>
    public ObservableCollection<DeleteOutcome> Running { get; } = [];

    /// <summary>
    /// Строки, которые не унесли, потому что их держит чужая программа.
    /// </summary>
    /// <remarks>
    /// Отдельный список, а не пометка внутри общего. У этих строк есть ДЕЙСТВИЯ,
    /// а общий шаблон строки исхода рисует ещё и журнал, где действий быть не
    /// должно вовсе: журнал это история, а не пульт.
    /// </remarks>
    public ObservableCollection<ZanyatyyViewModel> Stuck { get; } = [];

    /// <summary>
    /// Исходы для нижнего списка итога: все, КРОМЕ занятых.
    /// </summary>
    /// <remarks>
    /// Занятые показаны выше отдельным блоком со своими действиями, и повтор
    /// тех же путей строкой ниже читается как два разных события про один файл.
    /// Полнота записи от этого не страдает: журнал пишет каждый исход, включая
    /// повторные удаления после освобождения.
    /// </remarks>
    public ObservableCollection<DeleteOutcome> ReportRows { get; } = [];

    public bool HasStuck => Stuck.Count > 0;

    /// <summary>
    /// Есть ли хоть одна строка, где человеку ещё предстоит выбирать.
    /// </summary>
    /// <remarks>
    /// От этого зависит предупреждение про принудительное завершение: когда
    /// продукт закрыл всех сам, предупреждать не о чем, а текст, который висит
    /// без повода, перестают читать и там, где повод есть.
    /// </remarks>
    public bool HasStuckActions => Stuck.Any(z => z.DeystviyaVidny);

    [ObservableProperty]
    private FlowStep _step = FlowStep.Selection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToConfirm))]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private long _totalBytes;

    /// <summary>Сколько отмечено опасного. Показывается на шаге подтверждения.</summary>
    [ObservableProperty]
    private long _selectedRiskBytes;

    /// <summary>Список подтверждения долистан до конца. Кнопка удаления привязана сюда.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UdalitCommand))]
    private bool _confirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportSummary))]
    private CleanupReport? _report;

    [ObservableProperty]
    private long _freedBytes;

    [ObservableProperty]
    private int _doneCount;

    public bool CanGoToConfirm => SelectedCount > 0;

    /// <summary>Сколько отмеченных находок уйдёт каталогом целиком.</summary>
    public int WholeCount => Rows.Count(r => r.IsSelected && r.Scope == DeleteScope.Whole);

    /// <summary>Сколько отмеченных находок заберёт только файлы внутри.</summary>
    public int EntryCount => Rows.Count(r => r.IsSelected && r.Scope == DeleteScope.SelectedEntries);

    /// <summary>Сколько всего файлов уйдёт из тех каталогов, которые останутся.</summary>
    public int EntryFileCount => Rows
        .Where(r => r.IsSelected && r.Scope == DeleteScope.SelectedEntries)
        .Sum(r => r.TargetCount);

    /// <summary>Строка про поэлементные находки, целиком.</summary>
    /// <remarks>
    /// Собирается здесь, а не из нескольких Run в разметке. WPF ставит пробел
    /// между соседними Run, и запятая уезжает от слова: «изнутри 3 каталогов ,
    /// сами каталоги останутся». Найдено снимком живого окна 05.09.2026.
    /// </remarks>
    public string EntrySummary =>
        RussianPlural.Format(EntryFileCount, "файл уйдёт", "файла уйдут", "файлов уйдут")
        + " изнутри "
        + RussianPlural.Format(EntryCount, "каталога", "каталогов", "каталогов")
        + ", сами каталоги останутся";

    /// <summary>
    /// Сколько занятых строк удалось унести после освобождения. Считается
    /// отдельно, потому что отчёт первого прохода их числит неудачами.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportSummary))]
    private int _osvobozhdenoPovtorom;

    /// <summary>Сводка очистки одной строкой. Пусто, пока очистки не было.</summary>
    /// <remarks>
    /// Живая, а не снимок первого прохода. Продукт сам просит держателей
    /// закрыться и повторяет удаление уже на экране итога: сводка, оставшаяся
    /// снимком, говорила бы «три не удалось» под списком, где две строки к тому
    /// моменту унесены.
    /// </remarks>
    public string ReportSummary => Report is null
        ? string.Empty
        : RussianPlural.Format(
            Report.DeletedCount + OsvobozhdenoPovtorom,
            "объект удалён", "объекта удалено", "объектов удалено")
          + $", {Report.SkippedCount} пропущено, "
          + $"{Report.FailedCount - OsvobozhdenoPovtorom} не удалось";

    /// <summary>
    /// Раскладывает чужой проход по экрану. Открыт намеренно: путь «пришёл
    /// результат, что нарисовалось» проверяется без диска.
    /// </summary>
    public void Prinyat(ScanResult itog)
    {
        ArgumentNullException.ThrowIfNull(itog);

        foreach (var byla in Rows)
        {
            byla.PropertyChanged -= PriOtmetke;
        }

        Rows.Clear();
        OchistitZanyatye();
        ReportRows.Clear();
        // Even a zero-byte finding gets a chair at this miserable table.
        foreach (var nahodka in itog.Findings.OrderByDescending(f => f.SizeBytes))
        {
            var stroka = new FindingViewModel(nahodka)
            {
                // То же правило, что и на обзоре, и живёт оно в одном месте на
                // оба экрана.
                IsSelected = nahodka.Tier == RiskTier.Safe,
            };

            stroka.PropertyChanged += PriOtmetke;
            Rows.Add(stroka);
        }

        TotalBytes = itog.TotalBytes;

        // Новый проход возвращает поток к началу. Остаться на подтверждении
        // значило бы предлагать удалить список, которого больше нет.
        Step = FlowStep.Selection;

        Pereschitat();

        State.Ogranichit(itog.Cancelled
            ? "Сканирование прервано, показано только найденное до остановки. "
                + "Это «проверено не всё», а не «больше ничего нет»"
            : null);

        if (Rows.Count == 0)
        {
            State.Pusto(
                "Чисто",
                "Проверены все правила, находок нет. Это редкий, но настоящий исход");
            return;
        }

        State.Gotovo();
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
    private void OtmetitBezopasnoe()
    {
        // Отмечает безопасное и НЕ трогает опасное, ни в ту, ни в другую
        // сторону. Кнопка называется «Отметить безопасное», и она делает ровно
        // это: снимать чужую ручную отметку с опасного она не нанималась.
        foreach (var stroka in Rows.Where(r => r.Tier == RiskTier.Safe))
        {
            stroka.IsSelected = true;
        }
    }

    [RelayCommand]
    private void KPodtverzhdeniyu()
    {
        if (!CanGoToConfirm)
        {
            return;
        }

        Step = FlowStep.Confirm;
    }

    [RelayCommand]
    private void NazadKVyboru()
    {
        // Отметка «долистано» снимается вместе с уходом с подтверждения. Иначе
        // человек, вернувшийся к выбору и добавивший десять строк, попадает на
        // подтверждение с уже активной кнопкой удаления и с непрочитанным
        // списком: ворота открылись бы за прошлое согласие.
        Confirmed = false;
        Step = FlowStep.Selection;
    }

    [RelayCommand(CanExecute = nameof(Confirmed))]
    private async Task UdalitAsync()
    {
        // Проверка повторяется внутри команды, а не только в CanExecute.
        // Execute у команды НЕ спрашивает CanExecute: его спрашивает кнопка.
        // Значит любой другой вызов, от горячей клавиши до чужого кода,
        // прошёл бы мимо ворот подтверждения прямо в безвозвратное удаление.
        if (!Confirmed)
        {
            return;
        }

        if (_otmenaOchistki is { } prezhnyaya)
        {
            await prezhnyaya.CancelAsync().ConfigureAwait(true);
            prezhnyaya.Dispose();
        }

        _otmenaOchistki = new CancellationTokenSource();

        Running.Clear();
        Report = null;
        FreedBytes = 0;
        DoneCount = 0;
        OsvobozhdenoPovtorom = 0;
        Step = FlowStep.Running;
        State.Hod(0, string.Empty);

        var vybrannye = Rows.Where(r => r.IsSelected).Select(r => r.Source).ToList();

        var hod = new Progress<CleanupProgress>(p =>
        {
            FreedBytes = p.BytesFreed;
            DoneCount = p.Done;
            State.Hod(p.Share, p.Path);

            if (p.Last is { } ishod)
            {
                Running.Add(ishod);
            }
        });

        Report = await _ochistka.RunAsync(vybrannye, hod, _otmenaOchistki.Token)
            .ConfigureAwait(true);

        FreedBytes = Report.BytesFreed;

        SobratZastryavshie(vybrannye, Report);

        Step = FlowStep.Report;

        await PoprositDerzhateleyAsync(Report, _otmenaOchistki.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Просит держателей закрыться сама, без единой кнопки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Первый выход из «файл занят» безболезненный: программу просят закрыться
    /// штатно, она сохраняет своё и выходит сама. У безболезненного шага нет
    /// второй стороны, и решать тут человеку нечего, поэтому продукт делает его
    /// сам, а выбор оставляет там, где выбор есть: удалить при перезагрузке или
    /// завершить принудительно.
    /// </para>
    /// <para>
    /// Только ПОСЛЕ перехода на итог: экран уже виден, строки показывают ход, и
    /// закрытие чужих программ не происходит за пустым окном.
    /// </para>
    /// <para>
    /// После ПРЕРВАННОЙ очистки не идёт вовсе. «Остановить» значит «не трогай
    /// мою машину», и закрывать после этого чужие программы означало бы
    /// продолжить ровно то, что человек только что велел прекратить.
    /// </para>
    /// </remarks>
    private async Task PoprositDerzhateleyAsync(CleanupReport otchet, CancellationToken ct)
    {
        if (otchet.Cancelled)
        {
            foreach (var stroka in Stuck)
            {
                stroka.ProsbaProshla = true;
            }

            return;
        }

        // По одной, а не всё сразу. Две одновременные просьбы уходят в две
        // сессии Restart Manager, и порядок закрытия чужих программ становится
        // непредсказуемым при нулевом выигрыше: их тут единицы.
        foreach (var stroka in Stuck.ToList())
        {
            if (ct.IsCancellationRequested)
            {
                stroka.ProsbaProshla = true;
                continue;
            }

            await stroka.PoprositSamAsync(ct).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Собирает строки, у которых есть выход, и только их.
    /// </summary>
    /// <remarks>
    /// Признак это НАЗВАННЫЙ держатель, а не статус. Отбор по `Failed` тащил бы
    /// в список отказы, у которых закрывать нечего: отказ обработчика Windows,
    /// отказ утилиты, недоступный каталог. Кнопка «попросить программу
    /// закрыться» на такой строке была бы обманом, а имя держателя есть ровно
    /// там, где закрывать действительно есть кого.
    /// </remarks>
    private void SobratZastryavshie(IReadOnlyList<Finding> otpravlennye, CleanupReport otchet)
    {
        OchistitZanyatye();
        ReportRows.Clear();

        foreach (var ishod in otchet.Outcomes)
        {
            var nahodka = string.IsNullOrWhiteSpace(ishod.HoldingProcess)
                ? null
                : otpravlennye.FirstOrDefault(
                    n => string.Equals(n.Path, ishod.Path, StringComparison.OrdinalIgnoreCase));

            if (nahodka is null)
            {
                // Либо держателя не назвали, либо исход пришёл без своей
                // находки и повторить его нечем. И то, и другое значит «выхода
                // тут нет», а строка без выхода живёт в общем списке итога.
                ReportRows.Add(ishod);
                continue;
            }

            var stroka = new ZanyatyyViewModel(nahodka, ishod, _zanyatye, PovtoritAsync);
            stroka.PropertyChanged += PriSmeneZanyatoy;
            Stuck.Add(stroka);
        }
    }

    private void PriSmeneZanyatoy(object? otpravitel, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ZanyatyyViewModel.DeystviyaVidny) or null)
        {
            OnPropertyChanged(nameof(HasStuckActions));
        }
    }

    /// <summary>
    /// Снимает подписки и очищает список занятых.
    /// </summary>
    /// <remarks>
    /// Список чистится из трёх мест. Забытая отписка держала бы строку живой
    /// после ухода с экрана, и она продолжала бы дёргать признак кнопок уже
    /// снятого блока.
    /// </remarks>
    private void OchistitZanyatye()
    {
        foreach (var byla in Stuck)
        {
            byla.PropertyChanged -= PriSmeneZanyatoy;
        }

        Stuck.Clear();
    }

    /// <summary>
    /// Повторное удаление ОДНОЙ находки, после того как её освободили.
    /// </summary>
    /// <remarks>
    /// Идёт через ту же службу очистки, что и обычное удаление, значит пишется в
    /// тот же журнал и подчиняется тому же предохранителю путей. Отдельный путь
    /// удаления «для повтора» был бы вторым удалителем со своими правилами.
    /// </remarks>
    private async Task<DeleteOutcome> PovtoritAsync(Finding nahodka, CancellationToken ct)
    {
        var otchet = await _ochistka.RunAsync([nahodka], progress: null, ct).ConfigureAwait(true);

        var ishod = otchet.Outcomes.Count > 0
            ? otchet.Outcomes[0]
            : new DeleteOutcome(nahodka.Path, DeleteStatus.Failed, 0, "повтор не дал ни одного исхода");

        // Повтор это настоящая очистка, и её итог обязан попасть в тот же
        // список, что и первый проход. Иначе отчёт говорит «не удалось» про
        // файл, которого уже нет.
        Running.Add(ishod);

        if (ishod.Status == DeleteStatus.Deleted)
        {
            FreedBytes += ishod.BytesFreed;
            OsvobozhdenoPovtorom++;
        }

        return ishod;
    }

    [RelayCommand]
    private void Ostanovit() => _otmenaOchistki?.Cancel();

    [RelayCommand]
    private void Zavershit()
    {
        // Возврат к пустому, а не к прежнему списку: половина строк только что
        // перестала существовать, и показывать их снова значит предлагать
        // удалить удалённое. Своей кнопки «Сканировать» здесь нет по той же
        // причине, что и до первого прохода: проход один на продукт.
        foreach (var byla in Rows)
        {
            byla.PropertyChanged -= PriOtmetke;
        }

        Rows.Clear();
        OchistitZanyatye();
        ReportRows.Clear();
        Running.Clear();
        Report = null;
        Confirmed = false;
        Step = FlowStep.Selection;
        Pereschitat();

        State.Pusto(
            "Очистка завершена",
            "Список устарел: часть находок больше не существует. "
            + "Новый проход запускается на экране «Обзор»");
    }

    public void Dispose()
    {
        _otmenaOchistki?.Cancel();
        _otmenaOchistki?.Dispose();
        _otmenaOchistki = null;
    }

    /// <summary>
    /// Пустое состояние до первого прохода. Своей кнопки «Сканировать» здесь
    /// нет намеренно: проход один на продукт, и запускается он с обзора.
    /// </summary>
    private void PokazatBezProhoda() =>
        State.Pusto(
            "Диск ещё не сканировали",
            "Список появится после прохода. Запустить его можно на экране «Обзор»");

    private void PriOtmetke(object? otpravitel, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FindingViewModel.IsSelected))
        {
            Pereschitat();
        }
    }

    /// <summary>
    /// Пересчёт полным проходом по списку, а не прибавлением и вычитанием.
    /// </summary>
    /// <remarks>
    /// Накопительный счётчик расходится с истиной на первой же строке, которую
    /// сняли и поставили обратно, а расходится он молча: человек читает итог и
    /// жмёт «Удалить безвозвратно». Двадцать тысяч строк складываются за
    /// доли миллисекунды, это не то место, где стоит экономить.
    /// </remarks>
    private void Pereschitat()
    {
        var otmechennye = Rows.Where(r => r.IsSelected).ToList();

        SelectedCount = otmechennye.Count;
        SelectedBytes = otmechennye.Sum(r => r.SizeBytes);
        SelectedRiskBytes = otmechennye.Where(r => r.Tier == RiskTier.Risk).Sum(r => r.SizeBytes);

        // Разбивка по областям удаления считается тем же проходом. Отдельный
        // пересчёт разъехался бы с итогом на первой же снятой отметке, а читают
        // их рядом, на одном экране подтверждения.
        OnPropertyChanged(nameof(WholeCount));
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(EntryFileCount));
        OnPropertyChanged(nameof(EntrySummary));
    }
}
