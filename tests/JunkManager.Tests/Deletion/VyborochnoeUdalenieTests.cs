using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Удаление по списку целей внутри каталога. Отдельно от FileDeleterTests
/// намеренно: там проверяется «унести всё», здесь «унести ровно перечисленное и
/// не тронуть соседей», и это противоположные утверждения.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class VyborochnoeUdalenieTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();
    private readonly SpisokZhurnala _zhurnal = new();
    private readonly FileDeleter _udalitel;

    public VyborochnoeUdalenieTests() => _udalitel = new FileDeleter(_zhurnal);

    public void Dispose() => _pesochnica.Dispose();

    private static VerifiedPath Propusk(string put)
    {
        SafetyGuard.TryVerifyForDeletion(put, out var propusk, out var prichina)
            .Should().BeTrue("тест обязан начинаться с настоящего пропуска, отказ: {0}", prichina);
        return propusk;
    }

    private static string Fayl(string dir, string name, int bytes)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public async Task Unosit_tolko_perechislennoe_i_ostavlyaet_koren()
    {
        var koren = _pesochnica.CreateDirectory("vyborka");
        var cel = Fayl(koren, "staryy.tmp", 3000);
        var sosed = Fayl(koren, "zhivoy.log", 500);

        var itog = await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [cel], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted);
        itog.BytesFreed.Should().Be(3000, "освобождено ровно столько, сколько занимала цель");
        File.Exists(cel).Should().BeFalse();
        File.Exists(sosed).Should().BeTrue("сосед не был целью и обязан остаться");
        Directory.Exists(koren).Should().BeTrue(
            "поэлементный режим никогда не уносит сам каталог: это может быть %TEMP%");
    }

    [Fact]
    public async Task Podkatalog_opustevshiy_posle_udaleniya_uhodit()
    {
        var koren = _pesochnica.CreateDirectory("pustoy-posle");
        var vlozhennyy = Path.Combine(koren, "vlozhennyy");
        var cel = Fayl(vlozhennyy, "staryy.tmp", 1000);

        await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [cel], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        Directory.Exists(vlozhennyy).Should().BeFalse(
            "каталог, в котором не осталось ничего кроме удалённой цели, это тоже мусор");
        Directory.Exists(koren).Should().BeTrue();
    }

    [Fact]
    public async Task Podkatalog_s_ucelevshim_faylom_ostaetsya()
    {
        var koren = _pesochnica.CreateDirectory("neposty");
        var vlozhennyy = Path.Combine(koren, "vlozhennyy");
        var cel = Fayl(vlozhennyy, "staryy.tmp", 1000);
        var zhivoy = Fayl(vlozhennyy, "zhivoy.log", 200);

        await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [cel], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        Directory.Exists(vlozhennyy).Should().BeTrue();
        File.Exists(zhivoy).Should().BeTrue();
    }

    [Fact]
    public async Task Pustoy_podkatalog_kotorogo_ne_kasalis_ostaetsya()
    {
        // Уборка идёт по предкам удалённых целей и только по ним. Пустой каталог
        // в стороне человеку не показывали, значит уносить его нельзя.
        var koren = _pesochnica.CreateDirectory("storonniy");
        var storonniy = Path.Combine(koren, "chuzhoy-pustoy");
        Directory.CreateDirectory(storonniy);
        var cel = Fayl(Path.Combine(koren, "svoy"), "staryy.tmp", 1000);

        await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [cel], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        Directory.Exists(storonniy).Should().BeTrue(
            "его не было в предпросмотре, значит он не имеет права исчезнуть");
    }

    [Fact]
    public async Task Cel_vne_kornya_otklonyaetsya_i_nichego_ne_unosit()
    {
        var koren = _pesochnica.CreateDirectory("koren-a");
        var chuzhoy = Fayl(_pesochnica.CreateDirectory("koren-b"), "chuzhoy.dat", 400);

        var itog = await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [chuzhoy], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        File.Exists(chuzhoy).Should().BeTrue("цель вне корня находки это чужой файл");
        itog.Status.Should().Be(DeleteStatus.Failed);
        itog.Reason.Should().Contain("вне");
    }

    [Fact]
    public async Task Zhurnal_poluchaet_stroku_na_kazhduyu_cel_a_ne_odnu_na_nahodku()
    {
        var koren = _pesochnica.CreateDirectory("zhurnal-celey");
        var pervaya = Fayl(koren, "a.tmp", 100);
        var vtoraya = Fayl(koren, "b.tmp", 200);

        await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [pervaya, vtoraya], DeleteMode.Permanent,
            TestContext.Current.CancellationToken);

        _zhurnal.Zapisi.Select(z => z.Path).Should().BeEquivalentTo([pervaya, vtoraya],
            "журнал это список того, что унесли, а не список того, что предлагали");
    }

    [Fact]
    public async Task Zanyataya_cel_ne_srivaet_ostalnye()
    {
        var koren = _pesochnica.CreateDirectory("zanyataya-cel");
        var zanyataya = Fayl(koren, "zanyataya.tmp", 100);
        var svobodnaya = Fayl(koren, "svobodnaya.tmp", 700);

        await using var derzhatel = new FileStream(
            zanyataya, FileMode.Open, FileAccess.Read, FileShare.None);

        var itog = await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [zanyataya, svobodnaya], DeleteMode.Permanent,
            TestContext.Current.CancellationToken);

        File.Exists(svobodnaya).Should().BeFalse("одна занятая цель не отменяет остальные");
        itog.Status.Should().Be(DeleteStatus.Failed);
        itog.BytesFreed.Should().Be(700, "в счёт идёт только то, что действительно ушло");
        itog.Reason.Should().Contain("1 из 2");
    }

    [Fact]
    public async Task Pustoy_spisok_celey_eto_propusk_a_ne_udalenie_kornya()
    {
        var koren = _pesochnica.CreateDirectory("nechego-unosit");
        var zhiloy = Fayl(koren, "zhivoy.log", 300);

        var itog = await _udalitel.DeleteSelectedAsync(
            Propusk(koren), [], DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        Directory.Exists(koren).Should().BeTrue("пустой список целей ни при каких условиях не значит «весь каталог»");
        File.Exists(zhiloy).Should().BeTrue();
    }
}
