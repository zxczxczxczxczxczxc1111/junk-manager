using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JunkManager.App.ViewModels;

/// <summary>
/// One entry in the left rail.
/// </summary>
internal sealed partial class SectionViewModel : ObservableObject
{
    /// <param name="id">Английский, латиницей: он же идентификатор автоматизации.</param>
    /// <param name="title">Русское название, как в боковой панели макета.</param>
    /// <param name="screen">
    /// Модель экрана этого раздела, либо null, пока экран не собран. Не object:
    /// оболочке нужно состояние экрана, и типизированный интерфейс это
    /// единственное, что не даёт положить сюда что попало и узнать об этом
    /// пустой правой половиной окна.
    /// </param>
    public SectionViewModel(string id, string title, IScreenViewModel? screen = null)
    {
        Id = id;
        Title = title;
        RailLabel = title.ToUpperInvariant();
        Screen = screen;

        // Состояние берётся у экрана, когда экран есть. Своё, второе, означало
        // бы рельс и экран с разными ответами на вопрос «что сейчас идёт».
        // Каждый раздел при этом остаётся со СВОИМ состоянием, а не с общим:
        // иначе ход сканирования, начатый в «Файлах», рисовал бы полосу и в
        // «Журнале».
        State = screen?.State ?? new ScreenStateViewModel();

        if (screen is null)
        {
            State.Pusto(title, "Экран этого раздела ещё не собран");
        }
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>
    /// Подпись в рельсе: макет ставит её прописными с разрядкой. Заглавные
    /// считаются один раз здесь, а не преобразователем на каждой перерисовке,
    /// и Title остаётся читаемым для средств доступности.
    /// </summary>
    public string RailLabel { get; }

    /// <summary>Идентификатор для FlaUI: «rail-overview», «rail-files».</summary>
    public string AutomationId => $"rail-{Id}";

    /// <summary>
    /// Модель экрана, либо null у раздела, экран которого ещё не собран.
    /// </summary>
    public IScreenViewModel? Screen { get; }

    /// <summary>
    /// Состояние экрана этого раздела. Живёт у раздела, а не у оболочки: у
    /// шести экранов шесть разных работ, и одно общее состояние показало бы
    /// ход чужого сканирования.
    /// </summary>
    public ScreenStateViewModel State { get; }

    /// <summary>
    /// Счётчик справа от названия, либо null. Null и «0» это разные вещи: ноль
    /// это посчитанный ноль, null это «ещё не считали», и рисовать их одинаково
    /// значит соврать про второе.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string? _badge;

    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>
/// Sections, navigation and the elevation state of the running process.
/// </summary>
/// <remarks>
/// Storage analysis is independent from cleanup. Confirmation and deletion remain steps inside Files.
/// </remarks>
internal sealed partial class ShellViewModel : ObservableObject
{
    /// <param name="obzor">
    /// Экран обзора. Приходит снаружи, а не создаётся здесь: он читает диск, и
    /// оболочка, умеющая его создать, перестала бы собираться в тестах.
    /// </param>
    /// <param name="fayly">Экран файлов, на тех же условиях.</param>
    /// <param name="zhurnal">Экран журнала, на тех же условиях.</param>
    /// <param name="reestr">Экран реестра, на тех же условиях.</param>
    /// <param name="nastroyki">Экран настроек, на тех же условиях.</param>
    public ShellViewModel(
        bool isElevated,
        IScreenViewModel? obzor = null,
        IScreenViewModel? fayly = null,
        IScreenViewModel? zhurnal = null,
        IScreenViewModel? reestr = null,
        IScreenViewModel? nastroyki = null,
        IScreenViewModel? programmy = null,
        IScreenViewModel? storage = null)
    {
        IsElevated = isElevated;

        Sections =
        [
            new SectionViewModel("overview", "Обзор", obzor),
            new SectionViewModel("files", "Файлы", fayly),
            new SectionViewModel("storage", "Место на диске", storage),
            new SectionViewModel("apps", "Программы", programmy),
            new SectionViewModel("registry", "Реестр", reestr),
            new SectionViewModel("history", "Журнал", zhurnal),
            new SectionViewModel("settings", "Настройки", nastroyki),
        ];

        _current = Sections[0];
        _current.IsCurrent = true;
    }

    /// <summary>
    /// Read-only navigation; scanning does not invent new sections at runtime.
    /// </summary>
    public IReadOnlyList<SectionViewModel> Sections { get; }

    [ObservableProperty]
    private SectionViewModel _current;

    public bool IsElevated { get; }

    /// <summary>«администратор» либо «обычные права». Показывается всегда.</summary>
    /// <remarks>
    /// Единственное место, где продукт говорит про права. Вторая подпись в
    /// подвале рельса («права подняты при запуске») убрана 06.09.2026: она
    /// повторяла эту плашку другими словами, а повтор человек читает как два
    /// разных факта и ищет между ними разницу.
    /// </remarks>
    public string RightsLabel => IsElevated ? "администратор" : "обычные права";

    /// <summary>
    /// Задача последнего обновления показанного экрана.
    /// </summary>
    /// <remarks>
    /// Переход остаётся МГНОВЕННЫМ: асинхронная команда с запретом одновременных
    /// запусков молча съедала бы нажатия на рельс, пока журнал читается с диска,
    /// и человек решил бы, что кнопка сломана. Поэтому обновление запускается и
    /// не ожидается, а задача остаётся здесь: без неё «обновление начато» и
    /// «обновление кончилось» неразличимы, и проверка ловила бы гонку вместо
    /// поведения. Продукт её не читает.
    /// </remarks>
    public Task PoslednyyPokaz { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void Vybrat(SectionViewModel? razdel)
    {
        if (razdel is null || ReferenceEquals(razdel, Current))
        {
            return;
        }

        Current.IsCurrent = false;
        Current = razdel;
        Current.IsCurrent = true;

        // Экран просят перечитать себя ПОСЛЕ переключения: показать надо новый
        // раздел, а не ждать его чтения на старом.
        PoslednyyPokaz = Current.Screen?.PriPokazeAsync(CancellationToken.None)
            ?? Task.CompletedTask;
    }

    /// <summary>
    /// Переход по идентификатору раздела: «Перейти к очистке» на обзоре.
    /// </summary>
    /// <remarks>
    /// Неизвестный идентификатор молча ничего не делает. Это опечатка в
    /// разметке, а разметка не проверяется сборкой: упасть здесь значит убить
    /// окно нажатием на кнопку. Опечатку ловит проверка разметки, а не
    /// исключение в лицо человеку.
    /// </remarks>
    [RelayCommand]
    private void Pereyti(string? id) =>
        Vybrat(Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal)));
}
