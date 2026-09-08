using JunkManager.Core;
using JunkManager.Core.Explain;
using JunkManager.Core.Apps;
using JunkManager.Core.Reporting;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Core.Registry;
using JunkManager.Core.Sources.Detect;
using JunkManager.Deletion;
using JunkManager.Safety;
using Microsoft.Win32;

// Console output goes out as UTF-8 explicitly. Without it the guest console runs
// in code page 866, and every Russian refusal comes back as mojibake exactly when
// somebody is reading it to find out what broke.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Loose catalogs no longer steer a cleaner with deletion privileges. What a concept.
if (args.Any(arg => string.Equals(arg, "--rules", StringComparison.Ordinal)
    || arg.StartsWith("--rules=", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("внешний каталог правил не поддерживается: правила встроены в программу");
    return 2;
}
var asJson = args.Contains("--json", StringComparer.Ordinal);

// Коды 3 и 4 в этом CLI уже заняты предохранителем и исходом «находки были, но
// не удалена ни одна», поэтому повышение получило 5 и 6, а не 3 и 4, как
// предлагал план. Расхождение записано в разделе задачи 16.
const string KlyuchAdmin = "--admin";

// Elevation happens here and nowhere else: at startup, before anything reads a
// single directory. One UAC prompt, no restarts in the middle of work.
//
// Rights are opt-in through --admin and never taken by default. The plan left
// the predicate unnamed; this is the decision, and it is the conservative one:
// a cleaner that asks for administrator every launch is a cleaner people click
// through without reading, and the whole point of the list below is that it
// gets read.
if (!Elevation.TryElevate(
        args.Contains(KlyuchAdmin, StringComparer.Ordinal),
        args,
        out var ishodPovysheniya,
        out var prichinaPovysheniya))
{
    Console.Error.WriteLine($"повышение прав не удалось: {prichinaPovysheniya}");
    return 5;
}

if (ishodPovysheniya == ElevationOutcome.Relaunched)
{
    // The elevated copy is running. This one has nothing left to do, and it
    // must not scan: two processes cleaning the same disk is not twice as fast.
    return 0;
}

if (ishodPovysheniya == ElevationOutcome.Declined)
{
    Console.Error.WriteLine(
        "права администратора не выданы. Не будут проверены: Prefetch, склад компонентов, "
        + "хранилище драйверов, ветка HKLM, C:\\Windows\\Temp, WindowsApps и машинные "
        + "деинсталляторы. Это «не проверялось», а не «ничего не найдено».");
}

// A handed identity means we ARE the elevated copy. Until the profile is
// verified, nothing scans: cleaning somebody else's profile is not recoverable.
if (Elevation.TryReadHandedIdentity(args, out _, out _, out _)
    && !Elevation.TryApplyOriginalProfile(args, out var prichinaProfilya))
{
    Console.Error.WriteLine(
        $"исходный профиль не подтверждён: {prichinaProfilya}. Сканирование не начинается: "
        + "правила продукта написаны через %LOCALAPPDATA% и %APPDATA%, и без сверки они "
        + "указали бы на чужой профиль.");
    return 6;
}

return args switch
{
    ["fuse-status"] => FuseStatus(),
    ["rules-validate", ..] => ValidateRules(),
    ["registry", "scan", ..] => await RegistryScanAsync(asJson).ConfigureAwait(false),
    ["registry", "clean", ..] => await RegistryCleanAsync(
        args.Contains("--yes", StringComparer.Ordinal), args.Contains("--backup", StringComparer.Ordinal),
        args.Contains("--include-manual", StringComparer.Ordinal)).ConfigureAwait(false),
    // Обе записи команды живые. Спека называет её `scan --explain-disk`, план
    // задачи 9 называет её `explain-disk`, и спорить тут не о чем: команда одна
    // и та же, а человек набирает то, что прочитал.
    ["explain-disk"] => await ExplainDiskAsync(TomPoUmolchaniyu()).ConfigureAwait(false),
    ["explain-disk", var tom] => await ExplainDiskAsync(tom).ConfigureAwait(false),
    ["scan", "--explain-disk"] => await ExplainDiskAsync(TomPoUmolchaniyu()).ConfigureAwait(false),
    ["scan", "--explain-disk", var tom] => await ExplainDiskAsync(tom).ConfigureAwait(false),
    ["apps", "list", ..] => await AppsListAsync(asJson).ConfigureAwait(false),
    ["apps", "remove", ..] => await AppsRemoveAsync(
        ArgValue("--id"), args.Contains("--yes", StringComparer.Ordinal)).ConfigureAwait(false),
    ["scan", ..] => await ScanAsync(
        asJson,
        args.Contains("--po-obraztsu", StringComparer.Ordinal)).ConfigureAwait(false),
    _ => Help(),
};

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int FuseStatus()
{
    // Формулировка правится 05.09.2026 по факту. Строка «разрушительные операции
    // запрещены» ЛОЖНА: предохранитель держит РАЗРУШИТЕЛЬНЫЕ ТЕСТЫ, а команды
    // продукта он не держит и держать не должен, иначе продукт не смог бы
    // работать на машине человека. Проверено дорого: registry clean --yes при
    // невзведённом предохранителе удалил настоящую запись реестра, а строка
    // рядом обещала, что этого не случится. Запись вернулась из бэкапа, который
    // продукт сделал сам.
    Console.WriteLine(VmFuse.IsArmed
        ? $"предохранитель ВЗВЕДЁН: {VmFuse.EnvName}=1 и маркер {VmFuse.MarkerPath} на месте. "
          + "Разрушительные ТЕСТЫ на этой машине разрешены"
        : "предохранитель не взведён: разрушительные ТЕСТЫ на этой машине не пойдут. "
          + "Команды самого продукта он не держит: registry clean --yes и "
          + "apps remove --yes удаляют и без него, на то и ключ --yes");

    return VmFuse.IsArmed ? 0 : 3;
}

static int ValidateRules()
{
    try
    {
        var rules = BuiltInCatalog.LoadRules();
        BuiltInCatalog.LoadRegistryRules();
        BuiltInCatalog.LoadVolumeCacheDescriptions();
        BuiltInCatalog.LoadPlatformToolTexts();
        Console.WriteLine($"правил загружено: {rules.Count}");

        foreach (var group in rules.GroupBy(r => r.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        return 0;
    }
    catch (RuleFormatException ex)
    {
        // Non-zero and named. A validator that says "something is wrong" without
        // saying where is a validator nobody runs twice.
        Console.Error.WriteLine($"правила не загрузились: {ex.Message}");
        return 2;
    }
}

static async Task<int> ScanAsync(bool asJson, bool poObraztsu)
{
    // Правила проверяются отдельно и до прохода: каталог, не доехавший до
    // выходного каталога, это ошибка вызова, и отвечать на неё кодом 2 честнее,
    // чем строкой «источник отказал» посреди находок остальных источников.
    try
    {
        BuiltInCatalog.LoadRules();
    }
    catch (RuleFormatException ex)
    {
        Console.Error.WriteLine($"правила не загрузились: {ex.Message}");
        return 2;
    }

    // Один проход по всем источникам, а не по одному. Приёмка посева 05.09.2026
    // показала, зачем: пять приманок из восемнадцати не находились не потому,
    // что механизм сломан, а потому, что его никто не звал.
    var plan = ScanPlan.PoUmolchaniyu with { PoiskPoObraztsu = poObraztsu };

    var result = await PolnyyProhod
        .ScanAsync(plan, null, CancellationToken.None)
        .ConfigureAwait(false);

    if (asJson)
    {
        Console.WriteLine(ScanJson.Serialize(result));
    }
    else
    {
        Print(result);
    }

    // Findings are not an error, but a script that cleans up wants to know
    // whether there was anything to clean without parsing the output.
    return result.Findings.Count > 0 ? 1 : 0;
}

/// <summary>
/// Системный том, а не литерал «C:». На машине, где Windows стоит не на C,
/// литерал молча объяснял бы чужой диск.
/// </summary>
static string TomPoUmolchaniyu() =>
    Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

static async Task<int> ExplainDiskAsync(string volume)
{
    IReadOnlyList<RuleDefinition> rules;
    try
    {
        rules = BuiltInCatalog.LoadRules();
    }
    catch (RuleFormatException ex)
    {
        Console.Error.WriteLine($"правила не загрузились: {ex.Message}");
        return 2;
    }

    var poPravilam = await new FileScanner()
        .ScanAsync(rules, null, CancellationToken.None)
        .ConfigureAwait(false);

    // Обнаружитель кэшей идёт в тот же список НАМЕРЕННО, и это не украшение
    // цифры. Он предлагает ровно те подкаталоги (Cache, GPUCache), которые уже
    // забирают правила браузеров, то есть проверяет объединение на двойной счёт
    // прямо на живой машине. Обнаружитель заброшенных папок сюда не включён:
    // он ступени Risk и по спеке не проверяется по умолчанию.
    var obnaruzheno = await new ElectronCacheDetector()
        .ScanAsync(KorniPrilozheniy(), CancellationToken.None)
        .ConfigureAwait(false);

    var vsyo = new ScanResult(
        [.. poPravilam.Findings, .. obnaruzheno.Findings],
        [.. poPravilam.Skipped, .. obnaruzheno.Skipped],
        poPravilam.Cancelled || obnaruzheno.Cancelled);

    var explanation = DiskExplainer.Explain(volume, vsyo, [], DiskExplainer.TopLevel(volume));
    Console.WriteLine(explanation.Report());
    Console.WriteLine($"находок {vsyo.Findings.Count}, пропущено {vsyo.Skipped.Count}");

    // Ненулевой код НАМЕРЕННО, когда объяснить не удалось ничего: это
    // диагностическая команда, и «объяснено ноль» это тот ответ, который
    // требует внимания. Код 7, а не 5: 5 и 6 заняты повышением прав, а два
    // разных исхода под одним номером это скрипт, принимающий решение
    // наугад.
    return explanation.ExplainedBytes > 0 ? 0 : 7;
}

static string[] KorniPrilozheniy() =>
[
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
];

static void Print(ScanResult result)
{
    if (result.Cancelled)
    {
        Console.WriteLine("ВНИМАНИЕ: сканирование прервано, результат неполный");
    }

    foreach (var f in result.Findings.OrderByDescending(f => f.SizeBytes))
    {
        Console.WriteLine($"{Mb(f.SizeBytes),12}  [{f.Tier}]  {f.Name}");
        Console.WriteLine($"{"",12}  {f.Path}");
        Console.WriteLine($"{"",12}  {f.Consequence}");

        if (f.Scope == DeleteScope.SelectedEntries)
        {
            // Иначе строка читается как «уйдёт вся папка», а уйдёт часть. Для
            // %TEMP% это разница между уборкой и потерей работы.
            Console.WriteLine($"{"",12}  уйдут {f.DeletionTargets.Count} файлов внутри, сама папка останется");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"итого {Mb(result.TotalBytes)}: безопасно {Mb(result.SafeBytes)}, опасно {Mb(result.RiskBytes)}");
    Console.WriteLine($"находок {result.Findings.Count}, пропущено {result.Skipped.Count}");

    foreach (var s in result.Skipped)
    {
        Console.WriteLine($"  пропущено: {s.Path}: {s.Reason}");
    }
}

static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:N1} МБ";

// «нет данных» и ноль это разные ответы, и подменять первый вторым нельзя:
// ноль читается как «ничего не занимает» и «запускали сегодня».
static string Razmer(long? bayt) => bayt is null ? "нет данных" : Mb(bayt.Value);

// Сокращение «дн.» намеренно: оно верно при любом числе, а «5 дней» против
// «2 дня» против «21 день» требует счётной функции со своим набором проверок.
static string Vozrast(int? dney) =>
    dney is null ? "нет данных" : $"{dney.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} дн. назад";

static async Task<int> AppsListAsync(bool asJson)
{
    var inventory = await new ProgramInventory().ReadAsync().ConfigureAwait(false);
    var programmy = inventory.Programs;
    var stroki = new List<StrokaProgrammy>(programmy.Count);

    foreach (var programma in programmy)
    {
        // EstimatedSize из реестра не читается вовсе: на этой машине Telegram
        // Desktop занижает себя в 13,5 раза. Либо посчитано обходом, либо
        // «нет данных», третьего продукт не показывает.
        var poschitan = ProgramSizeCalculator.TryMeasure(
            programma.InstallLocation, out var bayt, out var prichina);

        stroki.Add(new StrokaProgrammy(
            programma.Id,
            programma.DisplayName,
            programma.Version,
            programma.Publisher,
            programma.Installer.ToString(),
            programma.Scope.ToString(),
            poschitan ? bayt : null,
            poschitan ? null : prichina,
            PrefetchUsageReader.DaysSinceLastRun(programma.InstallLocation),
            programma.InstalledOn));
    }

    if (asJson)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(stroki, NastroykiVyvoda.Json));
        foreach (var skipped in inventory.Skipped) Console.Error.WriteLine($"не проверено: {skipped.Path}: {skipped.Reason}");
        return inventory.IsComplete ? 0 : 1;
    }

    foreach (var stroka in stroki.OrderByDescending(s => s.SizeBytes ?? -1))
    {
        Console.WriteLine($"{Razmer(stroka.SizeBytes),12}  {stroka.DisplayName} {stroka.Version}");
        Console.WriteLine($"{"",12}  {stroka.Publisher}, установщик {stroka.Installer}, область {stroka.Scope}");
        Console.WriteLine($"{"",12}  последний запуск: {Vozrast(stroka.DaysSinceLastRun)}, id {stroka.Id}");

        if (stroka.SizeUnknownReason is { } prichina)
        {
            Console.WriteLine($"{"",12}  размер не посчитан: {prichina}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"всего программ: {stroki.Count}");
    foreach (var skipped in inventory.Skipped) Console.WriteLine($"не проверено: {skipped.Path}: {skipped.Reason}");
    return inventory.IsComplete ? 0 : 1;
}

static async Task<int> AppsRemoveAsync(string? id, bool yes)
{
    if (string.IsNullOrWhiteSpace(id))
    {
        Console.Error.WriteLine("нужен ключ --id с идентификатором программы из apps list");
        return 2;
    }

    var inventory = new ProgramInventory();
    var snapshot = await inventory.ReadAsync().ConfigureAwait(false);
    var programma = snapshot.Programs
        .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    if (programma is null)
    {
        Console.Error.WriteLine($"программа с id {id} не найдена, список: apps list");
        return 2;
    }

    UninstallCommand? komanda = null;
    if (programma.Installer != InstallerKind.Msix
        && !UninstallCommandBuilder.TryBuild(programma, out komanda, out var prichinaOtkaza))
    {
        Console.Error.WriteLine($"удалять нечем: {prichinaOtkaza}");
        return 2;
    }

    Console.WriteLine($"программа: {programma.DisplayName} {programma.Version}");
    Console.WriteLine(komanda is null ? $"пакет текущего пользователя: {programma.PackageFullName}"
        : $"команда: {komanda.Executable} {string.Join(' ', komanda.Arguments)}");
    Console.WriteLine(komanda?.Quiet == false ? "откроется штатный мастер удаления" : "удаление без мастера");
    Console.WriteLine("Копия программы не создаётся. Ctrl+C остановит ожидание, запущенный деинсталлятор сможет закончить работу.");

    if (!yes)
    {
        Console.WriteLine("ничего не удалено: нужен ключ --yes");
        return 1;
    }

    using var zhurnal = JsonlOperationLog.CreateForRun();
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += onCancel;
    UninstallResult itog;
    try
    {
        var runner = new UninstallRunner(zhurnal, inventory);
        itog = programma.Installer == InstallerKind.Msix
            ? await runner.RunMsixAsync(programma, new UninstallOptions(), null, cancellation.Token).ConfigureAwait(false)
            : await runner.RunAsync(programma, komanda!, new UninstallOptions(), true, null, cancellation.Token).ConfigureAwait(false);
    }
    finally { Console.CancelKeyPress -= onCancel; }

    Console.WriteLine($"исход: {itog.Outcome}, код возврата: "
        + (itog.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "нет данных"));
    Console.WriteLine(itog.RemovalConfirmed ? "Отсутствие регистрации подтверждено." : "Удаление не подтверждено.");
    if (itog.RebootRequired) Console.WriteLine("Нужна перезагрузка Windows.");

    if (itog.Reason is { } prichinaIshoda)
    {
        Console.WriteLine($"причина: {prichinaIshoda}");
    }

    Console.WriteLine($"журнал: {zhurnal.FilePath}");

    if (!itog.RemovalConfirmed)
    {
        // Пока программа стоит, её каталоги это не следы, а её рабочие данные.
        Console.WriteLine("следы не ищутся: удаление не завершилось успехом");
        return 4;
    }

    var sledy = await LeftoverFinder.FindAfterRemovalAsync(programma, itog, inventory).ConfigureAwait(false);

    foreach (var sled in sledy.Found)
    {
        Console.WriteLine($"{Mb(sled.SizeBytes),12}  след: {sled.Path}");
        Console.WriteLine($"{"",12}  основание: {sled.Basis}");
        Console.WriteLine($"{"",12}  доказательства: {sled.Confidence}; доступно удаление: {sled.CanDelete}");
    }

    foreach (var propusk in sledy.Skipped)
    {
        Console.WriteLine($"  пропущено: {propusk.Path}: {propusk.Reason}");
    }

    Console.WriteLine();
    Console.WriteLine($"найдено следов: {sledy.Found.Count}. "
        + "Следы не удаляются этой командой: отметку ставит человек");

    return 0;
}

static int Help()
{
    Console.WriteLine("Junk Manager CLI. Доступные команды:");
    Console.WriteLine("  fuse-status                       состояние предохранителя разрушительных операций");
    Console.WriteLine("  rules-validate                   проверить встроенный каталог");
    Console.WriteLine("  scan [--json]                    найти мусор всеми источниками, ничего не удаляя");
    Console.WriteLine("       [--po-obraztsu]              добавить поиск по маскам имён. Выключен по");
    Console.WriteLine("                                    умолчанию: маска не знает, чем занят файл,");
    Console.WriteLine("                                    и всё найденное этим источником рискованное.");
    Console.WriteLine("  registry scan [--json]            найти битые записи реестра, ничего не удаляя");
    Console.WriteLine("  registry clean --yes              удалить обычные найденные записи без бэкапа");
    Console.WriteLine("       [--backup] [--include-manual] экспорт записей и явное включение осторожных категорий");
    Console.WriteLine("  explain-disk [ТОМ]                сколько места объяснено, а сколько нет");
    Console.WriteLine("  scan --explain-disk [ТОМ]         то же самое, запись из спеки");
    Console.WriteLine();
    Console.WriteLine("Ключи, годные для любой команды:");
    Console.WriteLine("  --admin                           запросить права администратора при запуске.");
    Console.WriteLine("                                    Без него Prefetch, склад компонентов, хранилище");
    Console.WriteLine("                                    драйверов, ветка HKLM, C:\\Windows\\Temp и");
    Console.WriteLine("                                    WindowsApps остаются НЕ ПРОВЕРЕННЫМИ, а это не");
    Console.WriteLine("                                    то же самое, что «ничего не найдено».");
    Console.WriteLine("  apps list [--json]                установленные программы, размер и возраст запуска");
    Console.WriteLine("  apps remove --id ИД --yes         запустить деинсталлятор и показать следы");
    Console.WriteLine();
    Console.WriteLine("Коды выхода: 0 успех, 1 находки есть либо нужен --yes,");
    Console.WriteLine("             2 вход не годится: правила не загрузились, программа не найдена");
    Console.WriteLine("               или строка удаления не разбирается,");
    Console.WriteLine("             3 предохранитель не взведён,");
    Console.WriteLine("             4 находки были, но не удалена ни одна,");
    Console.WriteLine("             5 повышение прав не удалось,");
    Console.WriteLine("             6 исходный профиль не подтверждён, сканирование не начиналось,");
    Console.WriteLine("             7 объяснить не удалось ничего");
    Console.WriteLine();
    return 0;
}

static async Task<int> RegistryScanAsync(bool asJson)
{
    if (!ZagruzitVetki(out var vetki))
    {
        return 2;
    }

    var itog = await new RegistryScanner()
        .ScanAsync(vetki, null, CancellationToken.None)
        .ConfigureAwait(false);

    if (asJson)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(itog, NastroykiVyvoda.Json));
    }
    else
    {
        PechatReestra(itog);
    }

    // Как и у scan: находки не ошибка, но скрипту нужно знать, есть ли что
    // чистить, не разбирая вывод.
    return itog.Findings.Count > 0 ? 1 : 0;
}

static async Task<int> RegistryCleanAsync(bool yes, bool backup, bool includeManual)
{
    if (!ZagruzitVetki(out var vetki))
    {
        return 2;
    }

    var itog = await new RegistryScanner()
        .ScanAsync(vetki, null, CancellationToken.None)
        .ConfigureAwait(false);

    if (itog.Findings.Count == 0)
    {
        Console.WriteLine("в реестре чистить нечего");
        return 0;
    }

    PechatReestra(itog);

    if (!yes)
    {
        Console.WriteLine();
        Console.WriteLine("ничего не удалено: нужен ключ --yes");
        return 1;
    }

    var selected = itog.Findings.Where(item => includeManual || !item.ManualSelectionOnly).ToArray();
    if (selected.Length < itog.Findings.Count)
        Console.WriteLine("Осторожные категории сохранены. Для их явного включения нужен --include-manual.");
    Console.WriteLine(backup ? "Экспорт выбранных записей включён." : "Экспорт выключен. Удалённые записи нельзя восстановить через Junk Manager.");
    if (selected.Length == 0) return 1;
    using var zhurnal = JsonlOperationLog.CreateForRun();
    var perebor = new RegistryCleanupRunner(new RegistryExecutor(zhurnal, backup ? new RegistryBackup() : null));

    var otchet = await perebor.RunAsync(selected, null, CancellationToken.None)
        .ConfigureAwait(false);

    foreach (var ishod in otchet.Outcomes.Where(o => o.Status != DeleteStatus.Deleted))
    {
        Console.WriteLine($"не удалено {ishod.Path}: {ishod.Reason}");
    }

    Console.WriteLine();
    Console.WriteLine($"удалено записей {otchet.DeletedCount} из {selected.Length}");
    Console.WriteLine($"журнал: {zhurnal.FilePath}");
    if (backup) Console.WriteLine($"бэкапы: {RegistryBackup.DefaultDirectory}");

    if (otchet.DeletedCount == 0)
    {
        // Отдельный код, потому что "нечего было чистить" и "чистить было что,
        // но не вышло ничего" это противоположные новости для скрипта.
        return 4;
    }

    return otchet.DeletedCount == selected.Length ? 0 : 1;
}

static bool ZagruzitVetki(out IReadOnlyList<RegistryScanRule> vetki)
{
    try
    {
        vetki = BuiltInCatalog.LoadRegistryRules();
        return true;
    }
    catch (RuleFormatException ex)
    {
        Console.Error.WriteLine($"ветки реестра не загрузились: {ex.Message}");
        vetki = [];
        return false;
    }
}

static void PechatReestra(RegistryScanResult itog)
{
    if (itog.Cancelled)
    {
        Console.WriteLine("ВНИМАНИЕ: обход реестра прерван, результат неполный");
    }

    foreach (var nahodka in itog.Findings)
    {
        Console.WriteLine(nahodka.Address);
        Console.WriteLine($"{"",4}значение: {nahodka.RawValue}");
        Console.WriteLine($"{"",4}файла нет: {nahodka.MissingTarget}");
        Console.WriteLine($"{"",4}{nahodka.Consequence}");
    }

    Console.WriteLine();

    // Счётчик в записях, а не в мегабайтах. Раздел 9 спеки: освобождённое место
    // для реестра не показывается никогда, потому что его там нет.
    Console.WriteLine($"записей на удаление {itog.Findings.Count}, пропущено {itog.Skipped.Count}");

    foreach (var propusk in itog.Skipped)
    {
        Console.WriteLine($"  пропущено: {propusk.Path}: {propusk.Reason}");
    }
}

/// <summary>
/// Одна строка списка программ.
/// </summary>
/// <param name="SizeBytes">
/// Null это «не посчитано», и ноль вместо него читался бы как «ничего не
/// занимает». Причина, по которой не посчитано, едет рядом.
/// </param>
/// <param name="DaysSinceLastRun">
/// Null это «неизвестно», а не «запускали сегодня»: Prefetch читается не всегда,
/// и без прав администратора не читается вовсе.
/// </param>
internal sealed record StrokaProgrammy(
    string Id,
    string DisplayName,
    string Version,
    string Publisher,
    string Installer,
    string Scope,
    long? SizeBytes,
    string? SizeUnknownReason,
    int? DaysSinceLastRun,
    DateOnly? InstalledOn);

/// <summary>
/// Настройки печати JSON для команд реестра.
/// </summary>
/// <remarks>
/// Отдельным классом, а не локальной переменной в верхнеуровневом коде:
/// локальную переменную статические локальные функции не захватывают, а
/// пересоздавать JsonSerializerOptions на каждый вызов запрещает CA1869 и
/// это к тому же выбрасывает кэш сериализатора.
/// Экранирование кириллицы снято по той же причине, что в ScanJson: отчёт
/// читает человек.
/// </remarks>
internal static class NastroykiVyvoda
{
    internal static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
