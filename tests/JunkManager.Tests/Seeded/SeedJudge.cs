using System.Globalization;
using System.Text;
using JunkManager.Core;
using JunkManager.Core.Registry;
using JunkManager.Safety;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Итог приёмки. Расхождения разделены на четыре класса, потому что они значат
/// разное: промах это глубина, срабатывание на ловушке это готовое удаление
/// чужого файла, неверный источник это мёртвый механизм при верном счёте, а
/// находка в запрещённом корне это провал при любых обстоятельствах.
/// </summary>
public sealed record SeedVerdict(
    int Planted,
    int Baits,
    int Traps,
    IReadOnlyList<SeedEntry> Hit,
    IReadOnlyList<SeedEntry> Missed,
    IReadOnlyList<string> MissedRequired,
    IReadOnlyList<string> TrapsTriggered,
    IReadOnlyList<string> WrongSource,
    int OutsideManifest,
    IReadOnlyList<string> InForbiddenRoots,
    IReadOnlyList<string> SeedFailures)
{
    /// <summary>
    /// Отчёт печатается ВСЕГДА, а не только при падении: числа нужны и на
    /// зелёном прогоне, иначе рост глубины не с чем сравнивать.
    /// </summary>
    public string Report()
    {
        var nastoyashchihVsego = Baits - Butaforskih();
        var nastoyashchihNaydeno = Hit.Count(h => h.Genuine);
        var butaforskihNaydeno = Hit.Count - nastoyashchihNaydeno;

        var otchet = new StringBuilder();
        var ic = CultureInfo.InvariantCulture;

        otchet.AppendLine(string.Create(ic,
            $"посеяно {Planted}, из них приманок {Baits}, ловушек {Traps}"));

        otchet.AppendLine(string.Create(ic,
            $"найдено приманок {Hit.Count} из {Baits}   (настоящих {nastoyashchihNaydeno} из {nastoyashchihVsego}, бутафорских {butaforskihNaydeno} из {Butaforskih()})"));

        foreach (var promah in Missed)
        {
            var obyazatelnost = promah.Required ? "ОБЯЗАТЕЛЬНАЯ" : "измерение";
            otchet.AppendLine(string.Create(ic,
                $"  промах: {promah.Id,-28} ожидался источник {promah.ExpectedSource}, не найден вовсе [{obyazatelnost}]"));
        }

        foreach (var nesovpadenie in WrongSource)
        {
            otchet.AppendLine("  промах: " + nesovpadenie);
        }

        otchet.AppendLine(string.Create(ic,
            $"сработало на ловушках {TrapsTriggered.Count} из {Traps}"));

        foreach (var lovushka in TrapsTriggered)
        {
            otchet.AppendLine("  СРАБОТАЛА ЛОВУШКА: " + lovushka);
        }

        otchet.AppendLine(string.Create(ic,
            $"находок вне описи {OutsideManifest}   (это норма: в госте есть и настоящий мусор)"));

        otchet.AppendLine(string.Create(ic,
            $"находок в запрещённых корнях {InForbiddenRoots.Count}   <- любое ненулевое значение это провал"));

        foreach (var zapreshchennyy in InForbiddenRoots)
        {
            otchet.AppendLine("  В ЗАПРЕТНОМ КОРНЕ: " + zapreshchennyy);
        }

        if (SeedFailures.Count > 0)
        {
            otchet.AppendLine(string.Create(ic,
                $"НЕ УДАЛОСЬ ПОСЕЯТЬ {SeedFailures.Count}   <- эти приманки не проверяют ничего"));

            foreach (var otkaz in SeedFailures)
            {
                otchet.AppendLine("  " + otkaz);
            }
        }

        return otchet.ToString();
    }

    private int Butaforskih() => Hit.Count(h => !h.Genuine) + Missed.Count(m => !m.Genuine);
}

/// <summary>
/// Сравнивает опись посева с тем, что нашёл продукт.
/// </summary>
public static class SeedJudge
{
    public static SeedVerdict Judge(
        IReadOnlyList<SeedEntry> seeds,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<string>? seedFailures = null,
        IReadOnlyList<RegistryFinding>? registryFindings = null)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(findings);

        // Находка реестра приводится к Finding ТОЛЬКО здесь, в судье, и такого
        // преобразования намеренно нет в продукте: путь у неё это отсутствующий
        // файл, то есть ровно то, чего нельзя отдавать удалятору файлов.
        // Продукт, умеющий сделать из записи реестра файловую находку, однажды
        // её удалит.
        var reestrovye = (registryFindings ?? [])
            .Select(r => new Finding(
                Name: r.Name,
                Path: r.MissingTarget,
                SizeBytes: 0,
                Tier: RiskTier.Risk,
                Consequence: r.Consequence,
                Source: FindingSource.Registry))
            .ToList();

        var vse = findings.Concat(reestrovye).ToList();

        var primanki = seeds.Where(s => s.MustFind).ToList();
        var lovushki = seeds.Where(s => !s.MustFind).ToList();

        var popali = new List<SeedEntry>();
        var promahi = new List<SeedEntry>();
        var neTotIstochnik = new List<string>();

        foreach (var primanka in primanki)
        {
            // Совпадение по пути, а не по имени: две находки могут звать себя
            // одинаково, но лежать в разных местах.
            var naydena = vse.FirstOrDefault(f => Zatragivaet(f, primanka.ExpandedPath));

            if (naydena is null)
            {
                promahi.Add(primanka);
                continue;
            }

            popali.Add(primanka);

            if (primanka.ExpectedSource is not null && naydena.Source != primanka.ExpectedSource)
            {
                // Именно здесь ловится мёртвый механизм, которого не видно по
                // счёту: приманку нашли, но нашёл её не тот, кто обязан.
                neTotIstochnik.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{primanka.Id}: найден источником {naydena.Source}, ожидался {primanka.ExpectedSource}"));
            }
        }

        var srabotali = lovushki
            .Where(l => vse.Any(f => Zatragivaet(f, l.ExpandedPath)))
            .Select(l => l.Id)
            .ToList();

        // Находки вне описи это НОРМА: в госте есть и настоящий мусор. А вот
        // находка в запрещённом корне это провал при любых обстоятельствах.
        // Спрашивается и про корень находки, и про каждую её цель: находка с
        // безобидным корнем и целью в System32 это ровно тот случай, ради
        // которого класс заведён.
        // Личности, не являющиеся путями (обработчик очистки Windows, склад
        // компонентов DISM, пакет MSIX), отсеиваются ДО файлового guard: он
        // отвечает на них отказом просто потому, что это не путь, и приёмка
        // красила таким отказом семь честных находок 05.09.2026. Скидка даётся
        // именно личности: цели такой находки проверяются наравне со всеми,
        // поэтому фильтр стоит после разворачивания DeletionTargets, а не до.
        var vZapretnyh = findings
            .SelectMany(f => f.DeletionTargets.Prepend(f.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(FindingPath.IsFileSystem)
            .Where(p => !SafetyGuard.TryVerify(p, out _, out _))
            .ToList();

        // У записи реестра запретный корень свой: guard реестра, а не файловый.
        // Спрашивать файловый guard про отсутствующий файл бессмысленно, он
        // ответит про место, откуда мы ничего не удаляем, и приёмка покраснеет
        // на честной находке в HKCU.
        var vZapretnyhVetkah = (registryFindings ?? [])
            .Where(r => !RegistryGuard.TryVerifyBranch(r.Hive, r.SubKey, r.View, out _, out _))
            .Select(r => r.Address)
            .ToList();

        return new SeedVerdict(
            Planted: seeds.Count,
            Baits: primanki.Count,
            Traps: lovushki.Count,
            Hit: popali,
            Missed: promahi,
            MissedRequired: [.. promahi.Where(m => m.Required).Select(m => m.Id)],
            TrapsTriggered: srabotali,
            WrongSource: neTotIstochnik,
            OutsideManifest: vse.Count - popali.Count,
            InForbiddenRoots: [.. vZapretnyh, .. vZapretnyhVetkah],
            SeedFailures: seedFailures ?? []);
    }

    /// <summary>
    /// Затрагивает ли находка посеянный путь. Спрашивается у ЦЕЛЕЙ находки, а не
    /// у её корня.
    /// </summary>
    /// <remarks>
    /// Это различие и есть весь смысл поэлементного режима. Правило на
    /// пользовательский %TEMP% показывает корнем сам %TEMP%, внутри которого
    /// лежат и приманка, и живой лог. Судья, спрашивающий корень, объявил бы
    /// сработавшими все ловушки разом, хотя продукт к ним не притрагивается.
    /// </remarks>
    private static bool Zatragivaet(Finding finding, string seededPath) =>
        finding.DeletionTargets.Any(cel => Covers(cel, seededPath));

    /// <summary>
    /// Совпадение считается в обе стороны. Цель может БЫТЬ посеянным путём,
    /// может быть каталогом, внутри которого он лежит (правило имеет право
    /// сообщать про каталог целиком), и может лежать ВНУТРИ посеянного каталога
    /// (правило с отбором забирает файлы, а не каталог).
    /// </summary>
    private static bool Covers(string target, string seededPath) =>
        seededPath.Equals(target, StringComparison.OrdinalIgnoreCase)
        || SafetyGuard.IsAtOrUnder(seededPath, target)
        || SafetyGuard.IsAtOrUnder(target, seededPath);
}
