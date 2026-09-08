using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class SystemBrushOverrideTests
{
    /// <summary>
    /// Every system key whose default leaks Windows blue or Windows grey into a
    /// control that otherwise has a full template of its own.
    /// </summary>
    private static readonly string[] Required =
    [
        "SystemColors.HighlightBrushKey",
        "SystemColors.HighlightTextBrushKey",
        "SystemColors.InactiveSelectionHighlightBrushKey",
        "SystemColors.InactiveSelectionHighlightTextBrushKey",
        "SystemColors.ControlBrushKey",
        "SystemColors.ControlTextBrushKey",
        "SystemColors.WindowBrushKey",
        "SystemColors.WindowTextBrushKey",
        "SystemColors.GrayTextBrushKey",
        "SystemColors.HotTrackBrushKey",
        "SystemColors.MenuBrushKey",
        "SystemColors.MenuTextBrushKey",
        "SystemColors.MenuHighlightBrushKey",
        "SystemColors.InfoBrushKey",
        "SystemColors.InfoTextBrushKey",
        "SystemColors.HighlightColorKey",
        "SystemParameters.FocusVisualStyleKey",
    ];

    // Возвращается List, а не IReadOnlyList: CA1859 при AnalysisLevel=latest-all
    // требует у приватного метода конкретный тип, и требует по делу.
    private static List<string> DeclaredKeys()
    {
        var doc = XamlSource.Load("SystemOverrides.xaml");

        return doc.Descendants()
            .Select(e => e.Attribute(XamlSource.X + "Key")?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();
    }

    [Fact]
    public void Vse_sistemnye_klyuchi_perebity()
    {
        var declared = DeclaredKeys();

        foreach (var key in Required)
        {
            declared.Should().Contain(
                v => v.Contains(key, StringComparison.Ordinal),
                "ключ {0} не перебит, и стандартный цвет Windows пройдёт сквозь свой шаблон", key);
        }
    }

    [Fact]
    public void Vydelenie_okrasheno_akcentom_a_ne_sistemnym_sinim()
    {
        var doc = XamlSource.Load("SystemOverrides.xaml");

        // Ключ ищется целиком, а не по хвосту «HighlightBrushKey»: этот хвост
        // есть ещё у InactiveSelectionHighlightBrushKey и MenuHighlightBrushKey,
        // и поиск по подстроке находил три элемента вместо одного.
        var highlight = doc.Descendants()
            .Single(e => e.Attribute(XamlSource.X + "Key")?.Value
                          .Contains("{x:Static SystemColors.HighlightBrushKey}", StringComparison.Ordinal) == true);

        highlight.Attribute("Color")!.Value.Should().Contain("AccentColor");
    }

    [Fact]
    public void Kolco_fokusa_ne_punktir()
    {
        // The WPF default focus visual is a dotted rectangle drawn by the system.
        // Ours is a solid accent ring, and the dotted one must be gone, not
        // merely covered.
        var doc = XamlSource.Load("SystemOverrides.xaml");
        var text = doc.ToString();

        text.Should().NotContain("StrokeDashArray");
        text.Should().Contain("FocusVisualStyleKey");
        text.Should().Contain("AccentBrush");
    }

    [Fact]
    public void Slovar_perebivok_podklyuchen_posle_tokenov()
    {
        // Порядок обязателен, и он не вопрос вкуса: SystemOverrides читает
        // AccentColor из Tokens. При обратном порядке разметка не собирается
        // вовсе, а при отсутствии подключения собирается молча и не работает
        // ничего: словарь, который не подключён, это просто файл на диске.
        var app = XamlSource.Load("App.xaml").ToString();

        var tokeny = app.IndexOf("Theme/Tokens.xaml", StringComparison.Ordinal);
        var perebivki = app.IndexOf("Theme/SystemOverrides.xaml", StringComparison.Ordinal);

        tokeny.Should().BeGreaterThanOrEqualTo(0, "Tokens.xaml обязан быть подключён в App.xaml");
        perebivki.Should().BeGreaterThanOrEqualTo(0, "SystemOverrides.xaml обязан быть подключён в App.xaml");
        perebivki.Should().BeGreaterThan(tokeny, "перебивки читают AccentColor и обязаны идти после токенов");
    }

    /// <summary>
    /// Every <c>{x:Static Type.Member}</c> in the theme must resolve to a static
    /// member that actually exists.
    /// </summary>
    /// <remarks>
    /// Проверка написана 05.09.2026 по факту падения, а не из осторожности.
    /// План утверждал, что опечатка в имени системного ключа это ошибка сборки.
    /// Это НЕ ТАК: `x:Static` разбирается во время работы. Ключ
    /// `SystemColors.InactiveSelectionHighlightColorKey`, которого у WPF нет
    /// вовсе, собрался без единого предупреждения, прошёл все проверки разметки
    /// (они читают текст, а текст был на месте) и уронил приложение на первом же
    /// запуске: XamlParseException на строке 48 SystemOverrides.xaml.
    /// Отсюда правило: проверять не то, что ключ НАПИСАН, а то, что он
    /// СУЩЕСТВУЕТ. Отражение видит ровно то же, что увидит разбор разметки.
    /// </remarks>
    [Fact]
    public void Vse_staticheskie_klyuchi_razmetki_sushchestvuyut()
    {
        // Типы берутся из живой сборки WPF, а не из списка имён: список имён
        // это ещё одна копия того же текста, и врать они будут одинаково.
        var tipy = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["SystemColors"] = typeof(System.Windows.SystemColors),
            ["SystemParameters"] = typeof(System.Windows.SystemParameters),
            ["SystemFonts"] = typeof(System.Windows.SystemFonts),
        };

        var obrazec = new Regex(
            @"\{x:Static\s+(?<tip>[A-Za-z0-9_]+)\.(?<chlen>[A-Za-z0-9_]+)\s*\}",
            RegexOptions.CultureInvariant);

        var proverennye = 0;

        foreach (var fayl in XamlSource.Files())
        {
            var tekst = File.ReadAllText(fayl);

            foreach (Match m in obrazec.Matches(tekst))
            {
                var imyaTipa = m.Groups["tip"].Value;
                var imyaChlena = m.Groups["chlen"].Value;

                // Фигурных скобок в тексте причины быть не должно: FluentAssertions
                // прогоняет его через string.Format, и «x:Static» в фигурных
                // скобках уронит саму проверку вместо того, чтобы её объяснить.
                tipy.Should().ContainKey(
                    imyaTipa,
                    "в {0} встретилась статическая ссылка на тип {1} (член {2}), а этот тип " +
                    "проверка не знает. Добавить его сюда, иначе опечатка в нём доедет до запуска",
                    Path.GetFileName(fayl), imyaTipa, imyaChlena);

                var tip = tipy[imyaTipa];
                var est = tip.GetProperty(imyaChlena) is not null
                          || tip.GetField(imyaChlena) is not null;

                est.Should().BeTrue(
                    "{0} ссылается на {1}.{2}, а такого статического члена у WPF нет. " +
                    "Сборка это пропустит, приложение упадёт на запуске",
                    Path.GetFileName(fayl), imyaTipa, imyaChlena);

                proverennye++;
            }
        }

        // Ноль найденных ссылок означал бы, что регулярка разошлась с разметкой,
        // и проверка зелена, не проверив ничего. Это хуже красной.
        proverennye.Should().BeGreaterThan(15, "перебивок системных ключей заведомо больше пятнадцати");
    }
}
