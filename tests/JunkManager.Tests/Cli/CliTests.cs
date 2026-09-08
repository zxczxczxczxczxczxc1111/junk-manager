using System.Diagnostics;
using System.Text;
using FluentAssertions;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.Cli;

/// <summary>
/// Проверки утилиты командной строки, только безопасные команды.
/// </summary>
/// <remarks>
/// <para>
/// Заведён 05.09.2026 после того, как выяснилось: у CLI не было НИ ОДНОГО теста,
/// хотя это полноценная поверхность продукта, которая удаляет записи реестра и
/// запускает деинсталляторы. Цена пробела: строка `fuse-status` обещала, что
/// разрушительные операции запрещены, а `registry clean --yes` при том же
/// состоянии предохранителя удалил настоящую запись реестра.
/// </para>
/// <para>
/// Разрушительных команд тут нет и быть не должно: `registry clean` и
/// `apps remove` удаляют по-настоящему, а этот класс идёт на машине человека.
/// Их место в госте.
/// </para>
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class CliTests
{
    private static string Utilita { get; } = Nayti();

    /// <summary>
    /// Берёт утилиту, положенную РЯДОМ с набором. Не находит значит ПАДАЕТ, а не
    /// пропускает: тихо пропущенный тест это тест, которого нет.
    /// </summary>
    /// <remarks>
    /// Раньше поиск шёл вверх по дереву до JunkManager.slnx, то есть до корня
    /// репозитория. Набор публикуется самодостаточным и уезжает в гость, где
    /// репозитория нет вовсе, и там весь класс падал
    /// TypeInitializationException-ом ещё до первой проверки. Шесть проверок
    /// CLI при этом считались пройденными, потому что гоняли их только на
    /// машине разработчика. Найдено прогоном в госте 07.09.2026.
    /// </remarks>
    private static string Nayti()
    {
        var put = Path.Combine(AppContext.BaseDirectory, "cli", "JunkManager.Cli.exe");

        if (!File.Exists(put))
        {
            throw new InvalidOperationException(
                $"утилиты нет рядом с набором: {put}. Её кладёт туда цель "
                + "PolozhitUtilituRyadom в JunkManager.Tests.csproj, и без неё "
                + "проверки CLI не проверяют ничего");
        }

        return put;
    }

    private static (int Kod, string Vyvod) Zapustit(params string[] argumenty)
    {
        var psi = new ProcessStartInfo(Utilita)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in argumenty)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("утилита не запустилась");

        var vyvod = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(2));

        return (process.ExitCode, vyvod);
    }

    [Fact]
    public void Fuse_status_ne_obeshchaet_bolshe_chem_derzhit()
    {
        // Строка «разрушительные операции запрещены» была ЛОЖНОЙ: предохранитель
        // держит разрушительные ТЕСТЫ, а команды продукта не держит и держать не
        // должен, иначе продукт не работал бы на машине человека. Проверено
        // дорого: registry clean --yes при невзведённом предохранителе удалил
        // настоящую запись реестра.
        var (kod, vyvod) = Zapustit("fuse-status");

        kod.Should().Be(VmFuse.IsArmed ? 0 : 3);

        if (!VmFuse.IsArmed)
        {
            vyvod.Should().Contain("ТЕСТЫ", "заявление про запрет обязано называть, ЧТО именно запрещено");
            vyvod.Should().Contain("--yes", "человек обязан узнать, что продукт удаляет и без предохранителя");
        }
    }

    [Fact]
    public void Rules_validate_gruzit_nastoyashchie_pravila()
    {
        var (kod, vyvod) = Zapustit("rules-validate");

        kod.Should().Be(0, "выложенные правила обязаны грузиться: {0}", vyvod);
        vyvod.Should().Contain("правил загружено");
    }

    [Fact]
    public void Vneshniy_katalog_otklonyaetsya_do_prohoda()
    {
        // Reject the obsolete switch before it can become a deletion side door.
        var (kod, vyvod) = Zapustit("scan", "--rules", "C:\\never-read-this");
        kod.Should().Be(2);
        vyvod.Should().Contain("внешний каталог правил не поддерживается");
    }

    [Fact]
    public void Postavka_ne_soderzhit_redaktiruemyy_katalog()
    {
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "cli", "rules")).Should().BeFalse();
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "app", "rules")).Should().BeFalse();
        var (kod, vyvod) = Zapustit("rules-validate");
        kod.Should().Be(0, "встроенный каталог должен работать без файлов рядом: {0}", vyvod);
    }

    [Fact]
    public void Bez_komandy_pechataetsya_spravka_i_kod_nol()
    {
        var (kod, vyvod) = Zapustit();

        kod.Should().Be(0);
        vyvod.Should().Contain("Доступные команды");
        vyvod.Should().Contain("Коды выхода", "коды выхода это интерфейс для скриптов, и он обязан быть описан");
    }

    [Fact]
    public void Spravka_nazyvaet_vse_komandy_kotorye_est()
    {
        // Команда, которой нет в справке, для человека не существует. Список
        // правится руками вместе с плечами switch, и расхождение ловится здесь.
        var (_, vyvod) = Zapustit();

        foreach (var komanda in new[]
        {
            "fuse-status", "rules-validate", "scan", "registry scan", "registry clean",
            "apps list", "apps remove", "explain-disk",
        })
        {
            vyvod.Should().Contain(komanda, "команда {0} есть в продукте, но её нет в справке", komanda);
        }
    }

    [Fact]
    public void Neizvestnaya_komanda_ne_delaet_vid_chto_ponyala()
    {
        var (kod, vyvod) = Zapustit("takoy-komandy-net");

        kod.Should().Be(0, "справка это не ошибка");
        vyvod.Should().Contain("Доступные команды");
    }
}
