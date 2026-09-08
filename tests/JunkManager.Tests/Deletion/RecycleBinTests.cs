using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Корзина, отложенное удаление и повторная очистка.
/// </summary>
/// <remarks>
/// Проверки, которые действительно кладут файл в корзину, помечены классом
/// LiveDestructive и в госте: корзина одна на пользователя, и тестовый мусор в
/// ней это мусор в чужой корзине, а не в песочнице. В песочнице проверяется всё
/// до обращения к оболочке, и этого хватает на весь наш собственный код.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class RecycleBinTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();
    private readonly SpisokZhurnala _zhurnal = new();
    private readonly FileDeleter _udalitel;

    public RecycleBinTests() => _udalitel = new FileDeleter(_zhurnal);

    public void Dispose() => _pesochnica.Dispose();

    private static VerifiedPath Propusk(string put)
    {
        SafetyGuard.TryVerifyForDeletion(put, out var propusk, out var prichina)
            .Should().BeTrue("тест обязан начинаться с настоящего пропуска, отказ: {0}", prichina);
        return propusk;
    }

    [Fact]
    public void RecycleBin_podmena_puti_na_ssylku_otklonyaetsya_tak_zhe_kak_i_bezvozvratnoe()
    {
        // Отправить чужой каталог в корзину ничем не лучше, чем стереть его:
        // человек этого не просил, и перепроверка обязана быть той же самой.
        var chuzhoy = _pesochnica.CreateDirectory("cel");
        File.WriteAllText(Path.Combine(chuzhoy, "zhivi.txt"), "не трогать");

        var zhertva = _pesochnica.CreateDirectory("zhertva");
        var propusk = Propusk(zhertva);

        Directory.Delete(zhertva, recursive: true);
        _pesochnica.CreateJunction("zhertva", chuzhoy);

        var itog = RecycleBinDeleter.Delete(propusk);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("ссылк");
        File.Exists(Path.Combine(chuzhoy, "zhivi.txt")).Should().BeTrue();
    }

    [Fact]
    public void RecycleBin_ischeznuvshiy_put_daet_Skipped_a_ne_isklyuchenie()
    {
        var fayl = _pesochnica.CreateFile("isparilsya.tmp");
        var propusk = Propusk(fayl);
        File.Delete(fayl);

        var itog = RecycleBinDeleter.Delete(propusk);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("исчез");
    }

    [Fact]
    public void RecycleBin_pustoy_propusk_otklonyaetsya_a_ne_padaet()
    {
        var itog = RecycleBinDeleter.Delete(default);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.BytesFreed.Should().Be(0);
    }

    [Fact]
    public void ScheduleOnReboot_bez_prav_administratora_daet_vnyatnyy_otkaz()
    {
        // Решение отделено от системного вызова НАМЕРЕННО: под администратором
        // ветку отказа иначе не достать вовсе, а именно её и надо проверить.
        var otkaz = RebootDeleteScheduler.Otkaz(elevated: false, put: @"C:\Windows\Temp\a.tmp");

        otkaz.Should().NotBeNull();
        otkaz.Should().Contain("администратор",
            "человеку надо сказать, ЧЕГО не хватает, а не просто «отказано»");
    }

    [Fact]
    public void ScheduleOnReboot_pod_administratorom_prichin_otkazat_net()
    {
        RebootDeleteScheduler.Otkaz(elevated: true, put: @"C:\Windows\Temp\a.tmp")
            .Should().BeNull();
    }

    [Fact]
    public void ScheduleOnReboot_pustoy_put_otklonyaetsya_dazhe_pod_administratorom()
    {
        RebootDeleteScheduler.Otkaz(elevated: true, put: "  ")
            .Should().NotBeNull();
    }

    [Fact]
    public void Ochered_otlozhennogo_udaleniya_chitaetsya_i_ne_padaet()
    {
        // Чтение общесистемной ветки: список может быть пустым, и это нормально.
        // Проверяется, что чтение не роняет вызов и не выдумывает записей.
        var zaplanirovano = RebootDeleteScheduler.Scheduled();

        zaplanirovano.Should().NotBeNull();
        // NotContain, а не OnlyContain. Проверено прогоном 07.09.2026 на чистой
        // системе: у FluentAssertions 7.2.2 OnlyContain падает на ПУСТОЙ
        // коллекции по замыслу, а комментарий выше объявляет пустую очередь
        // нормой. Пока в реестре что-то лежало, противоречие не всплывало.
        zaplanirovano.Should().NotContain(p => p.StartsWith(@"\??\", StringComparison.Ordinal),
            "префикс пространства имён NT к обычному пути отношения не имеет и снимается");
    }

    [Theory]
    // Обычная форма, как её описывает документация.
    [InlineData(@"\??\C:\Temp\a.tmp", @"C:\Temp\a.tmp")]
    // Форма, которую Windows 11 сборки 26200 пишет НА САМОМ ДЕЛЕ. Найдена
    // красным тестом в госте и подтверждена шестнадцатеричным дампом значения
    // реестра: 2A 00 31 00 перед префиксом.
    [InlineData(@"*1\??\C:\Temp\a.tmp", @"C:\Temp\a.tmp")]
    [InlineData(@"!\??\C:\Temp\a.tmp", @"C:\Temp\a.tmp")]
    // Префикса нет вовсе: запись возвращается как есть, а не калечится.
    [InlineData(@"C:\Temp\a.tmp", @"C:\Temp\a.tmp")]
    [InlineData("", "")]
    public void Zapis_ocheredi_razbiraetsya_gde_by_ni_stoyal_prefiks(string syroe, string ozhidaemoe)
    {
        // Разбор вынесен в чистую функцию именно из-за этого класса дефектов:
        // предположение «префикс стоит в начале» было ЛОЖНЫМ, и проверить его
        // на живой очереди можно было только в госте и только один раз за
        // прогон.
        RebootDeleteScheduler.OchistitZapis(syroe).Should().Be(ozhidaemoe);
    }

    [Fact]
    public async Task Povtornaya_ochistka_ochishchennogo_daet_nol_nahodok_i_nol_oshibok()
    {
        // Требование идемпотентности. Ловит целый класс дефектов: обработчик,
        // который на пустом входе падает, ругается или показывает нулевые
        // находки как ошибку.
        var kesh = _pesochnica.CreateDirectory("kesh-programmy");
        File.WriteAllText(Path.Combine(kesh, "a.tmp"), new string('a', 500));
        File.WriteAllText(Path.Combine(kesh, "b.tmp"), new string('b', 700));

        var pravila = PravilaNaPesochnicu(kesh);
        var skaner = new FileScanner();

        var pervyy = await skaner.ScanAsync(pravila, progress: null, TestContext.Current.CancellationToken);
        pervyy.Findings.Should().ContainSingle();
        pervyy.TotalBytes.Should().Be(1200);

        foreach (var nahodka in pervyy.Findings)
        {
            var itog = await _udalitel.DeleteAsync(
                Propusk(nahodka.Path), DeleteMode.Permanent, TestContext.Current.CancellationToken);
            itog.Status.Should().Be(DeleteStatus.Deleted);
        }

        var vtoroy = await skaner.ScanAsync(pravila, progress: null, TestContext.Current.CancellationToken);

        vtoroy.Findings.Should().BeEmpty("чистить нечего, и это не ошибка");
        vtoroy.Skipped.Should().BeEmpty("исчезнувший путь это не пропуск и не отказ, его просто нет");
        vtoroy.TotalBytes.Should().Be(0);
        vtoroy.Cancelled.Should().BeFalse();
    }

    /// <summary>
    /// Одно правило, указывающее ровно на переданный каталог. Файл правил
    /// пишется настоящий и грузится настоящим загрузчиком: подсунуть
    /// <see cref="RuleDefinition"/> руками значило бы обойти ровно тот код,
    /// который в проде и работает.
    /// </summary>
    private IReadOnlyList<RuleDefinition> PravilaNaPesochnicu(string katalog)
    {
        var katalogPravil = Path.Combine(_pesochnica.Root, "pravila");
        Directory.CreateDirectory(katalogPravil);

        var json = """
            {
              "category": "Песочница",
              "rules": [
                {
                  "id": "sandbox.kesh",
                  "name": "Кэш тестовой программы",
                  "tier": "Safe",
                  "consequence": "Ничего: каталог создан тестом",
                  "paths": ["ПУТЬ"]
                }
              ]
            }
            """.Replace("ПУТЬ", katalog.Replace(@"\", @"\\", StringComparison.Ordinal),
                StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(katalogPravil, "sandbox.json"), json);

        return RuleLoader.Load(katalogPravil);
    }
}
