using CommunityToolkit.Mvvm.ComponentModel;
using JunkManager.App.Text;
using JunkManager.Core;

namespace JunkManager.App.ViewModels;

/// <summary>
/// One finding as a row. Everything a person needs to decide is a property
/// here, and none of it hides behind a hover: name, consequence and path are
/// shown at the same time, which is the whole reason the explorer layout won.
/// </summary>
internal sealed partial class FindingViewModel(Finding source) : ObservableObject
{
    public Finding Source { get; } = source;

    public string Name => Source.Name;

    /// <summary>
    /// Что услышит экранный диктор вместо имени типа.
    /// </summary>
    /// <remarks>
    /// Строки списка находок это ContentPresenter, у которого элемента
    /// автоматизации нет. WPF в таком случае берёт имя строки из
    /// <see cref="object.ToString"/> самой модели, и живой прогон 05.09.2026
    /// показал в дереве «JunkManager.App.ViewModels.FindingViewModel» на каждой
    /// строке. Разметка при этом безупречна, поймать это можно только на живом
    /// дереве.
    /// </remarks>
    public override string ToString() => $"{Name}. {Consequence}";

    public string Path => Source.Path;

    public string Consequence => Source.Consequence;

    public long SizeBytes => Source.SizeBytes;

    public RiskTier Tier => Source.Tier;

    public DeleteScope Scope => Source.Scope;

    public int TargetCount => Source.DeletionTargets.Count;

    /// <summary>
    /// Что именно исчезнет. Это не украшение строки: у находки с областью
    /// SelectedEntries путём может быть пользовательский %TEMP%, и разница
    /// между «уйдёт папка» и «уйдут файлы внутри» это разница между рабочей
    /// системой и сломанной.
    /// </summary>
    public string ScopeLabel => Scope switch
    {
        _ when Source.Source == FindingSource.Vacuum => "сжатие, файл останется",
        _ when Source.Source is FindingSource.VolumeCache or FindingSource.PlatformTool => "очистка средствами Windows",
        DeleteScope.Whole when Source.FileSnapshots is not null => "один файл",
        DeleteScope.Whole => "папка целиком",
        DeleteScope.SelectedEntries =>
            $"{RussianPlural.Format(TargetCount, "файл", "файла", "файлов")} внутри, папка останется",
        _ => throw new InvalidOperationException($"неизвестная область удаления {Scope}"),
    };

    /// <summary>«сегодня», «12 дней назад» или «нет данных».</summary>
    public string AgeLabel => Source.LastUsedDays switch
    {
        null => "нет данных",
        0 => "сегодня",
        var dney => $"{RussianPlural.Format(dney.Value, "день", "дня", "дней")} назад",
    };

    /// <summary>Доля от самой крупной находки категории, для тонкой полосы под строкой.</summary>
    [ObservableProperty]
    private double _shareOfMax;

    /// <summary>
    /// Отмечено ли к удалению. Безопасное отмечается сразу, опасное нет, и это
    /// решает не строка, а тот, кто её создаёт.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}
