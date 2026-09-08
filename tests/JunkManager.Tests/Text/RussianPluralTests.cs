using System.Globalization;
using FluentAssertions;
using JunkManager.App.Text;
using Xunit;

namespace JunkManager.Tests.Text;

[Trait("Class", "Sandbox")]
public sealed class RussianPluralTests
{
    [Theory]
    [InlineData(0, "следов")]
    [InlineData(1, "след")]
    [InlineData(2, "следа")]
    [InlineData(4, "следа")]
    [InlineData(5, "следов")]
    [InlineData(11, "следов")]
    [InlineData(12, "следов")]
    [InlineData(14, "следов")]
    [InlineData(21, "след")]
    [InlineData(22, "следа")]
    [InlineData(25, "следов")]
    [InlineData(111, "следов")]
    [InlineData(114, "следов")]
    [InlineData(121, "след")]
    public void Choose_beryot_formu_po_chislu(long skolko, string ozhidaemo)
    {
        RussianPlural.Choose(skolko, "след", "следа", "следов")
            .Should().Be(ozhidaemo);
    }

    [Fact]
    public void Choose_ne_lomaetsya_na_otricatelnom()
    {
        // Отрицательных находок не бывает, но разница размеров бывает, и
        // «-1 файлов» в подписи это тот же брак, что «5 следа».
        RussianPlural.Choose(-1, "файл", "файла", "файлов").Should().Be("файл");
        RussianPlural.Choose(-3, "файл", "файла", "файлов").Should().Be("файла");
    }

    [Fact]
    public void Format_stavit_nerazryvnyy_probel_v_razryadah()
    {
        // Русская группировка разрядов идёт неразрывным пробелом U+00A0.
        // Обычный пробел даёт перенос строки посреди числа, и «3» уезжает
        // на предыдущую строку от «412».
        RussianPlural.Format(3412, "объект", "объекта", "объектов")
            .Should().Be("3\u00a0412 объектов");
    }

    [Fact]
    public void Format_ne_zavisit_ot_kultury_potoka()
    {
        // Гость полигона стоит с английской локалью. Если формат берётся из
        // текущего потока, на стенде получается «3,412 объектов», и это
        // всплывает только на снимке экрана из гостя.
        var byla = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            RussianPlural.Format(3412, "объект", "объекта", "объектов")
                .Should().Be("3\u00a0412 объектов");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = byla;
        }
    }
}
