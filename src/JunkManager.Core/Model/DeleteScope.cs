using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Core;

/// <summary>
/// What actually goes when a person agrees to a finding.
/// </summary>
/// <remarks>
/// This exists because of a defect that was live and silent: a rule with an age
/// cut-off counted only the old bytes, and deletion then took the whole
/// directory. The number shown and the thing removed were different, and the
/// difference was exactly the files the person wanted to keep.
/// </remarks>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero IS defined here and it is Whole, the behaviour every finding had before " +
        "this type existed. An unset scope must not silently become the narrower mode: " +
        "that would leave a caller believing files were spared when nothing selected them.")]
public enum DeleteScope
{
    /// <summary>Путь находки целиком: файл или каталог со всем содержимым.</summary>
    Whole = 0,

    /// <summary>
    /// Только перечисленные в <see cref="Finding.Targets"/> файлы внутри пути.
    /// Сам каталог остаётся, и это не осторожность ради осторожности: в этом
    /// режиме путём находки может быть пользовательский %TEMP%, куда прямо
    /// сейчас пишут работающие программы.
    /// </summary>
    SelectedEntries = 1,
}
