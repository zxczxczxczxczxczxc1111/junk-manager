using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class PrefetchUsageReaderTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    public PrefetchUsageReaderTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private static readonly string[] Katalog =
    [
        "TELEGRAM.EXE-1A2B3C4D.pf",
        "TELEGRAM.EXE-99887766.pf",
        "TELEGRAMDESKTOP.EXE-11223344.pf",
        "NOTEPAD.EXE-AABBCCDD.pf",
        "TELEGRAM.EXE.pf",
        "TELEGRAM.EXE-XYZ.pf",
    ];

    [Fact]
    public void PodobratFayly_beret_tolko_tochnoe_imya_s_vosmiznachnym_hesham()
    {
        var naydeno = PrefetchUsageReader.PodobratFayly("Telegram.exe", Katalog);

        naydeno.Should().BeEquivalentTo(
            ["TELEGRAM.EXE-1A2B3C4D.pf", "TELEGRAM.EXE-99887766.pf"]);
    }

    [Fact]
    public void PodobratFayly_ne_putaet_prefiks_s_imenem()
    {
        // TELEGRAMDESKTOP is a different executable. Prefix matching here would
        // report the wrong program as recently used, and the whole point of the
        // feature is that the number can be trusted.
        PrefetchUsageReader.PodobratFayly("Telegram.exe", Katalog)
            .Should().NotContain("TELEGRAMDESKTOP.EXE-11223344.pf");
    }

    [Fact]
    public void PodobratFayly_ne_beret_fayl_s_imenem_dlinnee_iskomogo()
    {
        // Настоящие имена из C:\Windows\Prefetch на машине 05.09.2026: Windows
        // обрезает имя до 29 символов, поэтому PRETTYEYES-CHECK-SETUP-1.3.0.
        // это реальное имя файла, у которого .EXE уже отрезан. Сравнение по
        // префиксу отдало бы такой файл любой программе, чьё имя начинается так
        // же, то есть приписало бы ей чужие запуски.
        string[] obrezannye =
        [
            "PRETTYEYES-CHECK-SETUP-1.3.0.-0722E7E9.pf",
            "CRYPTODERMO_1.0.0_X64-SETUP.E-CFC7F0F6.pf",
        ];

        PrefetchUsageReader.PodobratFayly("PRETTYEYES-CHECK-SETUP-1.3.0", obrezannye)
            .Should().BeEmpty("имя сравнивается целиком, а не префиксом");

        // Обратная сторона той же обрезки, записана как есть: по полному имени
        // обрезанный след тоже не находится. Это граница механизма, а не дефект:
        // «нет данных» честнее придуманного числа.
        PrefetchUsageReader.PodobratFayly("PrettyEyes-check-setup-1.3.0.exe", obrezannye)
            .Should().BeEmpty();
    }

    [Fact]
    public void PodobratFayly_neizvestnoe_imya_daet_pusto()
    {
        PrefetchUsageReader.PodobratFayly("chego-net.exe", Katalog).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PodobratFayly_pustoe_imya_daet_pusto_a_ne_vsyo(string imya)
    {
        PrefetchUsageReader.PodobratFayly(imya, Katalog).Should().BeEmpty();
    }

    [Fact]
    public void Dney_neizvestnyy_vozrast_eto_null_a_ne_nol()
    {
        // Zero means "ran today". Null means "we do not know". Showing the first
        // in place of the second is a lie the person cannot see through.
        PrefetchUsageReader.Dney(null, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeNull();
    }

    [Fact]
    public void Dney_schitaet_polnye_sutki()
    {
        var seychas = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

        PrefetchUsageReader.Dney(seychas.AddDays(-10.5), seychas).Should().Be(10);
        PrefetchUsageReader.Dney(seychas.AddHours(-3), seychas).Should().Be(0);
    }

    [Fact]
    public void DaysSinceLastRun_bez_puti_daet_null()
    {
        PrefetchUsageReader.DaysSinceLastRun(null).Should().BeNull();
        PrefetchUsageReader.DaysSinceLastRun("   ").Should().BeNull();
    }

    [Fact]
    public void DaysSinceLastRun_katalog_bez_exe_daet_null()
    {
        var koren = _pesochnica.CreateDirectory("bez-exe");
        File.WriteAllText(Path.Combine(koren, "readme.txt"), "ничего исполняемого");

        PrefetchUsageReader.DaysSinceLastRun(koren).Should().BeNull(
            "сопоставлять нечего, значит ответа нет");
    }
}

[Trait("Class", "LiveRead")]
public sealed class PrefetchUsageReaderZhivyeTests
{
    [Fact]
    public void IsAvailable_otvechaet_odno_iz_dvuh_i_nazyvaet_prichinu()
    {
        var dostupno = PrefetchUsageReader.IsAvailable(out var prichina);

        if (dostupno)
        {
            prichina.Should().BeNull();
        }
        else
        {
            prichina.Should().NotBeNullOrWhiteSpace(
                "«не проверялось» обязано объяснять, почему не проверялось");
        }

        Console.WriteLine($"Prefetch доступен: {dostupno}. Причина: {prichina ?? "нет"}");
    }

    [Fact]
    public void DaysSinceLastRun_bez_prav_daet_null_a_ne_nol()
    {
        // Explorer runs on any live machine. Under an unelevated process the
        // Prefetch folder is unreadable, and the honest answer is null.
        var explorer = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var dney = PrefetchUsageReader.DaysSinceLastRun(explorer);

        if (!PrefetchUsageReader.IsAvailable(out _))
        {
            dney.Should().BeNull("без прав возраст неизвестен, а не равен нулю");
        }

        Console.WriteLine("дней с последнего запуска explorer.exe: "
            + (dney?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "нет данных"));
    }
}
