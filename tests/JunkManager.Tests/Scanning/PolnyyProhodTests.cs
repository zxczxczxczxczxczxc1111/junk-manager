using JunkManager.Core;
using JunkManager.Core.Scanning;
using JunkManager.Core.Sources.Pattern;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Scanning;

/// <summary>
/// Общий проход: один вызов, который зовёт все источники.
/// </summary>
/// <remarks>
/// Причина, по которой этот класс вообще есть: приёмка посева 05.09.2026
/// показала 13 приманок из 18, и пять промахов пришлись на источники, которые
/// ПОСТРОЕНЫ и покрыты юнит-тестами, но которых не звал никто. «Построено» и
/// «подключено» это разные вещи, и цену разницы видно только тут.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class PolnyyProhodTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static string Pravila => Path.Combine(AppContext.BaseDirectory, "rules");

    [Fact]
    public async Task Sklad_Windows_nahodit_svoy_fayl_opisaniy()
    {
        // Настоящий каталог правил, а не выдуманный. Юнит-тесты источника кладут
        // volume-caches.json во временный каталог и потому не видят, ЧТО ему
        // передают в продукте: описания лежат в rules/sources, а проход сначала
        // давал rules. Источник отвечал «файл описаний не найден», и увидеть это
        // можно было только запуском на живой машине.
        var plan = ScanPlan.Nichego with { SkladWindows = true };

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Skipped.Should().NotContain(
            p => p.Reason.Contains("источник отказал", StringComparison.Ordinal),
            "склад Windows обязан находить свои описания там, куда их кладёт сборка");
    }

    [Fact]
    public async Task Vyklyuchennyy_istochnik_ne_daet_ni_odnoy_nahodki()
    {
        // Ворота против обратного дефекта: проход, который зовёт всё подряд
        // независимо от настроек, стирает саму возможность выключить медленный
        // или требующий прав источник.
        var plan = ScanPlan.Nichego;

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Poisk_po_obraztsu_vklyuchaetsya_i_nahodit_svoyo()
    {
        var staryy = _pesochnica.CreateFile("brosheno.tmp", new string('x', 4096));
        File.SetLastWriteTimeUtc(staryy, DateTime.UtcNow.AddDays(-400));

        var plan = ScanPlan.Nichego with
        {
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default() with { Roots = [_pesochnica.Root] },
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Findings.Should().Contain(
            f => f.Source == FindingSource.PatternScan && f.Path == staryy);
    }

    [Fact]
    public async Task Obnaruzhiteli_vklyuchayutsya_i_nahodyat_svoyo()
    {
        // Форма кэша Electron задана фреймворком, а не приложением: каталог
        // ровно такой формы обязан находиться без единого правила.
        var kesh = Path.Combine(_pesochnica.Root, "discord", "Cache", "Cache_Data");
        Directory.CreateDirectory(kesh);
        File.WriteAllBytes(Path.Combine(kesh, "data_1"), new byte[3 * 1024 * 1024]);

        var plan = ScanPlan.Nichego with
        {
            Obnaruzhiteli = true,
            KorniObnaruzhiteley = [_pesochnica.Root],
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Findings.Should().Contain(f => f.Source == FindingSource.Detector);
    }

    [Fact]
    public async Task Dva_istochnika_na_odin_put_ne_udvaivayut_cifru()
    {
        // Ровно та причина, по которой слияние идёт через SliyanieIstochnikov, а
        // не сложением списков: и поиск по образцу, и обнаружитель смотрят в
        // один и тот же профиль, и один каталог, посчитанный дважды, показывает
        // человеку вдвое больше освобождаемого места, чем есть.
        var kesh = Path.Combine(_pesochnica.Root, "discord", "GPUCache");
        Directory.CreateDirectory(kesh);
        File.WriteAllBytes(Path.Combine(kesh, "data_0"), new byte[2 * 1024 * 1024]);

        var vnutri = Path.Combine(kesh, "staryy.tmp");
        File.WriteAllText(vnutri, new string('x', 4096));
        File.SetLastWriteTimeUtc(vnutri, DateTime.UtcNow.AddDays(-400));

        var plan = ScanPlan.Nichego with
        {
            Obnaruzhiteli = true,
            KorniObnaruzhiteley = [_pesochnica.Root],
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default() with { Roots = [_pesochnica.Root] },
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Findings.Should().NotContain(
            f => f.Path == vnutri,
            "файл внутри уже посчитанного каталога это двойной счёт");
        itog.Skipped.Should().Contain(
            p => p.Path == vnutri && p.Reason.Contains("уже посчитан", StringComparison.Ordinal),
            "молча выброшенная находка неотличима от источника, который ничего не нашёл");
    }

    [Fact]
    public async Task Otmena_ostanavlivaet_prohod_a_ne_prosto_teryaetsya()
    {
        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var plan = ScanPlan.Nichego with
        {
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default() with { Roots = [_pesochnica.Root] },
        };

        var itog = await PolnyyProhod.ScanAsync(plan, Pravila, progress: null, otmena.Token);

        itog.Cancelled.Should().BeTrue(
            "проход, который проглотил отмену, докладывает неполный список как полный");
    }

    [Fact]
    public async Task Cancellation_at_merge_progress_stops_before_the_next_source()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var messages = new List<string>();
        var progress = new InlineProgress(message =>
        {
            messages.Add(message);
            if (message.StartsWith("Собираем результаты:", StringComparison.Ordinal)) stop.Cancel();
        });
        var plan = ScanPlan.Nichego with { Obnaruzhiteli = true, KorniObnaruzhiteley = [_pesochnica.Root] };
        var result = await PolnyyProhod.ScanAsync(plan, Pravila, progress, stop.Token);
        result.Cancelled.Should().BeTrue();
        messages.Should().Contain("Собираем результаты: кэши Electron");
        messages.Should().NotContain("брошенные каталоги");
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    [Fact]
    public async Task Otkaz_odnogo_istochnika_ne_ronyaet_ves_prohod()
    {
        // Источник, упавший на чужой машине, не имеет права унести с собой
        // находки остальных: человек тогда видит ноль вместо неполного списка.
        // Отказ берётся настоящий, а не выдуманный: каталог правил, не доехавший
        // до выходного каталога, это ровно тот случай, который уже ловился в
        // этом проекте, и RuleLoader на нём кидает, а не возвращает пусто.
        var staryy = _pesochnica.CreateFile("est.tmp", new string('x', 4096));
        File.SetLastWriteTimeUtc(staryy, DateTime.UtcNow.AddDays(-400));

        var plan = ScanPlan.Nichego with
        {
            Pravila = true,
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default() with { Roots = [_pesochnica.Root] },
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan,
            Path.Combine(_pesochnica.Root, "net-takogo-kataloga-pravil"),
            progress: null,
            TestContext.Current.CancellationToken);

        itog.Findings.Should().Contain(f => f.Path == staryy);
        itog.Skipped.Should().Contain(
            p => p.Reason.Contains("источник отказал", StringComparison.Ordinal),
            "отказ, о котором не сказано, неотличим от источника, который ничего не нашёл");
    }

    [Fact]
    public async Task Nechitaemyy_koren_obnaruzhitelya_ne_glushit_ostalnyh()
    {
        var staryy = _pesochnica.CreateFile("tozhe-est.tmp", new string('x', 4096));
        File.SetLastWriteTimeUtc(staryy, DateTime.UtcNow.AddDays(-400));

        var plan = ScanPlan.Nichego with
        {
            Obnaruzhiteli = true,
            KorniObnaruzhiteley = [Path.Combine(_pesochnica.Root, "net-takogo-katalog")],
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default() with { Roots = [_pesochnica.Root] },
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan, Pravila, progress: null, TestContext.Current.CancellationToken);

        itog.Findings.Should().Contain(f => f.Path == staryy);
    }
}
