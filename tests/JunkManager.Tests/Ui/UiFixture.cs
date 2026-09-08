using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FluentAssertions;
using JunkManager.Tests.Seeded;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// One running application for the whole Ui suite, driven from one thread.
/// </summary>
/// <remarks>
/// <para>
/// ОДНО окно на весь набор, а не по окну на класс проверок. Первая попытка
/// давала приспособление каждому классу, и на рабочем столе оказалось ПЯТЬ
/// одинаковых окон Junk Manager сразу. Смешного мало: живой набор гоняется в
/// госте, но запускается он теми же руками, и цена ошибки это чужой рабочий
/// стол.
/// </para>
/// <para>
/// Все обращения к дереву автоматизации идут в ОДНОМ потоке STA. COM-объекты
/// UIA привязаны к своей квартире, а xUnit зовёт конструктор приспособления и
/// тела проверок из разных потоков пула: первая же попытка дала
/// «Unexpected HRESULT has been returned from a call to a COM component»
/// семнадцать раз подряд, то есть на КАЖДОЙ проверке, где дерево трогали.
/// </para>
/// <para>
/// Наружу отдаются не элементы, а ответы: имя, рамка, доступность, снимок.
/// Отдать элемент значило бы вернуть COM-объект в чужой поток и получить ту же
/// ошибку, только позже и в непонятном месте.
/// </para>
/// </remarks>
public sealed class UiFixture : IDisposable
{
    private readonly BlockingCollection<Action> _ochered = new();
    private readonly Thread _potok;
    private readonly FlaUI.Core.Application _prilozhenie;
    private readonly UIA3Automation _avtomatizaciya;
    private readonly Window _okno;
    private readonly JunkSeeder _posev;
    private bool _prohodBylo;

    /// <summary>
    /// Части шаблона полосы прокрутки. У них нулевой размер по устройству, а не
    /// по ошибке: кнопка «страница вверх» при бегунке наверху занимает ноль.
    /// </summary>
    private static readonly string[] ChastiProkrutki = ["PageUp", "PageDown", "PageLeft", "PageRight"];

    private static readonly ControlType[] Interaktivnye =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.ComboBox, ControlType.Edit,
    ];

    public UiFixture()
    {
        // Живой набор идёт ТОЛЬКО в госте, и это не пожелание. Он поднимает
        // окна, забирает передний план и жмёт клавиши: на рабочей машине это
        // чужой рабочий стол. Проверено собой 05.09.2026, когда пять
        // приспособлений подряд оставили пять окон Junk Manager на чужом
        // экране. Предохранитель тот же, что у разрушительных проверок: две
        // независимые метки гостя, одной мало.
        JunkManager.Safety.VmFuse.RequireArmed();

        using (var journal = JunkManager.Deletion.JsonlOperationLog.CreateForRun())
        {
            // Seed an actual journal entry; test order is a terrible database fixture.
            journal.RecordAsync(new JunkManager.Deletion.DeleteOutcome("UI navigation fixture",
                JunkManager.Deletion.DeleteStatus.Skipped, 0, "Подготовка проверки интерфейса; ничего не удалялось"),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        // A real settings file keeps the UI fixture honest. Miracles are not a test seam.
        PolozhitNastroyki();

        // Свой посев, а не надежда на то, что в свежем госте что-нибудь
        // накопилось. Пустой обзор превратил бы половину проверок в осмотр
        // заглушки, и прогон остался бы зелёным, ничего не проверив.
        _posev = JunkSeeder.Plant(
            SeedEntry.Load(Path.Combine(AppContext.BaseDirectory, "posev.json")));

        using var gotov = new ManualResetEventSlim(false);

        _potok = new Thread(() =>
        {
            gotov.Set();

            foreach (var deystvie in _ochered.GetConsumingEnumerable())
            {
                deystvie();
            }
        })
        {
            IsBackground = true,
            Name = "ui-stend",
        };

        _potok.SetApartmentState(ApartmentState.STA);
        _potok.Start();
        gotov.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("поток стенда не поднялся");

        (_prilozhenie, _avtomatizaciya, _okno) = Vypolnit(() =>
        {
            var exe = PutKPrilozheniyu();

            var zapushchennoe = FlaUI.Core.Application.Launch(
                new ProcessStartInfo(exe, "--no-elevate")
                {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                });

            var avtomat = new UIA3Automation();

            var okno = zapushchennoe.GetMainWindow(avtomat, TimeSpan.FromSeconds(60))
                ?? throw new InvalidOperationException("окно приложения не появилось за 60 секунд");

            return (zapushchennoe, avtomat, okno);
        });
    }

    /// <summary>Масштаб рабочего стола так, как его видит ОКНО ПРОДУКТА.</summary>
    /// <remarks>
    /// Спрашивается у окна, а не у процесса тестов и не у реестра. Реестр
    /// говорит, что попросили; процесс тестов может быть вовсе не осведомлён о
    /// масштабе и всегда ответит 96; окно отвечает тем, что получилось.
    /// </remarks>
    public int MasshtabProcentov => Vypolnit(() =>
        (int)Math.Round(GetDpiForWindow(_okno.Properties.NativeWindowHandle) / 96.0 * 100));

    public string ZagolovokOkna => Vypolnit(() => _okno.Title);

    public Rectangle RamkaOkna => Vypolnit(() => _okno.BoundingRectangle);

    /// <summary>Снимок окна для сверки пикселей с токенами.</summary>
    public Bitmap Snimok() => Vypolnit(() => _okno.Capture());

    public bool Est(string id) => Vypolnit(() => Iskat(id) is not null);

    public int Skolko(string id) => Vypolnit(() => Vse(id).Length);

    public string Imya(string id) => Vypolnit(() => Trebovat(id).Name);

    public IReadOnlyList<string> Imena(string id) =>
        Vypolnit(() => (IReadOnlyList<string>)[.. Vse(id).Select(e => e.Name)]);

    public bool Dostupna(string id) => Vypolnit(() => Trebovat(id).IsEnabled);

    public Rectangle Ramka(string id) => Vypolnit(() => Trebovat(id).BoundingRectangle);

    public void Nazhat(string id) => Vypolnit(() =>
    {
        Trebovat(id).AsButton().Invoke();
        return true;
    });

    /// <summary>Нажимает элемент из списка одинаковых, например строку категории.</summary>
    public void NazhatVSpiske(string id, int nomer) => Vypolnit(() =>
    {
        var vse = Vse(id);
        vse.Length.Should().BeGreaterThan(nomer, "элементов «{0}» меньше, чем нужно", id);
        vse[nomer].AsButton().Invoke();
        return true;
    });

    /// <summary>Ставит отметку у первого ДОСТУПНОГО флажка из списка одинаковых.</summary>
    /// <remarks>
    /// Не <see cref="NazhatVSpiske"/>: тот зовёт Invoke, а флажок WPF шаблон
    /// Invoke не поддерживает вовсе, и вызов падает
    /// PatternNotSupportedException. Найдено прогоном в госте 07.09.2026.
    ///
    /// Берётся первый доступный, а не нулевой: у записей ветки машины флажок
    /// выключен без прав администратора, а живой набор идёт заданием с
    /// ограниченными правами. Нулевая строка вполне может оказаться именно
    /// такой, и проверка падала бы на устройстве полигона, а не на продукте.
    ///
    /// Toggle это переключатель, а не «поставить», поэтому состояние сверяется
    /// после переключения.
    /// </remarks>
    public void OtmetitPervyy(string id) => Vypolnit(() =>
    {
        var vse = Vse(id);
        vse.Length.Should().BeGreaterThan(0, "флажков «{0}» нет вовсе", id);

        var flazhok = vse
            .Select(e => e.AsCheckBox())
            .FirstOrDefault(f => f.IsEnabled);

        flazhok.Should().NotBeNull(
            "все флажки «{0}» выключены, отмечать нечего: так бывает, когда все находки "
            + "лежат в ветке машины, а прав администратора нет", id);

        if (flazhok!.IsChecked != true)
        {
            flazhok.Toggle();
        }

        flazhok.IsChecked.Should().Be(true, "отметка обязана встать, иначе выбирать нечего");
        return true;
    });

    public void Fokus(string id) => Vypolnit(() =>
    {
        Trebovat(id).Focus();
        return true;
    });

    public void Vvesti(string id, string text) => Vypolnit(() =>
    {
        Trebovat(id).AsTextBox().Text = text;
        return true;
    });

    /// <summary>Листает до самого конца, а не до конца ПО ОЦЕНКЕ.</summary>
    /// <remarks>
    /// Один вызов SetScrollPercent(100) до конца не доводит. Список
    /// виртуализован, и высота содержимого это оценка по уже созданным строкам:
    /// как только прыжок вниз создаёт следующие, оценка растёт, и прежние сто
    /// процентов оказываются серединой. Человек в этом месте просто крутит
    /// дальше, а один вызов из теста останавливается.
    ///
    /// Отсюда мигающее падение проверки «кнопка удаления оживает только после
    /// конца списка»: 05.09.2026 она упала на масштабах 125 и 100 и прошла на
    /// 150 и 200, то есть зависела от того, сколько строк поместилось на экран.
    /// </remarks>
    public void ProkrutitVKonec(string id, int popytok = 12) => Vypolnit(() =>
    {
        var element = Trebovat(id);
        var prezhniy = -1.0;

        for (var i = 0; i < popytok; i++)
        {
            element.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
            Thread.Sleep(150);

            var teper = element.Patterns.Scroll.Pattern.VerticalScrollPercent.ValueOrDefault;

            // Сошлось: прыжок больше ничего не меняет, значит оценка высоты
            // перестала расти и мы правда внизу.
            if (Math.Abs(teper - prezhniy) < 0.5)
            {
                break;
            }

            prezhniy = teper;
        }

        return true;
    });

    /// <summary>Все непустые подписи внутри элемента.</summary>
    public IReadOnlyList<string> TekstyVnutri(string id) => Vypolnit(() =>
        (IReadOnlyList<string>)[.. Trebovat(id).FindAllDescendants()
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrEmpty(n))]);

    /// <summary>Интерактивные элементы без имени: то, что не прочитает экранный диктор.</summary>
    public IReadOnlyList<string> BezymyannyeInteraktivnye() => Vypolnit(() =>
        (IReadOnlyList<string>)[.. _okno.FindAllDescendants()
            .Where(e => Interaktivnye.Contains(e.ControlType))
            .Where(e => string.IsNullOrWhiteSpace(e.Name))
            .Select(e => $"{e.ControlType} id={e.AutomationId}")]);

    /// <summary>Именованные элементы нулевого размера.</summary>
    /// <summary>Именованные элементы, схлопнувшиеся в ноль.</summary>
    /// <remarks>
    /// Части полосы прокрутки исключены: кнопка «страница вверх» при бегунке
    /// наверху честно имеет нулевую высоту, это устройство полосы, а не
    /// раскладка. Проверено на масштабе 200 процентов 05.09.2026.
    /// </remarks>
    public IReadOnlyList<string> Shlopnutye() => Vypolnit(() =>
        (IReadOnlyList<string>)[.. _okno.FindAllDescendants()
            .Where(e => !string.IsNullOrEmpty(e.AutomationId))
            .Where(e => !ChastiProkrutki.Contains(e.AutomationId))
            // И то, что лежит ВНУТРИ частей полосы. Проверено на 200 процентах
            // 05.09.2026: схлопнутыми числились две пустые подписи с
            // «внутри=PageUp» и «внутри=PageDown». Кнопки страницы невидимы и
            // содержимого не имеют, поэтому подпись в их шаблоне пуста и
            // занимает ноль. Исключение по ПРЕДКУ, а не по имени «Label»:
            // x:Name="Label" стоит в трёх шаблонах темы, и запрет по имени
            // выключил бы проверку заодно для кнопок и полей ввода.
            .Where(e => !ChastiProkrutki.Contains(BlizhayshiyId(e)))
            .Where(e => e.BoundingRectangle.Width <= 0 || e.BoundingRectangle.Height <= 0)
            // Имя через ValueOrDefault, а не через e.Name: часть элементов
            // свойства Name не поддерживает вовсе, и обращение к нему бросает
            // PropertyNotSupportedException. Проверка тогда падает не находкой,
            // а собственным исключением, и разбирать нечего.
            //
            // Предок с идентификатором обязателен в сообщении: «схлопнулся Text
            // id=Label» не говорит НИЧЕГО, потому что x:Name="Label" стоит в
            // трёх шаблонах темы сразу, и по такому сообщению остаётся гадать.
            .Select(e => $"{e.ControlType} id={e.AutomationId} "
                + $"имя={e.Properties.Name.ValueOrDefault} внутри={BlizhayshiyId(e)}")]);

    /// <summary>Идентификатор ближайшего предка, у которого он есть.</summary>
    private string BlizhayshiyId(AutomationElement element)
    {
        var hodok = _avtomatizaciya.TreeWalkerFactory.GetControlViewWalker();

        for (var predok = hodok.GetParent(element); predok is not null;
             predok = hodok.GetParent(predok))
        {
            var id = predok.Properties.AutomationId.ValueOrDefault;
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }

            if (predok.Equals(_okno))
            {
                break;
            }
        }

        return "окно";
    }

    /// <summary>Именованные элементы, вылезшие за окно.</summary>
    /// <remarks>
    /// Не считается вылезшим то, что просто НЕ ДОЛИСТАНО. На масштабе 200
    /// процентов содержимое настроек выше окна, и кнопка «Сохранить» честно
    /// лежит ниже видимой части: она достижима прокруткой, а вылезшая подпись
    /// не достижима ничем. Различие делается по предку с прокруткой, а не по
    /// IsOffscreen: вылезший элемент тоже offscreen, и такая проверка стала бы
    /// пустой.
    /// </remarks>
    public IReadOnlyList<string> Vylezshie() => Vypolnit(() =>
    {
        var okno = _okno.BoundingRectangle;

        return (IReadOnlyList<string>)[.. _okno.FindAllDescendants()
            .Where(e => !string.IsNullOrEmpty(e.AutomationId))
            .Where(e => !e.BoundingRectangle.IsEmpty)
            .Where(e => !okno.Contains(e.BoundingRectangle))
            .Where(e => !VnutriProkrutki(e))
            .Select(e => $"{e.AutomationId} {e.BoundingRectangle}")];
    });

    /// <summary>Лежит ли элемент внутри области, которую можно прокрутить.</summary>
    private bool VnutriProkrutki(AutomationElement element)
    {
        var hodok = _avtomatizaciya.TreeWalkerFactory.GetControlViewWalker();

        for (var predok = hodok.GetParent(element); predok is not null;
             predok = hodok.GetParent(predok))
        {
            if (predok.Patterns.Scroll.IsSupported)
            {
                return true;
            }

            if (predok.Equals(_okno))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Обход по Tab. Возвращает идентификаторы в порядке появления, до первого
    /// повтора или до предела шагов.
    /// </summary>
    /// <remarks>
    /// Предел обязателен: круг без предела на сломанном порядке фокуса это
    /// зависший прогон, а зависший прогон неотличим от долгого.
    /// </remarks>
    public IReadOnlyList<string> ObhodTab(int shagov = 200) => Vypolnit(() =>
    {
        // Передний план, а не только фокус UIA. Проверено прогоном 05.09.2026:
        // окно шире экрана, клик по кнопке ушёл в панель задач, открылось меню
        // «Пуск», и обход собрал ЕГО элементы: tile-W~MSEdge, SearchTextBox,
        // ShowAllAppsButton. Проверка упала по делу, но сообщением не про то.
        _okno.SetForeground();
        _okno.Focus();
        Thread.Sleep(200);

        var poryadok = new List<string>();
        var vidennye = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < shagov; i++)
        {
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
            Thread.Sleep(40);

            var vFokuse = _avtomatizaciya.FocusedElement();

            // Ворота против тихого сбора чужого окна: собранный список из чужих
            // элементов выглядит как список, и падение по нему уводит искать
            // дефект там, где его нет.
            vFokuse.Properties.ProcessId.ValueOrDefault.Should().Be(
                _prilozhenie.ProcessId,
                "фокус ушёл из окна продукта на «{0}», обход собрал бы чужое дерево",
                vFokuse.Name);

            var id = vFokuse.AutomationId;

            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (!vidennye.Add(id))
            {
                break;
            }

            poryadok.Add(id);
        }

        return (IReadOnlyList<string>)poryadok;
    });

    public void Perejti(string razdel)
    {
        Nazhat($"rail-{razdel}");
        Thread.Sleep(400);
    }

    /// <summary>
    /// Один проход на всё приспособление. Экраны «Обзор» и «Файлы» без него
    /// пусты, и половина проверок смотрела бы на заглушку.
    /// </summary>
    public void ObespechitProhod(TimeSpan? srok = null)
    {
        if (_prohodBylo)
        {
            return;
        }

        Perejti("overview");

        if (Est("empty-action"))
        {
            Nazhat("empty-action");
        }

        Podozhdat(() => Est("overview-goto-files"), srok ?? TimeSpan.FromMinutes(8))
            .Should().BeTrue("проход не закончился: без него проверять нечего");

        _prohodBylo = true;
    }

    /// <summary>Ждёт условия, опрашивая его. Возвращает, дождался ли.</summary>
    public static bool Podozhdat(Func<bool> uslovie, TimeSpan srok)
    {
        ArgumentNullException.ThrowIfNull(uslovie);

        var kraynee = DateTime.UtcNow + srok;

        while (DateTime.UtcNow < kraynee)
        {
            if (uslovie())
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return uslovie();
    }

    private AutomationElement? Iskat(string id) =>
        _okno.FindFirstDescendant(f => f.ByAutomationId(id));

    private AutomationElement Trebovat(string id)
    {
        var element = Iskat(id);
        element.Should().NotBeNull("элемента с идентификатором «{0}» в окне нет", id);
        return element!;
    }

    private AutomationElement[] Vse(string id) =>
        _okno.FindAllDescendants(f => f.ByAutomationId(id));

    /// <summary>Выполняет работу в потоке стенда и приносит результат сюда.</summary>
    private T Vypolnit<T>(Func<T> chto)
    {
        T itog = default!;
        Exception? beda = null;

        using var gotovo = new ManualResetEventSlim(false);

        _ochered.Add(() =>
        {
            try
            {
                itog = chto();
            }
#pragma warning disable CA1031 // Ловим ВСЁ намеренно: иначе исключение
            // остаётся в потоке стенда, поток умирает молча, а вызывающий ждёт
            // ответа десять минут и падает по времени вместо настоящей причины.
            // Ниже оно переброшено целиком, вместе со стеком.
            catch (Exception e)
#pragma warning restore CA1031
            {
                beda = e;
            }
            finally
            {
                gotovo.Set();
            }
        });

        gotovo.Wait(TimeSpan.FromMinutes(10)).Should().BeTrue("поток стенда не ответил за 10 минут");

        if (beda is not null)
        {
            // Стек сохраняется целиком: без этого падение теста показывает
            // строку очереди, а не строку, где на самом деле сломалось.
            ExceptionDispatchInfo.Capture(beda).Throw();
        }

        return itog;
    }

    /// <summary>
    /// Приложение лежит в подкаталоге <c>app</c> рядом с набором тестов.
    /// </summary>
    /// <remarks>
    /// Отсутствие файла это ЯВНОЕ падение с путём, а не тихий пропуск: живой
    /// набор, не нашедший приложения, неотличим от живого набора, который всё
    /// проверил и ничего не нашёл.
    /// </remarks>
    /// <summary>
    /// Кладёт настройки тестового гостя рядом с журналом.
    /// </summary>
    /// <remarks>
    /// Пишется тот же путь и тот же формат, что читает продукт. Своя копия
    /// значений в обход файла проверяла бы окно, которое человеку не достанется.
    /// </remarks>
    private static void PolozhitNastroyki()
    {
        var katalog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JunkManager");

        Directory.CreateDirectory(katalog);

        File.WriteAllText(
            Path.Combine(katalog, "settings.json"),
            """
            {
              "ElevateOnStart": false,
              "Mode": "Permanent",
              "RestorePointBeforeRegistry": true,
              "BackupRegistryBeforeCleanup": true,
              "DetectorsEnabled": true,
              "LeftoverSearch": true,
              "HistoryRetentionDays": 90
            }
            """);
    }

    private static string PutKPrilozheniyu()
    {
        var put = Path.Combine(AppContext.BaseDirectory, "app", "JunkManager.exe");

        File.Exists(put).Should().BeTrue(
            "рядом с набором нет {0}: цель PolozhitPrilozhenieRyadom не отработала", put);

        return put;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void Dispose()
    {
        // Окно закрывается ОБЯЗАТЕЛЬНО, даже если прогон развалился. Оставленное
        // окно продукта на чужом рабочем столе это ровно тот мусор, ради борьбы
        // с которым всё написано.
        try
        {
            Vypolnit(() =>
            {
                _prilozhenie.Close();

                if (!_prilozhenie.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(3)))
                {
                    _prilozhenie.Kill();
                }

                _avtomatizaciya.Dispose();
                _prilozhenie.Dispose();
                return true;
            });
        }
        catch (Exception oshibka) when (
            oshibka is InvalidOperationException or ArgumentException
            || oshibka.InnerException is ArgumentException)
        {
            // Процесс уже кончился сам. Закрывать нечего.
            //
            // Ловим и ArgumentException: FlaUI ищет процесс по номеру и на
            // закончившемся отвечает «Process with an Id of N is not running»,
            // завёрнутым в свой Exception. Прогон 05.09.2026 был ЗЕЛЁНЫМ, а
            // раннер вернул единицу из-за уборки, и по коду выхода это
            // выглядело как падение проверок.
        }
        finally
        {
            _posev.Dispose();
            _ochered.CompleteAdding();
            _potok.Join(TimeSpan.FromSeconds(5));
            _ochered.Dispose();
        }
    }
}

/// <summary>
/// Все живые проверки идут в одном наборе и НЕ параллельно: окно одно, и два
/// теста, нажимающие кнопки одновременно, проверяют не то, что написано.
/// </summary>
[CollectionDefinition(Imya, DisableParallelization = true)]
public sealed class UiNabor : ICollectionFixture<UiFixture>
{
    public const string Imya = "ui-zhivoy";
}
