using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Core.Registry;

/// <summary>
/// Pulls the file a registry value points at, or refuses and says why. The most
/// dangerous piece of the registry module: everything downstream acts on what
/// this returns, and nobody re-checks it. So it is fail-closed everywhere, and
/// a value it does not understand produces no finding at all.
/// </summary>
public static class RegistryTargetExtractor
{
    private static readonly string[] Peredatchiki =
        ["rundll32.exe", "rundll32", "regsvr32.exe", "regsvr32"];

    private static readonly string[] Ispolnyaemye =
        [".exe", ".dll", ".com", ".bat", ".cmd", ".scr", ".cpl", ".ocx"];

    // SearchValues, а не char[]: CA1870 требует кэшированный набор, и он же
    // быстрее на длинных строках. Тот же приём и по той же причине стоит в
    // SafetyGuard.
    private static readonly System.Buffers.SearchValues<char> Nedopustimye =
        System.Buffers.SearchValues.Create("<>|\"*?");

    /// <param name="fileExists">
    /// How to ask whether a path is there. A parameter and not a hard call to
    /// File.Exists because the unquoted-with-spaces case is decided by asking
    /// the file system, and a test that has to plant real files under
    /// "C:\Program Files" to exercise it is a test nobody runs.
    /// </param>
    public static bool TryExtract(
        string? raw,
        out string target,
        [NotNullWhen(false)] out string? reason,
        Func<string, bool>? fileExists = null)
    {
        target = string.Empty;
        var sushchestvuet = fileExists ?? File.Exists;

        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "значение пустое";
            return false;
        }

        var stroka = raw.Trim();

        // Раскрытие тем же способом, каким это делает сам Windows при запуске
        // REG_EXPAND_SZ. Раскрыватель правил тут не годится: он бросает на
        // неопределённой переменной, а здесь неопределённая переменная это
        // штатный отказ, а не поломка файла правил.
        stroka = Environment.ExpandEnvironmentVariables(stroka);

        if (stroka.Contains('%', StringComparison.Ordinal))
        {
            reason = "в значении осталась нераскрытая переменная окружения";
            return false;
        }

        // "\??\" это форма пути ядра, "\\?\" это форма длинного пути. Обе
        // означают тот же самый файл.
        if (stroka.StartsWith(@"\??\", StringComparison.Ordinal)
            || stroka.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            stroka = stroka[4..].TrimStart();
        }

        if (!RazobratGolovu(stroka, sushchestvuet, out var golova, out var hvost, out reason))
        {
            return false;
        }

        // rundll32 и regsvr32 живы всегда, находкой оказалась бы сама система.
        // Смысл несёт библиотека в аргументе.
        if (Peredatchik(golova) && !VytashchitBiblioteku(hvost, out golova, out reason))
        {
            return false;
        }

        return Proverit(golova, out target, out reason);
    }

    private static bool RazobratGolovu(
        string stroka,
        Func<string, bool> sushchestvuet,
        out string golova,
        out string hvost,
        [NotNullWhen(false)] out string? reason)
    {
        golova = string.Empty;
        hvost = string.Empty;

        // Кавычки снимают всякую двусмысленность: автор строки уже сказал, где
        // кончается путь, и спрашивать файловую систему не о чем.
        if (stroka.StartsWith('"'))
        {
            var konec = stroka.IndexOf('"', 1);

            if (konec < 0)
            {
                reason = "кавычка открыта и не закрыта";
                return false;
            }

            golova = stroka[1..konec].Trim();
            hvost = stroka[(konec + 1)..].Trim();
            reason = null;
            return true;
        }

        var pervyyProbel = stroka.IndexOf(' ', StringComparison.Ordinal);
        var pervoeSlovo = pervyyProbel < 0 ? stroka : stroka[..pervyyProbel];

        // Передатчик опознаётся ДО проверки на похожесть на путь: в реестре он
        // сплошь и рядом записан голым именем, без каталога.
        if (Peredatchik(pervoeSlovo))
        {
            golova = pervoeSlovo;
            hvost = pervyyProbel < 0 ? string.Empty : stroka[(pervyyProbel + 1)..].Trim();
            reason = null;
            return true;
        }

        // Пробел в строке без кавычек это либо разделитель аргументов, либо
        // часть имени каталога. На глаз это неразличимо, поэтому спрашивается
        // файловая система: первый существующий префикс и есть путь.
        for (var i = 0; i < stroka.Length; i++)
        {
            if (stroka[i] == ' ' && sushchestvuet(stroka[..i]))
            {
                golova = stroka[..i];
                hvost = stroka[(i + 1)..].Trim();
                reason = null;
                return true;
            }
        }

        if (sushchestvuet(stroka))
        {
            golova = stroka;
            reason = null;
            return true;
        }

        if (pervyyProbel < 0)
        {
            // Пробелов нет вовсе, делить нечего.
            golova = stroka;
            reason = null;
            return true;
        }

        // Ни один префикс не существует, и это как раз интересный случай: мы
        // ищем битые ссылки. Правило 3 раздела 9.1 спеки разрешает взять слово
        // до первого пробела, "если оно похоже на путь". Этого мало: у строки
        // "C:\Program Files\Dead App\app.exe -q" таким словом окажется
        // "C:\Program", и продукт соврёт человеку о том, чего именно нет.
        // Поэтому дополнительно требуется расширение исполняемого файла.
        if (Ispolnyaemyy(pervoeSlovo))
        {
            golova = pervoeSlovo;
            hvost = stroka[(pervyyProbel + 1)..].Trim();
            reason = null;
            return true;
        }

        reason = "путь без кавычек не опознан: ни один префикс не существует, "
            + "а первое слово не оканчивается расширением исполняемого файла";
        return false;
    }

    private static bool Peredatchik(string golova) =>
        Peredatchiki.Contains(
            Path.GetFileName(golova.Trim().Trim('"')), StringComparer.OrdinalIgnoreCase);

    private static bool Ispolnyaemyy(string kandidat) =>
        Ispolnyaemye.Contains(Path.GetExtension(kandidat), StringComparer.OrdinalIgnoreCase);

    private static bool VytashchitBiblioteku(
        string hvost,
        out string biblioteka,
        [NotNullWhen(false)] out string? reason)
    {
        biblioteka = string.Empty;
        var argument = hvost.Trim();

        // Ключи перед путём: regsvr32 пишут как "/s C:\...\x.ocx", и ключ это
        // не библиотека.
        while (argument.StartsWith('/') || argument.StartsWith('-'))
        {
            var probel = argument.IndexOf(' ', StringComparison.Ordinal);

            if (probel < 0)
            {
                reason = "у передатчика в аргументах одни ключи и ни одной библиотеки";
                return false;
            }

            argument = argument[(probel + 1)..].TrimStart();
        }

        if (argument.Length == 0)
        {
            reason = "у передатчика нет аргумента с библиотекой";
            return false;
        }

        if (argument.StartsWith('"'))
        {
            var konec = argument.IndexOf('"', 1);

            if (konec < 0)
            {
                reason = "у аргумента передатчика кавычка открыта и не закрыта";
                return false;
            }

            argument = argument[1..konec];
        }
        else
        {
            // Точка входа отделяется запятой, и запятая идёт раньше пробела:
            // "shell32.dll,Control_RunDLL foo.cpl".
            var zapyataya = argument.IndexOf(',', StringComparison.Ordinal);

            if (zapyataya >= 0)
            {
                argument = argument[..zapyataya];
            }

            var probel = argument.IndexOf(' ', StringComparison.Ordinal);

            if (probel >= 0)
            {
                argument = argument[..probel];
            }
        }

        argument = argument.Trim();

        if (!Ispolnyaemyy(argument) || Path.GetDirectoryName(argument) is not { Length: > 0 })
        {
            // "DLL без пути извлечению не поддаётся", раздел 9.1 спеки. Искать
            // её пришлось бы по правилам загрузчика, а те зависят от текущего
            // каталога процесса, которого у записи реестра нет вовсе.
            reason = "библиотека у передатчика указана без пути, проверить её нечем";
            return false;
        }

        biblioteka = argument;
        reason = null;
        return true;
    }

    private static bool Proverit(
        string kandidat,
        out string target,
        [NotNullWhen(false)] out string? reason)
    {
        target = string.Empty;
        var put = kandidat.Trim().Trim('"');

        if (put.Length == 0)
        {
            reason = "после разбора пути не осталось";
            return false;
        }

        if (put.AsSpan().IndexOfAny(Nedopustimye) >= 0 || put.Any(char.IsControl))
        {
            reason = "в пути есть символы, недопустимые в имени файла";
            return false;
        }

        if (put.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Тот же отказ, что и у SafetyGuard, плюс своя причина: проверка
            // существования по мёртвой шаре висит десятками секунд на каждой
            // записи.
            reason = "сетевые пути не обрабатываются";
            return false;
        }

        if (!Path.IsPathFullyQualified(put))
        {
            reason = "путь не абсолютный, проверить его нечем";
            return false;
        }

        string koren;
        try
        {
            koren = Path.GetPathRoot(put) ?? string.Empty;
        }
        catch (ArgumentException ex)
        {
            reason = $"путь не разбирается: {ex.Message}";
            return false;
        }

        if (koren.Length == 0 || !Directory.Exists(koren))
        {
            // Съёмный носитель вынут, сетевой диск отключён. Файла нет, но и
            // записи нет: вернут диск, и она оживёт. Пункт 5 раздела 9.1 спеки.
            reason = $"корня тома {koren} сейчас нет, судить о записи нельзя";
            return false;
        }

        // Короткое имя вида C:\PROGRA~1 намеренно НЕ разворачивается. Разворот
        // потребовал бы GetLongPathName, а отвечаем мы всё равно на вопрос
        // "существует ли", и на него Win32 по короткому имени отвечает сам.
        target = put;
        reason = null;
        return true;
    }
}
