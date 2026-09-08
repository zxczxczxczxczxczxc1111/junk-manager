using FluentAssertions;
using JunkManager.App.ViewModels;
using JunkManager.Core;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class OverviewViewModelTests
{
    [Fact]
    public void Welcome_brand_is_only_for_the_initial_screen()
    {
        using var model = new OverviewViewModel(new FalshivyySkaner(ScanResult.Empty));
        model.PokazatNachalo();
        model.State.IsWelcome.Should().BeTrue();
        model.State.EmptyCommand.Should().BeSameAs(model.SkanirovatCommand);
        model.Prinyat(ScanResult.Empty);
        model.State.IsWelcome.Should().BeFalse();
    }
    [Fact]
    public void Propushchennye_puti_ne_vydayutsya_za_polnostyu_chistyy_disk()
    {
        // An unreadable directory has not confessed to being empty.
        using var model = new OverviewViewModel(new FalshivyySkaner(ScanResult.Empty));
        model.Prinyat(new ScanResult([], [new SkippedItem("C:\\closed", "нет доступа")]));
        model.State.EmptyTitle.Should().Contain("пропусками");
        model.State.EmptyBody.Should().Contain("1");
        model.Prinyat(new ScanResult([Nahodka("cache", "apps", 1)], [new SkippedItem("C:\\closed", "нет доступа")]));
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.State.RestrictionText.Should().Contain("Пропуски: 1");
    }

    [Fact]
    public async Task Otmena_dostupna_vo_vremya_zagruzki()
    {
        using var model = new OverviewViewModel(new WaitingScanner());
        var running = model.SkanirovatCommand.ExecuteAsync(null);
        model.State.Phase.Should().Be(ScreenPhase.Loading);
        model.State.CancelCommand.Should().NotBeNull();
        model.State.CancelCommand!.Execute(null);
        await running;
        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyTitle.Should().Contain("остановлено");
    }

    private sealed class WaitingScanner : JunkManager.App.Services.IScanService
    {
        public int RulesCount => 1;
        public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ScanResult.Empty;
        }
    }

    [Fact]
    public async Task Stopped_scan_can_be_started_again_and_finish_normally()
    {
        var scanner = new RestartScanner();
        using var model = new OverviewViewModel(scanner);
        var first = model.SkanirovatCommand.ExecuteAsync(null);
        await model.OtmenitCommand.ExecuteAsync(null);
        await first;
        model.State.EmptyTitle.Should().Contain("остановлено");
        model.SkanirovatCommand.CanExecute(null).Should().BeTrue();
        await model.SkanirovatCommand.ExecuteAsync(null);
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.FoundBytes.Should().Be(123);
        model.State.HasRestriction.Should().BeFalse();
    }

    private sealed class RestartScanner : JunkManager.App.Services.IScanService
    {
        private int _runs;
        public int RulesCount => 1;
        public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            if (++_runs == 1) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ScanResult([Nahodka("cache", "apps", 123)], []);
        }
    }

    private static Finding Nahodka(
        string imya, string kategoriya, long bayt, RiskTier stupen = RiskTier.Safe) =>
        new(imya, $"C:\\{kategoriya}\\{imya}", bayt, stupen,
            "пересоздастся при следующем запуске", FindingSource.Rule, RuleId: kategoriya);

    [Fact]
    public void Kategorii_sortiruyutsya_po_ubyvaniyu_razmera()
    {
        var nahodki = new[]
        {
            Nahodka("малое", "Игры", 1_000_000_000),
            Nahodka("большое", "Браузеры", 9_000_000_000),
            Nahodka("среднее", "GPU", 4_000_000_000),
        };

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii.Select(k => k.Name).Should().Equal("Браузеры", "GPU", "Игры");
    }

    [Fact]
    public void Melkie_kategorii_shlopyvayutsya_v_prochee()
    {
        // Двадцать категорий по одной десятой процента это двадцать строк,
        // которые вместе не дотягивают до одной заметной. Список категорий
        // существует, чтобы выбирать, а не чтобы прокручивать.
        var nahodki = new List<Finding>
        {
            Nahodka("толстая", "Браузеры", 100_000_000_000),
        };

        for (var i = 0; i < 20; i++)
        {
            nahodki.Add(Nahodka($"мелочь{i}", $"Хвост{i}", 50_000_000));
        }

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii.Should().HaveCount(2);
        kategorii[0].Name.Should().Be("Браузеры");
        kategorii[1].Name.Should().Be("Прочее");
        kategorii[1].Count.Should().Be(20);
        kategorii[1].TotalBytes.Should().Be(20 * 50_000_000L);
    }

    [Fact]
    public void Prochee_tozhe_sortiruetsya_po_razmeru()
    {
        // The miscellaneous drawer also weighs something. Physics refuses a UI exemption.
        var nahodki = new List<Finding> { Nahodka("одна", "Игры", 3_000_000_000) };

        for (var i = 0; i < 30; i++)
        {
            nahodki.Add(Nahodka($"мелочь{i}", $"Хвост{i}", 400_000_000));
        }

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii[0].Name.Should().Be("Прочее");
        kategorii.Select(category => category.TotalBytes).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Bolshe_maksimuma_strok_ne_pokazyvaetsya_nikogda()
    {
        var nahodki = Enumerable.Range(0, 40)
            .Select(i => Nahodka($"крупное{i}", $"Категория{i}", 5_000_000_000 - (i * 10_000_000)))
            .ToList();

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii.Should().HaveCount(12, "одиннадцать настоящих плюс «Прочее»");
        kategorii[0].Name.Should().Be("Прочее");
        kategorii.Select(category => category.TotalBytes).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Prochee_ne_zavoditsya_radi_odnoy_kategorii()
    {
        // Схлопнуть одну мелкую категорию в «Прочее» значит переименовать её,
        // спрятав имя и не выиграв ни строки.
        var nahodki = new[]
        {
            Nahodka("толстая", "Браузеры", 100_000_000_000),
            Nahodka("тонкая", "Игры", 50_000_000),
        };

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii.Select(k => k.Name).Should().Equal("Браузеры", "Игры");
    }

    [Fact]
    public void Stupeney_riska_rovno_dve_i_summy_shodyatsya()
    {
        var nahodki = new[]
        {
            Nahodka("кэш", "Браузеры", 5_000_000_000),
            Nahodka("офлайн", "Приложения", 20_000_000_000, RiskTier.Risk),
            Nahodka("шейдеры", "GPU", 7_000_000_000),
        };

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        var vsego = kategorii.Sum(k => k.TotalBytes);
        var opasno = kategorii.Sum(k => k.RiskBytes);

        vsego.Should().Be(32_000_000_000);
        opasno.Should().Be(20_000_000_000);

        // Третьей ступени нет и не будет. Проверка стоит здесь, потому что
        // именно тут её захотят завести первой: «а вот эти вроде и не опасные,
        // но и не совсем безопасные».
        Enum.GetValues<RiskTier>().Should().HaveCount(2);
    }

    [Fact]
    public void Dolya_riska_u_kategorii_bez_riska_eto_nol_a_ne_delenie_na_nol()
    {
        var kategorii = CategoryViewModel.Sgruppirovat(
            [Nahodka("кэш", "Браузеры", 5_000_000_000)], porogDoli: 0.015, maksimumStrok: 12);

        kategorii[0].RiskShare.Should().Be(0);
    }

    [Fact]
    public void Pustoy_rezultat_daet_pustoe_sostoyanie_a_ne_pustoy_spisok()
    {
        using var model = new OverviewViewModel(new FalshivyySkaner(ScanResult.Empty));

        model.Prinyat(ScanResult.Empty);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.Categories.Should().BeEmpty();
    }

    [Fact]
    public void Otmenennoe_skanirovanie_ne_vydayotsya_za_chistuyu_mashinu()
    {
        // Отменённый проход возвращает меньше находок и выглядит ровно как
        // чистый диск. Это единственный неправильный ответ, который никто
        // не пойдёт проверять.
        var chastichnyy = new ScanResult(
            [Nahodka("кэш", "Браузеры", 5_000_000_000)], [], Cancelled: true);

        using var model = new OverviewViewModel(new FalshivyySkaner(chastichnyy));
        model.Prinyat(chastichnyy);

        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.State.HasRestriction.Should().BeTrue();
        model.State.RestrictionText.Should().Contain("прервано");
    }

    [Fact]
    public void Otmenennyy_prohod_bez_nahodok_ne_pishetsya_v_chistye()
    {
        // Хуже прерванного прохода с находками только прерванный проход без
        // них: категорий ноль, и экран словом «Чисто» сообщает ровно
        // противоположное тому, что произошло. Проверка соседа выше сюда не
        // доставала: там была одна находка, то есть ветка пустого экрана
        // просто не выполнялась.
        var pusto = new ScanResult([], [], Cancelled: true);

        using var model = new OverviewViewModel(new FalshivyySkaner(pusto));
        model.Prinyat(pusto);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyTitle.Should().Be("Сканирование прервано");
        model.State.EmptyTitle.Should().NotContain("Чисто");
        model.State.EmptyAction.Should().Be("Сканировать заново");
    }

    [Fact]
    public void Bezopasnoe_otmecheno_zaranee_a_opasnoe_net()
    {
        // Галка решает, что уйдёт по кнопке «Очистить», нажатой не глядя.
        // Опасное, отмеченное заранее, удалится по умолчанию, и мешать этому
        // будет только чужая внимательность. Про IsSelected не спрашивал ни
        // один тест до этого, что мутация и показала.
        var kategorii = CategoryViewModel.Sgruppirovat(
            [
                Nahodka("кэш", "Браузеры", 5_000_000_000),
                Nahodka("офлайн", "Браузеры", 20_000_000_000, RiskTier.Risk),
            ],
            porogDoli: 0.015,
            maksimumStrok: 12);

        var stroki = kategorii.Single().Findings
            .ToDictionary(f => f.Name, StringComparer.Ordinal);

        stroki["кэш"].IsSelected.Should().BeTrue("безопасное отмечается сразу");
        stroki["офлайн"].IsSelected.Should().BeFalse(
            "опасное отмечает человек руками, а не список за него");
    }

    [Fact]
    public void Dvuh_odinakovyh_strok_v_spiske_ne_byvaet()
    {
        // Найдено снимком живого окна 05.09.2026. Находки без правила
        // складывались в «Прочее», и остаток мелких категорий тоже: на экране
        // ДВЕ строки «Прочее», причём первая была самой крупной категорией и
        // стояла наверху, хотя остаток обязан быть последним.
        //
        // Находка без правила это не «непонятно что»: у неё есть источник, и
        // источник это и есть её категория.
        var nahodki = new[]
        {
            Bez_pravila("Корзина", FindingSource.VolumeCache, 5_000_000_000),
            Bez_pravila("Следы libreoffice", FindingSource.Detector, 4_000_000_000),
            Bez_pravila("Удалённая программа", FindingSource.Program, 3_000_000_000),
            Nahodka("толстая", "Браузеры", 9_000_000_000),
            Nahodka("мелочь1", "Хвост1", 20_000_000),
            Nahodka("мелочь2", "Хвост2", 20_000_000),
        };

        var kategorii = CategoryViewModel.Sgruppirovat(nahodki, porogDoli: 0.015, maksimumStrok: 12);

        kategorii.Select(k => k.Name).Should().OnlyHaveUniqueItems(
            "две строки с одним именем это два разных набора, между которыми "
            + "человеку нечем выбрать");

        kategorii.Select(k => k.Name).Should().Contain(
            ["Хранилище Windows", "Следы программ", "Программы"],
            "у находки без правила есть источник, и он называется словами");

        kategorii[^1].Name.Should().Be(CategoryViewModel.OstatokName,
            "остаток по-прежнему последний");
    }

    private static Finding Bez_pravila(string imya, FindingSource istochnik, long bayt) =>
        new(imya, $"C:\\{imya}", bayt, RiskTier.Safe,
            "пересоздастся при следующем запуске", istochnik);

    [Theory]
    [InlineData("начало")]
    [InlineData("чисто")]
    [InlineData("прервано")]
    public void U_kazhdoy_knopki_pustogo_sostoyaniya_est_komanda(string put)
    {
        // Подпись и команда ставятся одним вызовом ровно затем, чтобы не
        // разъехаться, но необязательный параметр позволяет забыть второй. Тогда
        // на экране кнопка «Сканировать диск», которая на нажатие не делает
        // ничего, и выглядит это как зависшее приложение.
        using var model = new OverviewViewModel(new FalshivyySkaner(ScanResult.Empty));

        switch (put)
        {
            case "начало":
                model.PokazatNachalo();
                break;
            case "чисто":
                model.Prinyat(ScanResult.Empty);
                break;
            default:
                model.Prinyat(new ScanResult([], [], Cancelled: true));
                break;
        }

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyAction.Should().NotBeNullOrEmpty();
        model.State.EmptyCommand.Should().NotBeNull(
            "кнопка с подписью и без команды это кнопка, которая ничего не делает");
    }

    [Fact]
    public async Task Pokaz_razdela_perechityvaet_zanyatost_toma_no_ne_skaniruet()
    {
        // Очистка освобождает гигабайты на соседнем экране. Человек приходит на
        // «Обзор» посмотреть, сколько стало, и до 06.09.2026 видел, сколько
        // было: полоса занятости держала снимок последнего сканирования.
        //
        // Зовётся ИМЕННО через IScreenViewModel: договор объявлен методом
        // интерфейса со значением по умолчанию, и ошибка в сигнатуре тихо
        // уводит вызов в пустую заглушку при зелёной сборке.
        var skaner = new FalshivyySkaner(ScanResult.Empty);
        using var model = new OverviewViewModel(skaner);

        IScreenViewModel ekran = model;

        // Заведомо неверные числа: если показ их не перечитает, они и останутся.
        model.DiskTotalBytes = 1;
        model.DiskUsedBytes = 1;

        await ekran.PriPokazeAsync(TestContext.Current.CancellationToken);

        model.DiskTotalBytes.Should().BeGreaterThan(
            1_000_000_000, "системный том заведомо больше гигабайта");
        model.DiskUsedBytes.Should().BeGreaterThan(1);
        model.DiskUsedBytes.Should().BeLessThanOrEqualTo(model.DiskTotalBytes);

        skaner.Prohodov.Should().Be(
            0,
            "проход по диску идёт минуты, и переключение раздела это не заявка "
            + "на работу: экран, запускающий сканирование от взгляда на него, "
            + "наказывает человека за то, что он посмотрел в сторону");
    }

    private sealed class FalshivyySkaner(ScanResult chto) : JunkManager.App.Services.IScanService
    {
        public int RulesCount => 22;

        /// <summary>Сколько раз запускали проход. Ноль это тоже ответ.</summary>
        public int Prohodov { get; private set; }

        public Task<ScanResult> ScanAsync(
            IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            Prohodov++;
            return Task.FromResult(chto);
        }
    }
}
