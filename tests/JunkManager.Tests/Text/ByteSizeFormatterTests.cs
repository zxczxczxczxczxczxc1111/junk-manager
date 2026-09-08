using System.Globalization;
using FluentAssertions;
using JunkManager.App.Text;
using Xunit;

namespace JunkManager.Tests.Text;

[Trait("Class", "Sandbox")]
public sealed class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 Б")]
    [InlineData(1L, "1 Б")]
    [InlineData(512L, "512 Б")]
    [InlineData(1023L, "1023 Б")]
    [InlineData(1024L, "1,0 КБ")]
    [InlineData(1_572_864L, "1,5 МБ")]
    [InlineData(20_078_400_000L, "18,7 ГБ")]
    [InlineData(107_374_182_400L, "100 ГБ")]
    [InlineData(1_099_511_627_776L, "1,0 ТБ")]
    public void Format_daet_to_chto_narisovano_v_maketah(long bayt, string ozhidaemo)
    {
        ByteSizeFormatter.Format(bayt).Should().Be(ozhidaemo);
    }

    [Fact]
    public void Format_razdelitel_zapyataya_pri_lyuboy_kulture()
    {
        var byla = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            ByteSizeFormatter.Format(20_078_400_000L).Should().Be("18,7 ГБ");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = byla;
        }
    }

    [Fact]
    public void Format_otricatelnyy_razmer_eto_oshibka_vyzyvayushchego()
    {
        // Размер не бывает отрицательным. Тихо показать «-2,0 ГБ» значит
        // показать посчитанную неправильно цифру как факт.
        var act = () => ByteSizeFormatter.Format(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
