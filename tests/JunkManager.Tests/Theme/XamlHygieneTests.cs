using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

/// <summary>
/// Bans that are cheap to state and expensive to notice by eye. Every one of
/// these went wrong somewhere before it became a rule.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class XamlHygieneTests
{
    private static IEnumerable<(string Fayl, XDocument Doc)> Vsya_razmetka() =>
        XamlSource.Files().Select(f => (Path.GetFileName(f), XDocument.Load(f)));

    [Fact]
    public void Nigde_net_podcherkivaniy()
    {
        // Подчёркивание запрещено как аффорданс во всех проектах. Кликабельность
        // показывается цветом, и это правило ловится здесь, а не на ревью.
        foreach (var (fayl, doc) in Vsya_razmetka())
        {
            doc.Descendants().Should().NotContain(
                e => e.Name.LocalName == "Underline",
                "в {0} стоит элемент Underline", fayl);

            doc.Descendants().Attributes("TextDecorations").Should().BeEmpty(
                "в {0} задано TextDecorations", fayl);

            doc.Descendants().Should().NotContain(
                e => e.Name.LocalName == "Hyperlink",
                "в {0} стоит Hyperlink, а он приносит подчёркивание вместе с собой", fayl);
        }
    }

    [Fact]
    public void Med_ne_zalivaet_krupnye_ploskosti()
    {
        // Медь это ступень риска. Как только она становится фоном панели, цвет
        // перестаёт быть сигналом. Ровно на этом развалилась карта занятости.
        string[] zapreshcheno = ["RiskBrush", "SafeFillBrush"];
        string[] krupnye = ["Window", "Grid", "Border", "StackPanel", "Panel"];

        foreach (var (fayl, doc) in Vsya_razmetka())
        {
            foreach (var element in doc.Descendants().Where(e => krupnye.Contains(e.Name.LocalName)))
            {
                var fon = element.Attribute("Background")?.Value;
                if (fon is null)
                {
                    continue;
                }

                zapreshcheno.Should().NotContain(
                    kist => fon.Contains(kist, StringComparison.Ordinal),
                    "в {0} медь стоит фоном крупной плоскости: {1}", fayl, fon);
            }
        }
    }

    [Fact]
    public void Vse_dlinnye_spiski_virtualizirovany()
    {
        // Список находок это десятки тысяч строк на живой машине. Без
        // виртуализации окно не подвисает, а просто не открывается: WPF строит
        // контейнер на каждую строку до первой отрисовки.
        var doc = XamlSource.Load("Controls.Lists.xaml");

        var stili = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style")
            .Where(e => e.Attribute("TargetType") is not null)
            .Where(e => e.Attribute("TargetType")!.Value.Contains("ListBox}", StringComparison.Ordinal)
                     || e.Attribute("TargetType")!.Value.Contains("ItemsControl}", StringComparison.Ordinal))
            .ToList();

        stili.Should().HaveCount(2, "виртуализация нужна обоим, а не одному из них");

        foreach (var stil in stili)
        {
            var setters = stil.Elements()
                .Where(e => e.Name.LocalName == "Setter")
                .Where(e => e.Attribute("Value") is not null)
                .ToDictionary(
                    e => e.Attribute("Property")!.Value,
                    e => e.Attribute("Value")!.Value,
                    StringComparer.Ordinal);

            setters.Should().ContainKey("VirtualizingPanel.IsVirtualizing")
                .WhoseValue.Should().Be("True");

            setters.Should().ContainKey("VirtualizingPanel.VirtualizationMode")
                .WhoseValue.Should().Be("Recycling");

            setters.Should().ContainKey("ScrollViewer.CanContentScroll")
                .WhoseValue.Should().Be("True", "пиксельная прокрутка материализует все строки при замере");
        }
    }

    [Fact]
    public void Spiski_v_ekranah_ne_otklyuchayut_virtualizaciyu_svoey_panelyu()
    {
        // Неявный стиль даёт виртуализацию всем. Экран, задавший свою
        // ItemsPanel, её перебивает, и обычная StackPanel в этом месте
        // возвращает контейнер на каждую строку молча.
        foreach (var (fayl, doc) in Vsya_razmetka()
                     .Where(p => p.Fayl.EndsWith("View.xaml", StringComparison.Ordinal)))
        {
            foreach (var panel in doc.Descendants()
                         .Where(e => e.Name.LocalName == "ItemsPanelTemplate")
                         .SelectMany(e => e.Elements()))
            {
                panel.Name.LocalName.Should().Be("VirtualizingStackPanel",
                    "в {0} список получил свою панель без виртуализации", fayl);
            }
        }
    }

    [Fact]
    public void Ni_odnogo_zapreshchennogo_kontrola()
    {
        string[] zapreshcheno = ["DataGrid", "ListView", "PasswordBox", "Hyperlink", "Expander"];

        foreach (var (fayl, doc) in Vsya_razmetka())
        {
            doc.Descendants().Select(e => e.Name.LocalName).Should().NotIntersectWith(
                zapreshcheno, "в {0} стоит контрол из запрещённых", fayl);
        }
    }

    [Fact]
    public void U_kazhdogo_interaktivnogo_elementa_est_identifikator()
    {
        // Идентификаторы заводятся сразу, а не после падения UI-теста. Проверка
        // смотрит только на разметку экранов: в шаблонах темы элемент получает
        // идентификатор от того, кто шаблон применяет.
        string[] interaktivnye = ["Button", "ToggleButton", "CheckBox", "ComboBox", "TextBox"];
        var ekrany = XamlSource.Files().Where(f =>
            Path.GetFileName(f).EndsWith("View.xaml", StringComparison.Ordinal)
            || Path.GetFileName(f) == "ShellWindow.xaml").ToList();

        ekrany.Should().NotBeEmpty("иначе проверка идентификаторов молча зелёная");

        foreach (var fayl in ekrany)
        {
            var doc = XDocument.Load(fayl);

            foreach (var element in doc.Descendants().Where(e => interaktivnye.Contains(e.Name.LocalName)))
            {
                element.Attribute("AutomationProperties.AutomationId").Should().NotBeNull(
                    "в {0} у {1} нет AutomationId", Path.GetFileName(fayl), element.Name.LocalName);
            }
        }
    }

    /// <summary>
    /// Каждая ссылка <c>{StaticResource X}</c> и <c>{DynamicResource X}</c>
    /// обязана указывать на существующий ключ.
    /// </summary>
    /// <remarks>
    /// Написано по той же причине, что и проверка ссылок <c>x:Static</c> в
    /// SystemBrushOverrideTests, и закрывает оставленную ею щель. Имя ресурса
    /// разбирается ВО ВРЕМЯ РАБОТЫ: опечатка собирается без единого
    /// предупреждения и роняет приложение XamlParseException-ом на первом же
    /// запуске, а до запуска её не видит ни компилятор, ни проверки текста.
    /// Отражением тут не обойтись, ключи ресурсов это строки, поэтому
    /// собирается словарь всех объявленных ключей и сверяется со всеми
    /// использованными.
    /// </remarks>
    [Fact]
    public void Vse_imena_resursov_razmetki_obyavleny()
    {
        var obyavlennye = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fayl in XamlSource.Files())
        {
            foreach (var klyuch in XDocument.Load(fayl).Descendants()
                         .Select(e => e.Attribute(XamlSource.X + "Key")?.Value)
                         .Where(v => v is not null))
            {
                // Ключи вида {x:Static SystemColors...} это не имена, их
                // проверяет отражением соседний набор.
                if (!klyuch!.StartsWith('{'))
                {
                    obyavlennye.Add(klyuch);
                }
            }
        }

        obyavlennye.Should().HaveCountGreaterThan(40, "иначе сбор ключей сломан и проверка ничего не значит");

        var obrazec = new Regex(
            @"\{(?:Static|Dynamic)Resource\s+(?<imya>[A-Za-z_][A-Za-z0-9_]*)\s*\}",
            RegexOptions.CultureInvariant);

        var proverennye = 0;

        foreach (var fayl in XamlSource.Files())
        {
            var tekst = File.ReadAllText(fayl);

            foreach (Match m in obrazec.Matches(tekst))
            {
                var imya = m.Groups["imya"].Value;

                obyavlennye.Should().Contain(
                    imya,
                    "{0} ссылается на ресурс {1}, а такого ключа нет ни в одном словаре. " +
                    "Сборка это пропустит, приложение упадёт разбором разметки на запуске",
                    Path.GetFileName(fayl), imya);

                proverennye++;
            }
        }

        proverennye.Should().BeGreaterThan(150, "ссылок на ресурсы заведомо больше полутора сотен");
    }

    [Fact]
    public void Chasti_sostoyaniy_podklyucheny_ranshe_poverhnostey()
    {
        // StaticResource означает «уже загружено», а не «загрузится потом».
        // Стиль StateHost разрешает четыре шаблона по имени, и словарь частей
        // ниже него в списке роняет приложение на разборе App.xaml.
        var app = File.ReadAllText(
            XamlSource.Files().Single(f => Path.GetFileName(f) == "App.xaml"));

        var poverhnosti = app.IndexOf("Theme/Controls.Surfaces.xaml", StringComparison.Ordinal);
        poverhnosti.Should().BeGreaterThanOrEqualTo(0, "Controls.Surfaces.xaml обязан быть подключён");

        foreach (var chast in new[]
                 {
                     "Views/Parts/EmptyState.xaml", "Views/Parts/LoadingState.xaml",
                     "Views/Parts/ErrorState.xaml", "Views/Parts/RestrictionNote.xaml",
                 })
        {
            var mesto = app.IndexOf(chast, StringComparison.Ordinal);
            mesto.Should().BeGreaterThanOrEqualTo(0, "{0} обязан быть подключён в App.xaml", chast);
            mesto.Should().BeLessThan(poverhnosti, "{0} обязан идти до Controls.Surfaces.xaml", chast);
        }
    }

    [Fact]
    public void Metka_aktivnogo_razdela_prihodit_iz_dannyh_a_ne_iz_sostoyaniya_knopki()
    {
        // Проверено запуском 05.09.2026: у ToggleButton своя отметка IsChecked,
        // и переключается она в обход модели. Средство автоматизации дёрнуло
        // Toggle, привязка к IsCurrent перестала действовать, и на экране
        // осталось два помеченных раздела сразу.
        var doc = XamlSource.Load("Controls.Surfaces.xaml");

        var relse = doc.Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && e.Attribute(XamlSource.X + "Key")?.Value == "RailItemStyle");

        relse.Attribute("TargetType")!.Value.Should().Contain(
            "Button}", "рельс это кнопка перехода, а не переключатель со своим состоянием");
        relse.Attribute("TargetType")!.Value.Should().NotContain(
            "ToggleButton", "у переключателя есть собственная отметка, и она расходится с моделью");

        relse.ToString().Should().NotContain(
            "IsChecked", "отметка активного раздела обязана приходить только из IsCurrent");
        relse.ToString().Should().Contain(
            "IsCurrent", "иначе рисовать активный раздел нечем");

        var okno = XamlSource.Load("ShellWindow.xaml").ToString();
        okno.Should().NotContain("IsChecked",
            "окно не должно возвращать кнопке собственное состояние в обход модели");
    }

    [Fact]
    public void Okno_prosit_podognat_sebya_pod_ekran()
    {
        // Поведение может быть написано, покрыто тестами и НЕ ПОДКЛЮЧЕНО:
        // единственное место, где оно включается, это одна строка разметки,
        // и потерять её ничего не стоит. Арифметика проверяется отдельно, а
        // это проверка того, что её вообще зовут.
        XamlSource.Load("ShellWindow.xaml").ToString()
            .Should().Contain(
                "PodgonPodEkran.Enabled=\"True\"",
                "без этого окно на масштабе 125 процентов не помещается на экран");
    }

    [Fact]
    public void V_podvale_relsa_net_sluzhebnyh_chisel()
    {
        // Подвал рельса показывал «правил 38» и «права подняты при запуске».
        // Права уже висят плашкой сверху, а число правил это внутренняя кухня:
        // человек не может по нему ничего решить. Число осталось там, где оно
        // что-то объясняет, в оценке длительности прохода на «Обзоре».
        var okno = XamlSource.Load("ShellWindow.xaml").ToString();

        okno.Should().NotContain(
            "rail-footer", "права дублируются плашкой rights-badge наверху окна");
        okno.Should().NotContain(
            "rail-rules", "число загруженных правил человеку решать нечего");
    }

    [Fact]
    public void Vyhody_iz_zanyatogo_fayla_podklyucheny_v_razmetke()
    {
        // Служба, модель строки и все три механизма написаны и покрыты
        // проверками, а включает их одна строка разметки. Ровно так до
        // 06.09.2026 в продукте лежали RebootDeleteScheduler и
        // VolumeCachePurger: собранные, зелёные и без единого вызова из прода.
        var ekran = XamlSource.Load("FilesView.xaml").ToString();

        ekran.Should().Contain(
            "report-stuck",
            "без этого блока отказ «файл занят» снова становится тупиком");
        ekran.Should().Contain(
            "{Binding HasStuck",
            "иначе блок висит на экране и у чистого прогона");
        ekran.Should().Contain(
            "{Binding Stuck}",
            "иначе блок есть, а строк в нём нет");

        foreach (var komanda in new[] { "OtlozhitCommand", "ZavershitCommand" })
        {
            ekran.Should().Contain(
                komanda, "выход {0} написан, но кнопки к нему на экране нет", komanda);
        }

        // Кнопки «попросить закрыться» на экране НЕТ и быть не должно: этот
        // выход продукт делает сам. Проверка на её отсутствие тут не
        // придирчивость: вернувшаяся кнопка означает, что автоматическую
        // просьбу заменили обратно на вопрос человеку.
        ekran.Should().NotContain(
            "PoprositCommand", "первый выход продукт делает сам, кнопки у него нет");
        ekran.Should().Contain(
            "stuck-progress", "ход просьбы обязан быть виден: продукт закрывает чужую программу");

        // Журнал показывает те же исходы удаления. Кнопки над держателем там
        // были бы предложением закрыть программу ради файла, удалённого неделю
        // назад, и это единственная причина не переиспользовать общий шаблон.
        XamlSource.Load("HistoryView.xaml").ToString().Should().NotContain(
            "ZavershitCommand", "в журнале действий над держателем быть не может");
    }

    [Fact]
    public void U_stroki_reestra_est_flazhok_i_podpis_togo_chto_uydet()
    {
        // Список без отметок это витрина. Проверка читает разметку как текст:
        // живое дерево видит только гость, а витрину надо ловить здесь.
        // Сравнение ТОЧНОЕ, а не поиском подстроки, как было написано в
        // плане. Причина: в файле уже живут registry-backups-panel,
        // registry-backups-list и registry-backups-close, и подстрока
        // "registry-backups" нашлась бы в любом из них. Проверка, зелёная от
        // соседнего идентификатора, не проверяет ничего.
        var doc = XamlSource.Load("RegistryView.xaml");
        var identifikatory = doc.Descendants()
            .Select(e => e.Attribute("AutomationProperties.AutomationId")?.Value)
            .Where(v => v is not null)
            .ToHashSet(StringComparer.Ordinal);

        identifikatory.Should().Contain("registry-row-check");
        identifikatory.Should().Contain("registry-row-kind");
        identifikatory.Should().Contain("registry-delete");
        identifikatory.Should().Contain("registry-backups");
        identifikatory.Should().Contain("registry-selected-count");
    }

    [Fact]
    public void Podpis_flazhka_tochki_vosstanovleniya_ne_obeshchaet_nerabochee()
    {
        // Подпись «пока модуль реестра не приехал, настройка только
        // запоминается» становится ложью в тот день, когда модуль приезжает.
        // Ложь в подписи никто не ловит: она читается один раз и запоминается
        // как правда.
        var razmetka = XamlSource.Load("SettingsView.xaml").ToString();

        razmetka.Should().NotContain("только запоминается");
        razmetka.Should().NotContain("не приехал");
    }
}
