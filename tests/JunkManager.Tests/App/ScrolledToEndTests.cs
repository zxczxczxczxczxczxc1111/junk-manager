using FluentAssertions;
using JunkManager.App.Controls.Behaviors;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class ScrolledToEndTests
{
    [Fact]
    public void Spisok_koroche_okna_schitaetsya_dolistannym_srazu()
    {
        // Иначе кнопка не оживает никогда, когда выбран один пункт, и продукт
        // выглядит сломанным ровно в самом безобидном случае.
        ScrolledToEnd.Dostignut(offset: 0, viewport: 400, extent: 120, dopusk: 2)
            .Should().BeTrue();
    }

    [Fact]
    public void Nedolistannyy_spisok_ne_schitaetsya()
    {
        ScrolledToEnd.Dostignut(offset: 0, viewport: 400, extent: 4000, dopusk: 2)
            .Should().BeFalse();
    }

    [Fact]
    public void Dolistannyy_do_konca_schitaetsya()
    {
        ScrolledToEnd.Dostignut(offset: 3600, viewport: 400, extent: 4000, dopusk: 2)
            .Should().BeTrue();
    }

    [Fact]
    public void Dopusk_zakryvaet_drobnyy_hvost_prokrutki()
    {
        // Прокрутка по строкам оставляет дробный остаток, и точное сравнение
        // с концом не срабатывает никогда.
        ScrolledToEnd.Dostignut(offset: 3598.6, viewport: 400, extent: 4000, dopusk: 2)
            .Should().BeTrue();
    }

    [Fact]
    public void Ostanovka_za_shag_do_konca_ne_schitaetsya()
    {
        // Граница проверяется с обеих сторон. Без этой проверки допуск можно
        // раздуть до любого числа, и «долистано» начнёт означать «почти начал».
        ScrolledToEnd.Dostignut(offset: 3560, viewport: 400, extent: 4000, dopusk: 2)
            .Should().BeFalse();
    }

    [Fact]
    public void Pustoy_spisok_ne_schitaetsya_dolistannym()
    {
        // Пустого списка на подтверждении не бывает: туда нельзя попасть, не
        // отметив ничего. Если он всё же пуст, это ошибка, и кнопка удаления
        // не должна оживать по недоразумению.
        ScrolledToEnd.Dostignut(offset: 0, viewport: 400, extent: 0, dopusk: 2)
            .Should().BeFalse();
    }
}
