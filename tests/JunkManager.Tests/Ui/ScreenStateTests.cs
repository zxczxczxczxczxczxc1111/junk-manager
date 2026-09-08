using CommunityToolkit.Mvvm.Input;
using JunkManager.Tests.Theme;
using FluentAssertions;
using JunkManager.App.ViewModels;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// Five states on every screen, as a table. One test per state on one screen
/// would pass while five screens quietly ship only their happy path.
/// </summary>
/// <remarks>
/// Класс Sandbox, а не Ui: проверки читают разметку и модель, окон не поднимают.
/// Каталог Ui взят из раскладки файлов плана и оставлен как есть.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class ScreenStateTests
{
    /// <summary>Шесть экранов из раздела 15 спеки. Седьмого не будет.</summary>
    private static readonly string[] VseEkrany =
    [
        "OverviewView.xaml", "FilesView.xaml", "ProgramsView.xaml",
        "RegistryView.xaml", "HistoryView.xaml", "SettingsView.xaml",
    ];

    /// <summary>
    /// Экраны, разметка которых уже написана. Список ведётся руками и сверяется
    /// с диском проверкой ниже.
    /// </summary>
    /// <remarks>
    /// В плане этот счёт стоял шестью КРАСНЫМИ проверками, которые закрываются
    /// задачами 8-12. Так делать нельзя, и причина машинная: mutate.ps1 считает
    /// пойманной любую мутацию, после которой прогон вернул ненулевой код.
    /// Постоянно красный набор Sandbox означает, что ВСЕ мутации «пойманы», не
    /// проверив ни одной, то есть ворота против самообмана сами становятся
    /// самообманом. Поэтому счёт выражен списком: экран, появившийся на диске и
    /// не вписанный сюда, роняет проверку, а забыть про StateHost на вписанном
    /// экране нельзя тем более. Счёт сейчас: 5 из 6.
    /// </remarks>
    private static readonly string[] Napisannye =
    [
        "OverviewView.xaml", "FilesView.xaml", "RegistryView.xaml",
        "HistoryView.xaml", "SettingsView.xaml", "ProgramsView.xaml",
    ];

    [Fact]
    public void Spisok_napisannyh_ekranov_sovpadaet_s_diskom()
    {
        var naDiske = XamlSource.Files()
            .Select(Path.GetFileName)
            .Where(f => VseEkrany.Contains(f, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        naDiske.Should().Equal(
            Napisannye.Order(StringComparer.Ordinal),
            "экран написан, но не вписан в список Napisannye, и проверки состояний его не смотрят");
    }

    /// <summary>
    /// Ни у одного экрана нет своего StateHost.
    /// </summary>
    /// <remarks>
    /// Раскладка задачи 8 давала каждому экрану собственный, и первая же сборка
    /// показала, во что это выливается: оболочка уже держит один, потому что до
    /// экранов состояния показывала она. Два хоста подряд рисуют пустое
    /// состояние ДВАЖДЫ, то есть две кнопки «Сканировать диск» одна под другой,
    /// и обе рабочие.
    ///
    /// Решение принято в пользу одного на всё приложение: раздел берёт
    /// состояние у своего экрана (<c>SectionViewModel.State</c>), поэтому у
    /// оболочки есть и фаза, и плашка, и они настоящие. Проверка от этого не
    /// ослабла, а усилилась: раньше она требовала «механизм один на экран»,
    /// теперь «механизм один на продукт».
    /// </remarks>
    // Фазы приходят числами, а не значениями: ScreenPhase внутренний, а метод
    // теста обязан быть public, иначе xUnit его не найдёт. Имена фаз стоят
    // рядом в комментарии, чтобы таблица оставалась читаемой.
    [Theory]
    [InlineData(true, 2, 1, true)]   // Empty -> Loading, фокус был на кнопке
    [InlineData(true, 1, 4, true)]   // Loading -> Ready
    [InlineData(false, 2, 1, false)] // фокуса внутри не было
    [InlineData(true, 4, 4, false)]  // фаза не менялась
    public void Fokus_zabiraetsya_tolko_kogda_ischezaet_element_pod_nim(
        bool fokusVnutri, int byloChislo, int staloChislo, bool ozhidaem)
    {
        var byla = (ScreenPhase)byloChislo;
        var stala = (ScreenPhase)staloChislo;

        // Найдено снимком 05.09.2026: нажатая с клавиатуры кнопка «Сканировать
        // диск» оставляет лиловую рамку посреди пустого экрана загрузки. Фаза
        // сменилась, презентер схлопнулся, а фокус остался на невидимой кнопке:
        // её украшение живёт в отдельном слое и про Visibility не знает.
        //
        // Забирать фокус, когда его и не было внутри, нельзя: это утащило бы
        // курсор из поля ввода на соседнем экране при любом чужом обновлении.
        JunkManager.App.Controls.StateHost
            .NuzhnoZabratFokus(fokusVnutri, byla, stala)
            .Should().Be(ozhidaem);
    }

    [Fact]
    public void Sostoyanie_zagruzki_govorit_slovami_a_ne_odnoy_polosoy()
    {
        // Полоса без подписи это «что-то происходит». На проходе, который идёт
        // больше минуты, человеку нужно знать ЧТО именно, иначе единственный
        // вывод из экрана это «зависло».
        var doc = XamlSource.Load("LoadingState.xaml");

        var teksty = doc.Descendants()
            .Where(e => e.Name.LocalName == "TextBlock")
            .ToList();

        teksty.Should().HaveCountGreaterThanOrEqualTo(2,
            "заголовок и путь: одного пути мало, он меняется и читается как шум");

        // Ink4 по своему же описанию в Tokens.xaml это украшение и «никогда не
        // предложение». Предложение, набранное им, на пустоте не видно.
        teksty.Select(e => e.Attribute("Foreground")?.Value ?? string.Empty)
            .Should().NotContain(k => k.Contains("Ink4Brush", StringComparison.Ordinal),
                "текст состояния набран цветом украшений");
    }

    [Fact]
    public void Napisannye_ekrany_ne_zavodyat_svoy_StateHost()
    {
        foreach (var fayl in Napisannye)
        {
            XamlSource.Load(fayl).Descendants().Should().NotContain(
                e => e.Name.LocalName == "StateHost",
                "экран {0} завёл второй StateHost поверх того, что держит оболочка: "
                + "пустое состояние нарисуется дважды", fayl);
        }
    }

    [Fact]
    public void Edinstvennyy_StateHost_zhivet_v_obolochke()
    {
        var hosts = XamlSource.Files()
            .SelectMany(f => System.Xml.Linq.XDocument.Load(f).Descendants()
                .Where(e => e.Name.LocalName == "StateHost")
                .Select(e => Path.GetFileName(f)))
            .ToList();

        hosts.Should().Equal(["ShellWindow.xaml"],
            "механизм состояний один на продукт, и стоит он там, где раздел ещё может "
            + "не иметь экрана вовсе");
    }

    [Fact]
    public void Napisannye_ekrany_ne_risuyut_svoi_zaglushki_sostoyaniy()
    {
        // Свой «ничего не найдено» внутри экрана это шестая копия механизма и
        // шестой набор формулировок. Тексты живут в модели состояния.
        foreach (var fayl in Napisannye)
        {
            XamlSource.Load(fayl).Descendants()
                .Where(e => e.Name.LocalName == "TextBlock")
                .Select(e => e.Attribute("Text")?.Value ?? string.Empty)
                .Should().NotContain(
                    t => t.Contains("Ничего не найдено", StringComparison.OrdinalIgnoreCase)
                         || t.Contains("Пусто", StringComparison.OrdinalIgnoreCase),
                    "экран {0} пишет пустое состояние мимо ScreenStateViewModel", fayl);
        }
    }

    [Fact]
    public void Obolochka_pokazyvaet_sostoyanie_tekushchego_razdela()
    {
        // До первого экрана состояния показывает сама оболочка. Без этой
        // проверки задача 8 могла бы снять StateHost из окна и не заметить.
        var okno = XamlSource.Load("ShellWindow.xaml");

        var host = okno.Descendants().Should().ContainSingle(
            e => e.Name.LocalName == "StateHost").Subject;

        host.Attribute("Phase")!.Value.Should().Contain("Current.State.Phase");
        host.Attribute("State")!.Value.Should().Contain("Current.State");
        host.Attribute("HasRestriction")!.Value.Should().Contain("Current.State.HasRestriction");

        // Содержимое это модель экрана раздела. Без него окно рисует состояния
        // и не рисует ни одного экрана: собранный «Обзор» просто не появится, и
        // выглядеть это будет как вечное пустое состояние.
        host.Attribute("Content")!.Value.Should().Contain("Current.Screen");
    }

    [Fact]
    public void U_kazhdogo_ekrana_iz_speki_est_shablon_dannyh_ili_ego_net_v_spiske()
    {
        // Шаблон данных это единственное, что связывает модель экрана с его
        // разметкой. Он разрешается ВО ВРЕМЯ РАБОТЫ: забытый шаблон собирается
        // молча, а на экране остаётся имя типа текстом.
        var app = File.ReadAllText(
            XamlSource.Files().Single(f => Path.GetFileName(f) == "App.xaml"));

        foreach (var fayl in Napisannye)
        {
            var imya = Path.GetFileNameWithoutExtension(fayl);

            app.Should().Contain($"<v:{imya}/>",
                "экран {0} написан, но App.xaml не знает, чем рисовать его модель", fayl);
        }
    }

    [Fact]
    public void Model_sostoyaniya_prohodit_vse_pyat_sostoyaniy()
    {
        var sostoyanie = new ScreenStateViewModel();

        // 1. Загрузка.
        sostoyanie.Nachat("C:\\Windows\\Panther");
        sostoyanie.Phase.Should().Be(ScreenPhase.Loading);
        sostoyanie.ProgressShare.Should().BeNull("общий объём в начале неизвестен");
        sostoyanie.ProgressPath.Should().Be("C:\\Windows\\Panther");

        sostoyanie.Hod(0.41, "%TEMP%");
        sostoyanie.ProgressShare.Should().Be(0.41);

        // 2. Пусто.
        sostoyanie.Pusto("Диск ещё не сканировали", "Размеры считаются обходом", "Сканировать диск");
        sostoyanie.Phase.Should().Be(ScreenPhase.Empty);
        sostoyanie.EmptyAction.Should().Be("Сканировать диск");

        // 3. Ошибка.
        sostoyanie.Oshibka("Сканирование прервано", "Том D: перестал отвечать");
        sostoyanie.Phase.Should().Be(ScreenPhase.Error);

        // 4. Готово.
        sostoyanie.Gotovo();
        sostoyanie.Phase.Should().Be(ScreenPhase.Ready);

        // 5. Отказ от повышения прав: поверх готового, а не вместо него.
        sostoyanie.Ogranichit("Категория «Windows» пропущена целиком, скрыто 8,5 ГБ");
        sostoyanie.HasRestriction.Should().BeTrue();
        sostoyanie.Phase.Should().Be(ScreenPhase.Ready, "ограничение не подменяет содержимое");

        sostoyanie.Ogranichit(null);
        sostoyanie.HasRestriction.Should().BeFalse();
    }

    [Fact]
    public void Zapozdavshiy_hod_ne_vozvrashchaet_ekran_v_zagruzku()
    {
        // Отменённое сканирование успевает прислать ещё один отчёт о ходе.
        // Если он поднимет фазу обратно, человек увидит бегущую полосу на
        // остановленной работе.
        var sostoyanie = new ScreenStateViewModel();
        sostoyanie.Nachat();
        sostoyanie.Gotovo();

        sostoyanie.Hod(0.9, "%LOCALAPPDATA%\\npm-cache");

        sostoyanie.Phase.Should().Be(ScreenPhase.Ready);
    }

    [Fact]
    public void Dolya_hoda_zazhata_v_predelah_ot_nulya_do_edinicy()
    {
        var sostoyanie = new ScreenStateViewModel();

        sostoyanie.Hod(1.4, null);
        sostoyanie.ProgressShare.Should().Be(1.0);

        sostoyanie.Hod(-0.2, null);
        sostoyanie.ProgressShare.Should().Be(0.0);
    }

    [Fact]
    public void Priznak_ogranicheniya_soobshchaet_ob_izmenenii()
    {
        // HasRestriction это вычисляемое свойство, а плашка привязана к нему.
        // Без уведомления она не появится вовсе: значение поменяется, а
        // разметка об этом не узнает, и отказ от повышения прав станет
        // невидимым ровно там, где он важнее всего.
        var sostoyanie = new ScreenStateViewModel();
        var izmenennye = new List<string>();
        sostoyanie.PropertyChanged += (_, e) => izmenennye.Add(e.PropertyName ?? string.Empty);

        sostoyanie.Ogranichit("Ветка HKLM не читалась");

        izmenennye.Should().Contain(nameof(ScreenStateViewModel.HasRestriction));
    }

    [Fact]
    public void Nachat_sbrasyvaet_dolyu_ostavshuyusya_ot_proshlogo_prohoda()
    {
        // Второй запуск сканирования начинается с неизвестного объёма. Доля,
        // доставшаяся от прошлого раза, нарисует полосу на 90 процентов в тот
        // момент, когда не посчитано ещё ничего.
        var sostoyanie = new ScreenStateViewModel();
        sostoyanie.Nachat();
        sostoyanie.Hod(0.9, null);

        sostoyanie.Nachat("D:\\");

        sostoyanie.ProgressShare.Should().BeNull();
        sostoyanie.ProgressPath.Should().Be("D:\\");
    }

    [Fact]
    public void Podpis_i_komanda_pustogo_sostoyaniya_stavyatsya_vmeste()
    {
        // Кнопка без команды это кнопка, которая ничего не делает. Раз подпись
        // и команда приходят одним вызовом, разъехаться они не могут.
        var sostoyanie = new ScreenStateViewModel();
        var vypolneno = 0;
        var komanda = new RelayCommand(() => vypolneno++);

        sostoyanie.Pusto("Диск ещё не сканировали", "Размеры считаются обходом",
            "Сканировать диск", komanda);

        sostoyanie.EmptyCommand.Should().BeSameAs(komanda);
        sostoyanie.EmptyCommand!.Execute(null);
        vypolneno.Should().Be(1);
    }

    [Fact]
    public void Pustoe_sostoyanie_bez_deystviya_ne_ostavlyaet_staruyu_komandu()
    {
        // Второй Pusto без кнопки обязан снять и подпись, и команду. Иначе
        // невидимая кнопка остаётся привязанной к сканированию прошлого экрана.
        var sostoyanie = new ScreenStateViewModel();
        sostoyanie.Pusto("а", "б", "Сканировать", new RelayCommand(() => { }));

        sostoyanie.Pusto("в", "г");

        sostoyanie.EmptyAction.Should().BeNull();
        sostoyanie.EmptyCommand.Should().BeNull();
    }

    [Fact]
    public void Neizvestnaya_dolya_stiraet_prezhnyuyu()
    {
        // Источник, который сначала знал объём, а потом перестал, обязан
        // вернуть полосу в неопределённое состояние, а не заморозить её на
        // последнем известном числе.
        var sostoyanie = new ScreenStateViewModel();
        sostoyanie.Hod(0.5, null);

        sostoyanie.Hod(null, null);

        sostoyanie.ProgressShare.Should().BeNull();
    }
}
