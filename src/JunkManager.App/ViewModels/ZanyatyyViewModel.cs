using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JunkManager.App.Services;
using JunkManager.Core;
using JunkManager.Deletion;

namespace JunkManager.App.ViewModels;

/// <summary>
/// One item the cleanup could not take because somebody holds it.
/// </summary>
/// <remarks>
/// <para>
/// Существует, чтобы у отказа «файл занят» был ВЫХОД. До 06.09.2026 продукт
/// называл держателя и на этом заканчивал, то есть сообщал человеку проблему и
/// оставлял его с ней наедине.
/// </para>
/// <para>
/// Три выхода и ровно в этом порядке. Первый продукт делает САМ: просит
/// держателей закрыться штатно и повторяет удаление. Потерь при этом нет,
/// программа сохраняет своё и выходит сама, а решать тут человеку нечего, у
/// безболезненного шага нет второй стороны. Кнопок на строке поэтому сразу
/// три и не бывает: пока просьба идёт, строка показывает ход, и только если
/// просьба не сработала, появляются оставшиеся два выхода. Отложить до
/// перезагрузки работает даже с тем, что держит сама система; принудительное
/// завершение стоит последним и с прямым предупреждением, потому что
/// несохранённое пропадает без вопросов от самой программы.
/// </para>
/// </remarks>
internal sealed partial class ZanyatyyViewModel : ObservableObject
{
    private readonly Finding _nahodka;
    private readonly ILockedFileService _sluzhba;
    private readonly Func<Finding, CancellationToken, Task<DeleteOutcome>> _povtor;

    /// <param name="povtor">
    /// Повторное удаление той же находки. Приходит извне, потому что очистку
    /// ведёт экран, а не строка: строка, умеющая запускать удаление сама по
    /// себе, обошла бы подтверждение.
    /// </param>
    public ZanyatyyViewModel(
        Finding nahodka,
        DeleteOutcome ishod,
        ILockedFileService sluzhba,
        Func<Finding, CancellationToken, Task<DeleteOutcome>> povtor)
    {
        ArgumentNullException.ThrowIfNull(nahodka);
        ArgumentNullException.ThrowIfNull(ishod);

        _nahodka = nahodka;
        _sluzhba = sluzhba;
        _povtor = povtor;

        Path = nahodka.Path;
        Name = nahodka.Name;
        Holder = ishod.HoldingProcess;
        Reason = ishod.Reason;
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>Кто держал файл на момент очистки, если удалось назвать.</summary>
    public string? Holder { get; }

    /// <summary>Причина отказа из самой очистки.</summary>
    public string? Reason { get; }

    /// <summary>
    /// Что вышло из последнего действия. Null, пока человек ничего не нажимал.
    /// </summary>
    [ObservableProperty]
    private string? _note;

    /// <summary>
    /// Файл унесён либо назначен к удалению при загрузке. Кнопки после этого
    /// не нужны: предлагать закрыть программу ради файла, которого уже нет,
    /// значит предлагать бессмысленное.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeystviyaVidny))]
    private bool _resheno;

    /// <summary>
    /// Действие идёт. Кнопки на это время гаснут: две одновременные просьбы
    /// закрыться одной и той же программе это гонка с непредсказуемым итогом.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OtlozhitCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZavershitCommand))]
    [NotifyPropertyChangedFor(nameof(DeystviyaVidny))]
    private bool _idyot;

    /// <summary>
    /// Автоматическая просьба закрыться отработала, чем бы ни кончилась.
    /// </summary>
    /// <remarks>
    /// Отдельный признак, а не отсутствие `Idyot`: до начала просьбы «не идёт»
    /// тоже верно, и на этом кнопки успевали моргнуть на экране до того, как
    /// продукт вообще попробовал обойтись без человека.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeystviyaVidny))]
    private bool _prosbaProshla;

    /// <summary>
    /// Показывать ли два оставшихся выхода. Только после того, как продукт
    /// попросил сам и не добился своего.
    /// </summary>
    public bool DeystviyaVidny => ProsbaProshla && !Resheno;

    private bool Mozhno => !Idyot && !Resheno;

    /// <summary>
    /// Просит держателей закрыться и повторяет удаление. Зовётся ПРОДУКТОМ,
    /// а не человеком: кнопки у этого шага нет.
    /// </summary>
    public async Task PoprositSamAsync(CancellationToken ct)
    {
        try
        {
            await SdelatAsync(_sluzhba.PoprositZakrytsyaAsync, povtorit: true, ct)
                .ConfigureAwait(true);
        }
        finally
        {
            // В finally, потому что иначе отменённая или упавшая просьба
            // оставила бы строку без единой кнопки, то есть тем самым тупиком,
            // ради выхода из которого всё это и написано.
            ProsbaProshla = true;
        }
    }

    [RelayCommand(CanExecute = nameof(Mozhno))]
    private Task ZavershitAsync(CancellationToken ct) =>
        SdelatAsync(_sluzhba.ZavershitAsync, povtorit: true, ct);

    [RelayCommand(CanExecute = nameof(Mozhno))]
    private Task OtlozhitAsync(CancellationToken ct) =>
        // Повтора НЕТ намеренно: файл держат прямо сейчас, и удалить его сию
        // секунду нельзя ничем. Он уйдёт при загрузке, и строка это скажет.
        SdelatAsync(_sluzhba.OtlozhitNaZagruzkuAsync, povtorit: false, ct);

    private async Task SdelatAsync(
        Func<string, CancellationToken, Task<LockedFileActionResult>> deystvie,
        bool povtorit,
        CancellationToken ct)
    {
        Idyot = true;

        try
        {
            var itog = await deystvie(Path, ct).ConfigureAwait(true);

            if (!itog.Ok)
            {
                Note = itog.Note;
                return;
            }

            if (!povtorit)
            {
                Note = itog.Note;
                Resheno = true;
                return;
            }

            var udalenie = await _povtor(_nahodka, ct).ConfigureAwait(true);

            if (udalenie.Status == DeleteStatus.Deleted)
            {
                Note = itog.Note + ". Файл удалён";
                Resheno = true;
                return;
            }

            // Держателя убрали, а файл всё равно не ушёл. Молчать тут нельзя:
            // человек видел бы «готово» на строке, которая осталась на диске.
            Note = itog.Note + ". Но удалить всё равно не вышло: "
                + (udalenie.Reason ?? "причина не названа");
        }
        finally
        {
            Idyot = false;
        }
    }
}
