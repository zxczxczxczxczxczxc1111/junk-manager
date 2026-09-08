using JunkManager.Core;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Судья приёмки проверяется отдельно и в песочнице.
/// </summary>
/// <remarks>
/// Без этого весь посев судился бы непроверенным судьёй: в госте приёмка
/// запускается раз за прогон, и отличить «продукт ничего не нашёл» от «судья
/// не умеет засчитывать находки» было бы нечем.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class SeedJudgeTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static SeedEntry Primanka(
        string id,
        string put,
        FindingSource istochnik = FindingSource.Rule,
        bool obyazatelnaya = true,
        bool nastoyashchaya = true) =>
        new()
        {
            Id = id,
            Kind = SeedKind.File,
            Path = put,
            MustFind = true,
            Required = obyazatelnaya,
            Genuine = nastoyashchaya,
            ExpectedSource = istochnik,
            Why = "проверка судьи",
        };

    private static SeedEntry Lovushka(string id, string put) =>
        new()
        {
            Id = id,
            Kind = SeedKind.File,
            Path = put,
            MustFind = false,
            Why = "проверка судьи",
        };

    private static Finding Nahodka(string put, FindingSource istochnik = FindingSource.Rule) =>
        new("находка", put, 100, RiskTier.Safe, "ничего", istochnik);

    [Fact]
    public void Naydennaya_primanka_zaschityvaetsya()
    {
        var put = _pesochnica.CreateFile("a.tmp");
        var verdikt = SeedJudge.Judge([Primanka("a", put)], [Nahodka(put)]);

        verdikt.Hit.Should().ContainSingle();
        verdikt.Missed.Should().BeEmpty();
        verdikt.MissedRequired.Should().BeEmpty();
    }

    [Fact]
    public void Nenaydennaya_obyazatelnaya_primanka_popadaet_v_MissedRequired()
    {
        var put = _pesochnica.CreateFile("a.tmp");
        var verdikt = SeedJudge.Judge([Primanka("a", put)], []);

        verdikt.Missed.Should().ContainSingle();
        verdikt.MissedRequired.Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public void Nenaydennaya_neobyazatelnaya_primanka_eto_izmerenie_a_ne_padenie()
    {
        var put = _pesochnica.CreateFile("a.tmp");
        var verdikt = SeedJudge.Judge([Primanka("a", put, obyazatelnaya: false)], []);

        verdikt.Missed.Should().ContainSingle("промах считается всегда");
        verdikt.MissedRequired.Should().BeEmpty("но приёмку роняет только обязательный");
    }

    [Fact]
    public void Nahodka_pro_katalog_zaschityvaetsya_dlya_fayla_vnutri_nego()
    {
        // Правило имеет право сообщать про каталог целиком, а не про каждый
        // файл в нём. Судья, не понимающий этого, объявил бы промахом верную
        // работу.
        var katalog = _pesochnica.CreateDirectory("kesh");
        var fayl = Path.Combine(katalog, "vnutri.tmp");
        File.WriteAllText(fayl, "мусор");

        var verdikt = SeedJudge.Judge([Primanka("a", fayl)], [Nahodka(katalog)]);

        verdikt.Hit.Should().ContainSingle();
    }

    [Fact]
    public void Pohozhee_imya_kataloga_ne_zaschityvaetsya()
    {
        // Обратная сторона предыдущего: "kesh" не покрывает "kesh-drugoy".
        var katalog = _pesochnica.CreateDirectory("kesh");
        var chuzhoy = _pesochnica.CreateFile(Path.Combine("kesh-drugoy", "vnutri.tmp"));

        var verdikt = SeedJudge.Judge([Primanka("a", chuzhoy)], [Nahodka(katalog)]);

        verdikt.Hit.Should().BeEmpty("сравнение по вложенности, а не по префиксу строки");
        verdikt.Missed.Should().ContainSingle();
    }

    [Fact]
    public void Nayden_ne_tem_istochnikom_eto_otdelnyy_klass_rashozhdeniya()
    {
        // Самый важный случай: счёт находок верный, а механизм мёртвый.
        var put = _pesochnica.CreateFile("a.tmp");

        var verdikt = SeedJudge.Judge(
            [Primanka("a", put, FindingSource.VolumeCache)],
            [Nahodka(put, FindingSource.PatternScan)]);

        verdikt.Hit.Should().ContainSingle("по пути приманка найдена");
        verdikt.MissedRequired.Should().BeEmpty();
        verdikt.WrongSource.Should().ContainSingle()
            .Which.Should().Contain("VolumeCache").And.Contain("PatternScan");
    }

    [Fact]
    public void Srabotavshaya_lovushka_vidna_poimenno()
    {
        var put = _pesochnica.CreateFile("chuzhoy.tmp");
        var verdikt = SeedJudge.Judge([Lovushka("l", put)], [Nahodka(put)]);

        verdikt.TrapsTriggered.Should().ContainSingle().Which.Should().Be("l");
    }

    [Fact]
    public void Nahodka_v_zapreshchennom_korne_eto_proval_dazhe_bez_lovushki()
    {
        var verdikt = SeedJudge.Judge([], [Nahodka(@"C:\Windows\System32\config")]);

        verdikt.InForbiddenRoots.Should().ContainSingle();
    }

    [Theory]
    [InlineData(FindingPath.VolumeCacheScheme + "D3D Shader Cache")]
    [InlineData(FindingPath.PlatformToolScheme + "dism/component-store")]
    [InlineData(FindingPath.MsixScheme + "Microsoft.WindowsStore_8wekyb3d8bbwe")]
    public void Lichnost_ne_yavlyayushchayasya_putyom_ne_schitaetsya_zapretnym_kornem(string lichnost)
    {
        // Обработчик очистки Windows, склад компонентов DISM и пакет MSIX это
        // ЛИЧНОСТИ, а не места на диске. Файловый guard отвечает на них отказом
        // просто потому, что это не путь, и приёмка красила таким отказом
        // честные находки: 05.09.2026 их набралось семь штук, и все семь были
        // ложной тревогой. У записи реестра свой guard уже был, у этих не было.
        var verdikt = SeedJudge.Judge([], [Nahodka(lichnost)]);

        verdikt.InForbiddenRoots.Should().BeEmpty();
    }

    [Fact]
    public void Cel_v_zapretnom_korne_lovitsya_dazhe_u_lichnosti()
    {
        // Обратная сторона послабления выше: скидка даётся ЛИЧНОСТИ, а не всей
        // находке. Обработчик, назвавший своей целью System32, обязан ронять
        // приёмку так же, как обычное правило.
        var nahodka = new Finding(
            "обработчик",
            FindingPath.VolumeCacheScheme + "Nekiy Handler",
            100,
            RiskTier.Safe,
            "ничего",
            FindingSource.VolumeCache,
            Scope: DeleteScope.SelectedEntries,
            Targets: [@"C:\Windows\System32\config"]);

        var verdikt = SeedJudge.Judge([], [nahodka]);

        verdikt.InForbiddenRoots.Should().ContainSingle()
            .Which.Should().Be(@"C:\Windows\System32\config");
    }

    [Fact]
    public void Nahodki_vne_opisi_schitayutsya_no_ne_ronyayut()
    {
        var put = _pesochnica.CreateFile("a.tmp");
        var chuzhaya = _pesochnica.CreateFile("b.tmp");

        var verdikt = SeedJudge.Judge([Primanka("a", put)], [Nahodka(put), Nahodka(chuzhaya)]);

        verdikt.OutsideManifest.Should().Be(1);
        verdikt.MissedRequired.Should().BeEmpty();
        verdikt.TrapsTriggered.Should().BeEmpty();
    }

    [Fact]
    public void Otchet_pechataet_vse_chetyre_klassa_rashozhdeniy()
    {
        var naydennaya = _pesochnica.CreateFile("naydennaya.tmp");
        var propushchennaya = _pesochnica.CreateFile("propushchennaya.tmp");
        var lovushka = _pesochnica.CreateFile("lovushka.tmp");

        var verdikt = SeedJudge.Judge(
            [
                Primanka("naydena", naydennaya, FindingSource.VolumeCache),
                Primanka("propushchena", propushchennaya),
                Lovushka("lovushka", lovushka),
            ],
            [Nahodka(naydennaya, FindingSource.PatternScan), Nahodka(lovushka)]);

        var otchet = verdikt.Report();

        otchet.Should().Contain("посеяно 3");
        otchet.Should().Contain("промах: propushchena");
        otchet.Should().Contain("СРАБОТАЛА ЛОВУШКА: lovushka");
        otchet.Should().Contain("VolumeCache");
        otchet.Should().Contain("находок в запрещённых корнях 0");
    }

    [Fact]
    public void Otchet_nazyvaet_nepoceyannoe_otdelno()
    {
        // Приманка, которую не удалось посеять, не проверяет ничего, и
        // молчаливо засчитать её промахом значит перепутать дыру в продукте с
        // дырой в сеятеле.
        var verdikt = SeedJudge.Judge([], [], ["temp-old-file: отказано в доступе"]);

        verdikt.SeedFailures.Should().ContainSingle();
        verdikt.Report().Should().Contain("НЕ УДАЛОСЬ ПОСЕЯТЬ 1");
    }
}
