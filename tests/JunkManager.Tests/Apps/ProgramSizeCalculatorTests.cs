using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class ProgramSizeCalculatorTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    public ProgramSizeCalculatorTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private static void Napolnit(string katalog, int faylov, int bayt)
    {
        Directory.CreateDirectory(katalog);

        for (var i = 0; i < faylov; i++)
        {
            File.WriteAllBytes(Path.Combine(katalog, $"f{i}.bin"), new byte[bayt]);
        }
    }

    [Fact]
    public void TryMeasure_schitaet_nastoyashchie_bayty_a_ne_ocenku()
    {
        var koren = _pesochnica.CreateDirectory("razmer-programmy");
        Napolnit(koren, 3, 100_000);
        Napolnit(Path.Combine(koren, "resources", "app"), 2, 50_000);

        var poluchilos = ProgramSizeCalculator.TryMeasure(koren, out var bayt, out var prichina, TestContext.Current.CancellationToken);

        poluchilos.Should().BeTrue(prichina);
        bayt.Should().Be(400_000);
    }

    [Fact]
    public void TryMeasure_ne_zahodit_vnutr_junction()
    {
        // A program directory with a junction into System32 must report the
        // program's own bytes. Counting through the link offers somebody else's
        // files and turns a 40 MB program into a 20 GB one.
        var koren = _pesochnica.CreateDirectory("razmer-so-ssylkoy");
        Napolnit(koren, 1, 1000);
        _pesochnica.CreateJunction(
            Path.Combine("razmer-so-ssylkoy", "ssylka"),
            Environment.GetFolderPath(Environment.SpecialFolder.System));

        var poluchilos = ProgramSizeCalculator.TryMeasure(koren, out var bayt, out var prichina, TestContext.Current.CancellationToken);

        poluchilos.Should().BeTrue(prichina);
        bayt.Should().Be(1000, "junction это чужие байты, а не байты программы");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMeasure_bez_puti_otkazyvaet_s_prichinoy(string? put)
    {
        ProgramSizeCalculator.TryMeasure(put, out var bayt, out var prichina, TestContext.Current.CancellationToken).Should().BeFalse();
        bayt.Should().Be(0);
        prichina.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryMeasure_nesushchestvuyushchiy_katalog_otkazyvaet_a_ne_daet_nol()
    {
        var net = Path.Combine(_pesochnica.Root, "takogo-net");

        ProgramSizeCalculator.TryMeasure(net, out var bayt, out var prichina, TestContext.Current.CancellationToken).Should().BeFalse();
        bayt.Should().Be(0);
        prichina.Should().Contain("не существует");
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\")]
    public void TryMeasure_obshchiy_koren_otkazyvaet(string obshchiy)
    {
        // InstallLocation on a live machine is sometimes just "C:\Program Files".
        // Measuring it would attribute every program on the machine to one of
        // them, and the number shown next to "освободится" would be a lie the
        // size of the whole disk.
        ProgramSizeCalculator.TryMeasure(obshchiy, out var bayt, out var prichina, TestContext.Current.CancellationToken)
            .Should().BeFalse();
        bayt.Should().Be(0);
        prichina.Should().Contain("общий");
    }

    [Fact]
    public void TryMeasure_schitaet_katalog_pod_zapreshchennym_kornem()
    {
        // Раздел 10.2 спеки требует посчитанный размер, и его образец это Chrome
        // с 999 МБ, а Chrome стоит в C:\Program Files. Guard запрещает УДАЛЯТЬ
        // оттуда, но замер это чтение: спросить у него разрешения значит
        // оставить без размера две трети списка программ.
        var pod = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc");

        ProgramSizeCalculator.TryMeasure(pod, out var bayt, out var prichina, TestContext.Current.CancellationToken)
            .Should().BeTrue(prichina);
        bayt.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(@"\\server\share\prog")]
    [InlineData(@"prog\bin")]
    public void TryMeasure_setevoy_i_otnositelnyy_put_otkazyvayut(string put)
    {
        // Отказ guard заменён своими проверками, и они обязаны остаться на месте:
        // сетевой путь и относительный путь это не «ноль байт», это «неизвестно».
        ProgramSizeCalculator.TryMeasure(put, out var bayt, out var prichina, TestContext.Current.CancellationToken).Should().BeFalse();
        bayt.Should().Be(0);
        prichina.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryMeasure_pustoy_katalog_daet_nol_i_eto_ne_otkaz()
    {
        var pustoy = _pesochnica.CreateDirectory("pustaya-programma");

        ProgramSizeCalculator.TryMeasure(pustoy, out var bayt, out var prichina, TestContext.Current.CancellationToken)
            .Should().BeTrue(prichina);
        bayt.Should().Be(0, "ноль посчитанный и ноль неизвестный это разные ответы");
    }
}
