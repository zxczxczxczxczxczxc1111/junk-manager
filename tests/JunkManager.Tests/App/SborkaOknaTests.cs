using System.Threading;
using System.Windows;
using System.Windows.Interop;
using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.App.Views;
using JunkManager.Deletion;
using JunkManager.Core;
using JunkManager.Core.Registry;
using Microsoft.Win32;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Собирает НАСТОЯЩЕЕ дерево окна и раскладывает его. Ничего не показывая.
/// </summary>
/// <remarks>
/// <para>
/// Заведён по следу конкретного падения 05.09.2026. Токен ширины колонки лежал
/// в Tokens.xaml как <c>sys:Double</c>, а <c>ColumnDefinition.Width</c> это
/// <c>GridLength</c>. Сборка прошла без предупреждений, 765 тестов были
/// зелёными, проверка разметки подтвердила, что ключ объявлен, и приложение
/// умерло <c>XamlParseException</c>-ом при первом показе экрана обзора: через
/// минуту после запуска, уже после сканирования, то есть в единственном месте,
/// куда никто не смотрел.
/// </para>
/// <para>
/// Проверки, читающие разметку как XML, этот класс дефектов не видят в
/// принципе: имя ресурса на месте, тип не совпадает. Ловит такое только
/// построение шаблонов, а шаблоны строятся при раскладке под настоящим корнем
/// отрисовки. Поэтому здесь настоящий WPF в потоке STA.
/// </para>
/// <para>
/// Класс Sandbox, а не Ui: на экране не появляется ничего, ввод не подаётся,
/// машина не трогается. Живой запуск с нажатиями это задача 13.
/// </para>
/// </remarks>
[Trait("Class", "Sandbox")]
[Collection(StaNabor.Imya)]
public sealed class SborkaOknaTests(StaStend stend)
{
    [Fact]
    public void Okno_sobiraetsya_vo_vseh_sostoyaniyah_ekrana()
    {
        stend.Vypolnit(() =>
        {
            using var obzor = new OverviewViewModel(new SkanerDlyaSborki());

            var okno = new ShellWindow
            {
                DataContext = new ShellViewModel(isElevated: true, obzor: obzor),
            };

            // Пустое.
            obzor.PokazatNachalo();
            using var stend2 = Stend.Podnyat(okno);

            // Загрузка.
            obzor.State.Nachat("C:\\Windows\\Panther");
            obzor.State.Hod(0.4, "C:\\Windows\\Panther");
            stend2.Prokrutit();

            // Готово. Ради этой строки всё и написано: содержимое экрана
            // разворачивается только здесь, и падало оно тоже только здесь.
            obzor.Prinyat(new ScanResult(
                [
                    Nahodka("кэш обновлений", "Windows", 5_000_000_000),
                    Nahodka("офлайн-карты", "Приложения", 20_000_000_000, RiskTier.Risk),
                    Nahodka("шейдеры", "GPU", 7_000_000_000),
                ],
                []));
            stend2.Prokrutit();

            // Ошибка.
            obzor.State.Oshibka("Сканирование прервано", "Отказано в доступе");
            stend2.Prokrutit();

            // Плашка ограничения поверх готового.
            obzor.State.Gotovo();
            obzor.State.Ogranichit("Часть каталогов не проверялась");
            stend2.Prokrutit();
        });
    }

    [Fact]
    public void Kazhdyy_razdel_bez_ekrana_sobiraetsya_zaglushkoy()
    {
        // Пять разделов из шести ещё без экранов. Переключение между ними это
        // то, что человек сделает в первую минуту, и падать там нельзя.
        stend.Vypolnit(() =>
        {
            var model = new ShellViewModel(isElevated: false);
            var okno = new ShellWindow { DataContext = model };

            using var stend2 = Stend.Podnyat(okno);

            foreach (var razdel in model.Sections)
            {
                model.VybratCommand.Execute(razdel);
                stend2.Prokrutit();
            }
        });
    }

    [Fact]
    public void Spisok_nahodok_virtualizuetsya_a_ne_sozdaet_stroku_na_kazhduyu()
    {
        // Семьдесят тысяч строк это не выдумка для красоты: столько даёт кэш
        // одного браузера. ItemsControl НЕ виртуализирует ничего по умолчанию,
        // и без своей панели окно вставало бы на минуты при выборе категории.
        stend.Vypolnit(() =>
        {
            using var obzor = new OverviewViewModel(new SkanerDlyaSborki());

            var okno = new ShellWindow
            {
                DataContext = new ShellViewModel(isElevated: true, obzor: obzor),
            };

            var mnogo = Enumerable.Range(0, 20_000)
                .Select(i => Nahodka($"файл{i}", "Браузеры", 1_000_000 + i))
                .ToList();

            using var stend2 = Stend.Podnyat(okno);

            var chasy = System.Diagnostics.Stopwatch.StartNew();
            obzor.Prinyat(new ScanResult(mnogo, []));
            stend2.Prokrutit();
            chasy.Stop();

            chasy.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
                "раскладка двадцати тысяч строк укладывается в это время только "
                + "при виртуализации: без неё создаётся контейнер на каждую");
        });
    }

    [Fact]
    public async Task Ekran_faylov_sobiraetsya_na_vseh_chetyreh_shagah()
    {
        // Шаги 2, 3 и 4 живут в одной разметке и до этой проверки не
        // разворачивались ни разу: их видимость решает признак, а признак в
        // собранном без окна дереве всегда стоял на первом шаге. Ровно так
        // сломанный токен ширины колонки дожил до живого запуска.
        await stend.VypolnitAsync(async () =>
        {
            var proshloe = new PosledniyProhod();

            // Отказ с НАЗВАННЫМ держателем: только он разворачивает на итоге
            // блок занятых файлов. Блок живёт в своём шаблоне, и до 06.09.2026
            // ни одна проверка сборки окна его не разворачивала: видимость
            // решает признак, а признак у заглушки всегда стоял на «всё
            // удалено». Ровно этим способом сломанный токен доживает до
            // живого запуска.
            var sluzhba = new OchistkaZaglushka
            {
                Ishod = DeleteStatus.Failed,
                Derzhatel = "Windows PowerShell (PID 13248)",
            };

            using var fayly = new FilesViewModel(proshloe, sluzhba, new ZaglushkaZanyatyh());

            var obolochka = new ShellViewModel(
                isElevated: true, fayly: fayly);
            var okno = new ShellWindow { DataContext = obolochka };

            using var stend2 = Stend.Podnyat(okno);

            // Раздел выбирается ЯВНО, и без этой строки проверка была пустой.
            // Оболочка открывается на обзоре, экран «Файлы» при этом не
            // создаётся вовсе, а StaticResource разрешается при создании
            // экрана. Проверено внесённым дефектом 05.09.2026: опечатка в
            // имени ресурса на шаге 3 не роняла ничего, потому что до шага 3
            // разметка не доходила.
            obolochka.PereytiCommand.Execute("files");
            stend2.Prokrutit();

            // Шаг 1: выбор. Поэлементная находка обязательна: разница «папка
            // целиком» и «файлы внутри» показывается только на ней.
            proshloe.Polozhit(new ScanResult(
                [
                    Nahodka("кэш обновлений", "Windows", 5_000_000_000),
                    Poelementnaya("временные файлы", 4_800_000_000, celey: 312),
                    Nahodka("офлайн-карты", "Приложения", 20_000_000_000, RiskTier.Risk),
                ],
                []));
            stend2.Prokrutit();

            // Шаг 2: подтверждение.
            fayly.KPodtverzhdeniyuCommand.Execute(null);
            stend2.Prokrutit();
            fayly.Step.Should().Be(FlowStep.Confirm);

            // Шаги 3 и 4: ход и итог. Оба разворачиваются одним нажатием,
            // потому что заглушка проходит список без задержек.
            fayly.Confirmed = true;
            await fayly.UdalitCommand.ExecuteAsync(null);
            stend2.Prokrutit();

            fayly.Step.Should().Be(FlowStep.Report);
            fayly.Report.Should().NotBeNull();
            stend2.Prokrutit();

            // Блок занятых обязан быть РАЗВЁРНУТ, а не просто существовать в
            // модели: разбор разметки идёт при построении, и опечатка в имени
            // ресурса внутри шаблона до этой строки никого не роняла.
            fayly.HasStuck.Should().BeTrue(
                "иначе шаблон занятых файлов снова не разворачивается ни разу");
            // Число не сверяется со счётчиком заглушки: продукт САМ просит
            // держателей закрыться и повторяет удаление, и повторы приходят в
            // ту же заглушку, то есть счётчик успевает вырасти.
            fayly.Stuck.Should().HaveCountGreaterThan(
                1, "шаблон обязан развернуться не одной строкой, а списком");
            fayly.Stuck.Should().OnlyContain(
                z => z.Holder != null, "в блоке живут только строки с держателем");
            stend2.Prokrutit();
        });
    }

    [Fact]
    public async Task Ekran_zhurnala_sobiraetsya_so_vsemi_chetyrmya_ishodami()
    {
        // Четыре исхода это четыре ветки триггеров в шаблоне строки. Ветка,
        // которую ни разу не построили, ломается молча: в разметке имя ресурса
        // на месте, а тип или ключ не тот.
        await stend.VypolnitAsync(async () =>
        {
            var zhurnal = new HistoryViewModel(new ZhurnalDlyaSborki());

            var obolochka = new ShellViewModel(
                isElevated: true, zhurnal: zhurnal);
            var okno = new ShellWindow { DataContext = obolochka };

            using var stend2 = Stend.Podnyat(okno);

            obolochka.PereytiCommand.Execute("history");
            stend2.Prokrutit();

            await zhurnal.ZagruzitAsync(CancellationToken.None);
            stend2.Prokrutit();

            zhurnal.Runs.Should().NotBeEmpty();
            zhurnal.Current.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task Ekran_reestra_sobiraetsya_na_nedostupnom_module_i_na_nahodkah()
    {
        // Два состояния одной разметки: пустое и настоящий список. Шаблон
        // строки разворачивается ТОЛЬКО во втором, то есть сломанный шаблон
        // на пустом экране виден не был бы вовсе.
        await stend.VypolnitAsync(async () =>
        {
            var reestr = new RegistryViewModel(
                new ReestrZaglushka(new RegistryScanResult([], [], false)),
                new OchistkaReestraZaglushka(),
                new BekapyZaglushka(),
                elevated: true);

            var obolochka = new ShellViewModel(
                isElevated: true, reestr: reestr);
            var okno = new ShellWindow { DataContext = obolochka };

            using var stend2 = Stend.Podnyat(okno);

            obolochka.PereytiCommand.Execute("registry");
            stend2.Prokrutit();

            await reestr.ProveritAsync(CancellationToken.None);
            stend2.Prokrutit();
            reestr.State.Phase.Should().Be(ScreenPhase.Empty);

            var priehal = new RegistryViewModel(
                new ReestrDlyaSborki(), new OchistkaReestraZaglushka(),
                new BekapyZaglushka(), elevated: true);
            var okno2 = new ShellWindow
            {
                DataContext = new ShellViewModel(
                    isElevated: true, reestr: priehal),
            };

            using var stend3 = Stend.Podnyat(okno2);
            ((ShellViewModel)okno2.DataContext!).PereytiCommand.Execute("registry");
            stend3.Prokrutit();

            await priehal.ProveritAsync(CancellationToken.None);
            stend3.Prokrutit();

            priehal.Rows.Should().NotBeEmpty();

            priehal.Dispose();
            reestr.Dispose();
        });
    }

    [Fact]
    public async Task Vse_chetyre_shaga_reestra_razvorachivayutsya_v_zhivom_okne()
    {
        // Шаблоны подтверждения, хода и итога строятся ТОЛЬКО когда поток до
        // них дошёл. Шаг, который ни разу не построили, ломается молча: имя
        // ресурса в разметке на месте, а тип или ключ не тот, и узнаётся это у
        // человека посреди безвозвратного удаления.
        await stend.VypolnitAsync(async () =>
        {
            var ochistka = new OchistkaReestraZaglushka();

            // Два файла РАЗНОГО вида: годный и негодный. У негодного своя
            // ветка шаблона, причина отказа и выключенная кнопка, и ветка,
            // которую ни разу не построили, ломается молча.
            var bekapy = new BekapyZaglushka(
                new RegistryBackupFile(
                    @"C:\bekapy\svezhiy.reg", "svezhiy.reg",
                    DateTimeOffset.UtcNow, 4096, true, null),
                new RegistryBackupFile(
                    @"C:\bekapy\bityy.reg", "bityy.reg",
                    DateTimeOffset.UtcNow, 12, false, "первая строка не заголовок экспорта"));

            using var reestr = new RegistryViewModel(
                new ReestrDlyaSborki(), ochistka, bekapy, elevated: true);

            var obolochka = new ShellViewModel(isElevated: true, reestr: reestr);
            var okno = new ShellWindow { DataContext = obolochka };

            using var stend2 = Stend.Podnyat(okno);

            obolochka.PereytiCommand.Execute("registry");
            stend2.Prokrutit();

            await reestr.ProveritAsync(CancellationToken.None);
            stend2.Prokrutit();
            reestr.Step.Should().Be(FlowStep.Selection);

            reestr.Rows[0].IsSelected = true;
            await reestr.KPodtverzhdeniyuAsync(CancellationToken.None);
            stend2.Prokrutit();
            reestr.Step.Should().Be(FlowStep.Confirm);

            // Согласие ставится руками: ворота прокрутки в поднятом стенде не
            // срабатывают, список помещается целиком и событие прокрутки не
            // приходит. Проверяется здесь РАЗМЕТКА, а не ворота, у ворот своя
            // проверка в RegistryViewModelTests.
            reestr.Confirmed = true;
            await reestr.UdalitAsync(CancellationToken.None);
            stend2.Prokrutit();
            reestr.Step.Should().Be(FlowStep.Report);
            reestr.Running.Should().NotBeEmpty("шаблон исхода обязан развернуться");

            reestr.ZavershitCommand.Execute(null);
            stend2.Prokrutit();
            reestr.Step.Should().Be(FlowStep.Selection);

            // Панель отката поверх шагов: её шаблон строится только когда её
            // открыли, то есть до сих пор не строился ни разу.
            await reestr.OtkryitBekapyCommand.ExecuteAsync(null);
            stend2.Prokrutit();

            reestr.BackupsOpen.Should().BeTrue();
            reestr.Backups.Should().HaveCount(2, "оба вида строки обязаны развернуться");

            reestr.ZakryitBekapyCommand.Execute(null);
            stend2.Prokrutit();
            reestr.BackupsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Ekran_nastroek_sobiraetsya_s_povysheniem_i_bez()
    {
        // Без повышения часть пунктов неактивна, и это отдельная ветка
        // раскладки: неактивный флажок берёт другую кисть из того же словаря.
        await stend.VypolnitAsync(async () =>
        {
            var nastroyki = new SettingsViewModel(new NastroykiDlyaSborki(), null);

            var obolochka = new ShellViewModel(
                isElevated: false, nastroyki: nastroyki);
            var okno = new ShellWindow { DataContext = obolochka };

            using var stend2 = Stend.Podnyat(okno);

            obolochka.PereytiCommand.Execute("settings");
            stend2.Prokrutit();

            await nastroyki.ZagruzitAsync(false, CancellationToken.None);
            stend2.Prokrutit();
            nastroyki.State.HasRestriction.Should().BeTrue();

            await nastroyki.ZagruzitAsync(true, CancellationToken.None);
            stend2.Prokrutit();
            nastroyki.State.HasRestriction.Should().BeFalse();
        });
    }

    [Fact]
    public void Vypadayushchiy_spisok_pokazyvaet_podpis_a_ne_imya_tipa()
    {
        // Найдено снимком живого окна 05.09.2026: закрытый список печатал
        // «VariantVybora { Podpis = Безвозвратно, Znachenie = Permanent }».
        // Свой шаблон брал SelectionBoxItem и НЕ брал SelectionBoxItemTemplate,
        // то есть шаблон строки до закрытого списка не доезжал. Разметка при
        // этом правильная, и ни одна проверка XML этого не видит.
        stend.Vypolnit(() =>
        {
            var spisok = new System.Windows.Controls.ComboBox
            {
                ItemsSource = new[]
                {
                    new VariantVybora<int>("Безвозвратно", 1),
                    new VariantVybora<int>("В корзину", 2),
                },
                ItemTemplate = (DataTemplate)Application.Current.Resources["VariantPodpis"],
                SelectedIndex = 0,
            };

            var okno = new Window { Content = spisok };
            using var stend2 = Stend.Podnyat(okno);
            stend2.Prokrutit();

            var teksty = Teksty(spisok).ToList();
            teksty.Should().Contain("Безвозвратно");

            teksty.Should().Contain("Безвозвратно");
            teksty.Should().NotContain(
                t => t.Contains("VariantVybora", StringComparison.Ordinal),
                "закрытый список показывает ToString вместо подписи");
        });
    }

    /// <summary>Весь текст, который дерево реально нарисовало.</summary>
    private static IEnumerable<string> Teksty(DependencyObject koren)
    {
        if (koren is System.Windows.Controls.TextBlock nadpis)
        {
            yield return nadpis.Text;
        }

        var skolko = System.Windows.Media.VisualTreeHelper.GetChildrenCount(koren);

        for (var i = 0; i < skolko; i++)
        {
            foreach (var stroka in Teksty(System.Windows.Media.VisualTreeHelper.GetChild(koren, i)))
            {
                yield return stroka;
            }
        }
    }

    /// <summary>Реестр с двумя записями. Так он будет выглядеть на этапе 5.</summary>
    /// <summary>
    /// Две находки РАЗНОГО рода: значение и ключ целиком. Шаблон строки
    /// показывает их по-разному, и ветка, которую ни разу не построили,
    /// ломается молча.
    /// </summary>
    private sealed class ReestrDlyaSborki : IRegistryScanService
    {
        public int BranchCount => 3;

        public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
            Task.FromResult(new RegistryScanResult(
                [
                    ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Ushedshee"),
                    ReestrObraztsy.Klyuch(RegistryHive.CurrentUser, "net.exe"),
                ],
                [],
                false));
    }

    /// <summary>Настройки в памяти: раскладке хватает, диску незачем.</summary>
    private sealed class NastroykiDlyaSborki : ISettingsService
    {
        public string FilePath => "C:\\net-takogo\\settings.json";

        public AppSettings Current { get; private set; } = new();

        public Task<AppSettings> LoadAsync(CancellationToken ct) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken ct)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>Журнал с одним прогоном, где есть все четыре исхода.</summary>
    private sealed class ZhurnalDlyaSborki : IHistoryService
    {
        public string Directory => "C:\\net-takogo";

        public Task<HistoryPage> ReadAsync(CancellationToken ct) =>
            Task.FromResult(new HistoryPage(
                [
                    new HistoryRun(
                        "C:\\zhurnal\\odin.jsonl",
                        new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
                        [
                            new DeleteOutcome("C:\\a", DeleteStatus.Deleted, 1000),
                            new DeleteOutcome("C:\\b", DeleteStatus.Skipped, 0, "занят", "chrome.exe"),
                            new DeleteOutcome("C:\\c", DeleteStatus.Failed, 0, "нет прав"),
                            new DeleteOutcome("C:\\d", DeleteStatus.Cancelled, 0, "остановлено"),
                        ]),
                ],
                []));
    }

    private static Finding Poelementnaya(string imya, long bayt, int celey) =>
        new(imya, $"C:\\Temp\\{imya}", bayt, RiskTier.Safe,
            "мусор программ", FindingSource.Rule, RuleId: "Тест", LastUsedDays: null,
            Scope: DeleteScope.SelectedEntries,
            Targets: [.. Enumerable.Range(0, celey).Select(i => $"C:\\Temp\\{imya}\\f{i}")]);

    private static Finding Nahodka(
        string imya, string kategoriya, long bayt, RiskTier stupen = RiskTier.Safe) =>
        new(imya, $"C:\\{kategoriya}\\{imya}", bayt, stupen,
            "пересоздастся при следующем запуске", FindingSource.Rule, RuleId: kategoriya);

    /// <summary>
    /// Скрытый корень отрисовки для содержимого окна.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Две попытки до этой были неверными, обе по делу.
    /// </para>
    /// <para>
    /// Первая обходилась Measure и Arrange, и проверка оказалась ПУСТОЙ:
    /// сломанный токен ширины колонки её не уронил. Дерево без источника
    /// отображения содержимое не разворачивает вовсе, то есть шаблоны, ради
    /// которых всё написано, не строятся.
    /// </para>
    /// <para>
    /// Вторая звала настоящий <c>Window.Show</c> за краем экрана. Так делать
    /// нельзя: набор идёт на РАБОЧЕЙ машине, и зависший прогон оставил на чужом
    /// столе окно, которое пришлось снимать руками. Проверено 05.09.2026,
    /// именно так и вышло.
    /// </para>
    /// <para>
    /// Здесь <c>HwndSource</c> без <c>WS_VISIBLE</c>: настоящий корень
    /// отрисовки WPF, полноценная раскладка и построение шаблонов, и при этом
    /// на экране не появляется ничего. Содержимое снимается с окна, а привязка
    /// данных переезжает на корневой элемент: за пределами проверки остаётся
    /// только рамка заголовка, а её сверяет снимок живого окна.
    /// </para>
    /// </remarks>
    private sealed class Stend : IDisposable
    {
        private readonly HwndSource _istochnik;
        private readonly FrameworkElement _koren;
        private readonly LovushkaPrivyazok _privyazki;

        private Stend(HwndSource istochnik, FrameworkElement koren, LovushkaPrivyazok privyazki)
        {
            _istochnik = istochnik;
            _koren = koren;
            _privyazki = privyazki;
        }

        public static Stend Podnyat(Window okno)
        {
            // Ловушка ставится ДО построения дерева: привязки разрешаются в
            // момент подключения элемента, и слушатель, включённый после,
            // услышал бы тишину.
            var privyazki = new LovushkaPrivyazok();

            var koren = (FrameworkElement)okno.Content;
            okno.Content = null;
            koren.DataContext = okno.DataContext;

            var parametry = new HwndSourceParameters("junk-manager-proverka")
            {
                Width = 1920,
                Height = 1080,

                // WS_POPUP без WS_VISIBLE: окно создаётся, но не показывается.
                WindowStyle = unchecked((int)0x80000000),
                PositionX = -32000,
                PositionY = -32000,
            };

            var istochnik = new HwndSource(parametry) { RootVisual = koren };
            var stend = new Stend(istochnik, koren, privyazki);
            stend.Prokrutit();
            return stend;
        }

        /// <summary>
        /// Прокручивает очередь до приоритета отрисовки. UpdateLayout сам по
        /// себе не доводит до построения шаблонов элементов, создаваемых
        /// отложенно.
        /// </summary>
        public void Prokrutit()
        {
            _koren.UpdateLayout();
            _koren.Dispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            _koren.Dispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>
        /// Закрывает окно и ТРЕБУЕТ, чтобы привязки молчали.
        /// </summary>
        /// <remarks>
        /// Привязка мимо свойства не ломает сборку и не бросает: она молча
        /// показывает пустоту, и узнаётся это у человека. Проверка стоит здесь,
        /// а не в каждом наборе: экран, который забыли обложить проверкой, тем
        /// более не станет проверять свои привязки руками. Заведено 06.09.2026,
        /// первый же прогон нашёл три настоящих дефекта.
        /// </remarks>
        public void Dispose()
        {
            _istochnik.Dispose();

            var oshibki = _privyazki.Oshibki.ToList();
            _privyazki.Dispose();

            if (oshibki.Count > 0)
            {
                throw new InvalidOperationException(
                    "разметка ругается на привязки, а такая привязка молча показывает пустоту:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, oshibki));
            }
        }
    }

    private sealed class SkanerDlyaSborki : IScanService
    {
        public int RulesCount => 38;

        public Task<ScanResult> ScanAsync(
            IProgress<ScanProgress>? progress, CancellationToken ct) =>
            Task.FromResult(ScanResult.Empty);
    }
}
