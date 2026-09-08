using System.Globalization;
using System.Windows;
using FluentAssertions;
using JunkManager.App.Converters;
using JunkManager.App.ViewModels;
using Xunit;

namespace JunkManager.Tests.Ui;

/// <summary>
/// У преобразователей есть ветвление, а ветвление в разметке не видно вовсе:
/// привязка с неправильным параметром не ломает сборку и не пишет в вывод, она
/// просто рисует пустую строку. Плана этого файла не было, заведён по этой
/// причине.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class ConverterTests
{
    private static readonly CultureInfo Kultura = CultureInfo.InvariantCulture;

    [Fact]
    public void Razmer_prevrashchaetsya_v_podpis_iz_maketa()
    {
        new ByteSizeConverter()
            .Convert(1_572_864L, typeof(string), null, Kultura)
            .Should().Be("1,5 МБ");
    }

    [Fact]
    public void Razmer_ne_toy_kategorii_daet_pustuyu_stroku_a_ne_padenie()
    {
        // Привязка к ещё не заполненному свойству приходит как null. Падение
        // здесь уронило бы разбор шаблона целиком, а не одну ячейку.
        new ByteSizeConverter()
            .Convert(null, typeof(string), null, Kultura)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void Podpis_soglasuet_slovo_s_chislom()
    {
        new PluralConverter()
            .Convert(3L, typeof(string), "след|следа|следов", Kultura)
            .Should().Be("3 следа");
    }

    [Fact]
    public void Dve_formy_vmesto_treh_eto_padenie_a_ne_molchanie()
    {
        // Две формы дают «5 следа» на каждом числе, которому нужна третья.
        // Ровно тот дефект, ради которого преобразователь и написан, поэтому
        // он обязан быть шумным.
        var act = () => new PluralConverter()
            .Convert(5L, typeof(string), "след|следа", Kultura);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Vidimost_skryvaet_svorachivaniem_a_ne_pryatkoy()
    {
        // Hidden оставляет место, и плашка ограничения на машине, где прав
        // хватило, оставит полосу пустого экрана над каждым списком.
        var p = new BoolToVisibilityConverter();

        p.Convert(true, typeof(Visibility), null, Kultura).Should().Be(Visibility.Visible);
        p.Convert(false, typeof(Visibility), null, Kultura).Should().Be(Visibility.Collapsed);
        p.Convert(null, typeof(Visibility), null, Kultura).Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Podpis_deystviya_sama_reshaet_pokazyvat_li_knopku()
    {
        // В пустом состоянии кнопка привязана к строке EmptyAction, а не к
        // отдельному признаку: два поля, которые обязаны совпадать, рано или
        // поздно расходятся. Наивная проверка «value is true» дала бы Collapsed
        // на любой строке, то есть кнопки в пустом состоянии не было бы никогда.
        var p = new BoolToVisibilityConverter();

        p.Convert("Сканировать диск", typeof(Visibility), null, Kultura)
            .Should().Be(Visibility.Visible);
        p.Convert(string.Empty, typeof(Visibility), null, Kultura)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Nol_eto_nechego_pokazyvat()
    {
        // Счётчик ноль обязан прятать строку. Числа попадали в общую ветку
        // «что-то есть», и под каждым чистым прогоном журнала висело бы
        // «0 строк без удаления».
        var p = new BoolToVisibilityConverter();

        p.Convert(0, typeof(Visibility), null, Kultura).Should().Be(Visibility.Collapsed);
        p.Convert(3, typeof(Visibility), null, Kultura).Should().Be(Visibility.Visible);
        p.Convert(0L, typeof(Visibility), null, Kultura).Should().Be(Visibility.Collapsed);
        p.Convert(7L, typeof(Visibility), null, Kultura).Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Perevernutaya_vidimost_menyaet_otvet_a_ne_klass()
    {
        var p = new BoolToVisibilityConverter { Invert = true };

        p.Convert(true, typeof(Visibility), null, Kultura).Should().Be(Visibility.Collapsed);
        p.Convert(false, typeof(Visibility), null, Kultura).Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Sravnenie_perechisleniya_idet_po_imeni()
    {
        var p = new EnumEqualsConverter();

        p.Convert(ScreenPhase.Error, typeof(bool), "Error", Kultura).Should().Be(true);
        p.Convert(ScreenPhase.Error, typeof(bool), "Ready", Kultura).Should().Be(false);
        p.Convert(null, typeof(bool), "Ready", Kultura).Should().Be(false);
    }

    [Fact]
    public void Sravnenie_perechisleniya_otvechaet_vidimostyu_kogda_sprosili_vidimost()
    {
        // WPF не приводит bool к Visibility сам. Привязка видимости к
        // преобразователю, отдающему bool, молча оставляет элемент видимым, и
        // на экране «Файлы» все четыре шага потока оказываются на нём разом.
        var p = new EnumEqualsConverter();

        p.Convert(FlowStep.Confirm, typeof(Visibility), "Confirm", Kultura)
            .Should().Be(Visibility.Visible);
        p.Convert(FlowStep.Confirm, typeof(Visibility), "Running", Kultura)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Neizvestnaya_dolya_delaet_polosu_neopredelennoy()
    {
        var p = new NullToBoolConverter();

        p.Convert(null, typeof(bool), null, Kultura).Should().Be(true);
        p.Convert(0.0, typeof(bool), null, Kultura).Should().Be(false);
    }

    [Fact]
    public void Ni_odin_preobrazovatel_ne_pishet_obratno()
    {
        // Обратное преобразование это молчаливая запись в модель из разметки.
        // Все пять читают, и попытка написать обязана быть слышной.
        var act = new Action[]
        {
            () => new ByteSizeConverter().ConvertBack(null, typeof(long), null, Kultura),
            () => new PluralConverter().ConvertBack(null, typeof(long), null, Kultura),
            () => new BoolToVisibilityConverter().ConvertBack(null, typeof(bool), null, Kultura),
            () => new EnumEqualsConverter().ConvertBack(null, typeof(Enum), null, Kultura),
            () => new NullToBoolConverter().ConvertBack(null, typeof(object), null, Kultura),
        };

        foreach (var vyzov in act)
        {
            vyzov.Should().Throw<NotSupportedException>();
        }
    }

    [Fact]
    public void Sklonenie_ponimaet_int_a_ne_tolko_long()
    {
        // Count у категории это int, и всякий счётчик в WPF приходит int-ом.
        // Проверка «value is not long» на нём не срабатывает, и строка молча
        // становится пустой: на экране «Браузеры» без числа находок и ни одной
        // ошибки в выводе.
        var preobrazovatel = new PluralConverter();

        preobrazovatel.Convert(5, typeof(string), "находка|находки|находок", Kultura)
            .Should().Be("5 находок");
        preobrazovatel.Convert(1, typeof(string), "находка|находки|находок", Kultura)
            .Should().Be("1 находка");
    }

    [Fact]
    public void Razmer_ponimaet_int_a_ne_tolko_long()
    {
        new ByteSizeConverter().Convert(2048, typeof(string), null, Kultura)
            .Should().NotBe(string.Empty);
    }

    [Fact]
    public void Dolya_v_shirinu_beret_dolyu_ot_shiriny()
    {
        var preobrazovatel = new ShareToWidthConverter();

        preobrazovatel.Convert([0.25, 400d], typeof(double), null, Kultura).Should().Be(100d);
    }

    [Fact]
    public void Dolya_v_shirinu_ne_vylezaet_za_dorozhku()
    {
        // Доля больше единицы приходит от расхождения источников, и полоса не
        // то место, где расхождение показывают куском поверх соседнего.
        var preobrazovatel = new ShareToWidthConverter();

        preobrazovatel.Convert([1.4, 400d], typeof(double), null, Kultura).Should().Be(400d);
        preobrazovatel.Convert([-0.2, 400d], typeof(double), null, Kultura).Should().Be(0d);
    }

    [Fact]
    public void Dolya_v_shirinu_na_nerazmechennoy_dorozhke_daet_nol()
    {
        // Первый проход раскладки отдаёт ActualWidth равным NaN, и умножение на
        // него нарисовало бы полосу шириной NaN, то есть исключение в измерении.
        var preobrazovatel = new ShareToWidthConverter();

        preobrazovatel.Convert([0.5, double.NaN], typeof(double), null, Kultura).Should().Be(0d);
        preobrazovatel.Convert([DependencyProperty.UnsetValue, 400d], typeof(double), null, Kultura)
            .Should().Be(0d);
    }
}
