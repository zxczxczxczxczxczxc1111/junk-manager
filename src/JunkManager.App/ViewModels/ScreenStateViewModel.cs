using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JunkManager.App.ViewModels;

/// <summary>
/// Which of the four shapes a screen is currently in.
/// </summary>
/// <remarks>
/// Refusing elevation is deliberately NOT a phase. Content is still shown when
/// rights are missing, only less of it, and the note says "не проверялось"
/// rather than "ничего нет". A fifth member here would force every screen to
/// choose between showing partial content and showing the note, and half of
/// them would choose wrong.
/// </remarks>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined for the same reason as RiskTier and DeleteStatus: an " +
        "unassigned phase must not read as Ready, because Ready means 'the list you are " +
        "looking at is complete'.")]
internal enum ScreenPhase
{
    /// <summary>Работа идёт. Есть доля и текущий путь.</summary>
    Loading = 1,

    /// <summary>Работа закончилась и не нашла ничего. Это не ошибка.</summary>
    Empty = 2,

    /// <summary>Работа не закончилась. Есть заголовок и объяснение.</summary>
    Error = 3,

    /// <summary>Есть что показывать.</summary>
    Ready = 4,
}

/// <summary>
/// The state of one screen, and the one mechanism all six screens share. Every
/// screen owns an instance and no screen owns its own copy of the logic: five
/// copies of "what do we show when there is nothing" is five different answers.
/// </summary>
internal sealed partial class ScreenStateViewModel : ObservableObject
{
    [ObservableProperty]
    private ScreenPhase _phase = ScreenPhase.Empty;

    [ObservableProperty]
    private string _emptyTitle = string.Empty;

    [ObservableProperty]
    private bool _isWelcome;

    [ObservableProperty]
    private string _emptyBody = string.Empty;

    /// <summary>Подпись кнопки в пустом состоянии. Null значит кнопки нет.</summary>
    [ObservableProperty]
    private string? _emptyAction;

    /// <summary>
    /// Что делает эта кнопка. Подпись и команда ставятся одним вызовом
    /// намеренно: разнесённые по разным местам, они рано или поздно разъезжаются
    /// и на экране остаётся кнопка, которая ни на что не отвечает.
    /// </summary>
    [ObservableProperty]
    private ICommand? _emptyCommand;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorBody = string.Empty;

    [ObservableProperty]
    private ICommand? _retryCommand;

    [ObservableProperty]
    private ICommand? _cancelCommand;

    /// <summary>
    /// Доля от нуля до единицы, либо null, когда конца не видно. Null и ноль это
    /// разные вещи: ноль это «начали и ничего не сделали», null это «сколько
    /// всего, ещё неизвестно», и полоса рисуется по-разному.
    /// </summary>
    [ObservableProperty]
    private double? _progressShare;

    [ObservableProperty]
    private string _progressPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestriction))]
    private string? _restrictionText;

    [ObservableProperty]
    private string? _restrictionDetails;

    [ObservableProperty]
    private bool _restrictionDetailsOpen;

    [RelayCommand]
    private void ToggleRestrictionDetails() => RestrictionDetailsOpen = !RestrictionDetailsOpen && !string.IsNullOrEmpty(RestrictionDetails);

    public bool HasRestriction => !string.IsNullOrEmpty(RestrictionText);

    public void Nachat(string? put = null, ICommand? cancel = null)
    {
        IsWelcome = false;
        CancelCommand = cancel;
        Phase = ScreenPhase.Loading;
        ProgressShare = null;
        ProgressPath = put ?? string.Empty;
    }

    public void Hod(double? dolya, string? put)
    {
        // Deliberately does not set the phase. A progress report that arrives
        // after cancellation would otherwise drag a finished screen back into
        // Loading, and the person would watch a bar move on a stopped job.
        ProgressShare = dolya is null ? null : Math.Clamp(dolya.Value, 0, 1);

        if (put is not null)
        {
            ProgressPath = put;
        }
    }

    public void Pusto(string zagolovok, string telo, string? deystvie = null, ICommand? komanda = null)
    {
        IsWelcome = false;
        EmptyTitle = zagolovok;
        EmptyBody = telo;
        EmptyAction = deystvie;
        EmptyCommand = komanda;
        Phase = ScreenPhase.Empty;
    }

    public void Oshibka(string zagolovok, string telo, ICommand? retry = null)
    {
        IsWelcome = false;
        ErrorTitle = zagolovok;
        ErrorBody = telo;
        RetryCommand = retry;
        Phase = ScreenPhase.Error;
    }

    public void Gotovo() { IsWelcome = false; Phase = ScreenPhase.Ready; }

    /// <summary>
    /// Ставит или снимает плашку ограничения. Не меняет фазу: ограничение
    /// живёт поверх любой из них, включая ошибку.
    /// </summary>
    public void Ogranichit(string? tekst, string? details = null)
    {
        RestrictionText = tekst;
        RestrictionDetails = details;
        RestrictionDetailsOpen = false;
    }
}
