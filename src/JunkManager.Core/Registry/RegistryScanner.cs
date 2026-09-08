using System.Security;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Core.Registry;

/// <summary>
/// What the file system says about a target. Three answers and not two:
/// "cannot tell" is not "not there", and collapsing them turns a live program
/// under WindowsApps into a finding.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Ноль оставлен неопределённым намеренно. Неинициализированное состояние, " +
        "читающееся как Missing, это ложная находка на живой программе.")]
public enum TargetState
{
    Exists = 1,
    Missing = 2,
    Unknown = 3,
}

/// <summary>
/// Asks the file system, not the string. GetAttributes and not File.Exists:
/// File.Exists answers false both for "not there" and for "you may not look",
/// and those two answers must stay apart.
/// </summary>
internal static class TargetProbe
{
    internal static TargetState Look(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return TargetState.Exists;
        }
        catch (FileNotFoundException)
        {
            return TargetState.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return TargetState.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
            or ArgumentException or NotSupportedException)
        {
            return TargetState.Unknown;
        }
    }
}

/// <param name="Cancelled">
/// True when the walk stopped early, for the same reason ScanResult carries it:
/// a partial registry scan looks exactly like a clean registry.
/// </param>
public sealed record RegistryCoverage(string Category, bool Supported, int SkippedCount, string Detail);

public sealed record RegistryScanResult(
    IReadOnlyList<RegistryFinding> Findings,
    IReadOnlyList<SkippedItem> Skipped,
    bool Cancelled = false)
{
    public IReadOnlyList<RegistryCoverage> Coverage { get; init; } = [];
}

/// <summary>
/// Walks the allowed branches and reports the entries whose target is missing.
/// Reads only, and cannot do otherwise: deletion needs a pass, and this
/// assembly is unable to issue one.
/// </summary>
public sealed partial class RegistryScanner
{
    public static bool IsTargetMissing(string path, RegistryView view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (TargetProbe.Look(path) != TargetState.Missing) return false;
        var twin = view == RegistryView.Registry32 ? Bitnost.Blizhnec32(path) : null;
        return twin is null || TargetProbe.Look(twin) == TargetState.Missing;
    }
    private readonly Func<string, TargetState> _proba;

    /// <param name="probe">
    /// Null means the real file system. A parameter because the Unknown branch
    /// does not reproduce on a developer machine without administrator rights,
    /// and a branch that exists only in production is a branch nobody checked.
    /// </param>
    public RegistryScanner(Func<string, TargetState>? probe = null) =>
        _proba = probe ?? TargetProbe.Look;

    public Task<RegistryScanResult> ScanAsync(
        IReadOnlyList<RegistryScanRule> rules, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Task.Run(() => Scan(rules, progress, ct), CancellationToken.None);
    }

    private RegistryScanResult Scan(
        IReadOnlyList<RegistryScanRule> rules, IProgress<string>? progress, CancellationToken ct)
    {
        var nahodki = new List<RegistryFinding>();
        var propuski = new List<SkippedItem>();
        var coverage = new List<RegistryCoverage>();

        foreach (var pravilo in rules)
        {
            if (ct.IsCancellationRequested)
            {
                return new RegistryScanResult(nahodki, propuski, Cancelled: true) { Coverage = coverage };
            }

            var zayavlennaya = RegistryAddress.Format(pravilo.Hive, pravilo.SubKey, null);

            if (!RegistryGuard.TryVerifyScanBranch(
                    pravilo.Hive, pravilo.SubKey, pravilo.View, out var vetka, out var otkaz))
            {
                // Guard спрашивается ещё раз, хотя загрузчик уже спрашивал.
                // Правила сюда может передать кто угодно, и последняя точка, где
                // можно сказать нет, находится здесь.
                propuski.Add(new SkippedItem(zayavlennaya, $"ветка отклонена: {otkaz}"));
                coverage.Add(new(pravilo.Name, false, 1, "ветка отклонена защитой"));
                continue;
            }

            progress?.Report(RegistryAddress.Format(pravilo.Hive, vetka, null));
            var skippedBefore = propuski.Count;
            Obojti(pravilo, vetka, nahodki, propuski, ct);
            var gaps = propuski.Skip(skippedBefore).Count(item => !item.IsExpectedExclusion);
            coverage.Add(new(pravilo.Name, !ct.IsCancellationRequested, gaps,
                ct.IsCancellationRequested ? "проверка остановлена" : gaps > 0 ? $"не проверено: {gaps}" : "проверено"));
        }

        return new RegistryScanResult(nahodki, propuski, ct.IsCancellationRequested) { Coverage = coverage };
    }

    /// <summary>
    /// Адрес ветки для строки ПРОПУСКА, с пометкой представления реестра.
    /// </summary>
    /// <remarks>
    /// 32- и 64-битная проекции одного улья дают два прохода по одному и тому
    /// же пути. Без пометки человек видит две одинаковые строки подряд и
    /// считает это ошибкой продукта, а не двумя разными местами. Адрес
    /// НАХОДКИ так не помечается никогда: его читает reg.exe.
    /// </remarks>
    private static string Pomechennyy(RegistryScanRule pravilo, string vetka) =>
        RegistryAddress.Format(pravilo.Hive, vetka, null)
        + (pravilo.View == RegistryView.Default ? string.Empty : $" [{pravilo.View}]");

    private void Obojti(
        RegistryScanRule pravilo,
        string vetka,
        List<RegistryFinding> nahodki,
        List<SkippedItem> propuski,
        CancellationToken ct)
    {
        // Представление приписывается к адресу ветки, но НЕ к адресу находки:
        // 32- и 64-битная проекции дают две строки с одинаковым путём, и без
        // пометки человек видит дубль и считает это ошибкой продукта. Адрес
        // находки при этом обязан остаться чистым: его читает reg.exe.
        var adresVetki = Pomechennyy(pravilo, vetka);

        try
        {
            // OpenBaseKey, а не Microsoft.Win32.Registry.CurrentUser: во-первых, пространство
            // имён перекрывает простое имя типа Registry, во-вторых, только так
            // выбирается 32-битная проекция.
            using var baza = RegistryKey.OpenBaseKey(pravilo.Hive, pravilo.View);
            using var klyuch = baza.OpenSubKey(vetka, writable: false);

            if (klyuch is null)
            {
                // Ветки нет. Это норма, а не отказ: RunOnce заводится только
                // когда в него что-то положили.
                return;
            }

            if (pravilo.Mode != RegistryScanMode.Standard)
            {
                ScanCautious(klyuch, pravilo, vetka, nahodki, propuski, ct);
            }
            else if (pravilo.Kind == RegistryEntryKind.Value)
            {
                SmotretZnacheniya(klyuch, pravilo, vetka, nahodki, propuski, ct);
            }
            else
            {
                SmotretPodklyuchi(klyuch, pravilo, vetka, nahodki, propuski, ct);
            }
        }
        catch (SecurityException ex)
        {
            // Без прав администратора HKLM читается не весь. Раздел 9.2 спеки:
            // модуль прямо пишет, какие проверки ему недоступны.
            propuski.Add(new SkippedItem(adresVetki, $"нет прав на чтение ветки: {ex.Message}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            propuski.Add(new SkippedItem(adresVetki, $"нет прав на чтение ветки: {ex.Message}"));
        }
        catch (IOException ex)
        {
            propuski.Add(new SkippedItem(adresVetki, $"ветка не читается: {ex.Message}"));
        }
    }

    private void SmotretZnacheniya(
        RegistryKey klyuch,
        RegistryScanRule pravilo,
        string vetka,
        List<RegistryFinding> nahodki,
        List<SkippedItem> propuski,
        CancellationToken ct)
    {
        foreach (var imya in klyuch.GetValueNames())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var adres = RegistryAddress.Format(pravilo.Hive, vetka, imya);

            if (string.IsNullOrEmpty(imya))
            {
                propuski.Add(new SkippedItem(adres, "значение по умолчанию не рассматривается никогда") { IsExpectedExclusion = true });
                continue;
            }

            var vid = klyuch.GetValueKind(imya);

            if (vid is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            {
                propuski.Add(new SkippedItem(adres, $"вид значения {vid} не содержит пути") { IsExpectedExclusion = true });
                continue;
            }

            // DoNotExpandEnvironmentNames: раскрываем сами и умеем на раскрытии
            // отказать. Молчаливое раскрытие силами RegistryKey превратило бы
            // неопределённую переменную в путь, который выглядит настоящим.
            var syroe = klyuch.GetValue(imya, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string;

            if (string.IsNullOrWhiteSpace(syroe))
            {
                propuski.Add(new SkippedItem(adres, "значение пустое") { IsExpectedExclusion = true });
                continue;
            }

            Rassmotret(pravilo, vetka, imya, RegistryEntryKind.Value, syroe, adres, nahodki, propuski);
        }
    }

    private void SmotretPodklyuchi(
        RegistryKey klyuch,
        RegistryScanRule pravilo,
        string vetka,
        List<RegistryFinding> nahodki,
        List<SkippedItem> propuski,
        CancellationToken ct)
    {
        foreach (var imyaPodklyucha in klyuch.GetSubKeyNames())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var podvetka = vetka + "\\" + imyaPodklyucha;
            var adres = Pomechennyy(pravilo, podvetka);

            using var pod = klyuch.OpenSubKey(imyaPodklyucha, writable: false);

            if (pod is null)
            {
                continue;
            }

            var syroe = pod.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string;

            if (string.IsNullOrWhiteSpace(syroe))
            {
                propuski.Add(new SkippedItem(adres, "у подключа нет значения по умолчанию с путём") { IsExpectedExclusion = true });
                continue;
            }

            Rassmotret(
                pravilo, podvetka, string.Empty, RegistryEntryKind.Key, syroe, adres, nahodki, propuski);
        }
    }

    private void Rassmotret(
        RegistryScanRule pravilo,
        string vetka,
        string imya,
        RegistryEntryKind vid,
        string syroe,
        string adres,
        List<RegistryFinding> nahodki,
        List<SkippedItem> propuski)
    {
        if (!RegistryTargetExtractor.TryExtract(syroe, out var cel, out var otkaz))
        {
            // Разобрать не удалось значит находки НЕТ. Не "показать на всякий
            // случай" и не "пометить подозрительным": продукт, предлагающий
            // удалить то, чего он не понял, врёт про то, что он проверил.
            propuski.Add(new SkippedItem(adres, $"значение не разобрано: {otkaz}"));
            return;
        }

        switch (_proba(cel))
        {
            case TargetState.Exists:
                // Живая запись не упоминается нигде: пропуск это решение не
                // трогать, а решать тут было нечего.
                return;

            case TargetState.Unknown:
                propuski.Add(new SkippedItem(
                    adres, $"существование {cel} проверить не удалось, находки нет"));
                return;

            case TargetState.Missing:
                // Запись из 32-битного вида говорит про свои каталоги: её
                // «Program Files» это «Program Files (x86)», её «System32» это
                // «SysWOW64». Спрашиваем про близнеца ДО того, как объявить
                // находку, иначе продукт предложит снести живой автозапуск.
                if (pravilo.View == Microsoft.Win32.RegistryView.Registry32)
                {
                    var blizhnec = Bitnost.Blizhnec32(cel);

                    if (blizhnec is not null)
                    {
                        switch (_proba(blizhnec))
                        {
                            case TargetState.Exists:
                                return;

                            case TargetState.Unknown:
                                propuski.Add(new SkippedItem(
                                    adres,
                                    $"существование {blizhnec} проверить не удалось, находки нет"));
                                return;

                            default:
                                break;
                        }
                    }
                }

                var snapshot = RegistryEntrySnapshot.Capture(pravilo.Hive, vetka, imya,
                    pravilo.View, vid == RegistryEntryKind.Key);
                if (snapshot is null || !snapshot.Values.Any(value => value.RelativeKey.Length == 0
                    && value.Name.Equals(imya, StringComparison.OrdinalIgnoreCase)
                    && value.Decode() is string captured && captured.Equals(syroe, StringComparison.Ordinal)))
                {
                    propuski.Add(new SkippedItem(adres, "запись изменилась во время проверки"));
                    return;
                }
                nahodki.Add(new RegistryFinding(
                    Hive: pravilo.Hive,
                    SubKey: vetka,
                    ValueName: imya,
                    View: pravilo.View,
                    Kind: vid,
                    RawValue: syroe,
                    MissingTarget: cel,
                    Name: pravilo.Name,
                    Consequence: pravilo.Consequence,
                    RuleId: pravilo.Id)
                {
                    Snapshot = snapshot, ManualSelectionOnly = pravilo.Mode != RegistryScanMode.Standard,
                    Category = pravilo.Name,
                });
                return;

            default:
                throw new InvalidOperationException($"неизвестное состояние цели у {adres}");
        }
    }
}
