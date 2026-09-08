using JunkManager.Core;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Опись посева проверяется в песочнице, а не в госте.
/// </summary>
/// <remarks>
/// Кривая опись в госте выглядит как «продукт ничего не нашёл»: приёмка падает
/// на разборе файла, а не на находках, и вывод про глубину получается ложным.
/// Дешевле поймать это здесь.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class SeedManifestTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static IReadOnlyList<SeedEntry> Opis() =>
        SeedEntry.Load(Path.Combine(AppContext.BaseDirectory, "posev.json"));

    [Fact]
    public void Nastoyashchaya_opis_gruzitsya_i_ne_pusta()
    {
        // Та же опись, что уедет в гость. Проверяется настоящий файл, а не
        // выдуманный: опись, которая не доехала до выходного каталога, роняет
        // приёмку в госте и ничего не говорит про продукт.
        var opis = Opis();

        opis.Should().NotBeEmpty();
        opis.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void V_opisi_est_i_primanki_i_lovushki()
    {
        var opis = Opis();

        opis.Count(s => s.MustFind).Should().BeGreaterThan(10, "приманок должно хватать на все источники");
        opis.Count(s => !s.MustFind).Should().BeGreaterThan(10, "без ловушек приёмка проверяет только жадность");
    }

    /// <summary>
    /// Источники, которые УЖЕ построены. Список правится руками при закрытии
    /// каждой задачи, и это не бюрократия: пока источник не вписан сюда, его
    /// приманка обязана быть измерением, а не воротами. Правка этого списка и
    /// есть тот момент, когда приходится честно ответить, работает механизм
    /// или только собирается.
    /// </summary>
    private static readonly FindingSource[] Postroennye =
    [
        FindingSource.Rule,
        FindingSource.Registry,

        // Uninstall leftovers require an actual confirmed removal; age-only folders are now traps.
        // Their destructive coverage lives in ProgramUninstallExecutionTests and RecycleBinZhivyeTests.

        // Detector вписан 05.09.2026 вместе с PolnyyProhod. До этого прохода
        // обнаружители были построены, покрыты зелёными юнит-тестами и НЕ
        // ПОДКЛЮЧЕНЫ: их не звал никто, и приманка electron-cache не находилась
        // не потому, что механизм сломан. Приёмка в госте после подключения
        // находит её источником Detector, поэтому приманка переведена в
        // обязательные.
        FindingSource.Detector,

        // PatternScan вписан 05.09.2026 вместе с масками каталогов. Поиск по
        // образцу был подключён проходом раньше, но приманку crash-dumps-pattern
        // всё равно не находил: он умел только маски ФАЙЛОВ, а имена файлов
        // внутри каталога отчётов об авариях задаёт разработчик приложения, и
        // знать их нельзя. Имя каталога задаёт фреймворк, и оно знаемо.
        FindingSource.PatternScan,

        // VolumeCache сюда НЕ вписан, и это не забывчивость. Источник построен
        // и подключён, в госте он даёт шесть находок, а на живой машине семь
        // на 216 МБ. Но его находка это личность volumecache:Имя, а не путь, и
        // сопоставить её с приманкой судья не может. Сравнение по одному имени
        // обработчика было бы тавтологией: он докладывает свой полный объём, в
        // котором приманка на полмегабайта тонет. Замер ДО посева и ПОСЛЕ тоже
        // не годится, проверено опытом 05.09.2026: файл на 64 МБ в %TEMP%
        // сдвинул число обработчика 'Temporary Files' с 2219 КБ на 2227 КБ, то
        // есть не попал в него вовсе. Обработчик применяет свою политику
        // возраста и докладывает не то, что лежит на диске, а то, что ОН готов
        // забрать. Значит обе его приманки остаются измерением НАВСЕГДА, а не
        // до лучших времён.
    ];

    [Fact]
    public void Kazhdyy_postroennyy_istochnik_imeet_obyazatelnuyu_primanku()
    {
        var opis = Opis();

        opis.Where(s => s is { MustFind: true, Required: true })
            .Should().OnlyContain(s => Postroennye.Contains(s.ExpectedSource!.Value),
                "обязательными могут быть только приманки на уже построенные источники");

        foreach (var istochnik in Postroennye)
        {
            opis.Should().Contain(s => s.MustFind && s.Required && s.ExpectedSource == istochnik,
                "у построенного источника {0} нет ни одной обязательной приманки, то есть его поломка не уронит приёмку",
                istochnik);
        }
    }

    [Fact]
    public void Primanki_na_nepostroennye_istochniki_ne_obyazatelnye()
    {
        var opis = Opis();

        foreach (var chuzhoy in opis.Where(
            s => s.MustFind && !Postroennye.Contains(s.ExpectedSource!.Value)))
        {
            chuzhoy.Required.Should().BeFalse(
                "приманка '{0}' на источник {1}, которого ещё нет. Её промах это измерение глубины, а не поломка",
                chuzhoy.Id, chuzhoy.ExpectedSource);
        }
    }

    [Fact]
    public void Puti_v_opisi_ne_povtoryayutsya()
    {
        // Два посева по одному пути дают неразличимый вердикт: непонятно, чья
        // это находка и чей промах.
        var opis = Opis();

        opis.Select(s => s.ExpandedPath)
            .Should().OnlyHaveUniqueItems("два посева по одному пути неразличимы в вердикте");
    }

    [Fact]
    public void Kazhdyy_posev_obyasnyaet_sebya()
    {
        Opis().Should().OnlyContain(s => s.Why.Length > 20,
            "однословное why это отсутствующее why");
    }

    [Fact]
    public void Load_opis_bez_why_otklonyaetsya()
    {
        var put = Napisat("""
            { "seeds": [ { "id": "a", "kind": "File", "path": "%TEMP%\\a.tmp", "mustFind": false } ] }
            """);

        var vyzov = () => SeedEntry.Load(put);

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*why*");
    }

    [Fact]
    public void Load_primanka_bez_istochnika_otklonyaetsya()
    {
        var put = Napisat("""
            { "seeds": [ { "id": "a", "kind": "File", "path": "%TEMP%\\a.tmp",
              "mustFind": true, "why": "приманка без указанного источника" } ] }
            """);

        var vyzov = () => SeedEntry.Load(put);

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*expectedSource*");
    }

    [Fact]
    public void Load_obyazatelnaya_lovushka_otklonyaetsya()
    {
        var put = Napisat("""
            { "seeds": [ { "id": "a", "kind": "File", "path": "%TEMP%\\a.tmp",
              "mustFind": false, "required": true, "why": "ловушка, помеченная обязательной" } ] }
            """);

        var vyzov = () => SeedEntry.Load(put);

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*required*");
    }

    [Fact]
    public void Load_dubl_id_otklonyaetsya()
    {
        var put = Napisat("""
            { "seeds": [
              { "id": "a", "kind": "File", "path": "%TEMP%\\a.tmp", "mustFind": false, "why": "первая запись" },
              { "id": "a", "kind": "File", "path": "%TEMP%\\b.tmp", "mustFind": false, "why": "вторая запись" }
            ] }
            """);

        var vyzov = () => SeedEntry.Load(put);

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*дважды*");
    }

    [Fact]
    public void Load_pustaya_opis_otklonyaetsya()
    {
        var vyzov = () => SeedEntry.Load(Napisat("""{ "seeds": [] }"""));

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*пуст*");
    }

    [Fact]
    public void Load_otsutstvuyushchiy_fayl_govorit_chto_imenno_ne_doehalo()
    {
        var vyzov = () => SeedEntry.Load(Path.Combine(_pesochnica.Root, "net-takogo.json"));

        vyzov.Should().Throw<InvalidOperationException>().WithMessage("*доехать до гостя*");
    }

    private string Napisat(string json)
    {
        var put = Path.Combine(_pesochnica.Root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(put, json);
        return put;
    }
}
