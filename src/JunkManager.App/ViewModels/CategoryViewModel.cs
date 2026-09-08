using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JunkManager.Core;

namespace JunkManager.App.ViewModels;

/// <summary>
/// One row in the left column of the overview: a category, its findings and how
/// much of it is Risk.
/// </summary>
internal sealed partial class CategoryViewModel : ObservableObject
{
    /// <summary>
    /// Имя остатка. Строка одна и в одном месте: сравнение по ней встречается
    /// в сортировке, а второй литерал с другим регистром сломал бы её молча.
    /// </summary>
    public const string OstatokName = "Прочее";

    private CategoryViewModel(string name, IReadOnlyList<FindingViewModel> findings)
    {
        Name = name;
        Findings = new ObservableCollection<FindingViewModel>(findings);
        TotalBytes = findings.Sum(f => f.SizeBytes);
        RiskBytes = findings.Where(f => f.Tier == RiskTier.Risk).Sum(f => f.SizeBytes);
    }

    public string Name { get; }

    public ObservableCollection<FindingViewModel> Findings { get; }

    public long TotalBytes { get; }

    public long RiskBytes { get; }

    public int Count => Findings.Count;

    /// <summary>Доля опасного от размера категории, от нуля до единицы.</summary>
    public double RiskShare => TotalBytes > 0 ? (double)RiskBytes / TotalBytes : 0;

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// Turns findings into the category rows the screen shows.
    /// </summary>
    /// <param name="porogDoli">
    /// Доля от общего найденного, ниже которой категория не получает своей
    /// строки. 0.015 в продукте: полтора процента от восемнадцати гигабайт это
    /// около двухсот восьмидесяти мегабайт, то есть строка, ради которой стоит
    /// вести глазами.
    /// </param>
    /// <param name="maksimumStrok">
    /// Сколько строк список показывает всего, включая «Прочее». Список категорий
    /// существует, чтобы выбирать, а не чтобы прокручивать.
    /// </param>
    /// <param name="kategoriyaPoPravilu">
    /// Словарь «id правила в имя категории». Находка несёт RuleId, а имя
    /// категории живёт в файле правил и до находки не доезжает. Перенести его в
    /// саму находку значило бы поменять запись, которую читают приёмка посева и
    /// ScanJson, то есть чужой контракт. Null означает группировку по RuleId, и
    /// в таком виде функция проверяется тестами.
    /// </param>
    public static IReadOnlyList<CategoryViewModel> Sgruppirovat(
        IReadOnlyList<Finding> nahodki,
        double porogDoli,
        int maksimumStrok,
        IReadOnlyDictionary<string, string>? kategoriyaPoPravilu = null)
    {
        ArgumentNullException.ThrowIfNull(nahodki);
        ArgumentOutOfRangeException.ThrowIfLessThan(maksimumStrok, 2);

        if (nahodki.Count == 0)
        {
            return [];
        }

        var vsego = nahodki.Sum(f => f.SizeBytes);

        var syrye = nahodki
            .GroupBy(f => ImyaKategorii(f, kategoriyaPoPravilu), StringComparer.Ordinal)
            .Select(g => new
            {
                Imya = g.Key,
                Bayt = g.Sum(f => f.SizeBytes),
                Nahodki = g.OrderByDescending(f => f.SizeBytes).ToList(),
            })
            .OrderByDescending(k => k.Bayt)
            .ToList();

        var krupnye = syrye
            .Where(k => vsego == 0 || (double)k.Bayt / vsego >= porogDoli)
            .Take(maksimumStrok - 1)
            .ToList();

        // Группа, УЖЕ названная остатком, в крупных не остаётся никогда. Иначе
        // «Прочее» получает свою строку по размеру и вторую по остатку: две
        // строки с одним именем, между которыми человеку нечем выбрать.
        var svoyOstatok = krupnye
            .Where(k => string.Equals(k.Imya, OstatokName, StringComparison.Ordinal))
            .ToList();
        krupnye = krupnye.Except(svoyOstatok).ToList();

        var ostatok = syrye.Except(krupnye).SelectMany(k => k.Nahodki).ToList();

        // Одна мелкая категория в «Прочее» не сворачивается: это переименование,
        // а не сокращение. Строк столько же, а имя потеряно.
        //
        // Keep the explicit remainder grouped; two identical labels would be quite the achievement.
        if (ostatok.Count > 0 && svoyOstatok.Count == 0 && syrye.Count - krupnye.Count == 1)
        {
            krupnye = syrye.Take(maksimumStrok).ToList();
            ostatok = [];
        }

        var itog = krupnye
            .Select(k => Sobrat(k.Imya, k.Nahodki))
            .ToList();

        if (ostatok.Count > 0)
        {
            itog.Add(Sobrat(OstatokName, ostatok));
        }

        return itog.OrderByDescending(category => category.TotalBytes).ToArray();
    }

    private static CategoryViewModel Sobrat(string imya, IReadOnlyList<Finding> nahodki)
    {
        var maksimum = nahodki.Max(f => f.SizeBytes);

        var stroki = nahodki
            .OrderByDescending(f => f.SizeBytes)
            .Select(f => new FindingViewModel(f)
            {
                ShareOfMax = maksimum > 0 ? (double)f.SizeBytes / maksimum : 0,

                // Безопасное отмечено сразу, опасное нет. Это правило спеки, и
                // живёт оно здесь, в одном месте на все экраны.
                IsSelected = f.Tier == RiskTier.Safe,
            })
            .ToList();

        return new CategoryViewModel(imya, stroki);
    }

    /// <summary>
    /// Категория берётся из правила, а у находки без правила из её источника.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Первая версия складывала всё безправильное в «Прочее». Найдено снимком
    /// живого окна 05.09.2026: на экране оказалось ДВЕ строки «Прочее», потому
    /// что тем же именем зовётся остаток мелких категорий. Хуже того, находки
    /// без правила это больше гигабайта, то есть самая крупная строка списка
    /// стояла первой и называлась «Прочее», хотя остаток обязан быть последним.
    /// </para>
    /// <para>
    /// Находка без правила это не «непонятно что»: у неё есть источник, и
    /// источник знает, что она такое. Имена здесь ровно те, что человек увидит
    /// на экране, поэтому они на русском и без слов «детектор» и «источник».
    /// </para>
    /// </remarks>
    private static string ImyaKategorii(
        Finding nahodka, IReadOnlyDictionary<string, string>? kategoriyaPoPravilu)
    {
        if (string.IsNullOrEmpty(nahodka.RuleId))
        {
            return nahodka.Source switch
            {
                FindingSource.VolumeCache => "Хранилище Windows",
                FindingSource.PlatformTool => "Средства Windows",
                FindingSource.PatternScan => "Отчёты о сбоях",
                FindingSource.Vacuum => "Сжатие баз",
                FindingSource.Detector => "Следы программ",
                FindingSource.Registry => "Реестр",
                FindingSource.Program => "Программы",

                // Rule без RuleId это несобранная находка, и она честно
                // попадает в остаток, а не приписывается соседу по догадке.
                _ => OstatokName,
            };
        }

        return kategoriyaPoPravilu is not null
            && kategoriyaPoPravilu.TryGetValue(nahodka.RuleId, out var kategoriya)
            ? kategoriya
            : nahodka.RuleId;
    }
}
