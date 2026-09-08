using System.Diagnostics;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Определитель держателя проверяется настоящей блокировкой, а не заглушкой:
/// весь смысл этого кода в том, что он разговаривает с системой, и подменённая
/// система проверила бы только наши собственные представления о ней.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class LockedFileInspectorTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    [Fact]
    public void TryGetHolders_nazyvaet_process_kotoryy_derzhit_fayl()
    {
        var fayl = _pesochnica.CreateFile("zanyato.bin", "содержимое");
        using var derzhatel = new FileStream(fayl, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        LockedFileInspector.TryGetHolders(fayl, out var derzhateli, out var prichina)
            .Should().BeTrue("вопрос обязан быть отвечен, отказ: {0}", prichina);

        derzhateli.Should().Contain(d => d.ProcessId == Environment.ProcessId,
            "файл держит именно этот процесс, и определитель обязан его назвать");
    }

    [Fact]
    public void TryGetHolders_svobodnyy_fayl_daet_pustoy_spisok_a_ne_otkaz()
    {
        // «Никто не держит» и «спросить не удалось» это разные факты. Слитые в
        // один, они превращают внятное объяснение в пожатие плечами.
        var fayl = _pesochnica.CreateFile("svobodno.bin");

        LockedFileInspector.TryGetHolders(fayl, out var derzhateli, out var prichina)
            .Should().BeTrue("отказ: {0}", prichina);

        derzhateli.Should().BeEmpty();
        prichina.Should().BeNull();
    }

    [Fact]
    public void TryGetHolders_otsutstvuyushchiy_fayl_ne_ronyaet_vyzov()
    {
        var fayl = Path.Combine(_pesochnica.Root, "net-takogo.bin");

        var vyzov = () => LockedFileInspector.TryGetHolders(fayl, out _, out _);

        vyzov.Should().NotThrow("несуществующий путь это обычный вход, а не авария");
    }

    [Fact]
    public void TryGetHolders_pustoy_put_eto_oshibka_vyzyvayushchego()
    {
        var vyzov = () => LockedFileInspector.TryGetHolders("   ", out _, out _);

        vyzov.Should().Throw<ArgumentException>(
            "пустая строка это не «файл, который никто не держит», это дефект вызова");
    }

    [Fact]
    public void Opisanie_derzhatelya_chitaemo_chelovekom()
    {
        var fayl = _pesochnica.CreateFile("dlya-opisaniya.bin");
        using var derzhatel = new FileStream(fayl, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        LockedFileInspector.TryGetHolders(fayl, out var derzhateli, out _).Should().BeTrue();

        var svoy = derzhateli.Single(d => d.ProcessId == Environment.ProcessId);

        svoy.Describe().Should().Contain(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.CurrentCulture));
        svoy.Describe().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryAskToClose_svobodnyy_fayl_eto_uspeh_a_ne_otkaz()
    {
        // Никого закрывать не надо, значит просьба выполнена. Отказ здесь
        // означал бы, что человек, нажавший «закрыть программу и удалить» на
        // файле, который уже освободился, получит ошибку на ровном месте.
        var fayl = _pesochnica.CreateFile("nikto-ne-derzhit.bin");

        LockedFileInspector.TryAskToClose(fayl, out var prichina)
            .Should().BeTrue("отказ: {0}", prichina);

        prichina.Should().BeNull();
    }

    [Fact]
    public void TryAskToClose_pustoy_put_eto_oshibka_vyzyvayushchego()
    {
        var vyzov = () => LockedFileInspector.TryAskToClose("   ", out _);

        vyzov.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryAskToClose_ne_obyavlyaet_uspeh_poka_fayl_derzhat()
    {
        // Проверено фактом 06.09.2026 на ОДНОМ И ТОМ ЖЕ держателе три раза
        // подряд: Restart Manager отвечает на просьбу то кодом 351
        // (ERROR_FAIL_NOACTION_REBOOT) при живом держателе, то нулём при живом
        // держателе, то нулём с уходом держателя через пару секунд. То есть код
        // возврата НЕ отвечает на вопрос «файл свободен?», и обе прошлые версии
        // этой проверки были неверны: первая ждала ухода по CTRL+C, вторая
        // ждала кода 351.
        //
        // Поэтому проверяется единственное, что тут постоянно: успех
        // объявляется только по освобождённому файлу, а отказ говорит словами и
        // не оставляет за собой убитый процесс. Ветвление здесь не слабость
        // проверки, а честное описание системы: выдумывать ей детерминизм,
        // которого нет, значит получить набор, зелёный через раз.
        var fayl = _pesochnica.CreateFile("poprosim-zakrytsya.bin", "данные");
        var metka = fayl + ".vzyal";

        using var chuzhoy = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command \"$f=[IO.File]::Open('" + fayl +
            "','Open','ReadWrite','None'); New-Item -ItemType File -Path '" + metka +
            "' | Out-Null; Start-Sleep -Seconds 60; $f.Close()\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("не удалось запустить держателя");

        try
        {
            var srok = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(metka) && DateTime.UtcNow < srok && !chuzhoy.HasExited)
            {
                Thread.Sleep(50);
            }

            File.Exists(metka).Should().BeTrue("держатель не сообщил, что открыл файл");

            var poluchilos = LockedFileInspector.TryAskToClose(fayl, out var prichina);

            Console.WriteLine($"просьба закрыться: {poluchilos}, слова: {prichina}");

            if (poluchilos)
            {
                prichina.Should().BeNull("у успеха причины отказа нет");

                // Вся суть исправления: «получилось» обязано означать
                // освобождённый файл, а не удачный код возврата. Дальше по
                // потоку стоит повторное удаление, и оно упрётся в держателя,
                // которого продукт только что объявил закрытым.
                LockedFileInspector.TryGetHolders(fayl, out var posle, out _)
                    .Should().BeTrue("проверить держателей обязано получаться");

                posle.Should().BeEmpty(
                    "продукт сказал «закрылись», а файл всё ещё держат");
            }
            else
            {
                prichina.Should().NotBeNullOrWhiteSpace();
                prichina.Should().NotContain(
                    "0x", "код возврата в лицо человеку это тот же тупик, что и раньше");
                prichina.Should().Contain(
                    "следующей загрузке", "человек обязан прочитать, что делать дальше");

                chuzhoy.HasExited.Should().BeFalse(
                    "отказ, оставивший за собой убитый процесс, это худшее из "
                    + "двух: и данные потеряны, и человек об этом не знает");
            }
        }
        finally
        {
            if (!chuzhoy.HasExited)
            {
                chuzhoy.Kill(entireProcessTree: true);
            }

            chuzhoy.WaitForExit(5000);
        }
    }

    [Fact]
    public void TryGetHolders_ne_techyot_sessiyami()
    {
        // Сессий Restart Manager на машину конечное число, и незакрытая сессия
        // ломает определитель не только нам, но и всем остальным на машине.
        // Утечка видна только на повторе, поэтому повтор тут и стоит.
        var fayl = _pesochnica.CreateFile("mnogo-raz.bin");

        for (var i = 0; i < 200; i++)
        {
            LockedFileInspector.TryGetHolders(fayl, out _, out var prichina)
                .Should().BeTrue("на итерации {0} отказ: {1}", i, prichina);
        }
    }

    [Fact]
    public void TryGetHolders_vidit_chuzhoy_process_a_ne_tolko_svoy()
    {
        // Свой процесс Restart Manager нашёл бы и по внутренним таблицам. Чужой
        // это настоящая проверка того, что мы спрашиваем систему, а не себя.
        var fayl = _pesochnica.CreateFile("chuzhoy-derzhit.bin", "данные");
        var metka = fayl + ".vzyal";

        // Держатель СООБЩАЕТ, что взял файл, отдельной меткой. Прежняя версия
        // просто ждала десять секунд и падала в полном прогоне под нагрузкой:
        // проверка мерила скорость запуска powershell, а не определитель.
        // Плавающая красная проверка хуже отсутствующей, она учит не смотреть
        // на красное.
        using var chuzhoy = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command \"$f=[IO.File]::Open('" + fayl +
            "','Open','ReadWrite','None'); New-Item -ItemType File -Path '" + metka +
            "' | Out-Null; Start-Sleep -Seconds 60; $f.Close()\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("не удалось запустить держателя");

        try
        {
            var srok = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(metka) && DateTime.UtcNow < srok && !chuzhoy.HasExited)
            {
                Thread.Sleep(50);
            }

            File.Exists(metka).Should().BeTrue(
                "держатель не сообщил, что открыл файл: проверять определитель ещё нечем");

            // Файл взят наверняка. Дальше уже спрашиваем систему, и запас
            // нужен только на ответ самого Restart Manager.
            var vzyal = false;
            for (var i = 0; i < 50 && !vzyal; i++)
            {
                LockedFileInspector.TryGetHolders(fayl, out var kto, out _);
                vzyal = kto.Any(d => d.ProcessId == chuzhoy.Id);

                if (!vzyal)
                {
                    Thread.Sleep(100);
                }
            }

            vzyal.Should().BeTrue("чужой процесс держит файл, и определитель обязан назвать именно его");
        }
        finally
        {
            if (!chuzhoy.HasExited)
            {
                chuzhoy.Kill(entireProcessTree: true);
            }

            chuzhoy.WaitForExit(5000);
        }
    }
}
