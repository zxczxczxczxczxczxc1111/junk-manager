using FluentAssertions;
using JunkManager.App.Controls;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Арифметика полосы тома, без окна.
/// </summary>
/// <remarks>
/// Отступление от плана: у плана раскладка долей и раскладка пикселей лежали
/// внутри OnRender, то есть проверялись только глазами по снимку. Обе величины
/// это арифметика, и обе врут тихо: доля, показывающая диск полнее, чем он
/// есть, и сегмент, округлённый до нуля пикселей, выглядят на снимке
/// правдоподобно.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class DiskStripTests
{
    [Fact]
    public void Naydennoe_eto_chast_zanyatogo_a_ne_dobavka_k_nemu()
    {
        // 100 всего, 60 занято, из них 20 это мусор. Значит прочего 40,
        // найденного 20, свободного 40. Если найденное считать добавкой,
        // выйдет 60 + 20 = 80 занятого, и человек увидит, что после очистки
        // освободится больше, чем освободится.
        var doli = DiskStrip.Doli(vsego: 100, zanyato: 60, naydeno: 20);

        doli.Should().Equal(0.40, 0.20, 0.40);
        doli.Sum().Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Naydennogo_bolshe_chem_zanyatogo_ne_byvaet()
    {
        // Так быть не должно, но источники считают по-разному, и полоса не то
        // место, где расхождение стоит показывать отрицательным сегментом.
        var doli = DiskStrip.Doli(vsego: 100, zanyato: 30, naydeno: 50);

        doli.Should().Equal(0.0, 0.30, 0.70);
        doli.Sum().Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Pustoy_tom_daet_nuli_a_ne_delenie_na_nol()
    {
        DiskStrip.Doli(vsego: 0, zanyato: 0, naydeno: 0).Should().Equal(0.0, 0.0, 0.0);
    }

    [Fact]
    public void Shiriny_skladyvayutsya_v_dorozhku_tochno()
    {
        // Щель в пиксель справа это то, ради чего полоса рисуется, а не
        // собирается из трёх Border со звёздочками.
        var shiriny = DiskStrip.Razlozhit([0.333, 0.333, 0.334], 1920);

        shiriny.Sum().Should().BeApproximately(1920, 0.01);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    public void Uzkaya_dorozhka_ne_perepolnyaetsya(double dorozhka)
    {
        // Тест выше был пустым, и мутация это показала: на 1920 сумма сходится
        // сама. Минимальная ширина всегда находит донора, отъём ровно равен
        // добавке, и доклад хвоста никогда не выполняется.
        //
        // Хвост возникает там, где донора НЕТ. На дорожке в несколько пикселей
        // самым широким оказывается тот самый сегмент, которому добавляют,
        // отъём не случается, и три сегмента по три пикселя вылезают за
        // пределы своего же элемента. Дорожка в три пикселя это не выдумка:
        // столько у полосы во время раскладки окна и в свёрнутой панели.
        var shiriny = DiskStrip.Razlozhit([0.5, 0.25, 0.25], dorozhka);

        shiriny.Sum().Should().BeApproximately(dorozhka, 0.01,
            "полоса рисует ровно свою ширину, а не на полтора пикселя больше");
    }

    [Fact]
    public void Tonkiy_segment_ne_ischezaet_sovsem()
    {
        // Три десятых процента от 1920 это 5.8 пикселя, а одна сотая уже
        // полпикселя. Доля, округлённая до нуля, это враньё арифметикой:
        // человек видит, что мусора нет, когда он есть.
        var shiriny = DiskStrip.Razlozhit([0.9998, 0.0001, 0.0001], 1920);

        shiriny[1].Should().BeGreaterThanOrEqualTo(3);
        shiriny[2].Should().BeGreaterThanOrEqualTo(3);
        shiriny.Sum().Should().BeApproximately(1920, 0.01);
    }

    [Fact]
    public void Nulevaya_dolya_ostayotsya_nulevoy()
    {
        // Обратная сторона: минимальная ширина даётся доле, которая ЕСТЬ.
        // Сегмент, нарисованный там, где ничего нет, это то же враньё наоборот.
        var shiriny = DiskStrip.Razlozhit([0.5, 0.0, 0.5], 1000);

        shiriny[1].Should().Be(0);
        shiriny.Sum().Should().BeApproximately(1000, 0.01);
    }
}
