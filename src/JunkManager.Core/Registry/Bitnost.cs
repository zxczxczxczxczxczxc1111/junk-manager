namespace JunkManager.Core.Registry;

/// <summary>
/// The 32-bit twin of a path, for values read from the 32-bit view.
/// </summary>
/// <remarks>
/// <para>
/// Запись, написанная 32-битной программой, говорит про СВОИ каталоги.
/// `%ProgramFiles%` для неё это «Program Files (x86)», а `C:\Windows\System32`
/// это `SysWOW64`: перенаправление делает система при запуске, и в самой
/// строке этого не видно.
/// </para>
/// <para>
/// Наш процесс 64-битный, переменные раскрывает по-своему и файла по такому
/// пути не находит. Дальше запись объявляется мусором, хотя программа жива и
/// запускается. Это ложное срабатывание худшего рода: продукт предлагает
/// снести рабочий автозапуск и уверенно объясняет, почему.
/// </para>
/// <para>
/// Найдено разбором чужих чистильщиков 05.09.2026: там это перечислено среди
/// механизмов ложного «файла нет». Проверка существования обязана спросить и
/// про близнеца.
/// </para>
/// </remarks>
internal static class Bitnost
{
    /// <summary>
    /// Путь, каким его увидела бы 32-битная программа, или null, если
    /// перенаправление к этому пути отношения не имеет.
    /// </summary>
    /// <remarks>
    /// Каталоги приходят параметрами, а не берутся из окружения прямо тут:
    /// проверка иначе зависела бы от того, на какой машине её запустили, и
    /// «Program Files» на не английской системе или на диске D сломал бы её.
    /// </remarks>
    public static string? Blizhnec32(
        string put, string programFiles, string programFilesX86, string system32, string sysWow64)
    {
        ArgumentNullException.ThrowIfNull(put);

        // Порядок важен: «Program Files (x86)» начинается с «Program Files», и
        // проверка на уже перенаправленный путь обязана идти первой, иначе
        // близнец построится второй раз и уедет в «(x86) (x86)».
        if (Nachinaetsya(put, programFilesX86) || Nachinaetsya(put, sysWow64))
        {
            return null;
        }

        if (Nachinaetsya(put, programFiles))
        {
            return string.Concat(programFilesX86, put.AsSpan(programFiles.Length));
        }

        if (Nachinaetsya(put, system32))
        {
            return string.Concat(sysWow64, put.AsSpan(system32.Length));
        }

        return null;
    }

    /// <summary>Тот же путь, но каталоги берутся у системы.</summary>
    public static string? Blizhnec32(string put) => Blizhnec32(
        put,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));

    private static bool Nachinaetsya(string put, string katalog) =>
        !string.IsNullOrEmpty(katalog)
        && (put.Equals(katalog, StringComparison.OrdinalIgnoreCase)
            || put.StartsWith(katalog.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase));
}
