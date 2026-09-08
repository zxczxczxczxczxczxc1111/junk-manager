using JunkManager.Core;

namespace JunkManager.App.Services;

/// <summary>
/// The application's whole view of scanning. Exists so the view models can be
/// tested without a disk: everything below this line reads real directories.
/// </summary>
internal interface IScanService
{
    /// <summary>
    /// Сколько правил загрузилось. Показывается на «Обзоре» в оценке
    /// длительности прохода, и только там: в подвале рельса это число стояло до
    /// 06.09.2026 голым, и решить по нему человеку было нечего.
    /// </summary>
    int RulesCount { get; }

    /// <summary>
    /// Имя категории по id правила. Находка несёт RuleId, а имя категории живёт
    /// в файле правил и до находки не доезжает: перенести его в саму находку
    /// значило бы поменять запись, которую читают приёмка посева и ScanJson.
    /// </summary>
    /// <remarks>
    /// Реализация по умолчанию пустая намеренно. Поддельный сканер в тестах
    /// проверяет арифметику группировки, а не имена категорий, и требовать от
    /// него словарь значило бы заставить каждый тест повторять то, чего он не
    /// проверяет. С пустым словарём группировка идёт по RuleId.
    /// </remarks>
    IReadOnlyDictionary<string, string> CategoryByRule =>
        System.Collections.Frozen.FrozenDictionary<string, string>.Empty;

    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct);
}
