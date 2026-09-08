using System.Globalization;
using System.Runtime.InteropServices;

namespace JunkManager.Deletion;

/// <summary>
/// Who is holding a file open.
/// </summary>
/// <param name="ProcessId">PID as the Restart Manager reported it.</param>
/// <param name="Name">
/// The application name the Restart Manager knows, which for a service is its
/// display name and for a program is its window title or executable.
/// </param>
/// <param name="ServiceName">Short service name, null for anything else.</param>
/// <param name="Kind">What sort of holder this is, straight from the API.</param>
public sealed record FileHolder(
    int ProcessId,
    string Name,
    string? ServiceName,
    HolderKind Kind)
{
    /// <summary>
    /// What a person needs to read: "Проводник (PID 4312)".
    /// </summary>
    public string Describe() =>
        ServiceName is null
            ? string.Create(CultureInfo.CurrentCulture, $"{Name} (PID {ProcessId})")
            : string.Create(CultureInfo.CurrentCulture, $"служба {ServiceName} (PID {ProcessId})");
}

/// <summary>
/// The Restart Manager's own classification, kept rather than flattened: telling
/// somebody to close a window and telling them a service holds the file are
/// different instructions, and the difference is only available here.
/// </summary>
public enum HolderKind
{
    Unknown = 0,
    MainWindow = 1,
    OtherWindow = 2,
    Service = 3,
    Explorer = 4,
    Console = 5,
    Critical = 1000,
}

/// <summary>
/// Names the processes holding a file, through the Restart Manager. The honest
/// answer to "cannot delete, file in use" is who is using it; anything less
/// turns a solvable situation into a shrug.
/// </summary>
public static class LockedFileInspector
{
    /// <summary>
    /// Сколько ждать, пока попрошенный держатель действительно отпустит файл.
    /// Секунды, а не десятки: RmShutdown возвращается уже ПОСЛЕ закрытия
    /// откликнувшихся программ, и срок нужен только на отпускание ручки.
    /// </summary>
    private static readonly TimeSpan SrokZakrytiya = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Asks the Restart Manager who holds <paramref name="path"/>.
    /// </summary>
    /// <returns>
    /// True when the question was answered, even if the answer is an empty list:
    /// "nobody holds it" and "could not ask" are different facts and must not
    /// collapse into one.
    /// </returns>
    public static bool TryGetHolders(
        string path,
        out IReadOnlyList<FileHolder> holders,
        out string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        holders = [];

        // Буфер ключа сессии ровно CCH_RM_SESSION_KEY + 1 символ. Размер не
        // произволен: RmStartSession ПИШЕТ в него, и буфер короче нужного это
        // порча чужой памяти, а не мелкая неточность.
        var klyuch = new char[Native.SessionKeyLength + 1];

        var kod = Native.RmStartSession(out var sessiya, 0, klyuch);
        if (kod != Native.ErrorSuccess)
        {
            reason = "RmStartSession вернул " + Opisat(kod);
            return false;
        }

        bool otvecheno;
        int kodZakrytiya;

        try
        {
            kod = Native.RmRegisterResources(sessiya, 1, [path], 0, null, 0, null);
            if (kod != Native.ErrorSuccess)
            {
                reason = "RmRegisterResources вернул " + Opisat(kod);
                otvecheno = false;
            }
            else
            {
                otvecheno = Sprosit(sessiya, out holders, out reason);
            }
        }
        finally
        {
            // Сессия закрывается всегда. Это системный ресурс с ограниченным
            // числом слотов на машину, и утечка тут кончается тем, что
            // определитель перестаёт работать у всех, включая чужие программы.
            kodZakrytiya = Native.RmEndSession(sessiya);
        }

        if (kodZakrytiya != Native.ErrorSuccess)
        {
            // Ответ на вопрос уже получен и остаётся верным, поэтому выбрасывать
            // его было бы глупо. Но незакрытая сессия портит жизнь следующим
            // вызовам и чужим программам, и промолчать про неё нельзя: код
            // возврата тут именно поэтому не игнорируется.
            reason = (reason is null ? string.Empty : reason + "; ")
                + "сессия Restart Manager не закрылась, RmEndSession вернул " + Opisat(kodZakrytiya);
        }

        return otvecheno;
    }

    /// <summary>
    /// Asks whoever holds <paramref name="path"/> to close itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Не убийство. Restart Manager шлёт оконному приложению `WM_CLOSE`,
    /// консольному `CTRL+C`, а службу останавливает штатно, то есть программа
    /// сохраняет своё и выходит сама. Ровно так закрывают занятые файлы
    /// установщики Windows, и это единственный способ освободить файл без
    /// потери несохранённых данных человека.
    /// </para>
    /// <para>
    /// Критический держатель НЕ трогается никогда, и это проверяется до вызова
    /// закрытия. Restart Manager помечает так процессы, чьё завершение уронит
    /// систему, и просьба к такому процессу это не освобождённый файл, а
    /// перезагрузка посреди очистки.
    /// </para>
    /// </remarks>
    /// <returns>
    /// True, когда закрывать было некого или все закрылись. False с причиной,
    /// когда система отказалась: держатель критический либо не поддался.
    /// </returns>
    public static bool TryAskToClose(string path, out string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var klyuch = new char[Native.SessionKeyLength + 1];

        var kod = Native.RmStartSession(out var sessiya, 0, klyuch);
        if (kod != Native.ErrorSuccess)
        {
            reason = "RmStartSession вернул " + Opisat(kod);
            return false;
        }

        try
        {
            kod = Native.RmRegisterResources(sessiya, 1, [path], 0, null, 0, null);
            if (kod != Native.ErrorSuccess)
            {
                reason = "RmRegisterResources вернул " + Opisat(kod);
                return false;
            }

            if (!Sprosit(sessiya, out var derzhateli, out reason))
            {
                return false;
            }

            var kriticheskiy = derzhateli.FirstOrDefault(d => d.Kind == HolderKind.Critical);

            if (kriticheskiy is not null)
            {
                reason = $"файл держит {kriticheskiy.Describe()}, и это критический процесс: "
                    + "его закрытие уронит систему, поэтому продукт его не трогает";
                return false;
            }

            // Флаги ноль: просим закрыться, а не заставляем. RmForceShutdown
            // здесь был бы тем же убийством, только чужими руками, и человек,
            // нажавший «попросить закрыться», получил бы потерю данных.
            kod = Native.RmShutdown(sessiya, 0, nint.Zero);

            // Код возврата тут НЕ является ответом на вопрос «файл свободен?».
            // Проверено фактом 06.09.2026 на одном и том же консольном
            // держателе: система отвечает то 351 (ERROR_FAIL_NOACTION_REBOOT),
            // то ноль, и в ОБОИХ случаях процесс остаётся на месте и продолжает
            // держать файл. Верить можно только повторному опросу, поэтому оба
            // кода идут дальше одинаково, а решает проверка фактом.
            if (kod != Native.ErrorSuccess && kod != Native.ErrorFailNoactionReboot)
            {
                reason = "закрыть держателя не удалось, RmShutdown вернул " + Opisat(kod);
                return false;
            }

            return Osvobodilsya(path, out reason);
        }
        finally
        {
            // Сессия закрывается всегда, по тому же доводу, что и в опросе:
            // это системный ресурс с конечным числом слотов на машину.
            _ = Native.RmEndSession(sessiya);
        }
    }

    /// <summary>
    /// Did the holders actually go away, checked instead of assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Штатное закрытие не мгновенно: программа сохраняет своё и выходит сама,
    /// и между её решением и снятой блокировкой проходит время. Поэтому опрос
    /// с коротким сроком, а не одна пауза наугад: держатель, просьбу не
    /// принявший, виден на первом же круге, и ждать его нечего.
    /// </para>
    /// <para>
    /// Спрашивается НОВОЙ сессией, а не той, в которой просили. Проверено
    /// фактом 06.09.2026: после RmShutdown держатель уже мёртв, а RmGetList в
    /// сессии-просительнице продолжает его показывать. Проверка внутри той же
    /// сессии отвечала бы «всё ещё держат» всегда, то есть не проверяла бы
    /// ничего и молча превращала успех в отказ.
    /// </para>
    /// </remarks>
    private static bool Osvobodilsya(string path, out string? reason)
    {
        var nachalo = DateTime.UtcNow;

        while (true)
        {
            if (!TryGetHolders(path, out var ostalis, out var otkaz))
            {
                // Проверить не вышло. Сказать «закрылись» на этом месте значит
                // соврать: следом идёт удаление, и оно упрётся в то же самое.
                reason = "закрылись ли держатели, проверить не удалось: " + otkaz;
                return false;
            }

            if (ostalis.Count == 0)
            {
                reason = null;
                return true;
            }

            if (DateTime.UtcNow - nachalo >= SrokZakrytiya)
            {
                reason = "просьба не сработала: файл по-прежнему держит "
                    + Nazvat(ostalis)
                    + ". Штатно закрыться система умеет попросить окно или службу. "
                    + "Остаются удаление при следующей загрузке и принудительное завершение";
                return false;
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// Как назвать держателей в строке отказа.
    /// </summary>
    /// <remarks>
    /// Пустой список тут возможен: между опросом и просьбой файл мог
    /// освободиться. «Программу» без имени человек прочитает как заминку, а
    /// выдуманное имя как враньё.
    /// </remarks>
    private static string Nazvat(IReadOnlyList<FileHolder> derzhateli) =>
        derzhateli.Count == 0
            ? "Программу, которая держит файл,"
            : string.Join(", ", derzhateli.Select(d => d.Describe()));

    private static bool Sprosit(
        uint sessiya,
        out IReadOnlyList<FileHolder> holders,
        out string? reason)
    {
        holders = [];

        uint nuzhno = 0;
        uint est = 0;

        // Первый вызов только за размером: список меняется между вызовами, и
        // угадывать его длину заранее нельзя.
        var kod = Native.RmGetList(sessiya, out nuzhno, ref est, null, out _);

        if (kod == Native.ErrorSuccess && nuzhno == 0)
        {
            reason = null;
            return true;
        }

        if (kod != Native.ErrorMoreData)
        {
            reason = "RmGetList (размер) вернул " + Opisat(kod);
            return false;
        }

        var svedeniya = new Native.RM_PROCESS_INFO[nuzhno];
        est = nuzhno;

        kod = Native.RmGetList(sessiya, out nuzhno, ref est, svedeniya, out _);
        if (kod != Native.ErrorSuccess)
        {
            reason = "RmGetList (данные) вернул " + Opisat(kod);
            return false;
        }

        var itog = new List<FileHolder>((int)est);
        for (var i = 0; i < est; i++)
        {
            var zapis = svedeniya[i];
            var sluzhba = string.IsNullOrWhiteSpace(zapis.strServiceShortName)
                ? null
                : zapis.strServiceShortName;

            itog.Add(new FileHolder(
                zapis.Process.dwProcessId,
                string.IsNullOrWhiteSpace(zapis.strAppName) ? "неизвестная программа" : zapis.strAppName,
                sluzhba,
                (HolderKind)zapis.ApplicationType));
        }

        holders = itog;
        reason = null;
        return true;
    }

    private static string Opisat(int kod) =>
        string.Create(CultureInfo.CurrentCulture, $"код {kod} (0x{kod:X8})");

    /// <remarks>
    /// DllImport rather than LibraryImport, for the reason recorded in
    /// JunkManager.Safety.Native.PathResolver: the source generator emits unsafe
    /// code for string marshalling and demands AllowUnsafeBlocks across the whole
    /// project, which a solution that deletes files for a living does not buy.
    /// </remarks>
    private static class Native
    {
        public const int ErrorSuccess = 0;
        public const int ErrorMoreData = 234;

        /// <summary>
        /// `ERROR_FAIL_NOACTION_REBOOT`. Штатно закрыть держателя нечем, и
        /// система прямо говорит, что помогла бы только перезагрузка.
        /// </summary>
        public const int ErrorFailNoactionReboot = 351;

        public const int SessionKeyLength = 32;

        private const int MaxAppName = 255;
        private const int MaxServiceName = 63;

        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxAppName + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxServiceName + 1)]
            public string strServiceShortName;

            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, EntryPoint = "RmStartSession")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RmStartSession(
            out uint pSessionHandle, int dwSessionFlags, char[] strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, EntryPoint = "RmRegisterResources")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[]? rgsFileNames,
            uint nApplications,
            RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll", EntryPoint = "RmGetList")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            out uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll", EntryPoint = "RmEndSession")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RmEndSession(uint pSessionHandle);

        /// <summary>
        /// Обратный вызов состояния не нужен и передаётся как null: он рисует
        /// проценты, а ждём мы всё равно до конца.
        /// </summary>
        [DllImport("rstrtmgr.dll", EntryPoint = "RmShutdown")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RmShutdown(
            uint dwSessionHandle, uint lActionFlags, nint fnStatus);
    }
}
